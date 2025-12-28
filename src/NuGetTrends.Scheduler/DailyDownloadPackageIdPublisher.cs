using System.Runtime.CompilerServices;
using Hangfire;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using RabbitMQ.Client;
using Sentry.Hangfire;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
[AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadPackageIdPublisher(
    IConnectionFactory connectionFactory,
    NuGetTrendsContext context,
    IHub hub,
    ILogger<DailyDownloadPackageIdPublisher> logger)
{
    // Hangfire's [DisableConcurrentExecution] uses a distributed lock keyed by method arguments.
    // However, with MemoryStorage and recurring jobs, the lock doesn't prevent concurrent execution
    // when a new scheduled job is triggered while a previous job (or its retry) is still running.
    // This static semaphore ensures only one daily download publisher runs at a time within this process.
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    // Batch size for streaming from PostgreSQL
    private const int BatchSize = 10_000;

    // Batch size for RabbitMQ messages
    private const int QueueBatchSize = 25;

    [SentryMonitorSlug("DailyDownloadPackageIdPublisher.Import")]
    public async Task Import(IJobCancellationToken token)
    {
        var jobId = token.ShutdownToken.GetHashCode().ToString("X8"); // Use token hash as a pseudo job ID for logging

        if (!await ImportLock.WaitAsync(TimeSpan.Zero))
        {
            logger.LogWarning("Job {JobId}: Skipping daily download publisher - another instance is already in progress", jobId);
            throw new ConcurrentExecutionSkippedException(
                $"Job {jobId}: Daily download publisher skipped - another instance is already in progress");
        }

        try
        {
            using var _ = hub.PushScope();
            var transaction = hub.StartTransaction("daily-download-pkg-id-publisher", "queue.write",
                "queues package ids to fetch download numbers");
            hub.ConfigureScope(s => s.SetTag("jobId", jobId));

            try
            {
                var connectionSpan = transaction.StartChild("queue.connect", "Connect to RabbitMQ");
                using var connection = connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                const string queueName = "daily-download";

                var queueDeclareOk = channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                logger.LogDebug("Job {JobId}: Queue creation OK, with '{QueueName}', '{ConsumerCount}', '{MessageCount}'",
                    jobId, queueDeclareOk.QueueName, queueDeclareOk.ConsumerCount, queueDeclareOk.MessageCount);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.Expiration = "43200000";
                connectionSpan.Finish();

                var messageCount = 0;

                try
                {
                    var queueIdsSpan = transaction.StartChild("queue.ids");

                    // Stream package IDs from PostgreSQL, filtering out those already checked today
                    var dbStreamSpan = queueIdsSpan.StartChild("db.stream", "Stream unprocessed package IDs from PostgreSQL");

                    var queueBatch = new List<string>(QueueBatchSize);

                    await foreach (var packageId in GetUnprocessedPackageIdsAsync(token.ShutdownToken))
                    {
                        messageCount++;
                        queueBatch.Add(packageId);

                        if (queueBatch.Count == QueueBatchSize)
                        {
                            Queue(queueBatch, channel, queueName, properties);
                            queueBatch.Clear();
                        }
                    }

                    dbStreamSpan.Finish();

                    // Queue remaining packages
                    if (queueBatch.Count != 0)
                    {
                        var queueSpan = queueIdsSpan.StartChild("queue.enqueue", "Enqueue final batch");
                        queueSpan.SetTag("count", queueBatch.Count.ToString());
                        Queue(queueBatch, channel, queueName, properties);
                        queueSpan.Finish();
                    }

                    queueIdsSpan.SetTag("queue-name", queueName);
                    queueIdsSpan.SetTag("batch-size", BatchSize.ToString());
                    queueIdsSpan.SetTag("queue-batch-size", QueueBatchSize.ToString());
                    queueIdsSpan.SetTag("message-count", messageCount.ToString());
                    queueIdsSpan.Finish();
                }
                finally
                {
                    logger.LogInformation("Job {JobId}: Finished publishing messages. Queued {QueuedCount} packages for download.",
                        jobId, messageCount);
                }

                transaction.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                transaction.Finish(e);
                throw;
            }
        }
        finally
        {
            ImportLock.Release();
        }
    }

    /// <summary>
    /// Streams package IDs from PostgreSQL that haven't been checked today.
    /// </summary>
    private async IAsyncEnumerable<string> GetUnprocessedPackageIdsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;

        await foreach (var packageId in context.GetUnprocessedPackageIds(todayUtc)
                           .AsAsyncEnumerable()
                           .WithCancellation(ct))
        {
            yield return packageId;
        }
    }

    private static void Queue(
        List<string> batch,
        IModel channel,
        string queueName,
        IBasicProperties properties)
    {
        var serializedBatch = MessagePackSerializer.Serialize(batch);

        channel.BasicPublish(
            exchange: string.Empty,
            routingKey: queueName,
            basicProperties: properties,
            body: serializedBatch);
    }
}
