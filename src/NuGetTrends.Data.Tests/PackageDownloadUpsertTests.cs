using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests for the PostgreSQL batch upsert functionality used by DailyDownloadWorker
/// to efficiently update package download records.
/// </summary>
[Collection("PostgreSql")]
public class PackageDownloadUpsertTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public PackageDownloadUpsertTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpsertPackageDownloadsAsync_NewPackages_InsertsAll()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("Sentry", 1000, now, "https://example.com/sentry.png"),
            new("Newtonsoft.Json", 2000, now, "https://example.com/newtonsoft.png"),
            new("Moq", 3000, now, null)
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(3);

        var allPackages = await context.PackageDownloads.ToListAsync();
        allPackages.Should().HaveCount(3);

        var sentry = allPackages.Single(p => p.PackageId == "Sentry");
        sentry.PackageIdLowered.Should().Be("sentry");
        sentry.LatestDownloadCount.Should().Be(1000);
        sentry.IconUrl.Should().Be("https://example.com/sentry.png");

        var moq = allPackages.Single(p => p.PackageId == "Moq");
        moq.IconUrl.Should().BeNull();
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_ExistingPackages_UpdatesAll()
    {
        // Arrange - Seed existing data
        var yesterday = DateTime.UtcNow.AddDays(-1);
        await _fixture.SeedPackageDownloadsAsync(
            ("Sentry", yesterday),
            ("Newtonsoft.Json", yesterday)
        );

        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("Sentry", 5000, now, "https://new-icon.com/sentry.png"),
            new("Newtonsoft.Json", 6000, now, "https://new-icon.com/newtonsoft.png")
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(2);

        var allPackages = await context.PackageDownloads.ToListAsync();
        allPackages.Should().HaveCount(2);

        var sentry = allPackages.Single(p => p.PackageIdLowered == "sentry");
        sentry.LatestDownloadCount.Should().Be(5000);
        sentry.IconUrl.Should().Be("https://new-icon.com/sentry.png");
        sentry.LatestDownloadCountCheckedUtc.Should().BeCloseTo(now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_MixedNewAndExisting_HandlesCorrectly()
    {
        // Arrange - Seed some existing data
        var yesterday = DateTime.UtcNow.AddDays(-1);
        await _fixture.SeedPackageDownloadsAsync(("Sentry", yesterday));

        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("Sentry", 5000, now, "https://updated.com/sentry.png"),      // Existing - update
            new("Newtonsoft.Json", 6000, now, "https://new.com/newtonsoft.png") // New - insert
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(2);

        var allPackages = await context.PackageDownloads.ToListAsync();
        allPackages.Should().HaveCount(2);

        allPackages.Should().Contain(p => p.PackageIdLowered == "sentry" && p.LatestDownloadCount == 5000);
        allPackages.Should().Contain(p => p.PackageIdLowered == "newtonsoft.json" && p.LatestDownloadCount == 6000);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_EmptyList_ReturnsZero()
    {
        // Arrange
        var packages = new List<PackageDownloadUpsert>();

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(0);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_CaseInsensitiveConflict_UpdatesCorrectly()
    {
        // Arrange - Seed with lowercase
        await _fixture.SeedPackageDownloadsAsync(("sentry", DateTime.UtcNow.AddDays(-1)));

        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("SENTRY", 9999, now, "https://updated.com/icon.png") // Different case
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(1);

        var allPackages = await context.PackageDownloads.ToListAsync();
        allPackages.Should().HaveCount(1);

        var pkg = allPackages.Single();
        pkg.PackageId.Should().Be("SENTRY"); // Updated to new casing
        pkg.PackageIdLowered.Should().Be("sentry");
        pkg.LatestDownloadCount.Should().Be(9999);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_NullIconUrl_OverwritesExisting()
    {
        // Arrange - Seed with an icon URL
        await using var seedContext = _fixture.CreateDbContext();
        seedContext.PackageDownloads.Add(new PackageDownload
        {
            PackageId = "Sentry",
            PackageIdLowered = "sentry",
            LatestDownloadCount = 1000,
            LatestDownloadCountCheckedUtc = DateTime.UtcNow.AddDays(-1),
            IconUrl = "https://old-icon.com/sentry.png"
        });
        await seedContext.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("Sentry", 2000, now, null) // Null icon URL
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        await context.UpsertPackageDownloadsAsync(packages);

        // Assert - Icon URL should be overwritten with null
        var pkg = await context.PackageDownloads.SingleAsync(p => p.PackageIdLowered == "sentry");
        pkg.IconUrl.Should().BeNull();
        pkg.LatestDownloadCount.Should().Be(2000);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_LargeBatch_HandlesEfficiently()
    {
        // Arrange - Large batch of 500 packages
        var now = DateTime.UtcNow;
        var packages = Enumerable.Range(1, 500)
            .Select(i => new PackageDownloadUpsert(
                $"Package.{i}",
                i * 100,
                now,
                $"https://example.com/package{i}.png"))
            .ToList();

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(500);

        var count = await context.PackageDownloads.CountAsync();
        count.Should().Be(500);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_SpecialCharactersInPackageId_HandlesCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("My.Package.With.Dots", 1000, now, null),
            new("Package-With-Dashes", 2000, now, null),
            new("Package_With_Underscores", 3000, now, null)
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(3);

        var allPackages = await context.PackageDownloads.ToListAsync();
        allPackages.Should().HaveCount(3);
        allPackages.Select(p => p.PackageId).Should().BeEquivalentTo(
            "My.Package.With.Dots",
            "Package-With-Dashes",
            "Package_With_Underscores");
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_SqlInjectionAttempt_HandlesSafely()
    {
        // Arrange - Attempt SQL injection via package ID
        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("'; DROP TABLE package_downloads; --", 1000, now, null)
        };

        await using var context = _fixture.CreateDbContext();

        // Act - Should not throw and should safely insert the malicious string
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(1);

        var pkg = await context.PackageDownloads.SingleAsync();
        pkg.PackageId.Should().Be("'; DROP TABLE package_downloads; --");

        // Table should still exist and be queryable
        var count = await context.PackageDownloads.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task UpsertPackageDownloadsAsync_VeryLargeDownloadCount_HandlesCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var packages = new List<PackageDownloadUpsert>
        {
            new("PopularPackage", long.MaxValue, now, null)
        };

        await using var context = _fixture.CreateDbContext();

        // Act
        var rowsAffected = await context.UpsertPackageDownloadsAsync(packages);

        // Assert
        rowsAffected.Should().Be(1);

        var pkg = await context.PackageDownloads.SingleAsync();
        pkg.LatestDownloadCount.Should().Be(long.MaxValue);
    }
}
