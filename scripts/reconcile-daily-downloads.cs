#!/usr/bin/env dotnet

#:package Npgsql@9.0.2
#:package ClickHouse.Driver@0.9.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClickHouse.Driver.ADO;
using Npgsql;

// ============================================================================
// Reconcile daily download data across PostgreSQL and ClickHouse
// ============================================================================
// Verifies data integrity across the download pipeline:
//   PostgreSQL (package_downloads) → ClickHouse (daily_downloads → weekly_downloads)
//
// Environment Variables:
//   PG_CONNECTION_STRING  - PostgreSQL connection string (optional, defaults to localhost dev)
//   CH_CONNECTION_STRING  - ClickHouse connection string (optional, defaults to localhost dev)
//
// Usage:
//   ./reconcile-daily-downloads.cs
//   PG_CONNECTION_STRING="Host=...;Database=nugettrends;..." ./reconcile-daily-downloads.cs
// ============================================================================

var pgConnectionString = Environment.GetEnvironmentVariable("PG_CONNECTION_STRING")
    ?? "Host=localhost;Database=nugettrends;Username=postgres;Password=PUg2rt6Pp8Arx7Z9FbgJLFvxEL7pZ2;Include Error Detail=true";
var chConnectionString = Environment.GetEnvironmentVariable("CH_CONNECTION_STRING")
    ?? "Host=localhost;Port=8123;Database=nugettrends";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--help":
        case "-h":
            Console.WriteLine(@"
Reconcile daily download data across PostgreSQL and ClickHouse.

Usage: ./reconcile-daily-downloads.cs

Environment Variables:
  PG_CONNECTION_STRING    PostgreSQL connection string
                          (default: Host=localhost;Database=nugettrends;Username=postgres;...)
  CH_CONNECTION_STRING    ClickHouse connection string
                          (default: Host=localhost;Port=8123;Database=nugettrends)
");
            return 0;
    }
}

Console.WriteLine("Daily Downloads Reconciliation");
Console.WriteLine($"  PostgreSQL: {MaskConnectionString(pgConnectionString)}");
Console.WriteLine($"  ClickHouse: {MaskConnectionString(chConnectionString)}");
Console.WriteLine();

await using var pgConn = new NpgsqlConnection(pgConnectionString);
await pgConn.OpenAsync();

await using var chConn = new ClickHouseConnection(chConnectionString);
await chConn.OpenAsync();

var hasWarnings = false;

// ─────────────────────────────────────────────────────────────
// Step 1: PostgreSQL package_downloads overview
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 1: PostgreSQL package_downloads overview ===");

var totalPackages = await PgScalarLong(pgConn, "SELECT count(*) FROM package_downloads");
var checkedToday = await PgScalarLong(pgConn,
    "SELECT count(*) FROM package_downloads WHERE latest_download_count_checked_utc::date = (now() AT TIME ZONE 'UTC')::date");
var notCheckedToday = totalPackages - checkedToday;
var stale = await PgScalarLong(pgConn,
    "SELECT count(*) FROM package_downloads WHERE latest_download_count_checked_utc < now() - interval '7 days'");
var oldest = await PgScalar(pgConn,
    "SELECT min(latest_download_count_checked_utc) FROM package_downloads");

var uncheckedPct = totalPackages > 0 ? (double)notCheckedToday / totalPackages * 100 : 0;

Console.WriteLine($"  Total packages:        {totalPackages,12:N0}");
Console.Write($"  Checked today:         {checkedToday,12:N0}");
Console.WriteLine(checkedToday > 0 ? "  \u2713" : "  \u26a0");
Console.Write($"  Not checked today:     {notCheckedToday,12:N0}");
if (notCheckedToday > 0 && uncheckedPct > 50)
{
    Console.WriteLine($"  \u26a0 ({uncheckedPct:F0}% unprocessed)");
    hasWarnings = true;
}
else
{
    Console.WriteLine($"  \u2713 ({uncheckedPct:F0}% unprocessed)");
}
Console.Write($"  Stale (>7 days):       {stale,12:N0}");
if (stale > 0)
{
    Console.WriteLine("  \u26a0");
    hasWarnings = true;
}
else
{
    Console.WriteLine("  \u2713");
}
Console.WriteLine($"  Oldest check:          {oldest}");
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Step 2: Catalog vs package_downloads gap
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 2: Catalog vs package_downloads gap ===");

var catalogDistinct = await PgScalarLong(pgConn,
    "SELECT count(DISTINCT package_id_lowered) FROM package_details_catalog_leafs");
var missingFromDownloads = await PgScalarLong(pgConn, """
    SELECT count(DISTINCT c.package_id_lowered)
    FROM package_details_catalog_leafs c
    LEFT JOIN package_downloads d ON c.package_id_lowered = d.package_id_lowered
    WHERE d.package_id_lowered IS NULL
    """);

Console.WriteLine($"  Catalog distinct pkgs: {catalogDistinct,12:N0}");
Console.Write($"  Missing from downloads:{missingFromDownloads,12:N0}");
if (missingFromDownloads > 0)
{
    Console.WriteLine("  \u26a0");
    hasWarnings = true;
}
else
{
    Console.WriteLine("  \u2713");
}

if (missingFromDownloads > 0)
{
    await using var cmd = new NpgsqlCommand("""
        SELECT DISTINCT c.package_id_lowered
        FROM package_details_catalog_leafs c
        LEFT JOIN package_downloads d ON c.package_id_lowered = d.package_id_lowered
        WHERE d.package_id_lowered IS NULL
        LIMIT 5
        """, pgConn);
    await using var reader = await cmd.ExecuteReaderAsync();
    Console.WriteLine("  Examples missing:");
    while (await reader.ReadAsync())
    {
        Console.WriteLine($"    - {reader.GetString(0)}");
    }
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Step 3: ClickHouse daily_downloads for today
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 3: ClickHouse daily_downloads for today ===");

var chTodayRows = await ChScalarLong(chConn, "SELECT count() FROM daily_downloads WHERE date = today()");
var chTodayPkgs = await ChScalarLong(chConn, "SELECT count(DISTINCT package_id) FROM daily_downloads WHERE date = today()");
var chYesterdayRows = await ChScalarLong(chConn, "SELECT count() FROM daily_downloads WHERE date = yesterday()");
var chYesterdayPkgs = await ChScalarLong(chConn, "SELECT count(DISTINCT package_id) FROM daily_downloads WHERE date = yesterday()");
var chTotalPkgs = await ChScalarLong(chConn, "SELECT count(DISTINCT package_id) FROM daily_downloads");

Console.Write($"  Today rows:            {chTodayRows,12:N0}");
Console.WriteLine(chTodayRows > 0 ? "  \u2713" : "  \u26a0");
Console.WriteLine($"  Today distinct pkgs:   {chTodayPkgs,12:N0}");
Console.Write($"  Yesterday rows:        {chYesterdayRows,12:N0}");
Console.WriteLine(chYesterdayRows > 0 ? "  \u2713" : "  \u26a0");
Console.WriteLine($"  Yesterday distinct pkgs:{chYesterdayPkgs,11:N0}");
Console.WriteLine($"  Total distinct pkgs:   {chTotalPkgs,12:N0}");
if (chTodayRows == 0)
{
    hasWarnings = true;
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Step 4: Cross-DB comparison — PG checked today vs CH today
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 4: Cross-DB comparison — PG checked today vs CH today ===");

var pgCheckedTodayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
{
    await using var cmd = new NpgsqlCommand(
        "SELECT package_id_lowered FROM package_downloads WHERE latest_download_count_checked_utc::date = (now() AT TIME ZONE 'UTC')::date",
        pgConn);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        pgCheckedTodayIds.Add(reader.GetString(0).ToLowerInvariant());
    }
}

var chTodayIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
{
    await using var cmd = chConn.CreateCommand();
    cmd.CommandText = "SELECT DISTINCT package_id FROM daily_downloads WHERE date = today()";
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        chTodayIds.Add(reader.GetString(0).ToLowerInvariant());
    }
}

var inPgNotCh = pgCheckedTodayIds.Except(chTodayIds, StringComparer.OrdinalIgnoreCase).ToList();
var inChNotPg = chTodayIds.Except(pgCheckedTodayIds, StringComparer.OrdinalIgnoreCase).ToList();

Console.WriteLine($"  PG checked today:      {pgCheckedTodayIds.Count,12:N0}");
Console.WriteLine($"  CH has today:          {chTodayIds.Count,12:N0}");
Console.Write($"  In PG, missing from CH:{inPgNotCh.Count,12:N0}");
if (inPgNotCh.Count > 0)
{
    Console.WriteLine("  \u26a0");
    hasWarnings = true;
    Console.WriteLine("  Examples (PG but not CH):");
    foreach (var id in inPgNotCh.Take(5))
    {
        Console.WriteLine($"    - {id}");
    }
}
else
{
    Console.WriteLine("  \u2713");
}

Console.Write($"  In CH, missing from PG:{inChNotPg.Count,12:N0}");
if (inChNotPg.Count > 0)
{
    Console.WriteLine("  \u26a0");
    hasWarnings = true;
    Console.WriteLine("  Examples (CH but not PG):");
    foreach (var id in inChNotPg.Take(5))
    {
        Console.WriteLine($"    - {id}");
    }
}
else
{
    Console.WriteLine("  \u2713");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Step 5: ClickHouse weekly_downloads integrity
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 5: ClickHouse weekly_downloads integrity ===");
Console.WriteLine("  Comparing avgMerge(download_avg)*7 from weekly_downloads");
Console.WriteLine("  vs avg(download_count)*7 from daily_downloads for sample packages");
Console.WriteLine();

{
    // Get 5 sample packages that have data in both tables for the current or previous week
    await using var sampleCmd = chConn.CreateCommand();
    sampleCmd.CommandText = """
        SELECT DISTINCT package_id
        FROM daily_downloads
        WHERE date >= toMonday(today()) - 7
        LIMIT 5
        """;

    var samplePackages = new List<string>();
    {
        await using var reader = await sampleCmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            samplePackages.Add(reader.GetString(0));
        }
    }

    if (samplePackages.Count == 0)
    {
        Console.WriteLine("  No packages with recent data to compare.  \u26a0");
        hasWarnings = true;
    }
    else
    {
        var anyDivergence = false;
        foreach (var pkg in samplePackages)
        {
            // weekly_downloads: avgMerge * 7
            await using var weeklyCmd = chConn.CreateCommand();
            weeklyCmd.CommandText = $"""
                SELECT
                    week,
                    toInt64(avgMerge(download_avg) * 7) AS weekly_total
                FROM weekly_downloads
                WHERE package_id = '{Escape(pkg)}'
                  AND week >= toMonday(today()) - 14
                GROUP BY week
                ORDER BY week DESC
                LIMIT 2
                """;

            var weeklyResults = new List<(string Week, long Total)>();
            {
                await using var reader = await weeklyCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    weeklyResults.Add((reader.GetValue(0)?.ToString() ?? "?", Convert.ToInt64(reader.GetValue(1))));
                }
            }

            // daily_downloads: avg * 7
            await using var dailyCmd = chConn.CreateCommand();
            dailyCmd.CommandText = $"""
                SELECT
                    toMonday(date) AS week,
                    toInt64(avg(download_count) * 7) AS weekly_total
                FROM daily_downloads
                WHERE package_id = '{Escape(pkg)}'
                  AND date >= toMonday(today()) - 14
                GROUP BY week
                ORDER BY week DESC
                LIMIT 2
                """;

            var dailyResults = new List<(string Week, long Total)>();
            {
                await using var reader = await dailyCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    dailyResults.Add((reader.GetValue(0)?.ToString() ?? "?", Convert.ToInt64(reader.GetValue(1))));
                }
            }

            Console.WriteLine($"  {pkg}:");
            foreach (var w in weeklyResults)
            {
                var dailyMatch = dailyResults.FirstOrDefault(d => d.Week == w.Week);
                var match = dailyMatch.Total != 0 && w.Total == dailyMatch.Total;
                var indicator = match ? "\u2713" : "\u26a0 DIVERGENCE";
                if (!match && dailyMatch.Total != 0)
                {
                    anyDivergence = true;
                }
                Console.WriteLine($"    week {w.Week}: weekly_downloads={w.Total:N0}  daily_downloads={dailyMatch.Total:N0}  {indicator}");
            }
        }

        if (anyDivergence)
        {
            Console.WriteLine("  \u26a0 Divergence detected — possible MV corruption from duplicate inserts");
            hasWarnings = true;
        }
        else
        {
            Console.WriteLine("  All samples match  \u2713");
        }
    }
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Step 6: Trending packages health
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Step 6: Trending packages health ===");

{
    // trending_packages_snapshot
    var snapshotCount = await ChScalarLong(chConn, "SELECT count() FROM trending_packages_snapshot");
    Console.WriteLine($"  Snapshot total rows:   {snapshotCount,12:N0}");

    await using var latestWeekCmd = chConn.CreateCommand();
    latestWeekCmd.CommandText = "SELECT max(week) FROM trending_packages_snapshot";
    var latestWeekObj = await latestWeekCmd.ExecuteScalarAsync();
    var latestWeekStr = latestWeekObj?.ToString() ?? "(none)";
    Console.WriteLine($"  Latest snapshot week:  {latestWeekStr}");

    // Check if snapshot week matches last Monday
    var lastMonday = DateOnly.FromDateTime(DateTime.UtcNow);
    while (lastMonday.DayOfWeek != DayOfWeek.Monday)
    {
        lastMonday = lastMonday.AddDays(-1);
    }

    var snapshotIsStale = true;
    if (latestWeekObj is DateOnly latestWeek)
    {
        snapshotIsStale = latestWeek < lastMonday;
    }
    else if (latestWeekObj is DateTime latestWeekDt)
    {
        snapshotIsStale = DateOnly.FromDateTime(latestWeekDt) < lastMonday;
    }

    if (snapshotCount == 0)
    {
        Console.WriteLine("  Snapshot status:       (empty)  \u26a0");
        hasWarnings = true;
    }
    else if (snapshotIsStale)
    {
        Console.WriteLine($"  Snapshot status:       STALE (expected {lastMonday})  \u26a0");
        hasWarnings = true;
    }
    else
    {
        Console.WriteLine("  Snapshot status:       current  \u2713");
    }

    // Top 5 by growth rate for latest week
    if (snapshotCount > 0)
    {
        await using var topCmd = chConn.CreateCommand();
        topCmd.CommandText = """
            SELECT package_id, growth_rate, week_downloads, comparison_week_downloads
            FROM trending_packages_snapshot FINAL
            WHERE week = (SELECT max(week) FROM trending_packages_snapshot)
            ORDER BY growth_rate DESC
            LIMIT 5
            """;
        await using var reader = await topCmd.ExecuteReaderAsync();
        Console.WriteLine("  Top 5 trending (latest week):");
        while (await reader.ReadAsync())
        {
            var pkgId = reader.GetString(0);
            var rate = Convert.ToDouble(reader.GetValue(1));
            var cur = Convert.ToInt64(reader.GetValue(2));
            var prev = Convert.ToInt64(reader.GetValue(3));
            Console.WriteLine($"    {pkgId,-40} growth={rate:+0.00%;-0.00%}  cur={cur:N0}  prev={prev:N0}");
        }
    }

    // package_first_seen
    var firstSeenCount = await ChScalarLong(chConn, "SELECT count() FROM package_first_seen");
    var latestFirstSeen = await ChScalar(chConn, "SELECT max(first_seen) FROM package_first_seen");
    Console.WriteLine($"  First-seen total:      {firstSeenCount,12:N0}");
    Console.WriteLine($"  Latest first_seen:     {latestFirstSeen}");
}
Console.WriteLine();

// ─────────────────────────────────────────────────────────────
// Summary
// ─────────────────────────────────────────────────────────────
Console.WriteLine("=== Summary ===");
if (hasWarnings)
{
    Console.WriteLine("  \u26a0 Warnings detected — review above for details");
}
else
{
    Console.WriteLine("  \u2713 All checks passed");
}
Console.WriteLine();

return hasWarnings ? 1 : 0;

// ─────────────────────────────────────────────────────────────
// Helpers
// ─────────────────────────────────────────────────────────────

static async Task<long> PgScalarLong(NpgsqlConnection conn, string sql)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt64(result);
}

static async Task<object?> PgScalar(NpgsqlConnection conn, string sql)
{
    await using var cmd = new NpgsqlCommand(sql, conn);
    return await cmd.ExecuteScalarAsync();
}

static async Task<long> ChScalarLong(ClickHouseConnection conn, string sql)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    var result = await cmd.ExecuteScalarAsync();
    return Convert.ToInt64(result);
}

static async Task<object?> ChScalar(ClickHouseConnection conn, string sql)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    return await cmd.ExecuteScalarAsync();
}

static string Escape(string value) => value.Replace("'", "\\'");

static string MaskConnectionString(string connStr)
{
    // Mask password in connection string for display
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
