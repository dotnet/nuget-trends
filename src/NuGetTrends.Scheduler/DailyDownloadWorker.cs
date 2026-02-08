using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sentry;

namespace NuGetTrends.Scheduler;

// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadWorker : IHostedService
{
    private readonly DailyDownloadWorkerOptions _options;
    private readonly IConnectionFactory _connectionFactory;
    private readonly IServiceProvider _services;
    private readonly INuGetSearchService _nuGetSearchService;
    private readonly IClickHouseService _clickHouseService;
    private readonly NuGetAvailabilityState _availabilityState;
    private readonly ILogger<DailyDownloadWorker> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentBag<(IModel, IConnection)> _connections = new();

    private readonly List<Task> _workers;

    public DailyDownloadWorker(
        IOptions<DailyDownloadWorkerOptions> options,
        IConnectionFactory connectionFactory,
        IServiceProvider services,
        INuGetSearchService nuGetSearchService,
        IClickHouseService clickHouseService,
        NuGetAvailabilityState availabilityState,
        ILogger<DailyDownloadWorker> logger)
    {
        _options = options.Value;
        _connectionFactory = connectionFactory;
        _services = services;
        _nuGetSearchService = nuGetSearchService;
        _clickHouseService = clickHouseService;
        _availabilityState = availabilityState;
        _logger = logger;
        _workers = new List<Task>(_options.WorkerCount);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting the worker.");
        var startWorkerTransaction = SentrySdk.StartTransaction("start-daily-download-worker", "worker.start.trigger");
        startWorkerTransaction.SetTag("worker_count", _options.WorkerCount.ToString());
        var traceHeader = startWorkerTransaction.GetTraceHeader();
        try
        {
            for (var i = 0; i < _options.WorkerCount; i++)
            {
                _workers.Add(Task.Run(async () =>
                {
                    using var _ = SentrySdk.PushScope();
                    var startSpan = SentrySdk.StartTransaction("worker.start", "worker.start", traceHeader);
                    SentrySdk.ConfigureScope(s =>
                    {
                        s.Transaction = startSpan;
                        s.SetTag("worker_thread_id", Thread.CurrentThread.ManagedThreadId.ToString());
                    });

                    try
                    {
                        var attempt = 0;
                        while (true)
                        {
                            attempt++;
                            var connectSpan =
                                startSpan.StartChild("rabbit.mq.connect", "Connecting to RabbitMQ");
                            try
                            {
                                Connect();
                                connectSpan.Finish(SpanStatus.Ok);
                                break;
                            }
                            catch (Exception e)
                            {
                                connectSpan.Finish(e);
                                SentrySdk.CaptureException(e);

                                var waitMs = attempt >= 6 ? 60_000 : attempt * 10_000;
                                _logger.LogError(e,
                                    "Failed to connect to the broker. Waiting for '{waitMs}' milliseconds. Attempt '{attempts}'.",
                                    waitMs, attempt);
                                await Task.Delay(waitMs, _cancellationTokenSource.Token);
                            }
                        }
                        startSpan.Finish(SpanStatus.Ok);
                    }
                    catch (Exception e)
                    {
                        startSpan.Finish(e);
                        throw;
                    }
                }, _cancellationTokenSource.Token));
            }

            startWorkerTransaction.Finish();
        }
        catch (Exception e)
        {
            startWorkerTransaction.Finish(e);
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

        _logger.LogDebug("Queue creation OK with '{QueueName}', '{ConsumerCount}' and '{MessageCount}'",
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
            _logger.LogWarning("DefaultConsumer fired message '{message}'.", Convert.ToBase64String(e.Body.ToArray()));
        };

        channel.DefaultConsumer = defaultConsumer;
    }

    private async Task OnConsumerOnReceived(object sender, BasicDeliverEventArgs ea)
    {
        const string queueName = "daily-download";
        using var _ = _logger.BeginScope("OnConsumerOnReceived");

        // Extract distributed trace context from message headers if present
        string? sentryTraceHeader = null;
        string? baggageHeader = null;
        string? messageId = null;

        long? enqueuedTime = null;
        if (ea.BasicProperties?.Headers != null)
        {
            if (ea.BasicProperties.Headers.TryGetValue("sentry-trace", out var traceObj) &&
                traceObj is byte[] traceBytes)
            {
                sentryTraceHeader = System.Text.Encoding.UTF8.GetString(traceBytes);
            }

            if (ea.BasicProperties.Headers.TryGetValue("baggage", out var baggageObj) &&
                baggageObj is byte[] baggageBytes)
            {
                baggageHeader = System.Text.Encoding.UTF8.GetString(baggageBytes);
            }

            if (ea.BasicProperties.Headers.TryGetValue("message-id", out var messageIdObj) &&
                messageIdObj is byte[] messageIdBytes)
            {
                messageId = System.Text.Encoding.UTF8.GetString(messageIdBytes);
            }

            // Aligned with Python SDK header naming
            if (ea.BasicProperties.Headers.TryGetValue("sentry-task-enqueued-time", out var enqueuedTimeObj) &&
                enqueuedTimeObj is long timestamp)
            {
                enqueuedTime = timestamp;
            }
        }

        // Use ContinueTrace to properly link producer and consumer spans
        var transactionContext = SentrySdk.ContinueTrace(sentryTraceHeader, baggageHeader);

        // Create the outer transaction - use 'task' op since queue.process should be on the child span
        ITransactionTracer transaction;
        if (transactionContext != null)
        {
            // Continue the trace from the producer, setting our own name and operation
            var context = new TransactionContext(
                name: "daily-download-process",
                operation: "task",
                traceId: transactionContext.TraceId,
                parentSpanId: transactionContext.SpanId,
                isSampled: transactionContext.IsSampled);
            transaction = SentrySdk.StartTransaction(context);
        }
        else
        {
            transaction = SentrySdk.StartTransaction("daily-download-process", "task");
        }

        SentrySdk.ConfigureScope(s => s.Transaction = transaction);

        // Create the inner queue.process span per Sentry Queues module conventions
        var queueProcessSpan = transaction.StartChild("queue.process", queueName);

        // Set required Sentry Queues module attributes (use SetData for span data attributes)
        queueProcessSpan.SetData("messaging.system", "rabbitmq");
        queueProcessSpan.SetData("messaging.destination.name", queueName);

        // Set message ID (required for linking producer/consumer)
        if (messageId != null)
        {
            queueProcessSpan.SetData("messaging.message.id", messageId);
        }

        // Set optional attributes
        queueProcessSpan.SetData("messaging.message.body.size", ea.Body.Length);
        if (ea.Redelivered)
        {
            queueProcessSpan.SetData("messaging.message.retry.count", 1); // RabbitMQ doesn't track exact retry count
        }

        // Calculate and set queue latency (time between publish and receive)
        if (enqueuedTime.HasValue)
        {
            var receiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var latencyMs = receiveTimestamp - enqueuedTime.Value;
            queueProcessSpan.SetData("messaging.message.receive.latency", (double)latencyMs);
        }

        List<string>? packageIds = null;
        var consumer = (AsyncEventingBasicConsumer)sender;
        try
        {
            var body = ea.Body;
            _logger.LogDebug("Received message with body size '{size}'.", body.Length);
            SentrySdk.ConfigureScope(s => s.SetTag("msg_size", body.Length.ToString()));
            var deserializationSpan = queueProcessSpan.StartChild("serialize.msgpack.deserialize", "Deserialize MessagePack");
            packageIds = MessagePackSerializer.Deserialize<List<string>>(body);
            deserializationSpan.Finish(SpanStatus.Ok);

            if (packageIds == null)
            {
                throw new InvalidOperationException($"Deserializing '{body}' resulted in a null reference.");
            }

            var updateCountSpan = queueProcessSpan.StartChild("update.download.count", "Updates the DB with the current daily downloads");
            updateCountSpan.SetTag("packageIds", packageIds.Count.ToString());
            try
            {
                await UpdateDownloadCount(packageIds, updateCountSpan);
                updateCountSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                updateCountSpan.Finish(e);
                throw;
            }

            consumer.Model.BasicAck(ea.DeliveryTag, false);
            queueProcessSpan.Finish(SpanStatus.Ok);
            transaction.Finish(SpanStatus.Ok);
        }
        catch (NuGetUnavailableException e)
        {
            // NuGet is unavailable - NACK the message so it gets redelivered later
            // Don't report to Sentry as this is expected during outages
            _logger.LogWarning(e, "NuGet unavailable, requeueing batch of {Count} packages.", packageIds?.Count ?? 0);
            consumer.Model.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            queueProcessSpan.Finish(SpanStatus.Unavailable);
            transaction.Finish(SpanStatus.Unavailable);
        }
        catch (AggregateException ae) when (ae.InnerExceptions.Any(e => e is NuGetUnavailableException))
        {
            // Multiple GetPackage calls failed due to NuGet unavailability (Task.WhenAll wraps in AggregateException)
            // NACK the message so it gets redelivered later
            _logger.LogWarning(ae, "NuGet unavailable (multiple failures), requeueing batch of {Count} packages.", packageIds?.Count ?? 0);
            consumer.Model.BasicNack(ea.DeliveryTag, multiple: false, requeue: true);
            queueProcessSpan.Finish(SpanStatus.Unavailable);
            transaction.Finish(SpanStatus.Unavailable);
        }
        catch (Exception e)
        {
            if (packageIds != null)
            {
                e.Data["batch_size"] = packageIds.Count;
            }
            queueProcessSpan.Finish(e);
            transaction.Finish(e);
            _logger.LogCritical(e, "Failed to process batch.");
            throw;
        }
    }

    private async Task UpdateDownloadCount(IList<string> packageIds, ISpan parentSpan)
    {
        // Fail-fast if NuGet is known to be unavailable - no point starting a batch of requests
        if (!_availabilityState.IsAvailable)
        {
            throw new NuGetUnavailableException(
                $"NuGet API unavailable since {_availabilityState.UnavailableSince} - skipping batch of {packageIds.Count} packages");
        }

        var packageInfoQueue = parentSpan.StartChild("package.info.queue", "Start task to fetch package detail");
        var tasks = new List<Task<IPackageSearchMetadata?>>(packageIds.Count);
        foreach (var id in packageIds)
        {
            tasks.Add(_nuGetSearchService.GetPackage(id, _cancellationTokenSource.Token));
        }
        packageInfoQueue.Finish(SpanStatus.Ok);
        var waitSpan = parentSpan.StartChild("package.info.wait", "Await for all tasks");
        var whenAll = Task.WhenAll(tasks);
        try
        {
            await whenAll;
            waitSpan.Finish(SpanStatus.Ok);
        }
        catch when (whenAll.Exception is { InnerExceptions.Count: > 1 } exs)
        {
            waitSpan.Finish(exs);
            throw exs; // re-throw the AggregateException to capture all errors with Sentry
        }

        // Process API responses and prepare batch data
        var processDataSpan = parentSpan.StartChild("function", "Process NuGet API responses");
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);
        var clickHouseDownloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();
        var postgresUpserts = new List<PackageDownloadUpsert>();
        var deletedPackageIds = new List<string>();

        for (var i = 0; i < tasks.Count; i++)
        {
            var expectedPackageId = packageIds[i];
            var packageMetadata = tasks[i].Result;
            if (packageMetadata == null)
            {
                // All versions are unlisted or:
                // "This package has been deleted from the gallery. It is no longer available for install/restore."
                _logger.LogInformation("Package with id '{packageId}' deleted.", expectedPackageId);
                deletedPackageIds.Add(expectedPackageId);
            }
            else
            {
                // Collect for batch insert to ClickHouse
                if (packageMetadata.DownloadCount is { } downloadCount)
                {
                    clickHouseDownloads.Add((packageMetadata.Identity.Id, today, downloadCount));

                    // Collect for batch upsert to PostgreSQL
                    postgresUpserts.Add(new PackageDownloadUpsert(
                        PackageId: packageMetadata.Identity.Id,
                        DownloadCount: downloadCount,
                        CheckedUtc: now,
                        IconUrl: packageMetadata.IconUrl?.ToString()));
                }
            }
        }

        processDataSpan.SetData("packages_processed", tasks.Count);
        processDataSpan.SetData("packages_with_downloads", clickHouseDownloads.Count);
        processDataSpan.SetData("deleted_packages", deletedPackageIds.Count);
        processDataSpan.Finish(SpanStatus.Ok);

        using var scope = _services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();

        // Batch insert to ClickHouse (span created inside ClickHouseService for Sentry Queries module)
        if (clickHouseDownloads.Count > 0)
        {
            await _clickHouseService.InsertDailyDownloadsAsync(
                clickHouseDownloads,
                _cancellationTokenSource.Token,
                parentSpan);
        }

        // Batch upsert to PostgreSQL (single query instead of N SELECT + N INSERT/UPDATE)
        if (postgresUpserts.Count > 0)
        {
            var pgUpsertSpan = StartDbSpan(parentSpan, "INSERT INTO package_downloads ... ON CONFLICT DO UPDATE", "postgresql", "UPSERT");
            pgUpsertSpan.SetTag("count", postgresUpserts.Count.ToString());
            try
            {
                var rowsAffected = await context.UpsertPackageDownloadsAsync(postgresUpserts, _cancellationTokenSource.Token);
                pgUpsertSpan.SetData("db.rows_affected", rowsAffected);
                pgUpsertSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                pgUpsertSpan.Finish(e);
                throw;
            }
        }

        // Handle deleted packages (batch query to avoid N+1)
        if (deletedPackageIds.Count > 0)
        {
            var deleteSpan = StartDbSpan(parentSpan, "DELETE FROM package_details_catalog_leafs WHERE package_id IN (...)", "postgresql", "DELETE");
            deleteSpan.SetTag("count", deletedPackageIds.Count.ToString());
            try
            {
                var packagesToRemove = await context.PackageDetailsCatalogLeafs
                    .Where(p => p.PackageId != null && deletedPackageIds.Contains(p.PackageId))
                    .ToListAsync(_cancellationTokenSource.Token);

                if (packagesToRemove.Count > 0)
                {
                    context.PackageDetailsCatalogLeafs.RemoveRange(packagesToRemove);
                    await context.SaveChangesAsync(_cancellationTokenSource.Token);
                }

                deleteSpan.SetData("db.rows_affected", packagesToRemove.Count);
                deleteSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                deleteSpan.Finish(e);
                throw;
            }
        }
    }

    /// <summary>
    /// Creates a database span with query source attributes for Sentry's Queries module.
    /// </summary>
    private ISpan StartDbSpan(
        ISpan parent,
        string description,
        string dbSystem,
        string dbOperation,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var span = parent.StartChild("db.sql.execute", description);
        span.SetData("db.system", dbSystem);
        span.SetData("db.operation", dbOperation);
        TelemetryHelpers.SetQuerySource<DailyDownloadWorker>(span, filePath, memberName, lineNumber);
        return span;
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
