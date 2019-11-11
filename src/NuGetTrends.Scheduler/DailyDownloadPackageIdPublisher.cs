using System.Collections.Generic;
using System.Threading.Tasks;
using Hangfire;
using MessagePack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NuGetTrends.Data;
using RabbitMQ.Client;

namespace NuGetTrends.Scheduler
{
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    // ReSharper disable once ClassNeverInstantiated.Global - DI
    public class DailyDownloadPackageIdPublisher
    {
        private readonly IConnectionFactory _connectionFactory;
        private readonly NuGetTrendsContext _context;
        private readonly ILogger<DailyDownloadPackageIdPublisher> _logger;

        public DailyDownloadPackageIdPublisher(
            IConnectionFactory connectionFactory,
            NuGetTrendsContext context,
            ILogger<DailyDownloadPackageIdPublisher> logger)
        {
            _connectionFactory = connectionFactory;
            _context = context;
            _logger = logger;
        }

        public async Task Import(IJobCancellationToken token)
        {
            using (var connection = _connectionFactory.CreateConnection())
            using (var channel = connection.CreateModel())
            {
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

                var messageCount = 0;
                try
                {
                    using (var conn = _context.Database.GetDbConnection())
                    {
                        conn.Open();

                        // Insert some data
                        using (var cmd = new NpgsqlCommand("SELECT package_id FROM pending_packages_daily_downloads", (NpgsqlConnection)conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            var batchSize = 25; // TODO: Configurable
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

                            if (batch.Count != 0)
                            {
                                Queue(batch, channel, queueName, properties);
                            }
                        }
                    }
                }
                finally
                {
                    _logger.LogInformation("Finished publishing messages. Messages queued: {count}", messageCount);
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
}
