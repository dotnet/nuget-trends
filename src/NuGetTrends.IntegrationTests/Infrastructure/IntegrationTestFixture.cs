using System.Runtime.CompilerServices;
using ClickHouse.Driver.ADO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Protocol.Catalog;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace NuGetTrends.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit fixture that manages PostgreSQL, ClickHouse, and RabbitMQ containers for E2E integration tests.
/// Also handles fetching packages from the real NuGet.org catalog.
/// </summary>
public class IntegrationTestFixture : IAsyncLifetime
{
    private const int TargetPackageCount = 7;
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    // RabbitMQ credentials
    private const string RabbitMqUser = "testuser";
    private const string RabbitMqPass = "testpass";

    // ClickHouse credentials (keep in sync with docker-compose.yml)
    private const string ClickHouseImage = "clickhouse/clickhouse-server:25.11.5";
    private const string ClickHouseDatabase = "nugettrends";
    private const string ClickHouseUser = "clickhouse";
    private const string ClickHousePass = "clickhouse";

    private readonly PostgreSqlContainer _postgresContainer;
    private readonly RabbitMqContainer _rabbitMqContainer;
    private readonly ClickHouseContainer _clickHouseContainer;
    private readonly HttpClient _httpClient;

    public IntegrationTestFixture()
    {
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:17")
            .Build();

        _rabbitMqContainer = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.12.0-management")
            .WithUsername(RabbitMqUser)
            .WithPassword(RabbitMqPass)
            .Build();

        _clickHouseContainer = new ClickHouseBuilder()
            .WithImage(ClickHouseImage)
            .WithUsername(ClickHouseUser)
            .WithPassword(ClickHousePass)
            .Build();

        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Packages imported from the NuGet.org catalog during initialization.
    /// </summary>
    public List<CatalogPackageInfo> ImportedPackages { get; private set; } = [];

    /// <summary>
    /// PostgreSQL connection string for tests.
    /// </summary>
    public string PostgresConnectionString => _postgresContainer.GetConnectionString();

    /// <summary>
    /// ClickHouse connection string for tests (includes database name).
    /// </summary>
    public string ClickHouseConnectionString =>
        $"Host={_clickHouseContainer.Hostname};Port={_clickHouseContainer.GetMappedPublicPort(8123)};Database={ClickHouseDatabase};Username={ClickHouseUser};Password={ClickHousePass}";

    /// <summary>
    /// ClickHouse admin connection string (without database, for setup).
    /// </summary>
    private string ClickHouseAdminConnectionString =>
        $"Host={_clickHouseContainer.Hostname};Port={_clickHouseContainer.GetMappedPublicPort(8123)};Username={ClickHouseUser};Password={ClickHousePass}";

    /// <summary>
    /// RabbitMQ hostname for tests.
    /// </summary>
    public string RabbitMqHostname => _rabbitMqContainer.Hostname;

    /// <summary>
    /// RabbitMQ port for tests.
    /// </summary>
    public int RabbitMqPort => _rabbitMqContainer.GetMappedPublicPort(5672);

    /// <summary>
    /// RabbitMQ username.
    /// </summary>
    public string RabbitMqUsername => RabbitMqUser;

    /// <summary>
    /// RabbitMQ password.
    /// </summary>
    public string RabbitMqPassword => RabbitMqPass;

    public async Task InitializeAsync()
    {
        // Start all containers in parallel
        await Task.WhenAll(
            _postgresContainer.StartAsync(),
            _rabbitMqContainer.StartAsync(),
            _clickHouseContainer.StartAsync());

        // Apply migrations in parallel
        await Task.WhenAll(
            ApplyPostgresMigrationsAsync(),
            ApplyClickHouseMigrationsAsync());

        // Fetch packages from real NuGet.org catalog
        ImportedPackages = await FetchAndImportCatalogPackagesAsync(TargetPackageCount);
    }

    public async Task DisposeAsync()
    {
        _httpClient.Dispose();
        await Task.WhenAll(
            _postgresContainer.DisposeAsync().AsTask(),
            _rabbitMqContainer.DisposeAsync().AsTask(),
            _clickHouseContainer.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Creates a new DbContext instance for tests.
    /// </summary>
    public NuGetTrendsContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NuGetTrendsContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        return new NuGetTrendsContext(options);
    }

    /// <summary>
    /// Creates a new ClickHouseService instance for tests.
    /// </summary>
    public IClickHouseService CreateClickHouseService()
    {
        var connectionInfo = ClickHouseConnectionInfo.Parse(ClickHouseConnectionString);
        return new ClickHouseService(
            ClickHouseConnectionString,
            NullLogger<ClickHouseService>.Instance,
            connectionInfo);
    }

    /// <summary>
    /// Truncates the ClickHouse daily_downloads table.
    /// Useful for test isolation.
    /// </summary>
    public async Task ResetClickHouseTableAsync()
    {
        await using var connection = new ClickHouseConnection(ClickHouseConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "TRUNCATE TABLE daily_downloads";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Forces ClickHouse ReplacingMergeTree to deduplicate rows immediately.
    /// </summary>
    public async Task OptimizeClickHouseTableAsync()
    {
        await using var connection = new ClickHouseConnection(ClickHouseConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "OPTIMIZE TABLE daily_downloads FINAL";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Executes a scalar query against ClickHouse.
    /// </summary>
    public async Task<T> ExecuteClickHouseScalarAsync<T>(string sql)
    {
        await using var connection = new ClickHouseConnection(ClickHouseConnectionString);
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return (T)Convert.ChangeType(result!, typeof(T));
    }

    private async Task ApplyPostgresMigrationsAsync()
    {
        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    private async Task ApplyClickHouseMigrationsAsync()
    {
        var migrationScripts = GetClickHouseMigrationScripts();

        await using var connection = new ClickHouseConnection(ClickHouseAdminConnectionString);
        await connection.OpenAsync();

        foreach (var script in migrationScripts)
        {
            // Split on semicolons to handle multiple statements per file
            var statements = script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var statement in statements)
            {
                var trimmed = statement.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    continue;
                }

                // Skip comment-only blocks
                var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var hasNonCommentLine = lines.Any(line =>
                {
                    var l = line.Trim();
                    return !string.IsNullOrEmpty(l) && !l.StartsWith("--");
                });

                if (!hasNonCommentLine)
                {
                    continue;
                }

                await using var cmd = connection.CreateCommand();
                cmd.CommandText = trimmed;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static List<string> GetClickHouseMigrationScripts([CallerFilePath] string callerFilePath = "")
    {
        // Navigate from this file to the migrations folder:
        // From: src/NuGetTrends.IntegrationTests/Infrastructure/IntegrationTestFixture.cs
        // To:   src/NuGetTrends.Data/ClickHouse/migrations/
        var thisFileDir = Path.GetDirectoryName(callerFilePath)
            ?? throw new InvalidOperationException("Could not determine directory of IntegrationTestFixture.cs");

        var migrationsDir = Path.GetFullPath(
            Path.Combine(thisFileDir, "..", "..", "NuGetTrends.Data", "ClickHouse", "migrations"));

        if (!Directory.Exists(migrationsDir))
        {
            throw new DirectoryNotFoundException(
                $"ClickHouse migrations directory not found at '{migrationsDir}'. " +
                "The directory structure may have changed.");
        }

        var sqlFiles = Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        if (sqlFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No .sql migration files found in '{migrationsDir}'.");
        }

        return sqlFiles.Select(File.ReadAllText).ToList();
    }

    /// <summary>
    /// Fetches recent packages from NuGet.org catalog and imports them into the database.
    /// Filters for stable, listed packages only.
    /// </summary>
    private async Task<List<CatalogPackageInfo>> FetchAndImportCatalogPackagesAsync(int targetCount)
    {
        var logger = NullLogger<CatalogClient>.Instance;
        var catalogClient = new CatalogClient(_httpClient, logger);

        var packages = await FetchRecentCatalogPackagesWithRetryAsync(catalogClient, targetCount);

        // Import packages into database
        await using var context = CreateDbContext();
        foreach (var package in packages)
        {
            var exists = await context.PackageDetailsCatalogLeafs.AnyAsync(
                p => p.PackageId == package.PackageId && p.PackageVersion == package.PackageVersion);

            if (!exists)
            {
                context.PackageDetailsCatalogLeafs.Add(new PackageDetailsCatalogLeaf
                {
                    PackageId = package.PackageId,
                    PackageIdLowered = package.PackageId.ToLowerInvariant(),
                    PackageVersion = package.PackageVersion,
                    CommitTimestamp = package.CommitTimestamp,
                    Listed = true,
                    IsPrerelease = false
                });
            }
        }

        await context.SaveChangesAsync();

        return packages;
    }

    private async Task<List<CatalogPackageInfo>> FetchRecentCatalogPackagesWithRetryAsync(
        CatalogClient catalogClient,
        int targetCount)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                return await FetchRecentCatalogPackagesAsync(catalogClient, targetCount);
            }
            catch (Exception) when (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay * attempt);
            }
        }

        throw new InvalidOperationException(
            $"Failed to fetch catalog packages after {MaxRetries} attempts");
    }

    private async Task<List<CatalogPackageInfo>> FetchRecentCatalogPackagesAsync(
        CatalogClient catalogClient,
        int targetCount)
    {
        const string catalogIndexUrl = "https://api.nuget.org/v3/catalog0/index.json";

        var index = await catalogClient.GetIndexAsync(catalogIndexUrl, CancellationToken.None);
        var packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packages = new List<CatalogPackageInfo>();

        // Start from the latest page, work backwards
        foreach (var pageItem in index.Items.OrderByDescending(p => p.CommitTimestamp))
        {
            var page = await catalogClient.GetPageAsync(pageItem.Url, CancellationToken.None);

            foreach (var leaf in page.Items
                         .Where(l => l.Type == CatalogLeafType.PackageDetails)
                         .OrderByDescending(l => l.CommitTimestamp))
            {
                if (string.IsNullOrEmpty(leaf.PackageId) || string.IsNullOrEmpty(leaf.PackageVersion))
                {
                    continue;
                }

                // Skip if we already have this package (different version)
                if (packageIds.Contains(leaf.PackageId))
                {
                    continue;
                }

                // Skip prereleases
                if (leaf.PackageVersion.Contains('-'))
                {
                    continue;
                }

                // Fetch the leaf to check if it's listed
                try
                {
                    var leafDetails = await catalogClient.GetPackageDetailsLeafAsync(leaf.Url, CancellationToken.None);
                    if (leafDetails is PackageDetailsCatalogLeaf details && details.Listed != false)
                    {
                        packageIds.Add(leaf.PackageId);
                        packages.Add(new CatalogPackageInfo(
                            leaf.PackageId,
                            leaf.PackageVersion,
                            leaf.CommitTimestamp));

                        if (packages.Count >= targetCount)
                        {
                            return packages;
                        }
                    }
                }
                catch
                {
                    // Skip packages that fail to fetch
                    continue;
                }
            }

            // Don't go too far back - limit to last 5 pages
            if (index.Items.Count - index.Items.IndexOf(pageItem) > 5)
            {
                break;
            }
        }

        if (packages.Count == 0)
        {
            throw new InvalidOperationException("No suitable packages found in the NuGet.org catalog");
        }

        return packages;
    }
}

/// <summary>
/// Information about a package fetched from the NuGet.org catalog.
/// </summary>
public record CatalogPackageInfo(
    string PackageId,
    string PackageVersion,
    DateTimeOffset CommitTimestamp);

/// <summary>
/// Collection definition for E2E integration tests.
/// Tests in this collection share the same IntegrationTestFixture instance.
/// </summary>
[CollectionDefinition("E2E")]
public class IntegrationTestCollection : ICollectionFixture<IntegrationTestFixture>
{
}
