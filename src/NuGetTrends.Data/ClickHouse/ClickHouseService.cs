using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Microsoft.Extensions.Logging;
using Sentry;

namespace NuGetTrends.Data.ClickHouse;

/// <summary>
/// Parsed ClickHouse connection information for Sentry span attributes.
/// Register as singleton to avoid re-parsing connection string on each request.
/// </summary>
public sealed class ClickHouseConnectionInfo
{
    public string? Host { get; init; }
    public string? Port { get; init; }
    public string? Database { get; init; }

    /// <summary>
    /// Parses a ClickHouse connection string to extract host, port, and database.
    /// Supports both URI format (http://host:port/db) and Key=Value format (Host=x;Port=y;Database=z).
    /// </summary>
    public static ClickHouseConnectionInfo Parse(string connectionString)
    {
        // ClickHouse connection strings can be in different formats:
        // 1. Key=Value format: "Host=localhost;Port=8123;Database=default"
        // 2. URI format: "http://localhost:8123/default"

        if (connectionString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            connectionString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // URI format
            if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri))
            {
                return new ClickHouseConnectionInfo
                {
                    Host = uri.Host,
                    Port = uri.Port.ToString(),
                    Database = !string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/"
                        ? uri.AbsolutePath.TrimStart('/')
                        : null
                };
            }
        }

        // Key=Value format
        string? host = null, port = null, database = null;
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
                host = value;
            }
            else if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
            {
                port = value;
            }
            else if (key.Equals("Database", StringComparison.OrdinalIgnoreCase))
            {
                database = value;
            }
        }

        return new ClickHouseConnectionInfo { Host = host, Port = port, Database = database };
    }
}

public class ClickHouseService : IClickHouseService
{
    private readonly string _connectionString;
    private readonly ILogger<ClickHouseService> _logger;
    private readonly ClickHouseConnectionInfo _connectionInfo;

    public ClickHouseService(
        string connectionString,
        ILogger<ClickHouseService> logger,
        ClickHouseConnectionInfo connectionInfo)
    {
        _connectionString = connectionString;
        _logger = logger;
        _connectionInfo = connectionInfo;
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

        var span = StartDatabaseSpan(parentSpan, query, "SELECT");

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
}
