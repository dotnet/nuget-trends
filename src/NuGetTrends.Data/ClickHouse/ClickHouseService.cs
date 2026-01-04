using System.Globalization;
using System.Runtime.CompilerServices;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Copy;
using Microsoft.Extensions.Logging;

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
    /// Supports URI formats (http://, https://, clickhouse://, tcp://) and Key=Value format.
    /// </summary>
    public static ClickHouseConnectionInfo Parse(string connectionString)
    {
        // ClickHouse connection strings can be in different formats:
        // 1. Key=Value format: "Host=localhost;Port=8123;Database=default"
        // 2. HTTP URI format: "http://localhost:8123/default" or "https://..."
        // 3. Binary protocol URI: "clickhouse://localhost:9000/default" or "tcp://..."

        // Try URI parsing for any scheme
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) &&
            !string.IsNullOrEmpty(uri.Host))
        {
            // Extract database from path (e.g., /default -> default)
            var uriDatabase = !string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/"
                ? uri.AbsolutePath.TrimStart('/').Split('?')[0] // Remove query string if present
                : null;

            // Handle empty database after trimming
            if (string.IsNullOrEmpty(uriDatabase))
            {
                uriDatabase = null;
            }

            return new ClickHouseConnectionInfo
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port.ToString() : null,
                Database = uriDatabase
            };
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

        // Bulk copy operation - describe the table and columns being inserted
        const string bulkInsertDescription = "INSERT INTO daily_downloads (package_id, date, download_count)";
        var span = StartDatabaseSpan(parentSpan, bulkInsertDescription, "INSERT");

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

            span?.SetData("db.rows_affected", downloadList.Count);

            // Add package IDs for traceability (limit to avoid huge payloads)
            const int maxPackageIdsToLog = 10;
            var packageIds = downloadList.Select(d => d.PackageId).Take(maxPackageIdsToLog).ToList();
            span?.SetData("package_ids", string.Join(", ", packageIds));
            if (downloadList.Count > maxPackageIdsToLog)
            {
                span?.SetData("package_ids_truncated", true);
            }

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
        // Query the pre-aggregated weekly_downloads table (populated by MV)
        // Uses avgMerge() to finalize the pre-computed aggregate state
        // GROUP BY is still needed as ClickHouse may not have merged all parts yet,
        // but this is now a trivial merge of aggregate states, not full aggregation
        const string query = """
            SELECT
                week,
                avgMerge(download_avg) AS download_count
            FROM weekly_downloads
            WHERE package_id = {packageId:String}
              AND week >= toMonday(today() - INTERVAL {months:Int32} MONTH)
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

            span?.SetData("db.rows_affected", results.Count);
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

    public async Task<List<TrendingPackage>> GetTrendingPackagesAsync(
        int limit = 10,
        long minWeeklyDownloads = 1000,
        int maxPackageAgeMonths = 12,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        // Query uses a self-join to compare current week vs previous week downloads.
        // To favor newer/emerging packages over established ones, we filter by first_seen date.
        // Growth rate = (current - previous) / previous
        //
        // The query:
        // 1. Gets first_seen date per package (min week in weekly_downloads)
        // 2. Joins current week and previous week data
        // 3. Filters to packages first seen within maxPackageAgeMonths
        // 4. Filters by minimum download threshold to reduce noise
        // 5. Orders by growth rate descending
        const string query = """
            WITH
                toMonday(today()) AS current_week,
                toMonday(today() - INTERVAL 1 WEEK) AS previous_week,
                today() - INTERVAL {maxAgeMonths:Int32} MONTH AS age_cutoff
            SELECT
                cur.package_id AS package_id,
                toInt64(avgMerge(cur.download_avg)) AS current_downloads,
                toInt64(avgMerge(prev.download_avg)) AS previous_downloads
            FROM weekly_downloads cur
            INNER JOIN weekly_downloads prev
                ON cur.package_id = prev.package_id
                AND prev.week = previous_week
            INNER JOIN (
                SELECT package_id, min(week) AS first_seen
                FROM weekly_downloads
                GROUP BY package_id
            ) first ON cur.package_id = first.package_id
            WHERE cur.week = current_week
              AND first.first_seen >= age_cutoff
            GROUP BY cur.package_id
            HAVING current_downloads >= {minDownloads:Int64}
               AND previous_downloads > 0
            ORDER BY (current_downloads - previous_downloads) / previous_downloads DESC
            LIMIT {limit:Int32}
            """;

        var span = StartDatabaseSpan(parentSpan, query, "SELECT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = query;

            var maxAgeParam = cmd.CreateParameter();
            maxAgeParam.ParameterName = "maxAgeMonths";
            maxAgeParam.Value = maxPackageAgeMonths;
            cmd.Parameters.Add(maxAgeParam);

            var minDownloadsParam = cmd.CreateParameter();
            minDownloadsParam.ParameterName = "minDownloads";
            minDownloadsParam.Value = minWeeklyDownloads;
            cmd.Parameters.Add(minDownloadsParam);

            var limitParam = cmd.CreateParameter();
            limitParam.ParameterName = "limit";
            limitParam.Value = limit;
            cmd.Parameters.Add(limitParam);

            var results = new List<TrendingPackage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                results.Add(new TrendingPackage
                {
                    PackageId = reader.GetString(0),
                    CurrentWeekDownloads = reader.GetInt64(1),
                    PreviousWeekDownloads = reader.GetInt64(2)
                });
            }

            span?.SetData("db.rows_affected", results.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Retrieved {Count} trending packages", results.Count);
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
    /// Includes query source attributes for the Sentry Queries module.
    /// </summary>
    private ISpan? StartDatabaseSpan(
        ISpan? parentSpan,
        string queryDescription,
        string operation,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var span = parentSpan?.StartChild("db.sql.execute", queryDescription)
                   ?? SentrySdk.GetSpan()?.StartChild("db.sql.execute", queryDescription);

        if (span == null)
        {
            return null;
        }

        // Required for Sentry Queries module (use SetData for span data attributes)
        span.SetData("db.system", "clickhouse");

        // Recommended attributes for better insights
        span.SetData("db.operation", operation);

        if (_connectionInfo.Database is not null)
        {
            span.SetData("db.name", _connectionInfo.Database);
        }

        if (_connectionInfo.Host is not null)
        {
            span.SetData("server.address", _connectionInfo.Host);
        }

        if (_connectionInfo.Port is not null)
        {
            span.SetData("server.port", _connectionInfo.Port);
        }

        // Query source attributes for Sentry Queries module
        span.SetData("code.filepath", TelemetryHelpers.GetRelativeFilePath(filePath));
        span.SetData("code.function", memberName);
        span.SetData("code.lineno", lineNumber);
        span.SetData("code.namespace", typeof(ClickHouseService).FullName);

        return span;
    }
}
