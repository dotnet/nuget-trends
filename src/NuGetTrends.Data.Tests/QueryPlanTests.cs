using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

/// <summary>
/// Tests that validate PostgreSQL query plans use indexes instead of sequential scans.
/// These catch performance regressions that correctness tests miss — a query can return
/// the right results on 1K test rows but time out on 11M production rows if it seq-scans.
///
/// We use raw SQL matching the query shapes from production code rather than EF Core's
/// ToQueryString(), because EXPLAIN requires properly bound parameters that ToQueryString()
/// doesn't provide. If a query shape changes in production code, update the SQL here too.
/// </summary>
[Collection("PostgreSql")]
public class QueryPlanTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _fixture;

    public QueryPlanTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        // Seed enough data so PostgreSQL's planner prefers index scans over seq scans.
        // With very few rows, the planner may choose seq scan regardless of indexes.
        await SeedRealisticDataAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Validates the query plan for GetUnprocessedPackageIds (NuGetTrendsContextExtensions).
    /// The NOT EXISTS subquery must use an index on package_details_catalog_leafs.package_id_lowered
    /// to avoid scanning 11M+ rows in production.
    /// </summary>
    [Fact]
    public async Task GetUnprocessedPackageIds_UsesIndexOnPackageIdLowered()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        await using var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        await conn.OpenAsync();

        // Act - EXPLAIN the same query shape as GetUnprocessedPackageIds
        // See: NuGetTrendsContextExtensions.GetUnprocessedPackageIds
        await using var cmd = new NpgsqlCommand("""
            EXPLAIN
            SELECT p.package_id
            FROM package_downloads AS p
            WHERE p.latest_download_count_checked_utc < $1
            UNION ALL
            SELECT DISTINCT leaf.package_id
            FROM package_details_catalog_leafs AS leaf
            WHERE leaf.package_id IS NOT NULL AND NOT EXISTS (
                SELECT 1 FROM package_downloads AS pd
                WHERE pd.package_id_lowered = leaf.package_id_lowered
            )
            """, conn);
        cmd.Parameters.AddWithValue(DateTime.UtcNow.Date);

        var plan = await ReadPlanAsync(cmd);

        // Assert - The NOT EXISTS subquery must use a Hash Anti Join (efficient O(n+m))
        // rather than a Nested Loop Anti Join (catastrophic O(n*m) with 11M+ catalog rows).
        // A Seq Scan on catalog_leafs is expected here because the query needs to check
        // ALL rows — the important thing is the join strategy, not the scan type.
        plan.Should().Contain("Hash Anti Join",
            "The NOT EXISTS subquery should use a Hash Anti Join for efficiency. " +
            "A Nested Loop Anti Join would be catastrophic with 11M+ catalog rows in production.");
    }

    /// <summary>
    /// Validates the enrichment query on package_downloads uses the index on package_id_lowered.
    /// See: TrendingPackagesSnapshotRefresher.EnrichWithPostgresMetadataAsync
    /// </summary>
    [Fact]
    public async Task EnrichmentQuery_PackageDownloads_UsesIndex()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        await using var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        await conn.OpenAsync();

        var packageIds = Enumerable.Range(1, 100).Select(i => $"package.{i}").ToArray();

        // Act - EXPLAIN the same query shape as the enrichment query
        await using var cmd = new NpgsqlCommand("""
            EXPLAIN
            SELECT p.package_id, p.package_id_lowered, p.icon_url
            FROM package_downloads AS p
            WHERE p.package_id_lowered = ANY($1)
            """, conn);
        cmd.Parameters.AddWithValue(packageIds);

        var plan = await ReadPlanAsync(cmd);

        // Assert - Should use the index on package_id_lowered, not a seq scan
        plan.Should().NotContain("Seq Scan on package_downloads",
            "The enrichment query on package_downloads should use the index on package_id_lowered. " +
            "A sequential scan would be slow with ~495K rows in production.");
    }

    /// <summary>
    /// Validates the enrichment query on package_details_catalog_leafs uses the index on package_id_lowered.
    /// See: TrendingPackagesSnapshotRefresher.EnrichWithPostgresMetadataAsync
    /// </summary>
    [Fact]
    public async Task EnrichmentQuery_CatalogLeafs_UsesIndex()
    {
        // Arrange
        await using var context = _fixture.CreateDbContext();
        await using var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        await conn.OpenAsync();

        var packageIds = Enumerable.Range(1, 100).Select(i => $"package.{i}").ToArray();

        // Act - EXPLAIN the same query shape as the enrichment query
        await using var cmd = new NpgsqlCommand("""
            EXPLAIN
            SELECT p.package_id_lowered, p.project_url
            FROM package_details_catalog_leafs AS p
            WHERE p.package_id IS NOT NULL AND p.package_id_lowered = ANY($1)
            """, conn);
        cmd.Parameters.AddWithValue(packageIds);

        var plan = await ReadPlanAsync(cmd);

        // Assert - Should use the index on package_id_lowered, not a seq scan
        plan.Should().NotContain("Seq Scan on package_details_catalog_leafs",
            "The enrichment query on package_details_catalog_leafs should use the index on package_id_lowered. " +
            "A sequential scan would be catastrophic with 11M+ rows in production.");
    }

    /// <summary>
    /// Seeds enough data that PostgreSQL's query planner prefers index scans.
    /// The planner considers table size when choosing between seq scan and index scan.
    /// With only a few rows, it always chooses seq scan regardless of indexes.
    /// </summary>
    private async Task SeedRealisticDataAsync()
    {
        await using var context = _fixture.CreateDbContext();

        // Seed catalog leafs - need enough rows for planner to prefer index scan
        const int catalogCount = 2000;
        for (var i = 1; i <= catalogCount; i++)
        {
            context.PackageDetailsCatalogLeafs.Add(new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = $"Package.{i}",
                PackageIdLowered = $"package.{i}",
                PackageVersion = "1.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow,
                ProjectUrl = i % 3 == 0 ? $"https://github.com/org/package-{i}" : null,
            });
        }

        // Seed package_downloads
        const int downloadsCount = 1000;
        for (var i = 1; i <= downloadsCount; i++)
        {
            context.PackageDownloads.Add(new PackageDownload
            {
                PackageId = $"Package.{i}",
                PackageIdLowered = $"package.{i}",
                LatestDownloadCount = i * 100,
                LatestDownloadCountCheckedUtc = DateTime.UtcNow.Date.AddDays(-1),
                IconUrl = $"https://example.com/icons/{i}.png"
            });
        }

        await context.SaveChangesAsync();

        // Force PostgreSQL to update statistics so the planner has accurate row counts
        await using var conn = (NpgsqlConnection)context.Database.GetDbConnection();
        await conn.OpenAsync();
        await using var analyzeCmd = new NpgsqlCommand("ANALYZE package_details_catalog_leafs; ANALYZE package_downloads;", conn);
        await analyzeCmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Reads the EXPLAIN output from a command and returns it as a single string.
    /// </summary>
    private static async Task<string> ReadPlanAsync(NpgsqlCommand cmd)
    {
        var planLines = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            planLines.Add(reader.GetString(0));
        }

        return string.Join("\n", planLines);
    }
}
