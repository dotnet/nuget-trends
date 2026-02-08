using System.Globalization;
using System.Runtime.CompilerServices;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Copy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    /// Converts to Key=Value format connection string for ClickHouse.Driver.ADO.
    /// </summary>
    public string ToConnectionString()
    {
        var parts = new List<string>();
        if (Host is not null) parts.Add($"Host={Host}");
        if (Port is not null) parts.Add($"Port={Port}");
        if (Database is not null) parts.Add($"Database={Database}");
        return string.Join(";", parts);
    }

    /// <summary>
    /// Normalizes a connection string to Key=Value format.
    /// Aspire injects endpoint URLs (http://host:port) but ClickHouse.Driver.ADO needs Key=Value format.
    /// If already in Key=Value format, returns as-is.
    /// </summary>
    public static string NormalizeConnectionString(string connectionString, string defaultDatabase = "nugettrends")
    {
        if (Uri.TryCreate(connectionString, UriKind.Absolute, out var uri) &&
            uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var info = Parse(connectionString);
            var database = info.Database ?? defaultDatabase;
            return $"Host={info.Host};Port={info.Port};Database={database}";
        }

        return connectionString;
    }

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
    private readonly ILoggerFactory? _loggerFactory;

    public ClickHouseService(
        string connectionString,
        ILogger<ClickHouseService> logger,
        ClickHouseConnectionInfo connectionInfo,
        ILoggerFactory? loggerFactory = null)
    {
        _connectionString = connectionString;
        _logger = logger;
        _connectionInfo = connectionInfo;
        _loggerFactory = loggerFactory;
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
        // Query uses a self-join to compare LAST week vs the WEEK BEFORE.
        // We use complete weeks only - not the current partial week.
        // To favor newer/emerging packages over established ones, we filter by first_seen date.
        // Growth rate = (week - comparison) / comparison
        //
        // The query joins against package_first_seen (pre-computed) to avoid the expensive
        // subquery that computes min(week) for every package on each request.
        const string query = """
            WITH
                toMonday(today() - INTERVAL 1 WEEK) AS data_week,
                toMonday(today() - INTERVAL 2 WEEK) AS comparison_week,
                toDate(today() - INTERVAL {maxAgeMonths:Int32} MONTH) AS age_cutoff
            SELECT
                data_week AS week,
                cur.package_id AS package_id,
                toInt64(avgMerge(cur.download_avg) * 7) AS week_downloads,
                toInt64(avgMerge(prev.download_avg) * 7) AS comparison_downloads
            FROM weekly_downloads cur
            INNER JOIN weekly_downloads prev
                ON cur.package_id = prev.package_id
                AND prev.week = comparison_week
            INNER JOIN package_first_seen first
                ON cur.package_id = first.package_id
            WHERE cur.week = data_week
              AND first.first_seen >= age_cutoff
            GROUP BY cur.package_id
            HAVING week_downloads >= {minDownloads:Int64}
               AND comparison_downloads > 0
            ORDER BY (week_downloads - comparison_downloads) / comparison_downloads DESC
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
                    Week = DateOnly.FromDateTime(reader.GetDateTime(0)),
                    PackageId = reader.GetString(1),
                    WeekDownloads = reader.GetInt64(2),
                    ComparisonWeekDownloads = reader.GetInt64(3)
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

    public async Task<List<TrendingPackage>> GetTrendingPackagesFromSnapshotAsync(
        int limit = 100,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        // Query the pre-computed snapshot table for the most recent week
        // Includes enrichment columns (package_id_original, icon_url, github_url)
        // populated by the scheduler from PostgreSQL at snapshot-refresh time
        const string query = """
            SELECT
                week,
                package_id,
                week_downloads,
                comparison_week_downloads,
                package_id_original,
                icon_url,
                github_url
            FROM trending_packages_snapshot
            WHERE week = (SELECT max(week) FROM trending_packages_snapshot)
            ORDER BY growth_rate DESC
            LIMIT {limit:Int32}
            """;

        var span = StartDatabaseSpan(parentSpan, query, "SELECT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = query;

            var limitParam = cmd.CreateParameter();
            limitParam.ParameterName = "limit";
            limitParam.Value = limit;
            cmd.Parameters.Add(limitParam);

            var results = new List<TrendingPackage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var packageId = reader.GetString(1);
                var packageIdOriginal = reader.GetString(4);

                results.Add(new TrendingPackage
                {
                    Week = DateOnly.FromDateTime(reader.GetDateTime(0)),
                    PackageId = packageId,
                    WeekDownloads = reader.GetInt64(2),
                    ComparisonWeekDownloads = reader.GetInt64(3),
                    PackageIdOriginal = string.IsNullOrEmpty(packageIdOriginal) ? packageId : packageIdOriginal,
                    IconUrl = reader.GetString(5),
                    GitHubUrl = reader.GetString(6)
                });
            }

            span?.SetData("db.rows_affected", results.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Retrieved {Count} trending packages from snapshot", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task<List<TrendingPackage>> ComputeTrendingPackagesAsync(
        long minWeeklyDownloads = 1000,
        int maxPackageAgeMonths = 12,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        // Compute trending packages but return them to the caller instead of inserting directly.
        // The caller (scheduler) will enrich with PostgreSQL metadata and then call
        // InsertTrendingPackagesSnapshotAsync to store the enriched data.
        //
        // IMPORTANT: We use LAST week vs WEEK BEFORE (not current vs previous)
        // This ensures we're comparing complete weeks, not partial data.
        // On Monday at 2 AM UTC, this computes trending for the week that just ended.
        //
        // The query joins against package_first_seen (pre-computed) to avoid the expensive
        // subquery that computes min(week) for every package (which caused OOM).
        const string query = """
            WITH
                toMonday(today() - INTERVAL 1 WEEK) AS data_week,
                toMonday(today() - INTERVAL 2 WEEK) AS comparison_week,
                toDate(today() - INTERVAL {maxAgeMonths:Int32} MONTH) AS age_cutoff
            SELECT
                data_week AS week,
                cur.package_id AS package_id,
                toInt64(avgMerge(cur.download_avg) * 7) AS week_downloads,
                toInt64(avgMerge(prev.download_avg) * 7) AS comparison_downloads
            FROM weekly_downloads cur
            INNER JOIN weekly_downloads prev
                ON cur.package_id = prev.package_id
                AND prev.week = comparison_week
            INNER JOIN package_first_seen first
                ON cur.package_id = first.package_id
            WHERE cur.week = data_week
              AND first.first_seen >= age_cutoff
            GROUP BY cur.package_id
            HAVING week_downloads >= {minDownloads:Int64}
               AND comparison_downloads > 0
            ORDER BY (week_downloads - comparison_downloads) / comparison_downloads DESC
            LIMIT 1000
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

            var results = new List<TrendingPackage>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                results.Add(new TrendingPackage
                {
                    Week = DateOnly.FromDateTime(reader.GetDateTime(0)),
                    PackageId = reader.GetString(1),
                    WeekDownloads = reader.GetInt64(2),
                    ComparisonWeekDownloads = reader.GetInt64(3)
                });
            }

            span?.SetData("db.rows_affected", results.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogInformation("Computed {Count} trending packages", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task<int> InsertTrendingPackagesSnapshotAsync(
        List<TrendingPackage> packages,
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        if (packages.Count == 0)
        {
            _logger.LogWarning("No trending packages to insert into snapshot");
            return 0;
        }

        const string bulkInsertDescription =
            "INSERT INTO trending_packages_snapshot (week, package_id, week_downloads, comparison_week_downloads, growth_rate, package_id_original, icon_url, github_url)";
        var span = StartDatabaseSpan(parentSpan, bulkInsertDescription, "INSERT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            // Delete existing rows for the target week to avoid duplicates on retries.
            // ReplacingMergeTree eventually deduplicates, but reads without FINAL could
            // return duplicates until background merges run.
            var week = packages[0].Week;
            await using var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "ALTER TABLE trending_packages_snapshot DELETE WHERE week = {week:Date}";
            var weekParam = deleteCmd.CreateParameter();
            weekParam.ParameterName = "week";
            weekParam.Value = week.ToDateTime(TimeOnly.MinValue);
            deleteCmd.Parameters.Add(weekParam);
            await deleteCmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("Deleted existing snapshot rows for week {Week} before re-insert", week);

            using var bulkCopy = new ClickHouseBulkCopy(connection)
            {
                DestinationTableName = "trending_packages_snapshot",
                ColumnNames = ["week", "package_id", "week_downloads", "comparison_week_downloads", "growth_rate", "package_id_original", "icon_url", "github_url"],
                BatchSize = packages.Count
            };

            var data = packages.Select(p => new object[]
            {
                p.Week.ToDateTime(TimeOnly.MinValue),
                p.PackageId,
                p.WeekDownloads,
                p.ComparisonWeekDownloads,
                p.GrowthRate ?? 0.0,
                p.PackageIdOriginal,
                p.IconUrl,
                p.GitHubUrl
            });

            await bulkCopy.InitAsync();
            await bulkCopy.WriteToServerAsync(data, ct);

            span?.SetData("db.rows_affected", packages.Count);
            span?.Finish(SpanStatus.Ok);

            _logger.LogInformation("Inserted {Count} enriched trending packages into snapshot", packages.Count);
            return packages.Count;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task<int> UpdatePackageFirstSeenAsync(
        CancellationToken ct = default,
        ISpan? parentSpan = null)
    {
        // Add new packages from last week to package_first_seen table.
        // This must be called BEFORE RefreshTrendingPackagesSnapshotAsync to ensure
        // newly published packages are included in the trending calculation.
        //
        // The query is idempotent - packages already in the table are skipped.
        // This allows safe retries without duplicating data.
        const string query = """
            INSERT INTO package_first_seen (package_id, first_seen)
            SELECT DISTINCT package_id, toMonday(today() - INTERVAL 1 WEEK) AS first_seen
            FROM weekly_downloads
            WHERE week = toMonday(today() - INTERVAL 1 WEEK)
              AND package_id NOT IN (SELECT package_id FROM package_first_seen)
            """;

        var span = StartDatabaseSpan(parentSpan, query, "INSERT");

        try
        {
            await using var connection = new ClickHouseConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var cmd = connection.CreateCommand();
            cmd.CommandText = query;

            var rowsAffected = await cmd.ExecuteNonQueryAsync(ct);

            span?.SetData("db.rows_affected", rowsAffected);
            span?.Finish(SpanStatus.Ok);

            _logger.LogInformation("Added {Count} new packages to package_first_seen", rowsAffected);
            return rowsAffected;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task RunMigrationsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting ClickHouse migrations");

        var migrationLogger = _loggerFactory?.CreateLogger<ClickHouseMigrationRunner>() 
            ?? NullLogger<ClickHouseMigrationRunner>.Instance;
        var migrationRunner = new ClickHouseMigrationRunner(_connectionString, migrationLogger);

        await migrationRunner.RunMigrationsAsync(ct);

        _logger.LogInformation("ClickHouse migrations completed");
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
