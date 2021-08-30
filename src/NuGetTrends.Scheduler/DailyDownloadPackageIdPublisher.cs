using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hangfire;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NuGetTrends.Data;
using RabbitMQ.Client;
using Sentry;

namespace NuGetTrends.Scheduler
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    // ReSharper disable once ClassNeverInstantiated.Global - DI
    public class DailyDownloadPackageIdPublisher
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly NuGetTrendsContext _context;
        private readonly IHub _hub;
        private readonly ILogger<DailyDownloadPackageIdPublisher> _logger;

        public DailyDownloadPackageIdPublisher(
            IConnectionFactory connectionFactory,
            NuGetTrendsContext context,
            IHub hub,
            ILogger<DailyDownloadPackageIdPublisher> logger)
        {
            _connectionFactory = connectionFactory;
            _context = context;
            _hub = hub;
            _logger = logger;
        }

        public async Task Import(IJobCancellationToken token)
        {
            using var _ = _hub.PushScope();
            var transaction = _hub.StartTransaction("daily-download-pkg-id-publisher", "queue.write",
                "queues package ids to fetch download numbers");
            try
            {
                var connectionSpan = transaction.StartChild("queue.connect", "Connect to RabbitMQ");
                using var connection = _connectionFactory.CreateConnection();
                using var channel = connection.CreateModel();
                const string queueName = "daily-download";

                var queueDeclareOk = channel.QueueDeclare(
                    queue: queueName,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null);

                _logger.LogDebug("Queue creation OK with {QueueName}, {ConsumerCount}, {MessageCount}",
                    queueDeclareOk.QueueName, queueDeclareOk.ConsumerCount, queueDeclareOk.MessageCount);

                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.Expiration = "43200000";
                connectionSpan.Finish();

                var messageCount = 0;
                try
                {
                    var queueIdsSpan = transaction.StartChild("queue.ids");
                    var dbConnectSpan = queueIdsSpan.StartChild("db.connect", "Connect to Postgres");
                    await using var conn = _context.Database.GetDbConnection();
                    await conn.OpenAsync();
                    dbConnectSpan.Finish();

                    const string queryIds = "SELECT package_id FROM pending_packages_daily_downloads";
                    var dbQuerySpan = queueIdsSpan.StartChild("db.query", queryIds);
                    await using var cmd = new NpgsqlCommand(queryIds, (NpgsqlConnection)conn);
                    await using var reader = await cmd.ExecuteReaderAsync();
                    dbQuerySpan.Finish();

                    var batchSize = 25; // TODO: Configurable
                    var processBatchSpan = queueIdsSpan.StartChild("db.read",
                        $"Go through reader and queue in batches of {batchSize} ids");
                    var batch = new List<string>(batchSize);
                    while (await reader.ReadAsync())
                    {
                        messageCount++;
                        batch.Add((string)reader[0]);

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
                    _logger.LogInformation("Finished publishing messages. Messages queued: {count}", messageCount);
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
}
