using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NuGet.Protocol.Core.Types;
using NuGetTrends.Data;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace NuGetTrends.Scheduler
{
    // ReSharper disable once ClassNeverInstantiated.Global - DI
    public class DailyDownloadWorker : IHostedService
    {
        private readonly DailyDownloadWorkerOptions _options;
        private readonly IConnectionFactory _connectionFactory;
        private readonly IServiceProvider _services;
        private readonly INuGetSearchService _nuGetSearchService;
        private readonly ILogger<DailyDownloadWorker> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly ConcurrentBag<(IModel,IConnection)> _connections = new ConcurrentBag<(IModel, IConnection)>();

        private readonly List<Task> _workers;

        public DailyDownloadWorker(
            IOptions<DailyDownloadWorkerOptions> options,
            IConnectionFactory connectionFactory,
            IServiceProvider services,
            INuGetSearchService nuGetSearchService,
            ILogger<DailyDownloadWorker> logger)
        {
            _options = options.Value;
            _connectionFactory = connectionFactory;
            _services = services;
            _nuGetSearchService = nuGetSearchService;
            _logger = logger;
            _workers = new List<Task>(_options.WorkerCount);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Starting the worker.");

            for (var i = 0; i < _options.WorkerCount; i++)
            {
                _workers.Add(Task.Run(async () =>
                {
                    // Poor man's Polly
                    const int maxAttempts = 3;
                    for (var attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        try
                        {
                            Connect();
                            break;
                        }
                        catch (Exception e)
                        {
                            if (attempt == maxAttempts)
                            {
                                _logger.LogCritical(e, "Couldn't connect to the broker. Attempts: {attempts}", attempt);
                                throw;
                            }

                            var waitMs = attempt * 10000;
                            _logger.LogInformation(e,
                                "Failed to connect to the broker. Waiting for {waitMs} milliseconds. Attempt {attempts}",
                                waitMs, attempt);
                            await Task.Delay(waitMs, cancellationToken);
                        }
                    }
                }, _cancellationTokenSource.Token));
            }
            return Task.CompletedTask;
        }

        private void Connect()
        {
            var connection = _connectionFactory.CreateConnection();
            var channel = connection.CreateModel();
            _connections.Add((channel, connection));

            const string queueName = "daily-download";
            var queueDeclareOk = channel.QueueDeclare(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            _logger.LogDebug("Queue creation OK with {QueueName}, {ConsumerCount}, {MessageCount}",
                queueDeclareOk.QueueName, queueDeclareOk.ConsumerCount, queueDeclareOk.MessageCount);

            var consumer = new AsyncEventingBasicConsumer(channel);

            consumer.Received += OnConsumerOnReceived;

            channel.BasicConsume(
                queue: queueName,
                autoAck: false,
                consumer: consumer);

            var defaultConsumer = new EventingBasicConsumer(channel);

            defaultConsumer.Received += (s, e) =>
            {
                _logger.LogWarning("DefaultConsumer fired: {message}", Convert.ToBase64String(e.Body));
            };

            channel.DefaultConsumer = defaultConsumer;
        }

        private async Task OnConsumerOnReceived(object sender, BasicDeliverEventArgs ea)
        {
            List<string>? packageIds = null;
            try
            {
                var body = ea.Body;
                _logger.LogDebug("Received message with body size: {size}", body.Length);

                packageIds = MessagePackSerializer.Deserialize<List<string>>(body);

                if (packageIds == null)
                {
                    throw new InvalidOperationException($"Deserializing {body} resulted in a null reference.");
                }

                await UpdateDownloadCount(packageIds);

                var consumer = (AsyncEventingBasicConsumer)sender;
                consumer.Model.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception e)
            {
                if (packageIds != null)
                {
                    for (var i = 0; i < packageIds.Count; i++)
                    {
                        e.Data.Add("Package:#" + i.ToString("D2"), packageIds[i]);
                    }
                }
                _logger.LogCritical(e, "Failed to process batch.");
                throw;
            }
        }

        private async Task UpdateDownloadCount(IList<string> packageIds)
        {
            var tasks = new List<Task<IPackageSearchMetadata?>>(packageIds.Count);
            foreach (var id in packageIds)
            {
                tasks.Add(_nuGetSearchService.GetPackage(id, _cancellationTokenSource.Token));
            }

            var whenAll = Task.WhenAll(tasks);
            try
            {
                await whenAll;
            }
            catch when (whenAll.Exception is {} exs && exs.InnerExceptions.Count > 1)
            {
                throw exs; // re-throw the AggregateException to capture all errors with Sentry
            }

            using var scope = _services.CreateScope();
            using var context = scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
            for (var i = 0; i < tasks.Count; i++)
            {
                var expectedPackageId = packageIds[i];
                var packageMetadata = tasks[i].Result;
                if (packageMetadata == null)
                {
                    // All versions are unlisted or:
                    // "This package has been deleted from the gallery. It is no longer available for install/restore."
                    _logger.LogInformation("Package deleted: {packageId}", expectedPackageId);
                    await RemovePackage(context, expectedPackageId, _cancellationTokenSource.Token);
                }
                else
                {
                    context.DailyDownloads.Add(new DailyDownload
                    {
                        PackageId = packageMetadata.Identity.Id,
                        Date = DateTime.UtcNow.Date,
                        DownloadCount = packageMetadata.DownloadCount
                    });

                    void Update(PackageDownload package, IPackageSearchMetadata metadata)
                    {
                        if (metadata.IconUrl?.ToString() is { } url)
                        {
                            package.IconUrl = url;
                        }
                        package.LatestDownloadCount = metadata.DownloadCount;
                        package.LatestDownloadCountCheckedUtc = DateTime.UtcNow;
                    }

                    var pkgDownload = await context.PackageDownloads.FirstOrDefaultAsync(p => p.PackageIdLowered == packageMetadata.Identity.Id.ToLower());
                    if (pkgDownload == null)
                    {
                        pkgDownload = new PackageDownload
                        {
                            PackageId = packageMetadata.Identity.Id,
                            PackageIdLowered = packageMetadata.Identity.Id.ToLower(),
                        };
                        Update(pkgDownload, packageMetadata);
                        context.PackageDownloads.Add(pkgDownload);
                    }
                    else
                    {
                        Update(pkgDownload, packageMetadata);
                        context.PackageDownloads.Update(pkgDownload);
                    }
                }

                try
                {
                    await context.SaveChangesAsync(_cancellationTokenSource.Token);
                }
                catch (DbUpdateException e)
                    when (e.InnerException is PostgresException pge
                          && (pge.ConstraintName == "PK_daily_downloads"))
                {
                    // Re-entrancy
                    _logger.LogWarning(e, "Skipping record already tracked.");
                }
            }
        }

        private async Task RemovePackage(NuGetTrendsContext context, string packageId, CancellationToken token)
        {
            var package = await context.PackageDetailsCatalogLeafs.Where(p => p.PackageId == packageId)
                .ToListAsync(token)
                .ConfigureAwait(false);

            if (package.Count == 0)
            {
                // This happens a lot.
                _logger.LogInformation("Package with Id {packageId} not found!.", packageId);
                return;
            }

            context.PackageDetailsCatalogLeafs.RemoveRange(package);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Stopping the worker.");

            try
            {
                _cancellationTokenSource.Cancel();

                if (_workers is { } workers)
                {
                    await Task.WhenAll(workers);
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed canceling the worker.");
            }
            finally
            {
                foreach (var (channel, connection) in _connections)
                {
                    // "Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods..."
                    // https://www.rabbitmq.com/dotnet-api-guide.html
                    // - Why?
                    channel?.Close();
                    connection?.Close();
                }
            }
        }
    }
}
