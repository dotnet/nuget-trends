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
// NuGet Trends: PostgreSQL → ClickHouse Migration Script
// ============================================================================
// Migrates daily_downloads table from PostgreSQL to ClickHouse.
//
// Environment Variables:
//   PG_CONNECTION_STRING - PostgreSQL connection string
//   CH_CONNECTION_STRING - ClickHouse connection string
//
// Usage:
//   ./migrate-daily-downloads-to-clickhouse.cs [options]
//
// Options:
//   --start-month YYYY-MM   First month to migrate (default: auto-detect)
//   --end-month YYYY-MM     Last month to migrate (default: current month)
//   --batch-size N          Rows per batch insert (default: 100000)
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
    public YearMonth? StartMonth { get; init; }
    public YearMonth? EndMonth { get; init; }
    public int BatchSize { get; init; } = 100_000;
    public bool VerifyOnly { get; init; }
    public bool DryRun { get; init; }
    public bool Reset { get; init; }
    public string ProgressFilePath { get; init; } = "";
}

public readonly record struct YearMonth(int Year, int Month) : IComparable<YearMonth>
{
    public static YearMonth FromDateTime(DateTime dt) => new(dt.Year, dt.Month);
    
    public static YearMonth Parse(string s)
    {
        var parts = s.Split('-');
        return new YearMonth(int.Parse(parts[0]), int.Parse(parts[1]));
    }
    
    public YearMonth AddMonths(int months)
    {
        var dt = new DateTime(Year, Month, 1).AddMonths(months);
        return new YearMonth(dt.Year, dt.Month);
    }
    
    public DateTime FirstDay => new(Year, Month, 1);
    public DateTime LastDay => FirstDay.AddMonths(1).AddDays(-1);
    public DateTime NextMonthFirstDay => FirstDay.AddMonths(1);
    
    public int CompareTo(YearMonth other)
    {
        var yearCmp = Year.CompareTo(other.Year);
        return yearCmp != 0 ? yearCmp : Month.CompareTo(other.Month);
    }
    
    public static bool operator <(YearMonth left, YearMonth right) => left.CompareTo(right) < 0;
    public static bool operator >(YearMonth left, YearMonth right) => left.CompareTo(right) > 0;
    public static bool operator <=(YearMonth left, YearMonth right) => left.CompareTo(right) <= 0;
    public static bool operator >=(YearMonth left, YearMonth right) => left.CompareTo(right) >= 0;
    
    public override string ToString() => $"{Year:D4}-{Month:D2}";
}

// ============================================================================
// Progress Tracking
// ============================================================================

public class MigrationProgress
{
    [JsonPropertyName("lastCompletedMonth")]
    public string? LastCompletedMonth { get; set; }
    
    [JsonPropertyName("totalRowsMigrated")]
    public long TotalRowsMigrated { get; set; }
    
    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }
    
    [JsonPropertyName("monthsCompleted")]
    public List<MonthProgress> MonthsCompleted { get; set; } = [];
    
    [JsonPropertyName("failedAttempts")]
    public Dictionary<string, int> FailedAttempts { get; set; } = [];
}

public class MonthProgress
{
    [JsonPropertyName("month")]
    public string Month { get; set; } = "";
    
    [JsonPropertyName("rows")]
    public long Rows { get; set; }
    
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }
}

public class ProgressTracker
{
    private readonly string _filePath;
    private MigrationProgress _progress;
    
    public ProgressTracker(string filePath)
    {
        _filePath = filePath;
        _progress = Load();
    }
    
    public MigrationProgress Progress => _progress;
    
    public YearMonth? GetLastCompletedMonth()
    {
        return _progress.LastCompletedMonth != null 
            ? YearMonth.Parse(_progress.LastCompletedMonth) 
            : null;
    }
    
    public int GetFailedAttempts(YearMonth month)
    {
        return _progress.FailedAttempts.TryGetValue(month.ToString(), out var count) ? count : 0;
    }
    
    public void RecordFailure(YearMonth month)
    {
        var key = month.ToString();
        _progress.FailedAttempts[key] = GetFailedAttempts(month) + 1;
        Save();
    }
    
    public void RecordSuccess(YearMonth month, long rows, long durationMs)
    {
        _progress.LastCompletedMonth = month.ToString();
        _progress.TotalRowsMigrated += rows;
        _progress.MonthsCompleted.Add(new MonthProgress
        {
            Month = month.ToString(),
            Rows = rows,
            DurationMs = durationMs
        });
        _progress.FailedAttempts.Remove(month.ToString());
        Save();
    }
    
    public void Reset()
    {
        _progress = new MigrationProgress { StartedAt = DateTime.UtcNow };
        Save();
    }
    
    private MigrationProgress Load()
    {
        if (!File.Exists(_filePath))
        {
            return new MigrationProgress { StartedAt = DateTime.UtcNow };
        }
        
        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<MigrationProgress>(json) 
            ?? new MigrationProgress { StartedAt = DateTime.UtcNow };
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
        Console.WriteLine(new string('═', text.Length));
        Console.ResetColor();
    }
    
    public static void WriteSuccess(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("✓ ");
        Console.ResetColor();
        Console.WriteLine(text);
    }
    
    public static void WriteError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("✗ ");
        Console.ResetColor();
        Console.WriteLine(text);
    }
    
    public static void WriteWarning(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("⚠ ");
        Console.ResetColor();
        Console.WriteLine(text);
    }
    
    public static void WriteInfo(string text)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }
    
    public static void WriteProgress(string month, string status, double percent, long current, long total, double rate)
    {
        var barWidth = 30;
        var filled = (int)(percent / 100 * barWidth);
        var bar = new string('█', filled) + new string('░', barWidth - filled);
        
        var eta = rate > 0 ? TimeSpan.FromSeconds((total - current) / rate) : TimeSpan.Zero;
        var etaStr = eta.TotalHours >= 1 
            ? $"{eta.Hours}h {eta.Minutes}m" 
            : eta.TotalMinutes >= 1 
                ? $"{eta.Minutes}m {eta.Seconds}s"
                : $"{eta.Seconds}s";
        
        Console.Write($"\r  [{month}] {bar} {percent,5:F1}% | {FormatNumber(current)}/{FormatNumber(total)} | {FormatNumber((long)rate)}/sec | ETA: {etaStr}    ");
    }
    
    public static void ClearLine()
    {
        Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
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
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
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
            Console.WriteLine("NuGet Trends: PostgreSQL → ClickHouse Migration");
            Console.WriteLine("════════════════════════════════════════════════");
            Console.ResetColor();
            
            // Parse configuration
            if (!TryParseConfig(args, out _config))
            {
                return 1;
            }
            
            _progress = new ProgressTracker(_config.ProgressFilePath);
            
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
            Console2.WriteInfo($"Progress File: {_config.ProgressFilePath}");
            
            if (_config.DryRun)
            {
                Console2.WriteWarning("DRY RUN MODE - No changes will be made");
            }
            
            // Detect date range
            Console2.WriteHeader("Detecting Data Range");
            var (pgMinDate, pgMaxDate, pgTotalRows) = await GetPostgresDataRangeAsync();
            Console2.WriteInfo($"PostgreSQL: {Console2.FormatNumber(pgTotalRows)} total rows");
            Console2.WriteInfo($"Date range: {pgMinDate:yyyy-MM-dd} to {pgMaxDate:yyyy-MM-dd}");
            
            var startMonth = _config.StartMonth ?? YearMonth.FromDateTime(pgMinDate);
            var endMonth = _config.EndMonth ?? YearMonth.FromDateTime(pgMaxDate);
            
            // Check for resume
            var lastCompleted = _progress.GetLastCompletedMonth();
            if (lastCompleted.HasValue && lastCompleted.Value >= startMonth)
            {
                Console2.WriteInfo($"Resuming from: {lastCompleted.Value.AddMonths(1)} (last completed: {lastCompleted.Value})");
                startMonth = lastCompleted.Value.AddMonths(1);
            }
            
            Console2.WriteInfo($"Will migrate: {startMonth} to {endMonth}");
            
            if (_config.VerifyOnly)
            {
                Console2.WriteHeader("Verification Only Mode");
                return await RunVerificationAsync() ? 0 : 1;
            }
            
            if (_config.DryRun)
            {
                Console2.WriteHeader("Dry Run Complete");
                return 0;
            }
            
            // Run migration
            Console2.WriteHeader("Migration Progress");
            
            var overallSw = Stopwatch.StartNew();
            var totalRowsMigrated = 0L;
            var monthsProcessed = 0;
            var currentMonth = startMonth;
            
            while (currentMonth <= endMonth)
            {
                var result = await MigrateMonthAsync(currentMonth);
                
                if (!result.Success)
                {
                    var attempts = _progress.GetFailedAttempts(currentMonth);
                    if (attempts >= 3)
                    {
                        Console.WriteLine();
                        Console2.WriteError($"Month {currentMonth} failed 3 times. Stopping migration.");
                        return 1;
                    }
                    
                    Console2.WriteWarning($"Month {currentMonth} failed (attempt {attempts}/3). Will retry...");
                    continue; // Retry same month
                }
                
                totalRowsMigrated += result.RowsMigrated;
                monthsProcessed++;
                currentMonth = currentMonth.AddMonths(1);
            }
            
            overallSw.Stop();
            
            // Run verification
            Console2.WriteHeader("Verification");
            var verificationPassed = await RunVerificationAsync();
            
            // Summary
            Console2.WriteHeader("Migration Complete");
            Console2.WriteInfo($"Duration: {Console2.FormatDuration(overallSw.Elapsed)}");
            Console2.WriteInfo($"Rows migrated: {Console2.FormatNumber(totalRowsMigrated)}");
            Console2.WriteInfo($"Months processed: {monthsProcessed}");
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
        
        YearMonth? startMonth = null;
        YearMonth? endMonth = null;
        var batchSize = 100_000;
        var verifyOnly = false;
        var dryRun = false;
        var reset = false;
        
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--start-month" when i + 1 < args.Length:
                    startMonth = YearMonth.Parse(args[++i]);
                    break;
                case "--end-month" when i + 1 < args.Length:
                    endMonth = YearMonth.Parse(args[++i]);
                    break;
                case "--batch-size" when i + 1 < args.Length:
                    batchSize = int.Parse(args[++i]);
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
            StartMonth = startMonth,
            EndMonth = endMonth,
            BatchSize = batchSize,
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
  --start-month YYYY-MM   First month to migrate (default: auto-detect)
  --end-month YYYY-MM     Last month to migrate (default: current month)
  --batch-size N          Rows per batch insert (default: 100000)
  --verify-only           Only run verification, skip migration
  --dry-run               Show plan without executing
  --reset                 Clear progress file and start fresh
  --help, -h              Show this help message
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
    
    private async Task<(DateTime MinDate, DateTime MaxDate, long TotalRows)> GetPostgresDataRangeAsync()
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(
            "SELECT MIN(date), MAX(date), COUNT(*) FROM daily_downloads", conn);
        cmd.CommandTimeout = 600; // 10 minutes for large tables
        
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        
        var minDate = reader.GetDateTime(0);
        var maxDate = reader.GetDateTime(1);
        var totalRows = reader.GetInt64(2);
        
        return (minDate, maxDate, totalRows);
    }
    
    private async Task<(bool Success, long RowsMigrated)> MigrateMonthAsync(YearMonth month)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            // Get PostgreSQL row count for this month
            var pgCount = await GetPostgresMonthCountAsync(month);
            
            if (pgCount == 0)
            {
                Console2.ClearLine();
                Console2.WriteInfo($"[{month}] No data in PostgreSQL, skipping");
                _progress.RecordSuccess(month, 0, 0);
                return (true, 0);
            }
            
            // Get ClickHouse row count for this month
            var chCount = await GetClickHouseMonthCountAsync(month);
            
            if (chCount == pgCount)
            {
                Console2.ClearLine();
                Console2.WriteSuccess($"[{month}] Already migrated ({Console2.FormatNumber(pgCount)} rows)");
                _progress.RecordSuccess(month, 0, 0);
                return (true, 0);
            }
            
            if (chCount > 0)
            {
                // Partial data exists, delete and re-migrate
                Console2.ClearLine();
                Console2.WriteWarning($"[{month}] Partial data found ({Console2.FormatNumber(chCount)}/{Console2.FormatNumber(pgCount)}), deleting...");
                await DeleteClickHouseMonthAsync(month);
            }
            
            // Stream from PostgreSQL and batch insert to ClickHouse
            var rowsMigrated = await StreamMigrateMonthAsync(month, pgCount);
            
            sw.Stop();
            
            // Verify
            var chCountAfter = await GetClickHouseMonthCountAsync(month);
            if (chCountAfter != pgCount)
            {
                Console2.ClearLine();
                Console2.WriteError($"[{month}] Row count mismatch after migration: PG={pgCount}, CH={chCountAfter}");
                _progress.RecordFailure(month);
                return (false, 0);
            }
            
            Console2.ClearLine();
            Console2.WriteSuccess($"[{month}] Complete | {Console2.FormatNumber(rowsMigrated)} rows | {Console2.FormatDuration(sw.Elapsed)}");
            
            _progress.RecordSuccess(month, rowsMigrated, sw.ElapsedMilliseconds);
            return (true, rowsMigrated);
        }
        catch (Exception ex)
        {
            Console2.ClearLine();
            Console2.WriteError($"[{month}] Error: {ex.Message}");
            _progress.RecordFailure(month);
            return (false, 0);
        }
    }
    
    private async Task<long> GetPostgresMonthCountAsync(YearMonth month)
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM daily_downloads WHERE date >= @start AND date < @end", conn);
        cmd.Parameters.AddWithValue("start", month.FirstDay);
        cmd.Parameters.AddWithValue("end", month.NextMonthFirstDay);
        cmd.CommandTimeout = 300;
        
        var result = await cmd.ExecuteScalarAsync();
        return (long)(result ?? 0L);
    }
    
    private async Task<long> GetClickHouseMonthCountAsync(YearMonth month)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count() FROM daily_downloads WHERE date >= '{month.FirstDay:yyyy-MM-dd}' AND date < '{month.NextMonthFirstDay:yyyy-MM-dd}'";
        
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }
    
    private async Task DeleteClickHouseMonthAsync(YearMonth month)
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE daily_downloads DELETE WHERE date >= '{month.FirstDay:yyyy-MM-dd}' AND date < '{month.NextMonthFirstDay:yyyy-MM-dd}'";
        
        await cmd.ExecuteNonQueryAsync();
        
        // Wait for mutation to complete
        await Task.Delay(1000);
    }
    
    private async Task<long> StreamMigrateMonthAsync(YearMonth month, long expectedRows)
    {
        await using var pgConn = new NpgsqlConnection(_config.PostgresConnectionString);
        await pgConn.OpenAsync();
        
        await using var pgCmd = new NpgsqlCommand(
            "SELECT LOWER(package_id), date, download_count FROM daily_downloads WHERE date >= @start AND date < @end ORDER BY date, package_id",
            pgConn);
        pgCmd.Parameters.AddWithValue("start", month.FirstDay);
        pgCmd.Parameters.AddWithValue("end", month.NextMonthFirstDay);
        pgCmd.CommandTimeout = 3600; // 1 hour for large months
        
        await using var reader = await pgCmd.ExecuteReaderAsync();
        
        var batch = new List<object[]>(_config.BatchSize);
        var totalRows = 0L;
        var sw = Stopwatch.StartNew();
        var lastReportTime = sw.Elapsed;
        
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
                
                // Report progress every 500ms
                if (sw.Elapsed - lastReportTime > TimeSpan.FromMilliseconds(500))
                {
                    var rate = totalRows / sw.Elapsed.TotalSeconds;
                    var percent = (double)totalRows / expectedRows * 100;
                    Console2.WriteProgress(month.ToString(), "Migrating", percent, totalRows, expectedRows, rate);
                    lastReportTime = sw.Elapsed;
                }
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
        
        var pgTotal = (await GetPostgresDataRangeAsync()).TotalRows;
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
        
        // Level 2: Per-month row counts
        Console.WriteLine();
        Console.WriteLine("  Level 2: Per-Month Row Counts");
        
        var pgMonthCounts = await GetPostgresMonthCountsAsync();
        var chMonthCounts = await GetClickHouseMonthCountsAsync();
        
        var monthMismatches = 0;
        foreach (var (month, pgCount) in pgMonthCounts)
        {
            var chCount = chMonthCounts.GetValueOrDefault(month, 0);
            if (pgCount != chCount)
            {
                Console2.WriteError($"  {month}: PG={Console2.FormatNumber(pgCount)}, CH={Console2.FormatNumber(chCount)}");
                monthMismatches++;
                allPassed = false;
            }
        }
        
        if (monthMismatches == 0)
        {
            Console2.WriteSuccess($"All {pgMonthCounts.Count} months match");
        }
        else
        {
            Console2.WriteError($"{monthMismatches} months have mismatched counts");
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
    
    private async Task<long> GetClickHouseTotalCountAsync()
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count() FROM daily_downloads";
        
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt64(result ?? 0L);
    }
    
    private async Task<Dictionary<string, long>> GetPostgresMonthCountsAsync()
    {
        await using var conn = new NpgsqlConnection(_config.PostgresConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = new NpgsqlCommand(
            "SELECT TO_CHAR(date, 'YYYY-MM') as month, COUNT(*) FROM daily_downloads GROUP BY month ORDER BY month", conn);
        cmd.CommandTimeout = 600;
        
        var result = new Dictionary<string, long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = reader.GetInt64(1);
        }
        return result;
    }
    
    private async Task<Dictionary<string, long>> GetClickHouseMonthCountsAsync()
    {
        await using var conn = new ClickHouseConnection(_config.ClickHouseConnectionString);
        await conn.OpenAsync();
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT formatDateTime(date, '%Y-%m') as month, count() FROM daily_downloads GROUP BY month ORDER BY month";
        
        var result = new Dictionary<string, long>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result[reader.GetString(0)] = Convert.ToInt64(reader.GetValue(1));
        }
        return result;
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
