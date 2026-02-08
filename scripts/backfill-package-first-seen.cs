#!/usr/bin/env dotnet

#:package ClickHouse.Driver@0.9.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClickHouse.Driver.ADO;

// ============================================================================
// Backfill package_first_seen from weekly_downloads (week by week)
// ============================================================================
// Fills in missing packages that were never added to package_first_seen,
// processing one week at a time to avoid OOM on large datasets.
//
// The NOT IN subquery shrinks each iteration as packages get added,
// so later weeks are cheaper. Each week's batch is small enough to fit in memory.
//
// Environment Variables:
//   CH_CONNECTION_STRING - ClickHouse connection string (optional, defaults to localhost)
//
// Usage:
//   ./backfill-package-first-seen.cs
//   CH_CONNECTION_STRING="Host=...;Database=nugettrends" ./backfill-package-first-seen.cs
//   ./backfill-package-first-seen.cs --dry-run
// ============================================================================

var connectionString = Environment.GetEnvironmentVariable("CH_CONNECTION_STRING")
    ?? "Host=localhost;Port=8123;Database=nugettrends";

var dryRun = false;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dry-run":
            dryRun = true;
            break;
        case "--help":
        case "-h":
            Console.WriteLine(@"
Backfill package_first_seen from weekly_downloads (week by week).

Usage: ./backfill-package-first-seen.cs [--dry-run]

Options:
  --dry-run    Show what would be done without making changes

Environment Variables:
  CH_CONNECTION_STRING    ClickHouse connection string
                          (default: Host=localhost;Port=8123;Database=nugettrends)
");
            return 0;
    }
}

Console.WriteLine("package_first_seen Backfill");
Console.WriteLine($"  ClickHouse: {MaskConnectionString(connectionString)}");
Console.WriteLine($"  Dry run:    {dryRun}");
Console.WriteLine();

await using var conn = new ClickHouseConnection(connectionString);
await conn.OpenAsync();

// Step 1: Count missing packages
var missingBefore = await ScalarLong(conn,
    "SELECT count(DISTINCT package_id) FROM weekly_downloads WHERE package_id NOT IN (SELECT package_id FROM package_first_seen FINAL)");
var totalFirstSeen = await ScalarLong(conn, "SELECT count() FROM package_first_seen FINAL");

Console.WriteLine($"  Packages in package_first_seen: {totalFirstSeen:N0}");
Console.WriteLine($"  Missing packages to backfill:   {missingBefore:N0}");
Console.WriteLine();

if (missingBefore == 0)
{
    Console.WriteLine("Nothing to backfill. All packages are already tracked.");
    return 0;
}

// Step 2: Get distinct weeks ordered ASC
var weeks = new List<string>();
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT DISTINCT week FROM weekly_downloads ORDER BY week ASC";
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        // Format as ISO date to avoid culture-dependent ToString() issues
        var dt = reader.GetDateTime(0);
        weeks.Add(dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}

Console.WriteLine($"  Total weeks in weekly_downloads: {weeks.Count}");
Console.WriteLine();

// Step 3: Process each week
var totalInserted = 0L;
for (var i = 0; i < weeks.Count; i++)
{
    var week = weeks[i];

    var sql = $"""
        INSERT INTO package_first_seen (package_id, first_seen)
        SELECT DISTINCT package_id, toDate('{Escape(week)}') AS first_seen
        FROM weekly_downloads
        WHERE week = '{Escape(week)}'
          AND package_id NOT IN (SELECT package_id FROM package_first_seen FINAL)
        """;

    if (dryRun)
    {
        Console.WriteLine($"  [{i + 1}/{weeks.Count}] Week {week}: (dry run, skipped)");
        continue;
    }

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var rowsAffected = await cmd.ExecuteNonQueryAsync();

    totalInserted += rowsAffected;
    Console.WriteLine($"  [{i + 1}/{weeks.Count}] Week {week}: +{rowsAffected:N0} packages (total: {totalInserted:N0})");
}

Console.WriteLine();

if (dryRun)
{
    Console.WriteLine($"Dry run complete. {weeks.Count} weeks would be processed.");
    return 0;
}

// Step 4: Verify
var missingAfter = await ScalarLong(conn,
    "SELECT count(DISTINCT package_id) FROM weekly_downloads WHERE package_id NOT IN (SELECT package_id FROM package_first_seen FINAL)");
var totalAfter = await ScalarLong(conn, "SELECT count() FROM package_first_seen FINAL");

Console.WriteLine($"  Packages inserted:              {totalInserted:N0}");
Console.WriteLine($"  package_first_seen total:       {totalAfter:N0}");
Console.Write($"  Still missing:                  {missingAfter:N0}");

if (missingAfter == 0)
{
    Console.WriteLine("  \u2713");
    Console.WriteLine();
    Console.WriteLine("Backfill complete. All packages are now tracked.");
    return 0;
}

Console.WriteLine("  \u26a0");
Console.WriteLine();
Console.WriteLine($"WARNING: {missingAfter:N0} packages still missing after backfill.");
return 1;

// ─────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────

static async Task<long> ScalarLong(ClickHouseConnection conn, string sql)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt64(result);
}

static string Escape(string value) => value.Replace("'", "\\'");

static string MaskConnectionString(string connStr)
{
    var parts = connStr.Split(';');
    var masked = parts.Select(p =>
    {
        var kv = p.Split('=', 2);
        if (kv.Length == 2 && kv[0].Trim().Equals("Password", StringComparison.OrdinalIgnoreCase))
        {
            return $"{kv[0]}=***";
        }
        return p;
    });
    return string.Join(";", masked);
}
