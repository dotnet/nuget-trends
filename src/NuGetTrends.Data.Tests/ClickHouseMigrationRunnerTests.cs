using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests for ClickHouse migration runner to ensure migrations are tracked and applied correctly.
/// </summary>
[Collection("ClickHouse")]
public class ClickHouseMigrationRunnerTests : IAsyncLifetime
{
    private readonly ClickHouseFixture _fixture;

    public ClickHouseMigrationRunnerTests(ClickHouseFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task RunMigrationsAsync_CreatesTrackingTable()
    {
        // Arrange
        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act
        await runner.RunMigrationsAsync();

        // Assert - tracking table should exist
        var tableExists = await _fixture.TableExistsAsync("nugettrends.clickhouse_migrations");
        tableExists.Should().BeTrue("the migration tracking table should be created");
    }

    [Fact]
    public async Task RunMigrationsAsync_TracksAppliedMigrations()
    {
        // Arrange
        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act
        await runner.RunMigrationsAsync();

        // Assert - all migration files should be tracked
        var appliedMigrations = await _fixture.GetAppliedMigrationsAsync();
        appliedMigrations.Should().NotBeEmpty("migrations should be tracked");
        appliedMigrations.Should().Contain(m => m.StartsWith("2025-12-26-01-init.sql"), 
            "the init migration should be tracked");
    }

    [Fact]
    public async Task RunMigrationsAsync_IsIdempotent()
    {
        // Arrange
        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act - run migrations twice
        await runner.RunMigrationsAsync();
        var firstRunMigrations = await _fixture.GetAppliedMigrationsAsync();

        await runner.RunMigrationsAsync();
        var secondRunMigrations = await _fixture.GetAppliedMigrationsAsync();

        // Assert - should have same migrations after second run (no duplicates)
        secondRunMigrations.Should().BeEquivalentTo(firstRunMigrations,
            "running migrations multiple times should be idempotent");
    }

    [Fact]
    public async Task RunMigrationsAsync_AppliesAllRequiredTables()
    {
        // Arrange
        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act
        await runner.RunMigrationsAsync();

        // Assert - key tables should exist
        var dailyDownloadsExists = await _fixture.TableExistsAsync("nugettrends.daily_downloads");
        var weeklyDownloadsExists = await _fixture.TableExistsAsync("nugettrends.weekly_downloads");
        var trendingSnapshotExists = await _fixture.TableExistsAsync("nugettrends.trending_packages_snapshot");
        var packageFirstSeenExists = await _fixture.TableExistsAsync("nugettrends.package_first_seen");

        dailyDownloadsExists.Should().BeTrue("daily_downloads table should exist");
        weeklyDownloadsExists.Should().BeTrue("weekly_downloads table should exist");
        trendingSnapshotExists.Should().BeTrue("trending_packages_snapshot table should exist");
        packageFirstSeenExists.Should().BeTrue("package_first_seen table should exist");
    }
}
