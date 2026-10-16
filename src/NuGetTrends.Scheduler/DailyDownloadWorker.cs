using System.Runtime.CompilerServices;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NuGet.Protocol.Core.Types;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Sentry;

namespace NuGetTrends.Scheduler;

// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadWorker : IHostedService
{
    internal const string QueueName = "daily-download";

    // Pause before reconnecting after the broker drops a consumer, so a broker that keeps cancelling doesn't spin the loop.
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    // With prefetch 1 a requeued message comes straight back: wait so it isn't spun through the broker during an outage.
    private static readonly TimeSpan NuGetUnavailableRequeueDelay = TimeSpan.FromSeconds(30);

    // How long a cancelled delivery gets to wind down before its consumer reconnects or the service stops.
    private static readonly TimeSpan InFlightDeliveryTimeout = TimeSpan.FromSeconds(30);

    private readonly DailyDownloadWorkerOptions _options;
    private readonly IConnectionFactory _connectionFactory;
    private readonly IServiceProvider _services;
    private readonly INuGetSearchService _nuGetSearchService;
    private readonly IClickHouseService _clickHouseService;
    private readonly NuGetAvailabilityState _availabilityState;
    private readonly ILogger<DailyDownloadWorker> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

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
                var workerIndex = i;
                _workers.Add(Task.Run(() => RunWorkerAsync(workerIndex, traceHeader, _cancellationTokenSource.Token)));
            }

            startWorkerTransaction.Finish();
        }
        catch (Exception e)
        {
            startWorkerTransaction.Finish(e);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Keeps one consumer connected until the service stops. RabbitMQ.Client doesn't recover a channel the broker
    /// closes (e.g. after consumer_timeout) or a consumer it cancels (e.g. queue deleted), and the process stays
    /// healthy without consumers. So every loss is reported as an error and the consumer reconnects.
    /// </summary>
    private async Task RunWorkerAsync(int workerIndex, SentryTraceHeader traceHeader, CancellationToken stoppingToken)
    {
        using var _ = SentrySdk.PushScope();
        SentrySdk.ConfigureScope(s => s.SetTag("worker_index", workerIndex.ToString()));

        var failedAttempts = 0;
        var connectedBefore = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            // Completed from the RabbitMQ client's threads, so continuations must not run inline there.
            var consumerLost = new TaskCompletionSource<ShutdownEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Cancels the delivery being processed when this consumer is lost or the service stops.
            using var processing = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var inFlight = new InFlightDelivery();
            IConnection? connection = null;
            try
            {
                var startSpan = SentrySdk.StartTransaction(connectedBefore ? "worker.reconnect" : "worker.start", "worker.start", traceHeader);
                SentrySdk.ConfigureScope(s => s.Transaction = startSpan);
                var connectSpan = startSpan.StartChild("rabbit.mq.connect", "Connecting to RabbitMQ");
                try
                {
                    connection = _connectionFactory.CreateConnection($"daily-download-worker-{workerIndex}");
                    Consume(connection, consumerLost, inFlight, processing.Token);
                    connectSpan.Finish(SpanStatus.Ok);
                    startSpan.Finish(SpanStatus.Ok);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    connectSpan.Finish(e);
                    startSpan.Finish(e);

                    failedAttempts++;
                    var waitMs = failedAttempts >= 6 ? 60_000 : failedAttempts * 10_000;
                    _logger.LogError(e,
                        "Failed to connect to the broker. Waiting for '{waitMs}' milliseconds. Attempt '{attempts}'.",
                        waitMs, failedAttempts);
                    await Task.Delay(waitMs, stoppingToken);
                    continue;
                }

                failedAttempts = 0;
                connectedBefore = true;

                ShutdownEventArgs reason;
                await using (stoppingToken.Register(() => consumerLost.TrySetCanceled(stoppingToken)))
                {
                    reason = await consumerLost.Task;
                }

                // The broker requeues the unacked delivery for another consumer: stop working on it here.
                processing.Cancel();
                _logger.LogError(new RabbitMqConsumerLostException(QueueName, reason),
                    "Consumer {WorkerIndex} on queue '{QueueName}' was lost. Reconnecting.", workerIndex, QueueName);
                await Task.Delay(ReconnectDelay, stoppingToken);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // Unexpected: keep the worker alive rather than silently leaving the queue with one consumer fewer.
                _logger.LogError(e, "Worker {WorkerIndex} failed unexpectedly. Reconnecting.", workerIndex);
                await Task.Delay(ReconnectDelay, CancellationToken.None);
            }
            finally
            {
                // Let the delivery being processed wind down before closing and reconnecting, so it never runs
                // alongside its own redelivery (that would insert into ClickHouse twice).
                processing.Cancel();
                await inFlight.WaitAsync(InFlightDeliveryTimeout);
                CloseConnection(connection);
            }
        }
    }

    /// <summary>
    /// The delivery a consumer is processing. With prefetch 1 there is at most one at a time.
    /// </summary>
    private sealed class InFlightDelivery
    {
        private Task _current = Task.CompletedTask;

        public void Set(Task task) => Volatile.Write(ref _current, task);

        public Task WaitAsync(TimeSpan timeout) => Task.WhenAny(Volatile.Read(ref _current), Task.Delay(timeout));
    }

    private void Consume(
        IConnection connection,
        TaskCompletionSource<ShutdownEventArgs> consumerLost,
        InFlightDelivery inFlight,
        CancellationToken processingToken)
    {
        var channel = connection.CreateModel();

        // Raised on the client's reader thread: only signal the worker loop from here.
        channel.ModelShutdown += (_, args) => consumerLost.TrySetResult(args);
        // Exceptions escaping the Received handler are otherwise swallowed by the client.
        channel.CallbackException += (_, args) =>
            _logger.LogError(args.Exception, "Unhandled exception in a RabbitMQ consumer callback.");

        // One unacked message per consumer: deliveries waiting in a client-side buffer count towards the
        // broker's consumer_timeout (30 minutes), after which it closes the channel.
        channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var queueDeclareOk = channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _logger.LogDebug("Queue creation OK with '{QueueName}', '{ConsumerCount}' and '{MessageCount}'",
            queueDeclareOk.QueueName, queueDeclareOk.ConsumerCount, queueDeclareOk.MessageCount);

        var consumer = new AsyncEventingBasicConsumer(channel);

        consumer.Received += (sender, ea) =>
        {
            var delivery = OnConsumerOnReceived(sender, ea, processingToken);
            inFlight.Set(delivery);
            return delivery;
        };
        // The broker can cancel a consumer without closing its channel, e.g. when the queue is deleted.
        consumer.ConsumerCancelled += (_, _) =>
        {
            consumerLost.TrySetResult(channel.CloseReason
                ?? new ShutdownEventArgs(ShutdownInitiator.Peer, 0, "Consumer cancelled by the broker"));
            return Task.CompletedTask;
        };

        channel.BasicConsume(
            queue: QueueName,
            autoAck: false,
            consumer: consumer);
    }

    private void CloseConnection(IConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        try
        {
            // "Disposing channel and connection objects is not enough, they must be explicitly closed with the API methods..."
            // https://www.rabbitmq.com/dotnet-api-guide.html
            // Closing the connection also closes its channel, unless the broker already did.
            if (connection.IsOpen)
            {
                connection.Close(TimeSpan.FromSeconds(5));
            }
            connection.Dispose();
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Failed to close the RabbitMQ connection.");
        }
    }

    /// <summary>
    /// Acks or nacks a delivery. The channel may have been closed while the batch was processed, in which case
    /// the broker already requeued the message.
    /// </summary>
    private void Settle(IModel channel, ulong deliveryTag, bool ack, bool requeue = false)
    {
        try
        {
            if (ack)
            {
                channel.BasicAck(deliveryTag, multiple: false);
            }
            else
            {
                channel.BasicNack(deliveryTag, multiple: false, requeue: requeue);
            }
        }
        catch (AlreadyClosedException e)
        {
            _logger.LogWarning(e, "Channel closed before delivery {DeliveryTag} could be settled; the broker requeues it.", deliveryTag);
        }
    }

    private static async Task DelayBeforeRequeueAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(NuGetUnavailableRequeueDelay, token);
        }
        catch (OperationCanceledException)
        {
            // Stopping: requeue right away.
        }
    }

    private async Task OnConsumerOnReceived(object sender, BasicDeliverEventArgs ea, CancellationToken token)
    {
        const string queueName = QueueName;
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

            SentrySdk.Experimental.Metrics.EmitDistribution<double>("worker.queue_latency", (double)latencyMs,
                MeasurementUnit.Duration.Millisecond,
                [new("queue", queueName)]);
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
                await UpdateDownloadCount(packageIds, ea.Redelivered, updateCountSpan, token);
                updateCountSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception e)
            {
                updateCountSpan.Finish(e);
                throw;
            }

            Settle(consumer.Model, ea.DeliveryTag, ack: true);

            SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.messages_processed", 1,
                [new("queue", queueName)]);
            SentrySdk.Experimental.Metrics.EmitDistribution<int>("worker.batch_size", packageIds.Count,
                MeasurementUnit.None, [new("queue", queueName)]);

            queueProcessSpan.Finish(SpanStatus.Ok);
            transaction.Finish(SpanStatus.Ok);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Stopping, or the consumer was lost, mid-batch (cancellation may surface wrapped, e.g. by Npgsql or
            // HttpClient): put the message back for the next consumer. Not an error.
            Settle(consumer.Model, ea.DeliveryTag, ack: false, requeue: true);
            queueProcessSpan.Finish(SpanStatus.Cancelled);
            transaction.Finish(SpanStatus.Cancelled);
        }
        catch (NuGetUnavailableException e)
        {
            // NuGet is unavailable - NACK the message so it gets redelivered later
            // Don't report to Sentry as this is expected during outages
            _logger.LogWarning(e, "NuGet unavailable, requeueing batch of {Count} packages.", packageIds?.Count ?? 0);
            await DelayBeforeRequeueAsync(token);
            Settle(consumer.Model, ea.DeliveryTag, ack: false, requeue: true);
            SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.messages_requeued", 1,
                [new("queue", queueName), new("reason", "nuget_unavailable")]);
            queueProcessSpan.Finish(SpanStatus.Unavailable);
            transaction.Finish(SpanStatus.Unavailable);
        }
        catch (AggregateException ae) when (ae.InnerExceptions.Any(e => e is NuGetUnavailableException))
        {
            // Multiple GetPackage calls failed due to NuGet unavailability (Task.WhenAll wraps in AggregateException)
            // NACK the message so it gets redelivered later
            _logger.LogWarning(ae, "NuGet unavailable (multiple failures), requeueing batch of {Count} packages.", packageIds?.Count ?? 0);
            await DelayBeforeRequeueAsync(token);
            Settle(consumer.Model, ea.DeliveryTag, ack: false, requeue: true);
            SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.messages_requeued", 1,
                [new("queue", queueName), new("reason", "nuget_unavailable")]);
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

            // Never leave the delivery unacked: it would hold the consumer's only prefetch slot until the broker
            // closes the channel (consumer_timeout), silently killing the consumer. Drop rather than requeue:
            // the batch may be partially written to ClickHouse and weekly_downloads can't deduplicate a retry.
            // Its packages stay unchecked and are queued again by the next publisher run.
            Settle(consumer.Model, ea.DeliveryTag, ack: false, requeue: false);
            SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.messages_failed", 1,
                [new("queue", queueName)]);
        }
    }

    private async Task UpdateDownloadCount(IList<string> packageIds, bool redelivered, ISpan parentSpan, CancellationToken token)
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
            tasks.Add(_nuGetSearchService.GetPackage(id, token));
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
                var downloadCount = packageMetadata.DownloadCount ?? 0;

                // Collect for batch insert to ClickHouse
                clickHouseDownloads.Add((packageMetadata.Identity.Id, today, downloadCount));

                // Collect for batch upsert to PostgreSQL
                postgresUpserts.Add(new PackageDownloadUpsert(
                    PackageId: packageMetadata.Identity.Id,
                    DownloadCount: downloadCount,
                    CheckedUtc: now,
                    IconUrl: packageMetadata.IconUrl?.ToString()));
            }
        }

        processDataSpan.SetData("packages_processed", tasks.Count);
        processDataSpan.SetData("packages_with_downloads", clickHouseDownloads.Count);
        processDataSpan.SetData("deleted_packages", deletedPackageIds.Count);
        processDataSpan.Finish(SpanStatus.Ok);

        SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.packages_processed", clickHouseDownloads.Count);
        if (deletedPackageIds.Count > 0)
        {
            SentrySdk.Experimental.Metrics.EmitCounter<int>("worker.packages_deleted", deletedPackageIds.Count);
        }

        using var scope = _services.CreateScope();
        await using var context = scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();

        // A redelivered batch may already be partly in ClickHouse: the previous attempt can be interrupted after the
        // insert (deploy, lost consumer). Inserting again would double-count weekly_downloads, which can't deduplicate.
        if (redelivered && clickHouseDownloads.Count > 0)
        {
            var alreadyInserted = await _clickHouseService.GetPackageIdsWithDailyDownloadsAsync(
                clickHouseDownloads.Select(d => d.PackageId).ToList(), today, token, parentSpan);
            var skipped = clickHouseDownloads.RemoveAll(d => alreadyInserted.Contains(d.PackageId.ToLowerInvariant()));
            parentSpan.SetData("redelivery_skipped_inserts", skipped);
        }

        // Batch insert to ClickHouse (span created inside ClickHouseService for Sentry Queries module)
        if (clickHouseDownloads.Count > 0)
        {
            await _clickHouseService.InsertDailyDownloadsAsync(
                clickHouseDownloads,
                token,
                parentSpan);
        }

        // Batch upsert to PostgreSQL (single query instead of N SELECT + N INSERT/UPDATE)
        if (postgresUpserts.Count > 0)
        {
            var pgUpsertSpan = StartDbSpan(parentSpan, "INSERT INTO package_downloads ... ON CONFLICT DO UPDATE", "postgresql", "UPSERT");
            pgUpsertSpan.SetTag("count", postgresUpserts.Count.ToString());
            try
            {
                var rowsAffected = await context.UpsertPackageDownloadsAsync(postgresUpserts, token);
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
            var loweredIds = deletedPackageIds.Select(id => id.ToLowerInvariant()).Distinct().ToList();

            var deleteSpan = StartDbSpan(parentSpan,
                "DELETE FROM package_details_catalog_leafs, package_downloads WHERE package_id_lowered IN (...)",
                "postgresql", "DELETE");
            deleteSpan.SetTag("count", deletedPackageIds.Count.ToString());
            try
            {
                await using var transaction = await context.Database.BeginTransactionAsync(token);

                var deletedCatalogCount = await context.PackageDetailsCatalogLeafs
                    .Where(p => loweredIds.Contains(p.PackageIdLowered))
                    .ExecuteDeleteAsync(token);

                // Also remove stale package_downloads records so the publisher
                // doesn't keep re-queuing packages that no longer exist.
                var deletedDownloadsCount = await context.PackageDownloads
                    .Where(p => loweredIds.Contains(p.PackageIdLowered))
                    .ExecuteDeleteAsync(token);

                await transaction.CommitAsync(token);

                deleteSpan.SetData("db.catalog_rows_affected", deletedCatalogCount);
                deleteSpan.SetData("db.downloads_rows_affected", deletedDownloadsCount);
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

            // Each worker closes its own connection on the way out.
            await Task.WhenAll(_workers).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shutdown timed out before all workers stopped.");
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed canceling the worker.");
        }
    }
}
