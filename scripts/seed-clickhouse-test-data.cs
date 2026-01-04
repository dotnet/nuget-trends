#!/usr/bin/env dotnet

#:package ClickHouse.Driver@0.9.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Collections.Generic;
using System.Globalization;
using ClickHouse.Driver.ADO;
using ClickHouse.Driver.Copy;

// ============================================================================
// Seed ClickHouse with test data for the 'sentry' package
// ============================================================================
// Creates fake daily download data for testing the API endpoints.
//
// Environment Variables:
//   CH_CONNECTION_STRING - ClickHouse connection string (optional, defaults to localhost)
//
// Usage:
//   ./seed-clickhouse-test-data.cs [package-id] [--months N] [--clear]
// ============================================================================

var connectionString = Environment.GetEnvironmentVariable("CH_CONNECTION_STRING") 
    ?? "Host=localhost;Port=8123;Database=nugettrends";

var packageId = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "sentry";
var months = 12;
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
        case "--help":
        case "-h":
            Console.WriteLine(@"
Seed ClickHouse with test data for a package.

Usage: ./seed-clickhouse-test-data.cs [package-id] [options]

Arguments:
  package-id              Package ID to seed (default: sentry)

Options:
  --months N              Number of months of data to generate (default: 12)
  --clear                 Clear existing data for the package before seeding
  --help, -h              Show this help message

Environment Variables:
  CH_CONNECTION_STRING    ClickHouse connection string
                          (default: Host=localhost;Port=8123;Database=nugettrends)

Examples:
  ./seed-clickhouse-test-data.cs                    # Seed 'sentry' with 12 months
  ./seed-clickhouse-test-data.cs newtonsoft.json    # Seed 'newtonsoft.json'
  ./seed-clickhouse-test-data.cs sentry --months 24 # Seed 24 months of data
  ./seed-clickhouse-test-data.cs sentry --clear     # Clear and reseed
");
            return 0;
    }
}

Console.WriteLine($"Seeding ClickHouse with test data for '{packageId}'");
Console.WriteLine($"  Connection: {connectionString}");
Console.WriteLine($"  Months: {months}");

await using var connection = new ClickHouseConnection(connectionString);
await connection.OpenAsync();

if (clear)
{
    Console.WriteLine($"  Clearing existing data for '{packageId}'...");
    await using var deleteCmd = connection.CreateCommand();
    deleteCmd.CommandText = $"ALTER TABLE daily_downloads DELETE WHERE package_id = '{packageId.ToLower(CultureInfo.InvariantCulture)}'";
    await deleteCmd.ExecuteNonQueryAsync();
    await Task.Delay(1000); // Wait for mutation
}

// Generate test data
var endDate = DateOnly.FromDateTime(DateTime.UtcNow);
var startDate = endDate.AddMonths(-months);
var random = new Random(42); // Fixed seed for reproducibility

var data = new List<object[]>();
var currentDate = startDate;
var baseDownloads = 1_000_000L; // Start with 1M downloads

Console.WriteLine($"  Generating data from {startDate} to {endDate}...");

while (currentDate <= endDate)
{
    // Simulate organic growth with some randomness
    // ~0.5% daily growth on average, with variance
    var growthFactor = 1.0 + (random.NextDouble() * 0.01 - 0.002); // -0.2% to +0.8%
    baseDownloads = (long)(baseDownloads * growthFactor);
    
    // Add some weekly seasonality (weekends have fewer downloads)
    var dayOfWeek = currentDate.DayOfWeek;
    var seasonalFactor = dayOfWeek switch
    {
        DayOfWeek.Saturday => 0.6,
        DayOfWeek.Sunday => 0.5,
        DayOfWeek.Monday => 1.1,
        _ => 1.0
    };
    
    var dailyDownloads = (long)(baseDownloads * seasonalFactor * (0.95 + random.NextDouble() * 0.1));
    
    data.Add([packageId.ToLower(CultureInfo.InvariantCulture), currentDate, (ulong)dailyDownloads]);
    currentDate = currentDate.AddDays(1);
}

Console.WriteLine($"  Generated {data.Count} rows");
Console.WriteLine($"  Download range: {data[0][2]:N0} to {data[^1][2]:N0}");

// Bulk insert
Console.WriteLine("  Inserting into ClickHouse...");

using var bulkCopy = new ClickHouseBulkCopy(connection)
{
    DestinationTableName = "daily_downloads",
    ColumnNames = ["package_id", "date", "download_count"],
    BatchSize = data.Count
};

await bulkCopy.InitAsync();
await bulkCopy.WriteToServerAsync(data);

// Verify
await using var countCmd = connection.CreateCommand();
countCmd.CommandText = $"SELECT count(), min(date), max(date), min(download_count), max(download_count) FROM daily_downloads WHERE package_id = '{packageId.ToLower(CultureInfo.InvariantCulture)}'";

await using var reader = await countCmd.ExecuteReaderAsync();
await reader.ReadAsync();

var rowCount = Convert.ToInt64(reader.GetValue(0));
var minDate = reader.GetValue(1);
var maxDate = reader.GetValue(2);
var minDownloads = Convert.ToInt64(reader.GetValue(3));
var maxDownloads = Convert.ToInt64(reader.GetValue(4));

Console.WriteLine();
Console.WriteLine("Verification:");
Console.WriteLine($"  Rows in ClickHouse: {rowCount:N0}");
Console.WriteLine($"  Date range: {minDate} to {maxDate}");
Console.WriteLine($"  Download range: {minDownloads:N0} to {maxDownloads:N0}");
Console.WriteLine();
Console.WriteLine("Done!");

return 0;
