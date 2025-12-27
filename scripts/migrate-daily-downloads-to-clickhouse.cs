#!/usr/bin/env dotnet

#:package Npgsql@9.0.2
#:package ClickHouse.Client@7.14.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Npgsql;

// ============================================================================
// NuGet Trends: PostgreSQL -> ClickHouse Migration Script
// ============================================================================
// Migrates daily_downloads table from PostgreSQL to ClickHouse.
// Streams the entire table in a single query for maximum performance.
// Applies LOWER() on-the-fly to normalize package IDs.
//
// Environment Variables:
//   PG_CONNECTION_STRING - PostgreSQL connection string
//   CH_CONNECTION_STRING - ClickHouse connection string
//
// Usage:
//   ./migrate-daily-downloads-to-clickhouse.cs [options]
//
// Options:
//   --batch-size N    Rows per batch insert (default: 1000000)
//   --verify-only     Only run verification, skip migration
//   --dry-run         Show plan without executing
//   --help, -h        Show help message
//
// Before running:
//   # Drop and recreate ClickHouse table for clean slate
//   clickhouse-client --host=HOST --password=PASS --multiquery -q "
//   DROP TABLE IF EXISTS nugettrends.daily_downloads;
//   CREATE TABLE nugettrends.daily_downloads (
//     package_id String, date Date, download_count UInt64
//   ) ENGINE = ReplacingMergeTree()
//   PARTITION BY toYear(date)
//   ORDER BY (package_id, date);
//   "
// ============================================================================

var exitCode = await new MigrationRunner().RunAsync(args);
return exitCode;

// ============================================================================
// Configuration
// ============================================================================

public record MigrationConfig
{
    public string PostgresConnectionString { get; init; } = "";
    public string ClickHouseConnectionString { get; init; } = "";
    public int BatchSize { get; init; } = 1_000_000;
    public bool VerifyOnly { get; init; }
    public bool DryRun { get; init; }
}

// ============================================================================
// Console Output Helpers
// ============================================================================

public static class Console2
{
    public static void WriteHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.WriteLine(new string('=', text.Length));
        Console.ResetColor();
    }

    public static void WriteSuccess(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    public static void WriteError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[ERR] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    public static void WriteWarning(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[WARN] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    public static void WriteInfo(string text)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }

    public static void WriteProgress(long current, long total, double rowsPerSec)
    {
        var percent = total > 0 ? (double)current / total * 100 : 0;
        var barWidth = 30;
        var filled = (int)(percent / 100 * barWidth);
        var bar = new string('#', filled) + new string('-', barWidth - filled);

        var eta = rowsPerSec > 0 ? TimeSpan.FromSeconds((total - current) / rowsPerSec) : TimeSpan.Zero;

        Console.Write($"\r  [{bar}] {FormatNumber(current)} / {FormatNumber(total)} ({percent:F1}%) | {FormatNumber((long)rowsPerSec)}/sec | ETA: {FormatDuration(eta)}   ");
    }

    public static string FormatNumber(long n)
    {
        return n switch
        {
            >= 1_000_000_000 => $"{n / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
            >= 1_000 => $"{n / 1_000.0:F1}K",
            _ => n.ToString()
        };
    }

    public static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }
}

// ============================================================================
// Migration Runner
// ============================================================================

public class MigrationRunner
{
    private MigrationConfig _config = null!;

    public async Task<int> RunAsync(string[] args)
    {
        try
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("NuGet Trends: PostgreSQL -> ClickHouse Migration");
            Console.WriteLine("=================================================");
            Console.ResetColor();

            // Parse configuration
            if (!TryParseConfig(args, out _config))
            {
                return 1;
            }

            // Display configuration
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console2.WriteInfo($"PostgreSQL: {MaskConnectionString(_config.PostgresConnectionString)}");
            Console2.WriteInfo($"ClickHouse: {MaskConnectionString(_config.ClickHouseConnectionString)}");
            Console2.WriteInfo($"Batch Size: {_config.BatchSize:N0} rows");

            if (_config.DryRun)
            {
                Console2.WriteWarning("DRY RUN MODE - No changes will be made");
            }

            if (_config.VerifyOnly)
            {
                Console2.WriteHeader("Verification Only Mode");
                return await RunVerificationAsync() ? 0 : 1;
            }

            // Get row count estimate from PostgreSQL
            Console2.WriteHeader("Analyzing Source Data");
            var estimatedRows = await GetPostgresRowEstimateAsync();
            Console2.WriteInfo($"Estimated rows: {Console2.FormatNumber(estimatedRows)}");
            Console2.WriteInfo($"Estimated batches: {estimatedRows / _config.BatchSize:N0}");

            if (_config.DryRun)
            {
                Console2.WriteHeader("Dry Run Complete");
                Console2.WriteInfo($"Would migrate ~{Console2.FormatNumber(estimatedRows)} rows");
                Console2.WriteInfo($"In batches of {_config.BatchSize:N0} rows");
                return 0;
            }

            // Run migration
            Console2.WriteHeader("Migration Progress");
            Console2.WriteInfo("Streaming entire table from PostgreSQL...");
            Console.WriteLine();

            var sw = Stopwatch.StartNew();
            var rowsMigrated = await StreamMigrateAsync(estimatedRows);
            sw.Stop();

            Console.WriteLine();
            Console.WriteLine();

            Console2.WriteSuccess($"Migration completed in {Console2.FormatDuration(sw.Elapsed)}");
            Console2.WriteInfo($"Rows migrated: {Console2.FormatNumber(rowsMigrated)}");
            Console2.WriteInfo($"Avg throughput: {Console2.FormatNumber((long)(rowsMigrated / sw.Elapsed.TotalSeconds))}/sec");

            // Run verification
            Console2.WriteHeader("Verification");
            var verificationPassed = await RunVerificationAsync();

            return verificationPassed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console2.WriteError($"Fatal error: {ex.Message}");
            Console2.WriteInfo(ex.StackTrace ?? "");
            return 1;
        }
    }

    private bool TryParseConfig(string[] args, out MigrationConfig config)
    {
        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintHelp();
            config = null!;
            return false;
        }

        var pgConnStr = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING");
        var chConnStr = Environment.GetEnvironmentVariable("CH_CONNECTION_STRING");

        if (string.IsNullOrEmpty(pgConnStr))
        {
            Console2.WriteError("PG_CONNECTION_STRING environment variable is required");
            config = null!;
            return false;
        }

        if (string.IsNullOrEmpty(chConnStr))
        {
            Console2.WriteError("CH_CONNECTION_STRING environment variable is required");
            config = null!;
            return false;
        }

        var batchSize = 1_000_000;
        var verifyOnly = false;
        var dryRun = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--batch-size" when i + 1 < args.Length:
                    batchSize = int.Parse(args[++i]);
                    break;
                case "--verify-only":
                    verifyOnly = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
            }
        }

        config = new MigrationConfig
        {
            PostgresConnectionString = pgConnStr,
            ClickHouseConnectionString = chConnStr,
            BatchSize = batchSize,
            VerifyOnly = verifyOnly,
            DryRun = dryRun
        };

        return true;
    }

    private void PrintHelp()
    {
        Console.WriteLine(@"
NuGet Trends: PostgreSQL -> ClickHouse Migration

Streams the entire daily_downloads table from PostgreSQL to ClickHouse.
Applies LOWER() on-the-fly to normalize package IDs for case-insensitive queries.

Usage: ./migrate-daily-downloads-to-clickhouse.cs [options]

Environment Variables (required):
  PG_CONNECTION_STRING    PostgreSQL connection string
  CH_CONNECTION_STRING    ClickHouse connection string

Options:
  --batch-size N    Rows per batch insert (default: 1000000)
  --verify-only     Only run verification, skip migration
  --dry-run         Show plan without executing
  --help, -h        Show this help message

Examples:
  # Full migration
  ./migrate-daily-downloads-to-clickhouse.cs

  # Use smaller batches (if memory constrained)
  ./migrate-daily-downloads-to-clickhouse.cs --batch-size 500000

  # Verify only (no migration)
  ./migrate-daily-downloads-to-clickhouse.cs --verify-only

  # Dry run (show plan without executing)
  ./migrate-daily-downloads-to-clickhouse.cs --dry-run

Before running:
  # Drop and recreate ClickHouse table for clean slate
  clickhouse-client --host=$CH_HOST --password=$CH_PASS --multiquery -q ""
  DROP TABLE IF EXISTS nugettrends.daily_downloads;
  CREATE TABLE nugettrends.daily_downloads (
    package_id String, date Date, download_count UInt64
  ) ENGINE = ReplacingMergeTree()
  PARTITION BY toYear(date)
  ORDER BY (package_id, date);
  ""
");
    }

    private string MaskConnectionString(string connStr)
    {
        var parts = connStr.Split(';');
        var masked = parts.Select(p =>
        {
            if (p.StartsWith("Password=", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("Pwd=", StringComparison.OrdinalIgnoreCase))
            {
                var idx = p.IndexOf('=');
                return p[..(idx + 1)] + "***";
            }
            return p;
        });
        return string.Join(";", masked);
    }

    private async Task<long> GetPostgresRowEstimateAsync()
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        // Use pg_class for fast estimate instead of COUNT(*)
        await using var cmd = new NpgsqlCommand(
            "SELECT reltuples::bigint FROM pg_class WHERE relname = 'daily_downloads'", conn);

        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result ?? 0);
    }

    private async Task<long> StreamMigrateAsync(long estimatedTotalRows)
    {
        await using var pgConn = new NpgsqlConnection(_config.PostgresConnectionString);
        await pgConn.OpenAsync();

        // Stream entire table - apply LOWER() on-the-fly for case-insensitive package IDs
        // No ORDER BY needed - ClickHouse will sort on insert based on ORDER BY key
        await using var cmd = new NpgsqlCommand(
            "SELECT LOWER(package_id), date, download_count FROM daily_downloads", pgConn);
        cmd.CommandTimeout = 0; // No timeout for long-running query

        await using var reader = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess);

        var batch = new List<object[]>(_config.BatchSize);
        var totalRows = 0L;
        var sw = Stopwatch.StartNew();
        var lastProgressUpdate = sw.ElapsedMilliseconds;

        while (await reader.ReadAsync())
        {
            var packageId = reader.GetString(0);
            var date = DateOnly.FromDateTime(reader.GetDateTime(1));
            var downloadCount = reader.IsDBNull(2) ? 0UL : (ulong)reader.GetInt64(2);

            batch.Add([packageId, date, downloadCount]);
            totalRows++;

            if (batch.Count >= _config.BatchSize)
            {
                await InsertBatchToClickHouseAsync(batch);
                batch.Clear();

                // Update progress every 500ms
                var elapsed = sw.ElapsedMilliseconds;
                if (elapsed - lastProgressUpdate > 500)
                {
                    var rowsPerSec = totalRows / sw.Elapsed.TotalSeconds;
                    Console2.WriteProgress(totalRows, estimatedTotalRows, rowsPerSec);
                    lastProgressUpdate = elapsed;
                }
            }
        }

        // Insert remaining rows
        if (batch.Count > 0)
        {
            await InsertBatchToClickHouseAsync(batch);
        }

        // Final progress update
        var finalRowsPerSec = totalRows / sw.Elapsed.TotalSeconds;
        Console2.WriteProgress(totalRows, totalRows, finalRowsPerSec);

        return totalRows;
    }

    private async Task InsertBatchToClickHouseAsync(List<object[]> batch)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        using var bulkCopy = new ClickHouseBulkCopy(conn)
        {
            DestinationTableName = "daily_downloads",
            ColumnNames = ["package_id", "date", "download_count"],
            BatchSize = batch.Count
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(batch);
    }

    private async Task<bool> RunVerificationAsync()
    {
        var allPassed = true;

        // Level 1: Row count comparison
        Console.WriteLine();
        Console.WriteLine("  Level 1: Row Count Comparison");

        var pgEstimate = await GetPostgresRowEstimateAsync();
        var chCount = await GetClickHouseRowCountAsync();

        // Allow 1% variance due to estimate vs actual
        var variance = Math.Abs(pgEstimate - chCount) / (double)pgEstimate * 100;
        if (variance < 1)
        {
            Console2.WriteSuccess($"Row counts match (within 1%): PG~{Console2.FormatNumber(pgEstimate)}, CH={Console2.FormatNumber(chCount)}");
        }
        else if (variance < 5)
        {
            Console2.WriteWarning($"Row counts differ slightly: PG~{Console2.FormatNumber(pgEstimate)}, CH={Console2.FormatNumber(chCount)} ({variance:F1}% variance)");
            Console2.WriteInfo("Note: PostgreSQL count is an estimate from pg_class.");
        }
        else
        {
            Console2.WriteError($"Row counts differ significantly: PG~{Console2.FormatNumber(pgEstimate)}, CH={Console2.FormatNumber(chCount)} ({variance:F1}% variance)");
            allPassed = false;
        }

        // Level 2: Package count
        Console.WriteLine();
        Console.WriteLine("  Level 2: Unique Package Count");

        var chPackageCount = await GetClickHousePackageCountAsync();
        Console2.WriteInfo($"ClickHouse unique packages: {Console2.FormatNumber(chPackageCount)}");

        // Level 3: Sample package verification
        Console.WriteLine();
        Console.WriteLine("  Level 3: Sample Package Verification");

        var samplePackages = new[] { "newtonsoft.json", "sentry", "dapper", "nlog", "serilog", "system.text.json", "microsoft.extensions.logging" };
        foreach (var pkg in samplePackages)
        {
            var (rows, minDate, maxDate) = await GetClickHousePackageStatsAsync(pkg);
            if (rows > 0)
            {
                Console2.WriteSuccess($"{pkg}: {Console2.FormatNumber(rows)} rows ({minDate:yyyy-MM-dd} to {maxDate:yyyy-MM-dd})");
            }
            else
            {
                Console2.WriteWarning($"{pkg}: No data found (may not exist in source)");
            }
        }

        // Level 4: Case insensitivity check
        Console.WriteLine();
        Console.WriteLine("  Level 4: Case Insensitivity Check");

        // Query ClickHouse directly without lowercasing to verify stored case
        var lowerRows = await GetClickHouseRowCountForExactPackageIdAsync("newtonsoft.json");
        var upperRows = await GetClickHouseRowCountForExactPackageIdAsync("NEWTONSOFT.JSON");
        var mixedRows = await GetClickHouseRowCountForExactPackageIdAsync("Newtonsoft.Json");

        if (lowerRows > 0 && upperRows == 0 && mixedRows == 0)
        {
            Console2.WriteSuccess($"Package IDs are normalized to lowercase ({Console2.FormatNumber(lowerRows)} rows for 'newtonsoft.json')");
        }
        else
        {
            Console2.WriteWarning($"Case check: lowercase={lowerRows}, UPPERCASE={upperRows}, MixedCase={mixedRows}");
            if (upperRows > 0 || mixedRows > 0)
            {
                Console2.WriteError("Found non-lowercase package IDs in ClickHouse!");
                allPassed = false;
            }
        }

        Console.WriteLine();
        if (allPassed)
        {
            Console2.WriteSuccess("Verification complete!");
        }
        else
        {
            Console2.WriteError("Some verification checks failed");
        }

        return allPassed;
    }

    private async Task<long> GetClickHouseRowCountAsync()
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count() FROM daily_downloads";

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }

    private async Task<long> GetClickHousePackageCountAsync()
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(DISTINCT package_id) FROM daily_downloads";

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }

    private async Task<(long Rows, DateOnly MinDate, DateOnly MaxDate)> GetClickHousePackageStatsAsync(string packageId)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        // Query with lowercase - package_id is stored lowercase
        cmd.CommandText = $"SELECT count(), min(date), max(date) FROM daily_downloads WHERE package_id = '{packageId.ToLower(CultureInfo.InvariantCulture)}'";

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            var rows = Convert.ToInt64(reader.GetValue(0));
            if (rows == 0)
            {
                return (0, default, default);
            }
            var minDate = DateOnly.FromDateTime(reader.GetDateTime(1));
            var maxDate = DateOnly.FromDateTime(reader.GetDateTime(2));
            return (rows, minDate, maxDate);
        }

        return (0, default, default);
    }

    private async Task<long> GetClickHouseRowCountForExactPackageIdAsync(string packageId)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        // Query with EXACT case - no lowercasing - to verify stored case
        cmd.CommandText = $"SELECT count() FROM daily_downloads WHERE package_id = '{packageId}'";

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }
}
