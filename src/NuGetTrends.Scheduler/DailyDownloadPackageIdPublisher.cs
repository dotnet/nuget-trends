using System.Runtime.CompilerServices;
using Hangfire;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using RabbitMQ.Client;
using Sentry.Hangfire;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
// ReSharper disable once ClassNeverInstantiated.Global - DI
public class DailyDownloadPackageIdPublisher(
    IConnectionFactory connectionFactory,
    NuGetTrendsContext context,
    IHub hub,
    ILogger<DailyDownloadPackageIdPublisher> logger)
{
    // Batch size for streaming from PostgreSQL
    private const int BatchSize = 10_000;

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
                logger.LogInformation("Finished publishing messages. Queued {QueuedCount} packages for download.", messageCount);
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
    /// Streams package IDs from PostgreSQL that haven't been checked today.
    /// Uses a LEFT JOIN to filter out packages where LatestDownloadCountCheckedUtc is today.
    /// </summary>
    private async IAsyncEnumerable<string> GetUnprocessedPackageIdsAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;

        // Get distinct package IDs from catalog that either:
        // 1. Don't exist in package_downloads yet (new packages), OR
        // 2. Were last checked before today
        var query = from leaf in context.PackageDetailsCatalogLeafs
            join pd in context.PackageDownloads
                on leaf.PackageId!.ToLower() equals pd.PackageIdLowered into downloads
            from pd in downloads.DefaultIfEmpty()
            where pd == null || pd.LatestDownloadCountCheckedUtc < todayUtc
            select leaf.PackageId;

        await foreach (var packageId in query.Distinct().AsAsyncEnumerable().WithCancellation(ct))
        {
            if (packageId != null)
            {
                yield return packageId;
            }
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
