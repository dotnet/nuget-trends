#!/usr/bin/env dotnet

#:package Npgsql@9.0.2
#:package ClickHouse.Client@7.14.0
#:package Testcontainers@4.9.0
#:package Testcontainers.PostgreSql@4.9.0
#:package Testcontainers.ClickHouse@4.9.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;
using Npgsql;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;

// ============================================================================
// Migration Test Harness
// ============================================================================
// Sets up PostgreSQL and ClickHouse containers, generates mock data with
// realistic skewed distribution, runs the migration script, and validates.
//
// Usage:
//   ./MigrationTestHarness.cs [scenario]
//
// Scenarios:
//   quick       100 packages, 100K rows (correctness test)
//   small       1,000 packages, 1M rows (small stress test)
//   medium      10,000 packages, 10M rows (medium stress test)
//   full        100,000 packages, 50M rows (full scale test)
//
// Options:
//   --skip-migration    Only generate data, don't run migration
//   --skip-validation   Skip validation after migration
//   --keep-containers   Don't stop containers after test
//   --seed N            Random seed for reproducibility (default: 42)
// ============================================================================

var scenario = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "quick";
var skipMigration = args.Contains("--skip-migration");
var skipValidation = args.Contains("--skip-validation");
var keepContainers = args.Contains("--keep-containers");
var seed = 42;

for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--seed" && i + 1 < args.Length)
    {
        seed = int.Parse(args[++i]);
    }
}

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine(@"
Migration Test Harness
======================

Sets up PostgreSQL and ClickHouse containers, generates mock data with
realistic skewed distribution, runs the migration script, and validates.

Usage:
  ./MigrationTestHarness.cs [scenario] [options]

Scenarios:
  quick       100 packages, ~100K rows (correctness test) [default]
  small       1,000 packages, ~1M rows (small stress test)
  medium      10,000 packages, ~10M rows (medium stress test)
  full        100,000 packages, ~50M rows (full scale test)

Options:
  --skip-migration    Only generate data, don't run migration
  --skip-validation   Skip validation after migration
  --keep-containers   Don't stop containers after test
  --seed N            Random seed for reproducibility (default: 42)
  --help, -h          Show this help message

Examples:
  ./MigrationTestHarness.cs                    # Quick test
  ./MigrationTestHarness.cs medium             # Medium stress test
  ./MigrationTestHarness.cs full --seed 123    # Full test with custom seed
  ./MigrationTestHarness.cs quick --keep-containers  # Keep containers running
");
    return 0;
}

var harness = new MigrationTestHarness(scenario, seed, skipMigration, skipValidation, keepContainers);
return await harness.RunAsync();

// ============================================================================
// Test Scenarios
// ============================================================================

public record TestScenario(
    string Name,
    int TotalPackages,
    long TargetTotalRows,
    PackageDistribution[] Distribution);

public record PackageDistribution(
    string Category,
    int PackageCount,
    int RowsPerPackage);

public static class Scenarios
{
    public static TestScenario Get(string name) => name.ToLowerInvariant() switch
    {
        "quick" => new TestScenario(
            "Quick (Correctness)",
            TotalPackages: 100,
            TargetTotalRows: 100_000,
            Distribution:
            [
                new("Massive", 1, 20_000),      // 1 package with 20K rows
                new("Large", 4, 5_000),         // 4 packages with 5K rows each
                new("Medium", 15, 2_000),       // 15 packages with 2K rows each
                new("Small", 30, 1_000),        // 30 packages with 1K rows each
                new("Tiny", 50, 400),           // 50 packages with 400 rows each
            ]),

        "small" => new TestScenario(
            "Small Stress",
            TotalPackages: 1_000,
            TargetTotalRows: 1_000_000,
            Distribution:
            [
                new("Massive", 1, 100_000),     // 1 package with 100K rows
                new("VeryLarge", 4, 50_000),    // 4 packages with 50K rows each
                new("Large", 15, 20_000),       // 15 packages with 20K rows each
                new("Medium", 80, 5_000),       // 80 packages with 5K rows each
                new("Small", 200, 1_000),       // 200 packages with 1K rows each
                new("Tiny", 700, 143),          // 700 packages with ~143 rows each
            ]),

        "medium" => new TestScenario(
            "Medium Stress",
            TotalPackages: 10_000,
            TargetTotalRows: 10_000_000,
            Distribution:
            [
                new("Massive", 1, 500_000),     // 1 package with 500K rows
                new("VeryLarge", 9, 100_000),   // 9 packages with 100K rows each
                new("Large", 90, 30_000),       // 90 packages with 30K rows each
                new("Medium", 900, 5_000),      // 900 packages with 5K rows each
                new("Small", 4_000, 500),       // 4K packages with 500 rows each
                new("Tiny", 5_000, 100),        // 5K packages with 100 rows each
            ]),

        "full" => new TestScenario(
            "Full Scale",
            TotalPackages: 100_000,
            TargetTotalRows: 50_000_000,
            Distribution:
            [
                new("Massive", 1, 5_000_000),   // 1 package with 5M rows
                new("VeryLarge", 9, 500_000),   // 9 packages with 500K rows each
                new("Large", 90, 100_000),      // 90 packages with 100K rows each
                new("Medium", 900, 10_000),     // 900 packages with 10K rows each
                new("Small", 9_000, 1_000),     // 9K packages with 1K rows each
                new("Tiny", 90_000, 150),       // 90K packages with 150 rows each
            ]),

        _ => throw new ArgumentException($"Unknown scenario: {name}")
    };
}

// ============================================================================
// Mock Data Generator
// ============================================================================

public class MockDataGenerator
{
    private readonly Random _random;
    private readonly TestScenario _scenario;
    private readonly DateOnly _endDate;
    private readonly DateOnly _startDate;

    public MockDataGenerator(TestScenario scenario, int seed)
    {
        _scenario = scenario;
        _random = new Random(seed);
        _endDate = DateOnly.FromDateTime(DateTime.UtcNow);
        _startDate = _endDate.AddYears(-5); // 5 years of history
    }

    public IEnumerable<PackageData> GeneratePackages()
    {
        var packageIndex = 0;

        foreach (var dist in _scenario.Distribution)
        {
            for (var i = 0; i < dist.PackageCount; i++)
            {
                var packageId = GeneratePackageId(packageIndex, dist.Category);
                var downloadCount = CalculateLatestDownloadCount(dist.RowsPerPackage);

                yield return new PackageData(
                    PackageId: packageId,
                    LatestDownloadCount: downloadCount,
                    RowCount: dist.RowsPerPackage,
                    Category: dist.Category);

                packageIndex++;
            }
        }
    }

    private string GeneratePackageId(int index, string category)
    {
        // Generate realistic package names with some case variations
        var prefixes = new[] { "Microsoft", "System", "Newtonsoft", "Azure", "AWS", "Google", "Sentry", "NLog", "Serilog", "AutoMapper" };
        var suffixes = new[] { "Core", "Extensions", "Client", "SDK", "Api", "Common", "Utils", "Helpers", "Abstractions", "AspNetCore" };

        var prefix = prefixes[_random.Next(prefixes.Length)];
        var suffix = suffixes[_random.Next(suffixes.Length)];

        // Add case variations for some packages (simulates real-world case changes)
        var caseVariant = _random.Next(10);
        var name = $"{prefix}.{suffix}.Test{index:D6}";

        return caseVariant switch
        {
            0 => name.ToUpperInvariant(),
            1 => name.ToLowerInvariant(),
            _ => name
        };
    }

    private long CalculateLatestDownloadCount(int rowCount)
    {
        // More rows = older package = more total downloads
        // Add some randomness
        var baseCount = rowCount * 1000L;
        var variance = _random.NextDouble() * 0.5 + 0.75; // 0.75 to 1.25
        return (long)(baseCount * variance);
    }

    public IEnumerable<DailyDownloadRow> GenerateDailyDownloads(PackageData package)
    {
        var currentDate = _endDate;
        var baseDownloads = package.LatestDownloadCount / package.RowCount;
        var rowsGenerated = 0;

        while (rowsGenerated < package.RowCount && currentDate >= _startDate)
        {
            // Add some growth pattern (older dates have fewer downloads)
            var ageFactor = 1.0 - (rowsGenerated / (double)package.RowCount * 0.3); // 30% decline over time
            var dailyVariance = 0.8 + _random.NextDouble() * 0.4; // 0.8 to 1.2
            var downloads = (long)(baseDownloads * ageFactor * dailyVariance);

            yield return new DailyDownloadRow(
                PackageId: package.PackageId,
                Date: currentDate,
                DownloadCount: Math.Max(0, downloads));

            currentDate = currentDate.AddDays(-1);
            rowsGenerated++;
        }
    }
}

public record PackageData(
    string PackageId,
    long LatestDownloadCount,
    int RowCount,
    string Category);

public record DailyDownloadRow(
    string PackageId,
    DateOnly Date,
    long DownloadCount);

// ============================================================================
// Migration Test Harness
// ============================================================================

public class MigrationTestHarness
{
    private const string ClickHouseUser = "clickhouse";
    private const string ClickHousePass = "clickhouse";

    private readonly TestScenario _scenario;
    private readonly int _seed;
    private readonly bool _skipMigration;
    private readonly bool _skipValidation;
    private readonly bool _keepContainers;

    private PostgreSqlContainer? _postgresContainer;
    private ClickHouseContainer? _clickHouseContainer;

    private string PostgresConnectionString => _postgresContainer?.GetConnectionString()
        ?? throw new InvalidOperationException("PostgreSQL container not started");

    private string ClickHouseConnectionString =>
        $"Host={_clickHouseContainer?.Hostname};Port={_clickHouseContainer?.GetMappedPublicPort(8123)};Database=nugettrends;Username={ClickHouseUser};Password={ClickHousePass}";

    public MigrationTestHarness(string scenario, int seed, bool skipMigration, bool skipValidation, bool keepContainers)
    {
        _scenario = Scenarios.Get(scenario);
        _seed = seed;
        _skipMigration = skipMigration;
        _skipValidation = skipValidation;
        _keepContainers = keepContainers;
    }

    public async Task<int> RunAsync()
    {
        var sw = Stopwatch.StartNew();

        try
        {
            WriteHeader($"Migration Test: {_scenario.Name}");
            WriteInfo($"Packages: {_scenario.TotalPackages:N0}");
            WriteInfo($"Target rows: {_scenario.TargetTotalRows:N0}");
            WriteInfo($"Seed: {_seed}");

            // Start containers
            WriteHeader("Starting Containers");
            await StartContainersAsync();

            // Create schemas
            WriteHeader("Creating Schemas");
            await CreatePostgresSchemaAsync();
            await CreateClickHouseSchemaAsync();

            // Generate and insert mock data
            WriteHeader("Generating Mock Data");
            var stats = await GenerateAndInsertDataAsync();

            WriteSuccess($"Generated {stats.TotalRows:N0} rows for {stats.PackageCount:N0} packages");
            WriteInfo($"Data generation took: {stats.Duration}");

            if (_skipMigration)
            {
                WriteWarning("Skipping migration (--skip-migration)");
                PrintConnectionStrings();
                return 0;
            }

            // Run migration
            WriteHeader("Running Migration");
            var migrationResult = await RunMigrationAsync();

            if (!migrationResult.Success)
            {
                WriteError($"Migration failed with exit code {migrationResult.ExitCode}");
                return 1;
            }

            WriteSuccess($"Migration completed in {migrationResult.Duration}");

            if (_skipValidation)
            {
                WriteWarning("Skipping validation (--skip-validation)");
                return 0;
            }

            // Validate
            WriteHeader("Validating Migration");
            var validationResult = await ValidateMigrationAsync(stats);

            if (!validationResult.AllPassed)
            {
                WriteError("Validation failed!");
                return 1;
            }

            WriteSuccess("All validation checks passed!");

            // Summary
            WriteHeader("Test Complete");
            WriteInfo($"Total duration: {sw.Elapsed}");

            return 0;
        }
        catch (Exception ex)
        {
            WriteError($"Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            if (!_keepContainers)
            {
                await StopContainersAsync();
            }
            else
            {
                PrintConnectionStrings();
            }
        }
    }

    private async Task StartContainersAsync()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .Build();

        _clickHouseContainer = new ClickHouseBuilder()
            .WithImage("clickhouse/clickhouse-server:25.11.5")
            .WithUsername(ClickHouseUser)
            .WithPassword(ClickHousePass)
            .Build();

        var sw = Stopwatch.StartNew();

        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _clickHouseContainer.StartAsync());

        WriteSuccess($"Containers started in {sw.Elapsed.TotalSeconds:F1}s");
    }

    private async Task StopContainersAsync()
    {
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }

        if (_clickHouseContainer != null)
        {
            await _clickHouseContainer.DisposeAsync();
        }
    }

    private async Task CreatePostgresSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();

        // Create daily_downloads table (matches EF Core schema)
        await using var cmd = new NpgsqlCommand(@"
            CREATE TABLE daily_downloads (
                package_id TEXT NOT NULL,
                date TIMESTAMP NOT NULL,
                download_count BIGINT,
                PRIMARY KEY (package_id, date)
            );

            CREATE TABLE package_downloads (
                package_id TEXT NOT NULL PRIMARY KEY,
                package_id_lowered TEXT NOT NULL,
                latest_download_count BIGINT,
                latest_download_count_checked_utc TIMESTAMP NOT NULL,
                icon_url TEXT
            );

            CREATE UNIQUE INDEX ix_package_downloads_package_id_lowered
            ON package_downloads (package_id_lowered);
        ", conn);

        await cmd.ExecuteNonQueryAsync();
        WriteSuccess("PostgreSQL schema created");
    }

    private string ClickHouseAdminConnectionString =>
        $"Host={_clickHouseContainer?.Hostname};Port={_clickHouseContainer?.GetMappedPublicPort(8123)};Username={ClickHouseUser};Password={ClickHousePass}";

    private async Task CreateClickHouseSchemaAsync()
    {
        await using var conn = new ClickHouseConnection(ClickHouseAdminConnectionString);
        await conn.OpenAsync();

        // Create database
        await using var createDbCmd = conn.CreateCommand();
        createDbCmd.CommandText = "CREATE DATABASE IF NOT EXISTS nugettrends";
        await createDbCmd.ExecuteNonQueryAsync();

        // Create table
        await using var createTableCmd = conn.CreateCommand();
        createTableCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS nugettrends.daily_downloads (
                package_id String,
                date Date,
                download_count UInt64
            )
            ENGINE = ReplacingMergeTree()
            PARTITION BY toYYYYMM(date)
            ORDER BY (package_id, date)
        ";
        await createTableCmd.ExecuteNonQueryAsync();

        WriteSuccess("ClickHouse schema created");
    }

    private async Task<DataGenerationStats> GenerateAndInsertDataAsync()
    {
        var sw = Stopwatch.StartNew();
        var generator = new MockDataGenerator(_scenario, _seed);
        var packages = generator.GeneratePackages().ToList();

        var totalRows = 0L;
        var batchSize = 50_000;

        await using var pgConn = new NpgsqlConnection(PostgresConnectionString);
        await pgConn.OpenAsync();

        // Insert packages into package_downloads
        WriteInfo("Inserting package metadata...");
        foreach (var package in packages)
        {
            await using var cmd = new NpgsqlCommand(@"
                INSERT INTO package_downloads (package_id, package_id_lowered, latest_download_count, latest_download_count_checked_utc)
                VALUES (@id, @idLower, @count, @checked)", pgConn);

            cmd.Parameters.AddWithValue("id", package.PackageId);
            cmd.Parameters.AddWithValue("idLower", package.PackageId.ToLowerInvariant());
            cmd.Parameters.AddWithValue("count", package.LatestDownloadCount);
            cmd.Parameters.AddWithValue("checked", DateTime.UtcNow.AddDays(-1));

            await cmd.ExecuteNonQueryAsync();
        }

        // Insert daily downloads in batches
        WriteInfo("Inserting daily downloads...");
        var batch = new List<DailyDownloadRow>(batchSize);
        var packagesProcessed = 0;

        foreach (var package in packages)
        {
            foreach (var row in generator.GenerateDailyDownloads(package))
            {
                batch.Add(row);

                if (batch.Count >= batchSize)
                {
                    await InsertDailyDownloadsBatchAsync(pgConn, batch);
                    totalRows += batch.Count;
                    batch.Clear();

                    // Progress update
                    var percent = (double)packagesProcessed / packages.Count * 100;
                    Console.Write($"\r  Progress: {percent:F1}% ({totalRows:N0} rows)   ");
                }
            }

            packagesProcessed++;
        }

        // Insert remaining
        if (batch.Count > 0)
        {
            await InsertDailyDownloadsBatchAsync(pgConn, batch);
            totalRows += batch.Count;
        }

        Console.WriteLine();
        sw.Stop();

        return new DataGenerationStats(packages.Count, totalRows, sw.Elapsed);
    }

    private async Task InsertDailyDownloadsBatchAsync(NpgsqlConnection conn, List<DailyDownloadRow> batch)
    {
        await using var writer = await conn.BeginBinaryImportAsync(
            "COPY daily_downloads (package_id, date, download_count) FROM STDIN (FORMAT BINARY)");

        foreach (var row in batch)
        {
            await writer.StartRowAsync();
            await writer.WriteAsync(row.PackageId, NpgsqlTypes.NpgsqlDbType.Text);
            await writer.WriteAsync(row.Date.ToDateTime(TimeOnly.MinValue), NpgsqlTypes.NpgsqlDbType.Timestamp);
            await writer.WriteAsync(row.DownloadCount, NpgsqlTypes.NpgsqlDbType.Bigint);
        }

        await writer.CompleteAsync();
    }

    private async Task<MigrationResult> RunMigrationAsync()
    {
        var sw = Stopwatch.StartNew();

        // Set environment variables for the migration script
        Environment.SetEnvironmentVariable("PG_CONNECTION_STRING", PostgresConnectionString);
        Environment.SetEnvironmentVariable("CH_CONNECTION_STRING", ClickHouseConnectionString);

        // Find the migration script - try multiple locations
        var possiblePaths = new[]
        {
            // Relative to this script's location
            Path.Combine(AppContext.BaseDirectory, "..", "migrate-daily-downloads-to-clickhouse.cs"),
            // Relative to current working directory
            Path.Combine(Directory.GetCurrentDirectory(), "..", "migrate-daily-downloads-to-clickhouse.cs"),
            // Direct path from repo root
            Path.Combine(Directory.GetCurrentDirectory(), "scripts", "migrate-daily-downloads-to-clickhouse.cs"),
            // If running from scripts/test-migration
            Path.Combine(Directory.GetCurrentDirectory(), "migrate-daily-downloads-to-clickhouse.cs"),
        };

        var migrationScript = possiblePaths
            .Select(Path.GetFullPath)
            .FirstOrDefault(File.Exists);

        if (migrationScript == null)
        {
            WriteError("Migration script not found. Tried:");
            foreach (var p in possiblePaths)
            {
                WriteInfo($"  {Path.GetFullPath(p)}");
            }
            return new MigrationResult(false, 1, sw.Elapsed, "Script not found");
        }

        WriteInfo($"Running: dotnet {migrationScript}");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{migrationScript}\" --reset",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment =
            {
                ["PG_CONNECTION_STRING"] = PostgresConnectionString,
                ["CH_CONNECTION_STRING"] = ClickHouseConnectionString
            }
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return new MigrationResult(false, -1, sw.Elapsed, "Failed to start process");
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();
        sw.Stop();

        // Print output
        if (!string.IsNullOrEmpty(output))
        {
            foreach (var line in output.Split('\n').Take(50))
            {
                Console.WriteLine($"  {line}");
            }
        }

        if (!string.IsNullOrEmpty(error))
        {
            WriteError(error);
        }

        return new MigrationResult(
            process.ExitCode == 0,
            process.ExitCode,
            sw.Elapsed,
            output);
    }

    private async Task<ValidationResult> ValidateMigrationAsync(DataGenerationStats expectedStats)
    {
        var checks = new List<(string Name, bool Passed, string Details)>();

        // Check 1: Total row count in PostgreSQL
        var pgRowCount = await GetPostgresRowCountAsync();
        checks.Add(("PostgreSQL row count", pgRowCount == expectedStats.TotalRows,
            $"Expected {expectedStats.TotalRows:N0}, got {pgRowCount:N0}"));

        // Check 2: Total row count in ClickHouse
        var chRowCount = await GetClickHouseRowCountAsync();
        checks.Add(("ClickHouse row count", chRowCount == expectedStats.TotalRows,
            $"Expected {expectedStats.TotalRows:N0}, got {chRowCount:N0}"));

        // Check 3: Counts match
        checks.Add(("Row counts match", pgRowCount == chRowCount,
            $"PG={pgRowCount:N0}, CH={chRowCount:N0}"));

        // Check 4: Package counts
        var pgPackageCount = await GetPostgresPackageCountAsync();
        var chPackageCount = await GetClickHousePackageCountAsync();
        checks.Add(("Package counts match", pgPackageCount == chPackageCount,
            $"PG={pgPackageCount:N0}, CH={chPackageCount:N0}"));

        // Check 5: Sample package verification (10 random packages)
        var samplePackages = await GetSamplePackagesAsync(10);
        var samplesPassed = 0;

        foreach (var pkg in samplePackages)
        {
            var (pgRows, pgSum) = await GetPostgresPackageStatsAsync(pkg);
            var (chRows, chSum) = await GetClickHousePackageStatsAsync(pkg);

            if (pgRows == chRows && pgSum == chSum)
            {
                samplesPassed++;
            }
            else
            {
                WriteWarning($"  Package '{pkg}' mismatch: PG({pgRows:N0} rows, sum={pgSum:N0}) vs CH({chRows:N0} rows, sum={chSum:N0})");
            }
        }

        checks.Add(("Sample package verification", samplesPassed == samplePackages.Count,
            $"{samplesPassed}/{samplePackages.Count} packages verified"));

        // Check 6: Case insensitivity (query with different case)
        var caseTestPackage = samplePackages.FirstOrDefault();
        if (caseTestPackage != null)
        {
            var upperResult = await GetClickHousePackageStatsAsync(caseTestPackage.ToUpperInvariant());
            var lowerResult = await GetClickHousePackageStatsAsync(caseTestPackage.ToLowerInvariant());
            checks.Add(("Case insensitive queries", upperResult == lowerResult,
                $"Upper: {upperResult.Rows:N0} rows, Lower: {lowerResult.Rows:N0} rows"));
        }

        // Print results
        foreach (var (name, passed, details) in checks)
        {
            if (passed)
            {
                WriteSuccess($"{name}: {details}");
            }
            else
            {
                WriteError($"{name}: {details}");
            }
        }

        return new ValidationResult(checks.All(c => c.Passed));
    }

    private async Task<long> GetPostgresRowCountAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM daily_downloads", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> GetClickHouseRowCountAsync()
    {
        await using var conn = new ClickHouseConnection(ClickHouseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count() FROM daily_downloads";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> GetPostgresPackageCountAsync()
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT COUNT(DISTINCT package_id) FROM daily_downloads", conn);
        return (long)(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<long> GetClickHousePackageCountAsync()
    {
        await using var conn = new ClickHouseConnection(ClickHouseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(DISTINCT package_id) FROM daily_downloads";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }

    private async Task<List<string>> GetSamplePackagesAsync(int count)
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"SELECT package_id FROM package_downloads ORDER BY RANDOM() LIMIT {count}", conn);

        var packages = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            packages.Add(reader.GetString(0));
        }
        return packages;
    }

    private async Task<(long Rows, long Sum)> GetPostgresPackageStatsAsync(string packageId)
    {
        await using var conn = new NpgsqlConnection(PostgresConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*), COALESCE(SUM(download_count), 0) FROM daily_downloads WHERE LOWER(package_id) = LOWER(@id)", conn);
        cmd.Parameters.AddWithValue("id", packageId);

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private async Task<(long Rows, long Sum)> GetClickHousePackageStatsAsync(string packageId)
    {
        await using var conn = new ClickHouseConnection(ClickHouseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT count(), sum(download_count) FROM daily_downloads WHERE package_id = '{packageId.ToLower(CultureInfo.InvariantCulture)}'";

        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (Convert.ToInt64(reader.GetValue(0)), Convert.ToInt64(reader.GetValue(1)));
    }

    private void PrintConnectionStrings()
    {
        WriteHeader("Container Connection Strings");
        WriteInfo($"PostgreSQL: {PostgresConnectionString}");
        WriteInfo($"ClickHouse: {ClickHouseConnectionString}");
        WriteInfo("");
        WriteInfo("To connect manually:");
        WriteInfo($"  psql \"{PostgresConnectionString}\"");
        WriteInfo($"  clickhouse-client --host {_clickHouseContainer?.Hostname} --port {_clickHouseContainer?.GetMappedPublicPort(9000)}");
    }

    // Console helpers
    private static void WriteHeader(string text)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(text);
        Console.WriteLine(new string('=', text.Length));
        Console.ResetColor();
    }

    private static void WriteSuccess(string text)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write("[OK] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    private static void WriteError(string text)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Write("[ERR] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    private static void WriteWarning(string text)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("[WARN] ");
        Console.ResetColor();
        Console.WriteLine(text);
    }

    private static void WriteInfo(string text)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  {text}");
        Console.ResetColor();
    }
}

// ============================================================================
// Result Records
// ============================================================================

public record DataGenerationStats(int PackageCount, long TotalRows, TimeSpan Duration);
public record MigrationResult(bool Success, int ExitCode, TimeSpan Duration, string Output);
public record ValidationResult(bool AllPassed);
