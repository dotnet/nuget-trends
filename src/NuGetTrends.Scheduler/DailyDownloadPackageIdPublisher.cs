using System.Runtime.CompilerServices;
using Hangfire;
using Hangfire.Server;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using RabbitMQ.Client;
using Sentry.Hangfire;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
// Retries disabled: If this job fails and retries while messages are still in RabbitMQ,
// duplicate package IDs would be queued. The weekly_downloads MV uses AggregatingMergeTree
// which cannot deduplicate, so duplicates would inflate weekly averages. Manual rerun is
// preferred over automatic retry to allow time for the queue to drain first.
[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadPackageIdPublisher(
    IConnectionFactory connectionFactory,
    NuGetTrendsContext dbContext,
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
    private const int QueueBatchSize = 1000;

    [SentryMonitorSlug("DailyDownloadPackageIdPublisher.Import")]
    public async Task Import(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        // Start a new, independent transaction with its own trace ID
        // This ensures daily download publishing is not linked to other jobs' traces
        using var _ = hub.PushScope();
        var transactionContext = new TransactionContext(
            name: "daily-download-pkg-id-publisher",
            operation: "job",
            traceId: SentryId.Create(),
            spanId: SpanId.Create(),
            parentSpanId: null,
            isSampled: true);
        var transaction = hub.StartTransaction(transactionContext);
        hub.ConfigureScope(s =>
        {
            s.Transaction = transaction;
            s.SetTag("jobId", jobId);
        });

        try
        {
            if (!await ImportLock.WaitAsync(TimeSpan.Zero))
            {
                logger.LogWarning("Job {JobId}: Skipping daily download publisher - another instance is already in progress", jobId);
                transaction.Finish(SpanStatus.Aborted);
                throw new ConcurrentExecutionSkippedException(
                    $"Job {jobId}: Daily download publisher skipped - another instance is already in progress");
            }

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
                connectionSpan.Finish(SpanStatus.Ok);

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
                            Queue(queueBatch, channel, queueName, properties, queueIdsSpan);
                            queueBatch.Clear();
                        }
                    }

                    dbStreamSpan.Finish(SpanStatus.Ok);

                    // Queue remaining packages
                    if (queueBatch.Count != 0)
                    {
                        Queue(queueBatch, channel, queueName, properties, queueIdsSpan);
                    }

                    queueIdsSpan.SetTag("queue-name", queueName);
                    queueIdsSpan.SetTag("batch-size", BatchSize.ToString());
                    queueIdsSpan.SetTag("queue-batch-size", QueueBatchSize.ToString());
                    queueIdsSpan.SetTag("message-count", messageCount.ToString());
                    queueIdsSpan.Finish(SpanStatus.Ok);
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
            finally
            {
                ImportLock.Release();
            }
        }
        finally
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Streams package IDs from PostgreSQL that haven't been checked today.
    /// </summary>
    private async IAsyncEnumerable<string> GetUnprocessedPackageIdsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;

        await foreach (var packageId in dbContext.GetUnprocessedPackageIds(todayUtc)
                           .AsAsyncEnumerable()
                           .WithCancellation(ct))
        {
            yield return packageId;
        }
    }

    private void Queue(
        List<string> batch,
        IModel channel,
        string queueName,
        IBasicProperties properties,
        ISpan? parentSpan)
    {
        var serializedBatch = MessagePackSerializer.Serialize(batch);

        // Create a queue.publish span following Sentry Queues module conventions
        var publishSpan = parentSpan?.StartChild("queue.publish", queueName)
                          ?? hub.GetSpan()?.StartChild("queue.publish", queueName);

        if (publishSpan != null)
        {
            // Generate a unique message ID using the span ID
            var messageId = publishSpan.SpanId.ToString();

            // Required attributes for Sentry Queues module (use SetData for span data attributes)
            publishSpan.SetData("messaging.message.id", messageId);
            publishSpan.SetData("messaging.destination.name", queueName);
            publishSpan.SetData("messaging.system", "rabbitmq");

            // Optional but useful attributes
            publishSpan.SetData("messaging.message.body.size", serializedBatch.Length);

            // Inject trace context into message headers for distributed tracing
            properties.Headers ??= new Dictionary<string, object>();
            properties.Headers["sentry-trace"] = publishSpan.GetTraceHeader().ToString();
            properties.Headers["message-id"] = messageId; // Pass message ID to consumer
            // Enqueue timestamp for latency calculation (aligned with Python SDK naming)
            properties.Headers["sentry-task-enqueued-time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (hub.GetBaggage() is { } baggage)
            {
                properties.Headers["baggage"] = baggage.ToString();
            }
        }

        try
        {
            channel.BasicPublish(
                exchange: string.Empty,
                routingKey: queueName,
                basicProperties: properties,
                body: serializedBatch);

            publishSpan?.Finish(SpanStatus.Ok);
        }
        catch (Exception ex)
        {
            publishSpan?.Finish(ex);
            throw;
        }
    }
}
