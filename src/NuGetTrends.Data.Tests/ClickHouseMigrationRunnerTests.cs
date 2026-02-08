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

    [Fact]
    public async Task RunMigrationsAsync_FreshDatabase_CreatesEverythingFromScratch()
    {
        // Arrange - drop the entire database so nothing exists
        await _fixture.DropDatabaseAsync();

        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act
        await runner.RunMigrationsAsync();

        // Assert - all tables should be created from scratch
        var dailyDownloadsExists = await _fixture.TableExistsAsync("nugettrends.daily_downloads");
        var weeklyDownloadsExists = await _fixture.TableExistsAsync("nugettrends.weekly_downloads");
        var trendingSnapshotExists = await _fixture.TableExistsAsync("nugettrends.trending_packages_snapshot");
        var packageFirstSeenExists = await _fixture.TableExistsAsync("nugettrends.package_first_seen");
        var trackingExists = await _fixture.TableExistsAsync("nugettrends.clickhouse_migrations");

        dailyDownloadsExists.Should().BeTrue("daily_downloads should be created from scratch");
        weeklyDownloadsExists.Should().BeTrue("weekly_downloads should be created from scratch");
        trendingSnapshotExists.Should().BeTrue("trending_packages_snapshot should be created from scratch");
        packageFirstSeenExists.Should().BeTrue("package_first_seen should be created from scratch");
        trackingExists.Should().BeTrue("migration tracking table should be created");

        // All migrations should be tracked
        var appliedMigrations = await _fixture.GetAppliedMigrationsAsync();
        appliedMigrations.Should().HaveCountGreaterOrEqualTo(6,
            "all migration files should be tracked");
    }

    [Fact]
    public async Task RunMigrationsAsync_PartialMigrationsApplied_RunsOnlyPending()
    {
        // Arrange - first run the runner to get the full list of migration names
        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);
        await runner.RunMigrationsAsync();

        var fullMigrationList = (await _fixture.GetAppliedMigrationsAsync())
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        fullMigrationList.Should().HaveCountGreaterOrEqualTo(6);

        // Drop tracking and recreate it with only the first 4 migrations marked as applied
        await _fixture.DropMigrationTrackingAsync();
        await _fixture.ExecuteNonQueryAsync(
            "CREATE TABLE IF NOT EXISTS nugettrends.clickhouse_migrations " +
            "(migration_name String, applied_at DateTime DEFAULT now()) " +
            "ENGINE = MergeTree() ORDER BY (migration_name)");

        var alreadyApplied = fullMigrationList.Take(4).ToList();
        foreach (var migration in alreadyApplied)
        {
            await _fixture.InsertMigrationRecordAsync(migration);
        }

        var beforeCount = (await _fixture.GetAppliedMigrationsAsync()).Count;
        beforeCount.Should().Be(4, "only 4 migrations should be tracked before running");

        // Act - runner should only apply the remaining migrations
        await runner.RunMigrationsAsync();

        // Assert - all migrations should now be tracked
        var afterRunMigrations = (await _fixture.GetAppliedMigrationsAsync())
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();
        afterRunMigrations.Should().BeEquivalentTo(fullMigrationList,
            "all migrations should now be tracked after running pending ones");
    }

    [Fact]
    public async Task RunMigrationsAsync_ExistingDatabaseNoTracking_RecordsAllMigrations()
    {
        // Arrange - tables exist (from fixture setup) but drop only the tracking table.
        // This simulates the production first-deploy scenario where tables were created manually.
        await _fixture.DropMigrationTrackingAsync();

        var trackingExists = await _fixture.TableExistsAsync("nugettrends.clickhouse_migrations");
        trackingExists.Should().BeFalse("tracking table should not exist before running");

        // Tables from previous migrations should still exist
        var dailyDownloadsExists = await _fixture.TableExistsAsync("nugettrends.daily_downloads");
        dailyDownloadsExists.Should().BeTrue("daily_downloads should exist from fixture setup");

        var runner = new ClickHouseMigrationRunner(
            _fixture.AdminConnectionString,
            NullLogger<ClickHouseMigrationRunner>.Instance);

        // Act - runner creates tracking table, runs all migrations (idempotent no-ops), records them
        await runner.RunMigrationsAsync();

        // Assert - tracking table should exist with all migrations recorded
        trackingExists = await _fixture.TableExistsAsync("nugettrends.clickhouse_migrations");
        trackingExists.Should().BeTrue("tracking table should be created");

        var appliedMigrations = await _fixture.GetAppliedMigrationsAsync();
        appliedMigrations.Should().HaveCountGreaterOrEqualTo(6,
            "all migrations should be tracked even though tables already existed");
        appliedMigrations.Should().Contain("2025-12-26-01-init.sql");
        appliedMigrations.Should().Contain("2026-02-07-01-add-enrichment-to-trending-snapshot.sql");

        // Tables should still be intact
        var trendingSnapshotExists = await _fixture.TableExistsAsync("nugettrends.trending_packages_snapshot");
        trendingSnapshotExists.Should().BeTrue("existing tables should not be destroyed");
    }
}
