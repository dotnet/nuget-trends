using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;
using Sentry;

namespace NuGetTrends.Data.ClickHouse;

public class ClickHouseService : IClickHouseService
{
    private readonly string _connectionString;
    private readonly ILogger<ClickHouseService> _logger;
    private readonly ClickHouseConnectionInfo _connectionInfo;

    public ClickHouseService(string connectionString, ILogger<ClickHouseService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
        _connectionInfo = ParseConnectionString(connectionString);
    }

    public async Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        var downloadList = downloads.ToList();
        if (downloadList.Count == 0)
        {
            return;
        }

        const string queryDescription = "INSERT INTO daily_downloads (package_id, date, download_count) VALUES (?, ?, ?)";
        var span = StartDatabaseSpan(parentSpan, queryDescription, "INSERT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var bulkCopy = new ClickHouseBulkCopy(connection)
            {
                DestinationTableName = "daily_downloads",
                ColumnNames = ["package_id", "date", "download_count"],
                BatchSize = downloadList.Count
            };

            var data = downloadList.Select(d => new object[]
            {
                d.PackageId.ToLower(CultureInfo.InvariantCulture),
                d.Date,
                (ulong)d.DownloadCount
            });

            await bulkCopy.InitAsync();
            await bulkCopy.WriteToServerAsync(data, ct);

            span?.SetExtra("db.rows_affected", downloadList.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Inserted {Count} daily downloads to ClickHouse", downloadList.Count);
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId,
        int months,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        const string query = """
            SELECT
                toMonday(date) AS week,
                avg(download_count) AS download_count
            FROM daily_downloads
            WHERE package_id = {packageId:String}
              AND date >= today() - INTERVAL {months:Int32} MONTH
            GROUP BY week
            ORDER BY week
            """;

        // Parameterize the query for Sentry (replace ClickHouse {param:Type} with ?)
        var queryDescription = ParameterizeQuery(query);
        var span = StartDatabaseSpan(parentSpan, queryDescription, "SELECT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = query;

            var packageIdParam = cmd.CreateParameter();
            packageIdParam.ParameterName = "packageId";
            packageIdParam.Value = packageId.ToLower(CultureInfo.InvariantCulture);
            cmd.Parameters.Add(packageIdParam);

            var monthsParam = cmd.CreateParameter();
            monthsParam.ParameterName = "months";
            monthsParam.Value = months;
            cmd.Parameters.Add(monthsParam);

            var results = new List<DailyDownloadResult>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                results.Add(new DailyDownloadResult
                {
                    Week = reader.GetDateTime(0),
                    Count = reader.IsDBNull(1) ? null : (long?)reader.GetDouble(1)
                });
            }

            span?.SetExtra("db.rows_affected", results.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Retrieved {Count} weekly download results for package {PackageId}", results.Count, packageId);
            return results;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    /// <summary>
    /// Converts ClickHouse parameterized query syntax to Sentry-compatible format.
    /// Replaces {paramName:Type} with ? placeholder.
    /// </summary>
    private static string ParameterizeQuery(string query)
    {
        // Replace ClickHouse parameter syntax {name:Type} with ?
        return System.Text.RegularExpressions.Regex.Replace(
            query,
            @"\{[^}]+\}",
            "?");
    }

    /// <summary>
    /// Starts a database span following Sentry's Queries module conventions.
    /// </summary>
    private ISpan? StartDatabaseSpan(ISpan? parentSpan, string queryDescription, string operation)
    {
        var span = parentSpan?.StartChild("db", queryDescription)
                   ?? SentrySdk.GetSpan()?.StartChild("db", queryDescription);

        if (span == null)
        {
            return null;
        }

        // Required for Sentry Queries module
        span.SetExtra("db.system", "clickhouse");

        // Recommended attributes for better insights
        span.SetExtra("db.operation", operation);

        if (_connectionInfo.Database is not null)
        {
            span.SetExtra("db.name", _connectionInfo.Database);
        }

        if (_connectionInfo.Host is not null)
        {
            span.SetExtra("server.address", _connectionInfo.Host);
        }

        if (_connectionInfo.Port is not null)
        {
            span.SetExtra("server.port", _connectionInfo.Port);
        }

        return span;
    }

    /// <summary>
    /// Parses connection string to extract host, port, and database for span attributes.
    /// </summary>
    private static ClickHouseConnectionInfo ParseConnectionString(string connectionString)
    {
        var info = new ClickHouseConnectionInfo();

        // ClickHouse connection strings can be in different formats:
        // 1. Key=Value format: "Host=localhost;Port=8123;Database=default"
        // 2. URI format: "http://localhost:8123/default"

        if (connectionString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // URI format
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                info.Host = uri.Host;
                info.Port = uri.Port.ToString();
                if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/")
                {
                    info.Database = uri.AbsolutePath.TrimStart('/');
                }
            }
        }
        else
        {
            // Key=Value format
            var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var keyValue = part.Split('=', 2);
                if (keyValue.Length != 2)
                {
                    continue;
                }

                var key = keyValue[0].Trim();
                var value = keyValue[1].Trim();

                if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Server", StringComparison.OrdinalIgnoreCase))
                {
                    info.Host = value;
                }
                else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                {
                    info.Port = value;
                }
                else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase))
                {
                    info.Database = value;
                }
            }
        }

        return info;
    }

    private sealed class ClickHouseConnectionInfo
    {
        public string? Host { get; set; }
        public string? Port { get; set; }
        public string? Database { get; set; }
    }
}
