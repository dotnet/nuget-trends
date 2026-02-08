using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests for package name case sensitivity handling.
/// These tests verify that the ClickHouse-based approach correctly handles
/// packages that are republished with different casing (e.g., "MyPackage" -> "MYPackage").
/// This was a known issue with the previous PostgreSQL-based approach.
/// </summary>
[Collection("ClickHouse")]
public class PackageCaseSensitivityTests : IAsyncLifetime
{
    private readonly ClickHouseFixture _fixture;
    private readonly ClickHouseService _sut;

    public PackageCaseSensitivityTests(ClickHouseFixture fixture)
    {
        _fixture = fixture;
        var connectionInfo = ClickHouseConnectionInfo.Parse(fixture.ConnectionString);
        _sut = new ClickHouseService(fixture.ConnectionString, NullLogger<ClickHouseService>.Instance, connectionInfo, null);
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetTableAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InsertDailyDownloadsAsync_PackageNameCaseChange_AllDataIsAccessible()
    {
        // Arrange - Package was originally published as "MyPackage"
        // Later, the author republishes with different casing "MYPackage"
        // This scenario broke the previous PostgreSQL-based approach
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var originalCaseDownloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("MyPackage", today.AddDays(-10), 1000),
            ("MyPackage", today.AddDays(-9), 1100),
            ("MyPackage", today.AddDays(-8), 1200),
        };

        var newCaseDownloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("MYPackage", today.AddDays(-7), 1300),
            ("MYPackage", today.AddDays(-6), 1400),
            ("MYPackage", today.AddDays(-5), 1500),
        };

        // Act - Insert with original casing
        await _sut.InsertDailyDownloadsAsync(originalCaseDownloads);

        // Act - Insert with new casing (simulating author republishing with different case)
        await _sut.InsertDailyDownloadsAsync(newCaseDownloads);

        // Assert - All 6 records should exist and be accessible
        var count = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'mypackage'");
        count.Should().Be(6, "All records should be stored with lowercase package_id");
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_PackageNameCaseChange_QueryReturnsAllData()
    {
        // Arrange - Same scenario: package case changes over time
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Week 1: Original casing
        await _sut.InsertDailyDownloadsAsync([
            ("Sentry.AspNetCore", today.AddDays(-14), 1000),
            ("Sentry.AspNetCore", today.AddDays(-13), 1100),
            ("Sentry.AspNetCore", today.AddDays(-12), 1200),
        ]);

        // Week 2: Author changes casing (e.g., updates package metadata)
        await _sut.InsertDailyDownloadsAsync([
            ("SENTRY.ASPNETCORE", today.AddDays(-7), 1300),
            ("SENTRY.ASPNETCORE", today.AddDays(-6), 1400),
            ("SENTRY.ASPNETCORE", today.AddDays(-5), 1500),
        ]);

        // Week 3: Yet another casing variation
        await _sut.InsertDailyDownloadsAsync([
            ("sentry.aspnetcore", today, 1600),
        ]);

        // Act - Query with any casing
        var resultOriginal = await _sut.GetWeeklyDownloadsAsync("Sentry.AspNetCore", months: 1);
        var resultUpper = await _sut.GetWeeklyDownloadsAsync("SENTRY.ASPNETCORE", months: 1);
        var resultLower = await _sut.GetWeeklyDownloadsAsync("sentry.aspnetcore", months: 1);

        // Assert - All queries should return the same data
        resultOriginal.Should().NotBeEmpty("Query with original case should return data");
        resultUpper.Should().NotBeEmpty("Query with uppercase should return data");
        resultLower.Should().NotBeEmpty("Query with lowercase should return data");

        resultOriginal.Sum(r => r.Count ?? 0).Should().Be(resultUpper.Sum(r => r.Count ?? 0),
            "Different case queries should return same aggregated data");
        resultOriginal.Sum(r => r.Count ?? 0).Should().Be(resultLower.Sum(r => r.Count ?? 0),
            "Different case queries should return same aggregated data");
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_SimilarPackageNamesDifferentCasing_AreNotConflated()
    {
        // Arrange - Two ACTUALLY different packages that look similar when lowercased
        // This is an edge case - most packages have unique lowercased names
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _sut.InsertDailyDownloadsAsync([
            ("MyPackage", today, 1000),
            ("MyOtherPackage", today, 2000),
        ]);

        // Act
        var myPackageCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'mypackage'");
        var myOtherPackageCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'myotherpackage'");

        // Assert - They should remain separate
        myPackageCount.Should().Be(1);
        myOtherPackageCount.Should().Be(1);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_CaseVariationsInInput_ReturnConsistentResults()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await _sut.InsertDailyDownloadsAsync([
            ("Newtonsoft.Json", today.AddDays(-7), 1000000),
            ("Newtonsoft.Json", today, 1100000),
        ]);

        // Act - Query with various case permutations
        var caseVariations = new[]
        {
            "Newtonsoft.Json",
            "newtonsoft.json",
            "NEWTONSOFT.JSON",
            "NewtonSoft.Json",
            "newtonsoft.JSON",
            "NEWTONSOFT.json",
        };

        var results = new List<List<DailyDownloadResult>>();
        foreach (var caseVariant in caseVariations)
        {
            results.Add(await _sut.GetWeeklyDownloadsAsync(caseVariant, months: 1));
        }

        // Assert - All should return the same data
        results.Should().AllSatisfy(r => r.Should().NotBeEmpty());
        var firstResultSum = results[0].Sum(r => r.Count ?? 0);
        results.Should().AllSatisfy(r =>
            r.Sum(x => x.Count ?? 0).Should().Be(firstResultSum,
                "All case variations should return identical results"));
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_RealWorldCaseChangeScenario()
    {
        // This test simulates a real-world scenario:
        // 1. Package "EntityFramework" is tracked for years
        // 2. Microsoft updates the package and changes casing in metadata
        // 3. New catalog entry comes in as "ENTITYFRAMEWORK" or "entityframework"
        // 4. System should handle this gracefully without losing history

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Historical data (many years of tracking with original casing)
        var historicalData = Enumerable.Range(1, 365)
            .Select(i => ("EntityFramework", today.AddDays(-i), 50000L + i * 100))
            .ToList();

        await _sut.InsertDailyDownloadsAsync(historicalData);

        // New data comes in with different casing (simulating catalog update)
        var newData = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("ENTITYFRAMEWORK", today, 100000),
        };

        await _sut.InsertDailyDownloadsAsync(newData);

        // Query with the NEW casing (as the application might do after seeing the new catalog entry)
        var result = await _sut.GetWeeklyDownloadsAsync("ENTITYFRAMEWORK", months: 12);

        // Assert - Should have all historical data plus new data
        result.Should().NotBeEmpty();
        result.Sum(r => r.Count ?? 0).Should().BeGreaterThan(50000,
            "Should include historical data even when queried with different case");

        // Verify total record count
        var totalRecords = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'entityframework'");
        totalRecords.Should().Be(366, "All 365 historical + 1 new record should exist");
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_CaseChangeOnSameDay_DeduplicatesCorrectly()
    {
        // Scenario: On the same day, we receive two catalog entries for the same package
        // with different casing. ReplacingMergeTree should deduplicate.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // First import: "MyPackage"
        await _sut.InsertDailyDownloadsAsync([("MyPackage", today, 1000)]);

        // Second import (later the same day): "MYPACKAGE"
        await _sut.InsertDailyDownloadsAsync([("MYPACKAGE", today, 1500)]);

        // Force deduplication
        await _fixture.OptimizeTableAsync();

        // Assert - Should have exactly 1 record for today
        var count = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'mypackage' AND date = today()");
        count.Should().Be(1, "Duplicate (same package_id lowercase, same date) should be deduplicated");

        // And it should keep the latest value
        var downloadCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT download_count FROM daily_downloads WHERE package_id = 'mypackage' AND date = today()");
        downloadCount.Should().Be(1500, "Should keep the most recent value after deduplication");
    }
}
