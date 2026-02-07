using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests for the PostgreSQL query logic used by DailyDownloadPackageIdPublisher
/// to filter packages that haven't been checked today.
/// </summary>
[Collection("PostgreSql")]
public class DailyDownloadPublisherQueryTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public DailyDownloadPublisherQueryTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetUnprocessedPackageIds_NewPackages_ReturnsAll()
    {
        // Arrange - Packages in catalog but not in package_downloads
        await _fixture.SeedPackageCatalogAsync("Sentry", "Newtonsoft.Json", "Moq");

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - All packages should be returned (none have been checked)
        result.Should().HaveCount(3);
        result.Should().Contain("Sentry");
        result.Should().Contain("Newtonsoft.Json");
        result.Should().Contain("Moq");
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_PackagesCheckedToday_ExcludesFromResults()
    {
        // Arrange
        await _fixture.SeedPackageCatalogAsync("Sentry", "Newtonsoft.Json", "Moq");
        await _fixture.SeedPackageDownloadsAsync(
            ("Sentry", DateTime.UtcNow),        // Checked today - should be excluded
            ("Newtonsoft.Json", DateTime.UtcNow) // Checked today - should be excluded
            // Moq not in package_downloads - should be included
        );

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Only Moq should be returned
        result.Should().HaveCount(1);
        result.Should().Contain("Moq");
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_PackagesCheckedYesterday_IncludesInResults()
    {
        // Arrange
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);
        await _fixture.SeedPackageCatalogAsync("Sentry", "Newtonsoft.Json");
        await _fixture.SeedPackageDownloadsAsync(
            ("Sentry", yesterday),       // Checked yesterday - should be included
            ("Newtonsoft.Json", yesterday) // Checked yesterday - should be included
        );

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Both should be returned (checked before today)
        result.Should().HaveCount(2);
        result.Should().Contain("Sentry");
        result.Should().Contain("Newtonsoft.Json");
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_MixedScenario_ReturnsCorrectPackages()
    {
        // Arrange - Mix of: new packages, checked today, checked yesterday
        var today = DateTime.UtcNow;
        var yesterday = DateTime.UtcNow.Date.AddDays(-1);

        await _fixture.SeedPackageCatalogAsync(
            "Package.New",           // New - not in package_downloads
            "Package.Today",         // Checked today
            "Package.Yesterday",     // Checked yesterday
            "Package.LastWeek"       // Checked last week
        );

        await _fixture.SeedPackageDownloadsAsync(
            ("Package.Today", today),
            ("Package.Yesterday", yesterday),
            ("Package.LastWeek", DateTime.UtcNow.Date.AddDays(-7))
        );

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert
        result.Should().HaveCount(3);
        result.Should().Contain("Package.New");       // New package
        result.Should().Contain("Package.Yesterday"); // Checked before today
        result.Should().Contain("Package.LastWeek");  // Checked before today
        result.Should().NotContain("Package.Today");  // Checked today - excluded
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_AllPackagesCheckedToday_ReturnsEmpty()
    {
        // Arrange
        var today = DateTime.UtcNow;
        await _fixture.SeedPackageCatalogAsync("Sentry", "Newtonsoft.Json");
        await _fixture.SeedPackageDownloadsAsync(
            ("Sentry", today),
            ("Newtonsoft.Json", today)
        );

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_EmptyCatalog_ReturnsEmpty()
    {
        // Arrange - No packages in catalog
        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_CaseInsensitiveJoin_WorksCorrectly()
    {
        // Arrange - Catalog has mixed case, package_downloads has lowercase
        await _fixture.SeedPackageCatalogAsync("Sentry.AspNetCore", "NEWTONSOFT.JSON");
        await _fixture.SeedPackageDownloadsAsync(
            ("Sentry.AspNetCore", DateTime.UtcNow) // Should match despite case difference
        );

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Only NEWTONSOFT.JSON should be returned (Sentry.AspNetCore was checked today)
        result.Should().HaveCount(1);
        result.Should().Contain("NEWTONSOFT.JSON");
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_LargeDataset_HandlesEfficiently()
    {
        // Arrange - Simulate a large dataset
        var packageIds = Enumerable.Range(1, 1000)
            .Select(i => $"Package.{i}")
            .ToArray();

        await _fixture.SeedPackageCatalogAsync(packageIds);

        // Mark half as checked today
        var today = DateTime.UtcNow;
        var checkedToday = packageIds.Take(500)
            .Select(id => (id, (DateTime?)today))
            .ToArray();
        await _fixture.SeedPackageDownloadsAsync(checkedToday);

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Should return the 500 that weren't checked today
        result.Should().HaveCount(500);
        result.Should().AllSatisfy(id =>
        {
            var num = int.Parse(id.Split('.')[1]);
            num.Should().BeGreaterThan(500);
        });
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_PackageCheckedAtMidnight_ExcludedCorrectly()
    {
        // Arrange - Edge case: package checked exactly at midnight today
        var midnightToday = DateTime.UtcNow.Date; // Exactly 00:00:00 today

        await _fixture.SeedPackageCatalogAsync("Sentry");
        await _fixture.SeedPackageDownloadsAsync(("Sentry", midnightToday));

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Should be excluded (midnight counts as "today")
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_PackageCheckedJustBeforeMidnight_IncludedCorrectly()
    {
        // Arrange - Package checked 1 second before midnight (yesterday)
        var justBeforeMidnight = DateTime.UtcNow.Date.AddSeconds(-1);

        await _fixture.SeedPackageCatalogAsync("Sentry");
        await _fixture.SeedPackageDownloadsAsync(("Sentry", justBeforeMidnight));

        await using var context = _fixture.CreateDbContext();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Should be included (yesterday)
        result.Should().HaveCount(1);
        result.Should().Contain("Sentry");
    }

    [Fact]
    public async Task GetUnprocessedPackageIds_DuplicateCatalogEntries_ReturnsDistinct()
    {
        // Arrange - Multiple versions of same package in catalog
        await using var context = _fixture.CreateDbContext();

        // Add multiple versions of the same package
        context.PackageDetailsCatalogLeafs.AddRange(
            new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = "Sentry",
                PackageIdLowered = "sentry",
                PackageVersion = "1.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow,
            },
            new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = "Sentry",
                PackageIdLowered = "sentry",
                PackageVersion = "2.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow,
            },
            new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = "Sentry",
                PackageIdLowered = "sentry",
                PackageVersion = "3.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow,
            }
        );
        await context.SaveChangesAsync();

        // Act
        var result = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();

        // Assert - Should return only one "Sentry" (distinct)
        result.Should().HaveCount(1);
        result.Should().Contain("Sentry");
    }
}
