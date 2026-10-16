using FluentAssertions;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.IntegrationTests.Infrastructure;
using NuGetTrends.Scheduler;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace NuGetTrends.IntegrationTests;

/// <summary>
/// The daily download consumers must never die silently. In October 2026 failed batches were left unacked,
/// RabbitMQ's consumer_timeout closed every channel and the worker never reconnected, so nothing was processed
/// for days while the process looked healthy.
/// </summary>
[Collection("E2E")]
public class DailyDownloadWorkerResilienceTests(IntegrationTestFixture fixture)
{
    private const string QueueName = "daily-download";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task FailedBatch_IsSettled_NotLeftUnacked()
    {
        var connectionFactory = CreateConnectionFactory();
        PurgeQueue(connectionFactory);
        var nuGet = new FailingNuGetSearchService();
        var worker = CreateWorker(connectionFactory, nuGet);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForConsumer(connectionFactory);
            Publish(connectionFactory, "Some.Package");
            await WaitUntil(() => nuGet.Calls >= 1, "the worker never received the message");
            await Task.Delay(TimeSpan.FromSeconds(2)); // let the handler settle the delivery
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        // The broker requeues unacked deliveries when the consumer's connection closes.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        GetMessageCount(connectionFactory).Should().Be(0, "a failed batch must be nacked, not left unacked");
        nuGet.Calls.Should().Be(1, "a failed batch is dropped rather than retried");
    }

    [Fact]
    public async Task Consumer_Reconnects_AfterBrokerCancelsIt()
    {
        var connectionFactory = CreateConnectionFactory();
        PurgeQueue(connectionFactory);
        var nuGet = new FailingNuGetSearchService();
        var worker = CreateWorker(connectionFactory, nuGet);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForConsumer(connectionFactory);

            // Deleting the queue makes the broker cancel the consumer while leaving its channel open.
            using (var connection = connectionFactory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
                channel.QueueDelete(QueueName);
            }

            // The worker reconnects and re-declares the queue.
            await WaitForConsumer(connectionFactory);

            Publish(connectionFactory, "Some.Package");
            await WaitUntil(() => nuGet.Calls >= 1, "the reconnected consumer never received the message");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Shutdown_MidBatch_RequeuesOnce_WithoutRedeliveringToStoppingConsumer()
    {
        var connectionFactory = CreateConnectionFactory();
        PurgeQueue(connectionFactory);
        var nuGet = new BlockingNuGetSearchService();
        var worker = CreateWorker(connectionFactory, nuGet);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await WaitForConsumer(connectionFactory);
            Publish(connectionFactory, "Some.Package");
            await WaitUntil(() => nuGet.Calls >= 1, "the worker never received the message");
        }
        finally
        {
            // Stopping mid-batch is the scenario under test; in finally so a failed wait can't leave a consumer
            // blocking on the shared queue for later tests.
            await worker.StopAsync(CancellationToken.None);
        }

        // The interrupted delivery stays unacked until the connection closes. A nack would free the prefetch slot
        // and let the broker hand the message back to the stopping consumer (a narrow race before the close).
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        nuGet.Calls.Should().Be(1, "the stopping consumer must not receive the message again");
        GetMessageCount(connectionFactory).Should().Be(1, "the interrupted batch goes back to the queue for the next instance");
        PurgeQueue(connectionFactory);
    }

    private ConnectionFactory CreateConnectionFactory() => new()
    {
        HostName = fixture.RabbitMqHostname,
        Port = fixture.RabbitMqPort,
        UserName = fixture.RabbitMqUsername,
        Password = fixture.RabbitMqPassword,
        DispatchConsumersAsync = true,
        AutomaticRecoveryEnabled = false,
    };

    private DailyDownloadWorker CreateWorker(IConnectionFactory connectionFactory, INuGetSearchService nuGetSearchService)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<NuGetTrendsContext>(options => options.UseNpgsql(fixture.PostgresConnectionString));
        var serviceProvider = services.BuildServiceProvider();

        return new DailyDownloadWorker(
            Options.Create(new DailyDownloadWorkerOptions { WorkerCount = 1 }),
            connectionFactory,
            serviceProvider,
            nuGetSearchService,
            fixture.CreateClickHouseService(),
            new NuGetAvailabilityState(),
            NullLogger<DailyDownloadWorker>.Instance);
    }

    private static void Publish(IConnectionFactory connectionFactory, params string[] packageIds)
    {
        using var connection = connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        channel.BasicPublish("", QueueName, properties, MessagePackSerializer.Serialize(packageIds.ToList()));
    }

    private static void PurgeQueue(IConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false, arguments: null);
        channel.QueuePurge(QueueName);
    }

    private static uint GetMessageCount(IConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();
        return channel.QueueDeclarePassive(QueueName).MessageCount;
    }

    private static async Task WaitForConsumer(IConnectionFactory connectionFactory)
    {
        using var connection = connectionFactory.CreateConnection();
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // A failed passive declare closes the channel, so use a new one per attempt.
                using var channel = connection.CreateModel();
                if (channel.QueueDeclarePassive(QueueName).ConsumerCount >= 1)
                {
                    return;
                }
            }
            catch (OperationInterruptedException)
            {
                // Queue doesn't exist (yet)
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException($"No consumer on '{QueueName}' within {Timeout.TotalSeconds}s");
    }

    private static async Task WaitUntil(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(because);
    }

    /// <summary>Never completes a lookup until cancelled, keeping a batch in flight.</summary>
    private sealed class BlockingNuGetSearchService : INuGetSearchService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(System.Threading.Timeout.Infinite, token);
            return null;
        }
    }

    /// <summary>Fails every lookup, standing in for any failure while processing a batch (e.g. ClickHouse out of disk).</summary>
    private sealed class FailingNuGetSearchService : INuGetSearchService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IPackageSearchMetadata?> GetPackage(string packageId, CancellationToken token)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromException<IPackageSearchMetadata?>(
                new InvalidOperationException("Simulated failure while processing a batch."));
        }
    }
}
