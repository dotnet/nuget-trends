using Hangfire;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using RabbitMQ.Client;
using Sentry.Hangfire;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadPackageIdPublisher(
    IConnectionFactory connectionFactory,
    NuGetTrendsContext context,
    IClickHouseService clickHouseService,
    IHub hub,
    ILogger<DailyDownloadPackageIdPublisher> logger)
{
    // Batch size for checking against ClickHouse (memory efficient)
    private const int ClickHouseBatchSize = 10_000;

    // Batch size for RabbitMQ messages
    private const int QueueBatchSize = 25;

    [SentryMonitorSlug("DailyDownloadPackageIdPublisher.Import")]
    public async Task Import(IJobCancellationToken token)
    {
        using var _ = hub.PushScope();
        var transaction = hub.StartTransaction("daily-download-pkg-id-publisher", "queue.write",
            "queues package ids to fetch download numbers");
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

            logger.LogDebug("Queue creation OK, with '{QueueName}', '{ConsumerCount}', '{MessageCount}'",
                queueDeclareOk.QueueName, queueDeclareOk.ConsumerCount, queueDeclareOk.MessageCount);

            var properties = channel.CreateBasicProperties();
            properties.Persistent = true;
            properties.Expiration = "43200000";
            connectionSpan.Finish();

            var messageCount = 0;
            var totalPackagesChecked = 0;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            try
            {
                var queueIdsSpan = transaction.StartChild("queue.ids");

                // Stream package IDs from PostgreSQL in batches to avoid loading all into memory
                var dbStreamSpan = queueIdsSpan.StartChild("db.stream", "Stream package IDs from PostgreSQL");

                var queueBatch = new List<string>(QueueBatchSize);

                await foreach (var packageBatch in GetPackageIdBatchesAsync(ClickHouseBatchSize, token.ShutdownToken))
                {
                    totalPackagesChecked += packageBatch.Count;

                    // Check which packages from this batch are not yet processed in ClickHouse
                    var unprocessed = await clickHouseService.GetUnprocessedPackagesAsync(
                        packageBatch, today, token.ShutdownToken);

                    // Queue unprocessed packages
                    foreach (var packageId in unprocessed)
                    {
                        messageCount++;
                        queueBatch.Add(packageId);

                        if (queueBatch.Count == QueueBatchSize)
                        {
                            Queue(queueBatch, channel, queueName, properties);
                            queueBatch.Clear();
                        }
                    }
                }

                dbStreamSpan.SetTag("total-checked", totalPackagesChecked.ToString());
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
                queueIdsSpan.SetTag("clickhouse-batch-size", ClickHouseBatchSize.ToString());
                queueIdsSpan.SetTag("queue-batch-size", QueueBatchSize.ToString());
                queueIdsSpan.SetTag("total-checked", totalPackagesChecked.ToString());
                queueIdsSpan.SetTag("message-count", messageCount.ToString());
                queueIdsSpan.Finish();
            }
            finally
            {
                logger.LogInformation(
                    "Finished publishing messages. Checked {TotalPackages} packages, queued {QueuedCount} for download.",
                    totalPackagesChecked, messageCount);
            }
            transaction.Finish(SpanStatus.Ok);
        }
        catch (Exception e)
        {
            transaction.Finish(e);
            throw;
        }
    }

    /// <summary>
    /// Streams package IDs from PostgreSQL in batches to avoid loading all into memory.
    /// </summary>
    private async IAsyncEnumerable<List<string>> GetPackageIdBatchesAsync(
        int batchSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var batch = new List<string>(batchSize);

        await foreach (var packageId in context.PackageDetailsCatalogLeafs
            .Select(p => p.PackageId)
            .Distinct()
            .AsAsyncEnumerable()
            .WithCancellation(ct))
        {
            if (packageId == null)
            {
                continue;
            }

            batch.Add(packageId);

            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<string>(batchSize);
            }
        }

        if (batch.Count > 0)
        {
            yield return batch;
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
