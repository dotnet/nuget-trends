#!/usr/bin/env dotnet

#:package Npgsql@9.0.2
#:package ClickHouse.Client@7.14.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Npgsql;

// ============================================================================
// NuGet Trends: PostgreSQL -> ClickHouse Migration Script
// ============================================================================
// Migrates daily_downloads table from PostgreSQL to ClickHouse.
// Processes packages one at a time, ordered by download count (largest first).
// Uses existing primary key index on (package_id, date) - no new indexes needed.
//
// Environment Variables:
//   PG_CONNECTION_STRING - PostgreSQL connection string
//   CH_CONNECTION_STRING - ClickHouse connection string
//
// Usage:
//   ./migrate-daily-downloads-to-clickhouse.cs [options]
//
// Options:
//   --batch-size N          Rows per batch insert (default: 100000)
//   --save-every N          Save progress every N packages (default: 100)
//   --package PKG           Migrate only a specific package (for testing)
//   --limit N               Migrate only first N packages (for testing)
//   --verify-only           Only run verification, skip migration
//   --dry-run               Show plan without executing
//   --reset                 Clear progress file and start fresh
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
    public int BatchSize { get; init; } = 100_000;
    public int SaveEvery { get; init; } = 100;
    public string? SinglePackage { get; init; }
    public int? Limit { get; init; }
    public bool VerifyOnly { get; init; }
    public bool DryRun { get; init; }
    public bool Reset { get; init; }
    public string ProgressFilePath { get; init; } = "";
}

// ============================================================================
// Progress Tracking
// ============================================================================

public class MigrationProgress
{
    [JsonPropertyName("completedPackages")]
    public HashSet<string> CompletedPackages { get; set; } = [];

    [JsonPropertyName("totalRowsMigrated")]
    public long TotalRowsMigrated { get; set; }

    [JsonPropertyName("totalPackagesMigrated")]
    public int TotalPackagesMigrated { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; }

    [JsonPropertyName("currentPackage")]
    public string? CurrentPackage { get; set; }

    [JsonPropertyName("failedPackages")]
    public Dictionary<string, int> FailedPackages { get; set; } = [];
}

public class ProgressTracker
{
    private readonly string _filePath;
    private MigrationProgress _progress;
    private int _unsavedCount;
    private readonly int _saveEvery;

    public ProgressTracker(string filePath, int saveEvery)
    {
        _filePath = filePath;
        _saveEvery = saveEvery;
        _progress = Load();
    }

    public MigrationProgress Progress => _progress;

    public bool IsCompleted(string packageId)
    {
        return _progress.CompletedPackages.Contains(packageId.ToLowerInvariant());
    }

    public int GetFailedAttempts(string packageId)
    {
        return _progress.FailedPackages.TryGetValue(packageId.ToLowerInvariant(), out var count) ? count : 0;
    }

    public void RecordFailure(string packageId)
    {
        var key = packageId.ToLowerInvariant();
        _progress.FailedPackages[key] = GetFailedAttempts(packageId) + 1;
        _progress.LastUpdatedAt = DateTime.UtcNow;
        Save(); // Always save on failure
    }

    public void SetCurrentPackage(string packageId)
    {
        _progress.CurrentPackage = packageId;
    }

    public void RecordSuccess(string packageId, long rows)
    {
        var key = packageId.ToLowerInvariant();
        _progress.CompletedPackages.Add(key);
        _progress.TotalRowsMigrated += rows;
        _progress.TotalPackagesMigrated++;
        _progress.FailedPackages.Remove(key);
        _progress.CurrentPackage = null;
        _progress.LastUpdatedAt = DateTime.UtcNow;

        _unsavedCount++;
        if (_unsavedCount >= _saveEvery)
        {
            Save();
            _unsavedCount = 0;
        }
    }

    public void Reset()
    {
        _progress = new MigrationProgress { StartedAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow };
        Save();
    }

    public void ForceSave()
    {
        Save();
        _unsavedCount = 0;
    }

    private MigrationProgress Load()
    {
        if (!File.Exists(_filePath))
        {
            return new MigrationProgress { StartedAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow };
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<MigrationProgress>(json)
            ?? new MigrationProgress { StartedAt = DateTime.UtcNow, LastUpdatedAt = DateTime.UtcNow };
    }

    private void Save()
    {
        var json = JsonSerializer.Serialize(_progress, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
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

    public static void WriteProgress(int current, int total, string packageId, long rows, double rate)
    {
        var percent = (double)current / total * 100;
        var barWidth = 25;
        var filled = (int)(percent / 100 * barWidth);
        var bar = new string('#', filled) + new string('-', barWidth - filled);

        var eta = rate > 0 ? TimeSpan.FromSeconds((total - current) / rate) : TimeSpan.Zero;
        var etaStr = FormatDuration(eta);

        // Truncate package ID if too long
        var displayPkg = packageId.Length > 30 ? packageId[..27] + "..." : packageId;

        Console.Write($"\r  [{bar}] {current:N0}/{total:N0} ({percent:F1}%) | {displayPkg,-30} | {FormatNumber(rows)} rows | ETA: {etaStr}   ");
    }

    public static void ClearLine()
    {
        try
        {
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
        }
        catch
        {
            Console.Write("\r" + new string(' ', 120) + "\r");
        }
    }

    public static string FormatNumber(long n)
    {
        return n switch
        {
            >= 1_000_000_000 => $"{n / 1_000_000_000.0:F2}B",
            >= 1_000_000 => $"{n / 1_000_000.0:F2}M",
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
    private ProgressTracker _progress = null!;

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

            _progress = new ProgressTracker(_config.ProgressFilePath, _config.SaveEvery);

            if (_config.Reset)
            {
                Console2.WriteWarning("Resetting progress file...");
                _progress.Reset();
            }

            // Display configuration
            Console.WriteLine();
            Console.WriteLine("Configuration:");
            Console2.WriteInfo($"PostgreSQL: {MaskConnectionString(_config.PostgresConnectionString)}");
            Console2.WriteInfo($"ClickHouse: {MaskConnectionString(_config.ClickHouseConnectionString)}");
            Console2.WriteInfo($"Batch Size: {_config.BatchSize:N0} rows");
            Console2.WriteInfo($"Save Every: {_config.SaveEvery} packages");
            Console2.WriteInfo($"Progress File: {_config.ProgressFilePath}");

            if (_config.SinglePackage != null)
            {
                Console2.WriteInfo($"Single Package: {_config.SinglePackage}");
            }

            if (_config.Limit.HasValue)
            {
                Console2.WriteInfo($"Limit: {_config.Limit} packages");
            }

            if (_config.DryRun)
            {
                Console2.WriteWarning("DRY RUN MODE - No changes will be made");
            }

            if (_config.VerifyOnly)
            {
                Console2.WriteHeader("Verification Only Mode");
                return await RunVerificationAsync() ? 0 : 1;
            }

            // Get package list ordered by download count
            Console2.WriteHeader("Loading Package List");
            var packages = await GetPackageListAsync();

            var totalPackages = packages.Count;
            var alreadyMigrated = packages.Count(p => _progress.IsCompleted(p));
            var remaining = totalPackages - alreadyMigrated;

            Console2.WriteInfo($"Total packages: {totalPackages:N0}");
            Console2.WriteInfo($"Already migrated: {alreadyMigrated:N0}");
            Console2.WriteInfo($"Remaining: {remaining:N0}");

            if (_config.DryRun)
            {
                Console2.WriteHeader("Dry Run Complete");
                Console2.WriteInfo("First 10 packages to migrate:");
                foreach (var pkg in packages.Where(p => !_progress.IsCompleted(p)).Take(10))
                {
                    Console2.WriteInfo($"  - {pkg}");
                }
                return 0;
            }

            // Run migration
            Console2.WriteHeader("Migration Progress");

            var overallSw = Stopwatch.StartNew();
            var totalRowsMigrated = 0L;
            var packagesProcessed = 0;
            var packagesMigrated = 0;
            var startTime = DateTime.UtcNow;

            foreach (var packageId in packages)
            {
                packagesProcessed++;

                if (_progress.IsCompleted(packageId))
                {
                    continue;
                }

                _progress.SetCurrentPackage(packageId);

                var result = await MigratePackageAsync(packageId);

                if (!result.Success)
                {
                    var attempts = _progress.GetFailedAttempts(packageId);
                    if (attempts >= 3)
                    {
                        Console.WriteLine();
                        Console2.WriteError($"Package '{packageId}' failed 3 times. Stopping migration.");
                        Console2.WriteInfo("Use --reset to start fresh or manually fix the issue.");
                        _progress.ForceSave();
                        return 1;
                    }

                    Console2.WriteWarning($"Package '{packageId}' failed (attempt {attempts}/3). Continuing...");
                    continue;
                }

                totalRowsMigrated += result.RowsMigrated;
                packagesMigrated++;

                // Calculate rate (packages per second)
                var elapsed = DateTime.UtcNow - startTime;
                var rate = elapsed.TotalSeconds > 0 ? packagesMigrated / elapsed.TotalSeconds : 0;

                Console2.WriteProgress(packagesProcessed, totalPackages, packageId, result.RowsMigrated, rate);
            }

            _progress.ForceSave();
            overallSw.Stop();

            Console.WriteLine();
            Console.WriteLine();

            // Run verification
            Console2.WriteHeader("Verification");
            var verificationPassed = await RunVerificationAsync();

            // Summary
            Console2.WriteHeader("Migration Complete");
            Console2.WriteInfo($"Duration: {Console2.FormatDuration(overallSw.Elapsed)}");
            Console2.WriteInfo($"Rows migrated: {Console2.FormatNumber(totalRowsMigrated)}");
            Console2.WriteInfo($"Packages migrated: {packagesMigrated:N0}");
            Console2.WriteInfo($"Avg throughput: {Console2.FormatNumber((long)(totalRowsMigrated / overallSw.Elapsed.TotalSeconds))}/sec");

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
        // Check for help first
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

        var batchSize = 100_000;
        var saveEvery = 100;
        string? singlePackage = null;
        int? limit = null;
        var verifyOnly = false;
        var dryRun = false;
        var reset = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--batch-size" when i + 1 < args.Length:
                    batchSize = int.Parse(args[++i]);
                    break;
                case "--save-every" when i + 1 < args.Length:
                    saveEvery = int.Parse(args[++i]);
                    break;
                case "--package" when i + 1 < args.Length:
                    singlePackage = args[++i];
                    break;
                case "--limit" when i + 1 < args.Length:
                    limit = int.Parse(args[++i]);
                    break;
                case "--verify-only":
                    verifyOnly = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--reset":
                    reset = true;
                    break;
            }
        }

        var scriptDir = Path.GetDirectoryName(Environment.ProcessPath)
            ?? AppContext.BaseDirectory
            ?? ".";

        config = new MigrationConfig
        {
            PostgresConnectionString = pgConnStr,
            ClickHouseConnectionString = chConnStr,
            BatchSize = batchSize,
            SaveEvery = saveEvery,
            SinglePackage = singlePackage,
            Limit = limit,
            VerifyOnly = verifyOnly,
            DryRun = dryRun,
            Reset = reset,
            ProgressFilePath = Path.Combine(scriptDir, ".migration-progress.json")
        };

        return true;
    }

    private void PrintHelp()
    {
        Console.WriteLine(@"
Usage: ./migrate-daily-downloads-to-clickhouse.cs [options]

Environment Variables (required):
  PG_CONNECTION_STRING    PostgreSQL connection string
  CH_CONNECTION_STRING    ClickHouse connection string

Options:
  --batch-size N          Rows per batch insert (default: 100000)
  --save-every N          Save progress every N packages (default: 100)
  --package PKG           Migrate only a specific package (for testing)
  --limit N               Migrate only first N packages (for testing)
  --verify-only           Only run verification, skip migration
  --dry-run               Show plan without executing
  --reset                 Clear progress file and start fresh
  --help, -h              Show this help message

Examples:
  # Full migration
  ./migrate-daily-downloads-to-clickhouse.cs

  # Test with a single package
  ./migrate-daily-downloads-to-clickhouse.cs --package Newtonsoft.Json

  # Migrate first 100 packages (for testing)
  ./migrate-daily-downloads-to-clickhouse.cs --limit 100

  # Verify only (no migration)
  ./migrate-daily-downloads-to-clickhouse.cs --verify-only

  # Reset and start fresh
  ./migrate-daily-downloads-to-clickhouse.cs --reset
");
    }

    private string MaskConnectionString(string connStr)
    {
        // Simple masking of password
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

    private async Task<List<string>> GetPackageListAsync()
    {
        if (_config.SinglePackage != null)
        {
            return [_config.SinglePackage];
        }

        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        // Get packages ordered by download count (largest first)
        // Uses the package_downloads table which has the latest download counts
        await using var cmd = new NpgsqlCommand(
            "SELECT package_id FROM package_downloads ORDER BY latest_download_count DESC NULLS LAST", conn);
        cmd.CommandTimeout = 300;

        var packages = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packages.Add(reader.GetString(0));

            if (_config.Limit.HasValue && packages.Count >= _config.Limit.Value)
            {
                break;
            }
        }

        return packages;
    }

    private async Task<(bool Success, long RowsMigrated)> MigratePackageAsync(string packageId)
    {
        try
        {
            var rowsMigrated = await StreamMigratePackageAsync(packageId);
            _progress.RecordSuccess(packageId, rowsMigrated);
            return (true, rowsMigrated);
        }
        catch (Exception ex)
        {
            Console2.ClearLine();
            Console2.WriteError($"[{packageId}] Error: {ex.Message}");
            _progress.RecordFailure(packageId);
            return (false, 0);
        }
    }

    private async Task<long> StreamMigratePackageAsync(string packageId)
    {
        await using var pgConn = new NpgsqlConnection(_config.PostgresConnectionString);
        await pgConn.OpenAsync();

        // Query uses the primary key index on (package_id, date)
        await using var pgCmd = new NpgsqlCommand(
            "SELECT LOWER(package_id), date, download_count FROM daily_downloads WHERE package_id = @packageId ORDER BY date",
            pgConn);
        pgCmd.Parameters.AddWithValue("packageId", packageId);
        pgCmd.CommandTimeout = 3600; // 1 hour for very large packages

        await using var reader = await pgCmd.ExecuteReaderAsync();

        var batch = new List<object[]>(_config.BatchSize);
        var totalRows = 0L;

        while (await reader.ReadAsync())
        {
            var pkgIdLower = reader.GetString(0);
            var date = DateOnly.FromDateTime(reader.GetDateTime(1));
            var downloadCount = reader.IsDBNull(2) ? 0UL : (ulong)reader.GetInt64(2);

            batch.Add([pkgIdLower, date, downloadCount]);
            totalRows++;

            if (batch.Count >= _config.BatchSize)
            {
                await InsertBatchToClickHouseAsync(batch);
                batch.Clear();
            }
        }

        // Insert remaining rows
        if (batch.Count > 0)
        {
            await InsertBatchToClickHouseAsync(batch);
        }

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

        // Level 1: Total row count
        Console.WriteLine();
        Console.WriteLine("  Level 1: Total Row Count");

        var pgTotal = await GetPostgresTotalCountAsync();
        var chTotal = await GetClickHouseTotalCountAsync();

        if (pgTotal == chTotal)
        {
            Console2.WriteSuccess($"Total rows match: {Console2.FormatNumber(pgTotal)}");
        }
        else
        {
            Console2.WriteError($"Total rows MISMATCH: PG={Console2.FormatNumber(pgTotal)}, CH={Console2.FormatNumber(chTotal)}");
            allPassed = false;
        }

        // Level 2: Package count
        Console.WriteLine();
        Console.WriteLine("  Level 2: Unique Package Count");

        var pgPackageCount = await GetPostgresPackageCountAsync();
        var chPackageCount = await GetClickHousePackageCountAsync();

        if (pgPackageCount == chPackageCount)
        {
            Console2.WriteSuccess($"Package count matches: {Console2.FormatNumber(pgPackageCount)}");
        }
        else
        {
            Console2.WriteError($"Package count MISMATCH: PG={Console2.FormatNumber(pgPackageCount)}, CH={Console2.FormatNumber(chPackageCount)}");
            allPassed = false;
        }

        // Level 3: Sample package verification
        Console.WriteLine();
        Console.WriteLine("  Level 3: Sample Package Verification");

        var samplePackages = await GetSamplePackagesAsync();
        var packagesPassed = 0;

        foreach (var packageId in samplePackages)
        {
            var (pgRows, pgSum) = await GetPostgresPackageStatsAsync(packageId);
            var (chRows, chSum) = await GetClickHousePackageStatsAsync(packageId);

            if (pgRows == chRows && pgSum == chSum)
            {
                Console2.WriteSuccess($"{packageId}: {Console2.FormatNumber(pgRows)} rows, sum={Console2.FormatNumber(pgSum)}");
                packagesPassed++;
            }
            else
            {
                Console2.WriteError($"{packageId}: PG({Console2.FormatNumber(pgRows)} rows, sum={Console2.FormatNumber(pgSum)}) != CH({Console2.FormatNumber(chRows)} rows, sum={Console2.FormatNumber(chSum)})");
                allPassed = false;
            }
        }

        Console.WriteLine();
        if (allPassed)
        {
            Console2.WriteSuccess("All verification checks passed!");
        }
        else
        {
            Console2.WriteError("Some verification checks failed");
        }

        return allPassed;
    }

    private async Task<long> GetPostgresTotalCountAsync()
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM daily_downloads", conn);
        cmd.CommandTimeout = 600;

        var result = await cmd.ExecuteScalarAsync();
        return (long)(result ?? 0L);
    }

    private async Task<long> GetClickHouseTotalCountAsync()
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count() FROM daily_downloads";

        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }

    private async Task<long> GetPostgresPackageCountAsync()
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand("SELECT COUNT(DISTINCT package_id) FROM daily_downloads", conn);
        cmd.CommandTimeout = 600;

        var result = await cmd.ExecuteScalarAsync();
        return (long)(result ?? 0L);
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

    private async Task<List<string>> GetSamplePackagesAsync()
    {
        // Get a mix of packages: some popular, some random
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        // Get 5 packages with the most rows and 5 random packages
        await using var cmd = new NpgsqlCommand(@"
            (SELECT package_id FROM daily_downloads GROUP BY package_id ORDER BY COUNT(*) DESC LIMIT 5)
            UNION ALL
            (SELECT package_id FROM daily_downloads GROUP BY package_id ORDER BY RANDOM() LIMIT 5)", conn);
        cmd.CommandTimeout = 300;

        var packages = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var packageId = reader.GetString(0);
            if (!packages.Contains(packageId, StringComparer.OrdinalIgnoreCase))
            {
                packages.Add(packageId);
            }
        }

        return packages.Take(10).ToList();
    }

    private async Task<(long Rows, long Sum)> GetPostgresPackageStatsAsync(string packageId)
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*), COALESCE(SUM(download_count), 0) FROM daily_downloads WHERE LOWER(package_id) = LOWER(@packageId)", conn);
        cmd.Parameters.AddWithValue("packageId", packageId);
        cmd.CommandTimeout = 300;

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async Task<(long Rows, long Sum)> GetClickHousePackageStatsAsync(string packageId)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(), sum(download_count) FROM daily_downloads WHERE package_id = '{packageId.ToLower(CultureInfo.InvariantCulture)}'";

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();

        return (Convert.ToInt64(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)));
    }
}
