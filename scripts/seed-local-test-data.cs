#!/usr/bin/env dotnet

#:package ClickHouse.Driver@0.9.0
#:package Npgsql@9.0.3
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Globalization;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Copy;
using Npgsql;

// ============================================================================
// Seed PostgreSQL + ClickHouse with test data for local development
// ============================================================================
// Inserts packages into PostgreSQL (for search) and daily download history
// into ClickHouse (for charts and trending). The ClickHouse materialized view
// automatically populates weekly_downloads from daily_downloads inserts.
//
// Environment Variables:
//   PG_CONNECTION_STRING - PostgreSQL connection string
//   CH_CONNECTION_STRING - ClickHouse connection string
//
// Usage:
//   ./seed-local-test-data.cs
//   ./seed-local-test-data.cs --months 24
//   ./seed-local-test-data.cs --clear
// ============================================================================

var pgConnectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING")
    ?? "Host=localhost;Port=5432;Database=nugettrends;Username=postgres;Password=postgres";
var chConnectionString = Environment.GetEnvironmentVariable("CH_CONNECTION_STRING")
    ?? "Host=localhost;Port=8123;Database=nugettrends";

var months = 24;
var clear = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--months" when i + 1 < args.Length:
            months = int.Parse(args[++i]);
            break;
        case "--clear":
            clear = true;
            break;
        case "--help" or "-h":
            Console.WriteLine("""

Seed PostgreSQL and ClickHouse with test data for local development.

Usage: ./seed-local-test-data.cs [options]

Options:
  --months N    Number of months of history to generate (default: 24)
  --clear       Clear existing seed data before inserting
  --help, -h    Show this help message

Environment Variables:
  PG_CONNECTION_STRING  PostgreSQL connection string
  CH_CONNECTION_STRING  ClickHouse connection string

Examples:
  ./seed-local-test-data.cs                   # Seed 24 months of data
  ./seed-local-test-data.cs --months 12       # Seed 12 months
  ./seed-local-test-data.cs --clear           # Clear and reseed
""");
            return 0;
    }
}

// --- Package definitions ---
// Each package has: base daily downloads, daily growth rate, noise factor
var packages = new Dictionary<string, (string DisplayId, long BaseDailyDownloads, double DailyGrowth, double Noise, string? IconUrl)>
{
    ["sentry"]               = ("Sentry",               40_000,  0.003,  0.15, "https://api.nuget.org/v3-flatcontainer/sentry/5.6.0/icon"),
    ["sentry.aspnetcore"]    = ("Sentry.AspNetCore",     7_000,  0.004,  0.16, "https://api.nuget.org/v3-flatcontainer/sentry.aspnetcore/5.6.0/icon"),
    ["sentry.serilog"]       = ("Sentry.Serilog",        5_000,  0.004,  0.18, "https://api.nuget.org/v3-flatcontainer/sentry.serilog/5.6.0/icon"),
    ["newtonsoft.json"]      = ("Newtonsoft.Json",      900_000,  0.0005, 0.08, null),
    ["serilog"]              = ("Serilog",               75_000,  0.002,  0.10, "https://api.nuget.org/v3-flatcontainer/serilog/4.2.0/icon"),
    ["serilog.sinks.console"]= ("Serilog.Sinks.Console", 50_000,  0.002,  0.12, null),
    ["polly"]                = ("Polly",                 55_000,  0.003,  0.14, "https://api.nuget.org/v3-flatcontainer/polly/8.5.2/icon"),
    ["mediatr"]              = ("MediatR",               42_000,  0.003,  0.15, "https://api.nuget.org/v3-flatcontainer/mediatr/12.4.1/icon"),
    ["dapper"]               = ("Dapper",                75_000,  0.0012, 0.10, null),
    ["fluentvalidation"]     = ("FluentValidation",      95_000,  0.002,  0.12, null),
    ["automapper"]           = ("AutoMapper",           110_000,  0.0008, 0.10, null),
    ["moq"]                  = ("Moq",                  120_000,  0.001,  0.12, null),
    ["xunit"]                = ("xunit",                100_000,  0.001,  0.10, null),
    ["npgsql"]               = ("Npgsql",                80_000,  0.002,  0.12, null),
    ["fluentassertions"]     = ("FluentAssertions",      70_000,  0.0015, 0.12, null),
    ["bogus"]                = ("Bogus",                 15_000,  0.004,  0.18, "https://api.nuget.org/v3-flatcontainer/bogus/35.6.1/icon"),
    ["benchmarkdotnet"]      = ("BenchmarkDotNet",       12_000,  0.003,  0.20, "https://api.nuget.org/v3-flatcontainer/benchmarkdotnet/0.14.0/icon"),
    ["humanizer.core"]       = ("Humanizer.Core",        35_000,  0.002,  0.14, null),
    ["castle.core"]          = ("Castle.Core",          150_000,  0.001,  0.10, null),
};

// "Trending" packages — only recent data (last 6 months) so they appear in the trending query.
// The trending query filters to packages first seen within 12 months with >= 1000 weekly downloads.
var trendingPackages = new Dictionary<string, (string DisplayId, long BaseDailyDownloads, double DailyGrowth, double Noise, string? IconUrl)>
{
    ["aspire.hosting"]          = ("Aspire.Hosting",          2_000,  0.008, 0.20, null),
    ["microsoft.extensions.ai"] = ("Microsoft.Extensions.AI", 3_000,  0.010, 0.18, null),
    ["scalar.aspnetcore"]       = ("Scalar.AspNetCore",       1_500,  0.012, 0.22, null),
    ["testcontainers"]          = ("Testcontainers",          2_500,  0.007, 0.18, null),
    ["opentelemetry.api"]       = ("OpenTelemetry.Api",       4_000,  0.006, 0.15, null),
    ["yarp.reverseproxy"]       = ("Yarp.ReverseProxy",       1_800,  0.009, 0.20, null),
    ["fluentresults"]           = ("FluentResults",           1_200,  0.011, 0.22, null),
    ["mapperly"]                = ("Mapperly",                1_000,  0.015, 0.25, null),
    ["wolverine"]               = ("Wolverine",                 800,  0.013, 0.25, null),
    ["refit"]                   = ("Refit",                   3_500,  0.005, 0.15, null),
};

// Merge trending into main package list for PostgreSQL seeding
foreach (var (k, v) in trendingPackages)
{
    packages[k] = v;
}

Console.WriteLine($"Seeding local test data ({months} months of history, {packages.Count} packages)");
Console.WriteLine($"  PostgreSQL: {pgConnectionString}");
Console.WriteLine($"  ClickHouse: {chConnectionString}");
Console.WriteLine();

// ==================== PostgreSQL ====================
Console.WriteLine("--- PostgreSQL ---");

await using var pgConn = new NpgsqlConnection(pgConnectionString);
await pgConn.OpenAsync();

if (clear)
{
    Console.WriteLine("  Clearing existing seed data...");
    var packageIds = string.Join(",", packages.Keys.Select(k => $"'{k}'"));
    await using var deleteCmd = pgConn.CreateCommand();
    deleteCmd.CommandText = $"DELETE FROM package_downloads WHERE package_id_lowered IN ({packageIds})";
    var deleted = await deleteCmd.ExecuteNonQueryAsync();
    Console.WriteLine($"  Deleted {deleted} rows from package_downloads");
}

Console.WriteLine("  Upserting packages into package_downloads...");
var upserted = 0;
foreach (var (loweredId, pkg) in packages)
{
    // Compute a plausible total download count from daily data
    var totalDownloads = pkg.BaseDailyDownloads * 365 * 3; // rough estimate

    await using var cmd = pgConn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO package_downloads (package_id, package_id_lowered, latest_download_count, latest_download_count_checked_utc, icon_url)
        VALUES (@id, @idLowered, @count, NOW(), @icon)
        ON CONFLICT (package_id) DO UPDATE SET
            latest_download_count = EXCLUDED.latest_download_count,
            latest_download_count_checked_utc = EXCLUDED.latest_download_count_checked_utc,
            icon_url = COALESCE(EXCLUDED.icon_url, package_downloads.icon_url)
        """;
    cmd.Parameters.AddWithValue("id", pkg.DisplayId);
    cmd.Parameters.AddWithValue("idLowered", loweredId);
    cmd.Parameters.AddWithValue("count", totalDownloads);
    cmd.Parameters.AddWithValue("icon", (object?)pkg.IconUrl ?? DBNull.Value);
    await cmd.ExecuteNonQueryAsync();
    upserted++;
}
Console.WriteLine($"  Upserted {upserted} packages");

// ==================== ClickHouse ====================
Console.WriteLine();
Console.WriteLine("--- ClickHouse ---");

await using var chConn = new ClickHouseConnection(chConnectionString);
await chConn.OpenAsync();

if (clear)
{
    Console.WriteLine("  Clearing existing seed data...");
    // Delete all seeded packages (both regular and trending)
    foreach (var packageId in packages.Keys)
    {
        await using var deleteCmd = chConn.CreateCommand();
        deleteCmd.CommandText = $"ALTER TABLE daily_downloads DELETE WHERE package_id = '{packageId}'";
        await deleteCmd.ExecuteNonQueryAsync();
    }
    // Also clear weekly_downloads for these packages
    foreach (var packageId in packages.Keys)
    {
        await using var deleteCmd = chConn.CreateCommand();
        deleteCmd.CommandText = $"ALTER TABLE weekly_downloads DELETE WHERE package_id = '{packageId}'";
        await deleteCmd.ExecuteNonQueryAsync();
    }
    Console.WriteLine("  Waiting for mutations to complete...");
    await Task.Delay(3000);
}

Console.WriteLine("  Generating daily download data...");
var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
var startDate = endDate.AddMonths(-months);
var trendingStartDate = endDate.AddMonths(-6); // Trending packages: only last 6 months
var random = new Random(42);

var allRows = new List<object[]>();

foreach (var (packageId, pkg) in packages)
{
    // Trending packages get a shorter date range so their first_seen is recent
    var pkgStartDate = trendingPackages.ContainsKey(packageId) ? trendingStartDate : startDate;
    var currentDate = pkgStartDate;
    var baseDownloads = (double)pkg.BaseDailyDownloads;

    while (currentDate <= endDate)
    {
        var dayIndex = currentDate.DayNumber - pkgStartDate.DayNumber;
        var growthFactor = Math.Exp(pkg.DailyGrowth * dayIndex);
        var weekendFactor = currentDate.DayOfWeek switch
        {
            DayOfWeek.Saturday => 0.6,
            DayOfWeek.Sunday => 0.5,
            _ => 1.0
        };
        var noise = 1.0 + (random.NextDouble() * 2 - 1) * pkg.Noise;
        var count = (long)(baseDownloads * growthFactor * weekendFactor * noise);
        if (count < 0) count = 0;

        allRows.Add([packageId, currentDate, (ulong)count]);
        currentDate = currentDate.AddDays(1);
    }
}

Console.WriteLine($"  Generated {allRows.Count:N0} rows");

// Bulk insert in batches
const int batchSize = 100_000;
var totalInserted = 0;

for (var offset = 0; offset < allRows.Count; offset += batchSize)
{
    var batch = allRows.Skip(offset).Take(batchSize).ToList();

    using var bulkCopy = new ClickHouseBulkCopy(chConn)
    {
        DestinationTableName = "daily_downloads",
        ColumnNames = ["package_id", "date", "download_count"],
        BatchSize = batch.Count
    };

    await bulkCopy.InitAsync();
    await bulkCopy.WriteToServerAsync(batch);
    totalInserted += batch.Count;
    Console.Write($"\r  Inserted {totalInserted:N0} / {allRows.Count:N0} rows");
}
Console.WriteLine();

// ==================== Verification ====================
Console.WriteLine();
Console.WriteLine("=== Verification ===");

// PostgreSQL
await using var pgVerifyCmd = pgConn.CreateCommand();
pgVerifyCmd.CommandText = "SELECT COUNT(*) FROM package_downloads WHERE latest_download_count IS NOT NULL";
var pgCount = (long)(await pgVerifyCmd.ExecuteScalarAsync())!;
Console.WriteLine($"  PostgreSQL packages with download counts: {pgCount}");

// ClickHouse daily
await using var chDailyCmd = chConn.CreateCommand();
chDailyCmd.CommandText = "SELECT count() FROM daily_downloads";
await using var dailyReader = await chDailyCmd.ExecuteReaderAsync();
await dailyReader.ReadAsync();
Console.WriteLine($"  ClickHouse daily_downloads rows: {Convert.ToInt64(dailyReader.GetValue(0)):N0}");

// ClickHouse weekly
await using var chWeeklyCmd = chConn.CreateCommand();
chWeeklyCmd.CommandText = "SELECT count() FROM weekly_downloads";
await using var weeklyReader = await chWeeklyCmd.ExecuteReaderAsync();
await weeklyReader.ReadAsync();
Console.WriteLine($"  ClickHouse weekly_downloads rows: {Convert.ToInt64(weeklyReader.GetValue(0)):N0}");

// Sample data
Console.WriteLine();
Console.WriteLine("Sample weekly data for 'sentry' (last 5 weeks):");
await using var sampleCmd = chConn.CreateCommand();
sampleCmd.CommandText = """
    SELECT week, toInt64(avgMerge(download_avg) * 7) AS weekly_downloads
    FROM weekly_downloads
    WHERE package_id = 'sentry'
    GROUP BY week
    ORDER BY week DESC
    LIMIT 5
    """;
await using var sampleReader = await sampleCmd.ExecuteReaderAsync();
while (await sampleReader.ReadAsync())
{
    var week = sampleReader.GetDateTime(0);
    var downloads = Convert.ToInt64(sampleReader.GetValue(1));
    Console.WriteLine($"  {week:yyyy-MM-dd}  {downloads:N0} downloads/week");
}

Console.WriteLine();
Console.WriteLine("Done! You can now test:");
Console.WriteLine("  - Search: type 'sentry' or 'polly' in the search box");
Console.WriteLine("  - History: http://localhost:5100/packages/Sentry?months=24");
Console.WriteLine("  - Trending: http://localhost:5100/api/package/trending");
Console.WriteLine();
Console.WriteLine("Note: restart the web app if it was running, so cached data refreshes.");

return 0;
