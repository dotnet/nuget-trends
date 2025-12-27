using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using Testcontainers.PostgreSql;
using Xunit;

namespace NuGetTrends.Data.Tests.Infrastructure;

/// <summary>
/// xUnit fixture that manages a PostgreSQL container for integration tests.
/// The container is started once per test class and disposed after all tests complete.
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime
{
    private const string PostgresImage = "postgres:17";
    private const string DatabaseName = "nugettrends_test";
    private const string Username = "postgres";
    private const string Password = "testpassword";

    private readonly PostgreSqlContainer _container;

    public PostgreSqlFixture()
    {
        _container = new PostgreSqlBuilder()
            .WithImage(PostgresImage)
            .WithDatabase(DatabaseName)
            .WithUsername(Username)
            .WithPassword(Password)
            .Build();
    }

    /// <summary>
    /// Connection string for tests.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await RunMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    /// <summary>
    /// Creates a new DbContext for test operations.
    /// </summary>
    public NuGetTrendsContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NuGetTrendsContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new NuGetTrendsContext(options);
    }

    /// <summary>
    /// Resets the database by truncating relevant tables.
    /// Call this at the start of each test for isolation.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        await using var context = CreateDbContext();
        
        // Truncate tables in correct order (respecting FK constraints)
        await context.Database.ExecuteSqlRawAsync("""
            TRUNCATE TABLE package_downloads CASCADE;
            TRUNCATE TABLE package_details_catalog_leafs CASCADE;
            """);
    }

    /// <summary>
    /// Seeds test data for package_details_catalog_leafs.
    /// </summary>
    public async Task SeedPackageCatalogAsync(params string[] packageIds)
    {
        await using var context = CreateDbContext();
        
        foreach (var packageId in packageIds)
        {
            context.PackageDetailsCatalogLeafs.Add(new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = packageId,
                PackageVersion = "1.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow,
            });
        }
        
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds test data for package_downloads with specific checked timestamps.
    /// </summary>
    public async Task SeedPackageDownloadsAsync(params (string PackageId, DateTime? CheckedUtc)[] packages)
    {
        await using var context = CreateDbContext();
        
        foreach (var (packageId, checkedUtc) in packages)
        {
            context.PackageDownloads.Add(new PackageDownload
            {
                PackageId = packageId,
                PackageIdLowered = packageId.ToLowerInvariant(),
                LatestDownloadCount = 1000,
                LatestDownloadCountCheckedUtc = checkedUtc ?? DateTime.MinValue,
            });
        }
        
        await context.SaveChangesAsync();
    }

    private async Task RunMigrationsAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }
}

/// <summary>
/// Collection definition for PostgreSQL tests.
/// Tests in this collection share the same PostgreSqlFixture instance.
/// </summary>
[CollectionDefinition("PostgreSql")]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
}
