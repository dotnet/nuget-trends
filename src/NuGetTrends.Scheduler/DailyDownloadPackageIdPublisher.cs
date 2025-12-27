using System.Globalization;
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

                // Get all package IDs from PostgreSQL
                var dbQuerySpan = queueIdsSpan.StartChild("db.query", "Get all package IDs from PostgreSQL");
                var allPackages = await context.PackageDetailsCatalogLeafs
                    .Select(p => p.PackageId)
                    .Distinct()
                    .ToListAsync();
                dbQuerySpan.SetTag("count", allPackages.Count.ToString());
                dbQuerySpan.Finish();

                // Get packages already processed today from ClickHouse
                var chQuerySpan = queueIdsSpan.StartChild("clickhouse.query", "Get packages processed today");
                var processedToday = await clickHouseService.GetPackagesWithDownloadsForDateAsync(
                    DateOnly.FromDateTime(DateTime.UtcNow));
                chQuerySpan.SetTag("count", processedToday.Count.ToString());
                chQuerySpan.Finish();

                // Find pending packages (case-insensitive comparison since ClickHouse stores lowercase)
                var pendingSpan = queueIdsSpan.StartChild("filter.pending", "Filter pending packages");
                var pending = allPackages
                    .Where(p => p != null && !processedToday.Contains(p.ToLower(CultureInfo.InvariantCulture)))
                    .ToList();
                pendingSpan.SetTag("count", pending.Count.ToString());
                pendingSpan.Finish();

                var batchSize = 25; // TODO: Configurable
                var processBatchSpan = queueIdsSpan.StartChild("queue.batch",
                    $"Queue pending packages in batches of '{batchSize}' ids.");
                var batch = new List<string>(batchSize);
                foreach (var packageId in pending)
                {
                    messageCount++;
                    batch.Add(packageId!);

                    if (batch.Count == batchSize)
                    {
                        Queue(batch, channel, queueName, properties);
                        batch.Clear();
                    }
                }
                processBatchSpan.Finish();

                if (batch.Count != 0)
                {
                    var queueSpan = queueIdsSpan.StartChild("queue.enqueue", "Enqueue incomplete batch.");
                    queueSpan.SetTag("count", batch.Count.ToString());
                    Queue(batch, channel, queueName, properties);
                    queueSpan.Finish();
                }

                queueIdsSpan.SetTag("queue-name", queueName);
                queueIdsSpan.SetTag("batch-size", batchSize.ToString());
                queueIdsSpan.SetTag("message-count", messageCount.ToString());
                queueIdsSpan.Finish();
            }
            finally
            {
                logger.LogInformation("Finished publishing messages. '{count}' messages queued.", messageCount);
            }
            transaction.Finish(SpanStatus.Ok);
        }
        catch (Exception e)
        {
            transaction.Finish(e);
            throw;
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
