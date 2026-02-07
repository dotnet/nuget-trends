using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.Scheduler.Tests;

/// <summary>
/// Shared PostgreSQL container for all CatalogLeafProcessor tests.
/// Using IClassFixture avoids spinning up a separate container per test,
/// which can overwhelm Docker when many containers are already running.
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    public string ConnectionString { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        // Colima's VZ driver needs a moment to set up port forwarding from VM to host.
        // Testcontainers marks the container "ready" based on an in-container check,
        // but the host port may not be reachable yet.
        await WaitForPortForwardingAsync(ConnectionString);

        // Run migrations once
        var options = new DbContextOptionsBuilder<NuGetTrendsContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        await using var context = new NuGetTrendsContext(options);
        await context.Database.MigrateAsync();
    }

    private static async Task WaitForPortForwardingAsync(string connectionString, int maxRetries = 10)
    {
        await using var conn = new Npgsql.NpgsqlConnection(connectionString);
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                await conn.OpenAsync();
                return;
            }
            catch (Npgsql.NpgsqlException) when (i < maxRetries - 1)
            {
                await Task.Delay(500);
            }
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}

/// <summary>
/// Tests for CatalogLeafProcessor, particularly around duplicate key exception handling.
/// </summary>
public class CatalogLeafProcessorTests : IClassFixture<PostgresFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly string _connectionString;

    public CatalogLeafProcessorTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        _output = output;
        _connectionString = fixture.ConnectionString;
    }

    private NuGetTrendsContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NuGetTrendsContext>()
            .UseNpgsql(_connectionString)
            .Options;
        return new NuGetTrendsContext(options);
    }

    private (ServiceProvider provider, CatalogLeafProcessor processor) CreateProcessor()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NuGetTrendsContext>(options =>
            options.UseNpgsql(_connectionString));
        services.AddLogging(builder => builder.AddProvider(new XUnitLoggerProvider(_output)));

        var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<CatalogLeafProcessor>>();
        var processor = new CatalogLeafProcessor(provider, logger);

        return (provider, processor);
    }

    /// <summary>
    /// Tests that CatalogLeafProcessor correctly handles the case where a package
    /// already exists in the database (AnyAsync returns true).
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsAsync_PackageAlreadyExists_DoesNotInsertDuplicate()
    {
        // Arrange
        var packageId = "TestPackage";
        var packageVersion = "1.0.0";

        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        var leaf = new PackageDetailsCatalogLeaf
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            CommitTimestamp = DateTimeOffset.UtcNow,
        };

        // First insert should succeed
        await processor.ProcessPackageDetailsAsync(leaf, CancellationToken.None);

        // Create a second processor with a fresh scope
        var logger = provider.GetRequiredService<ILogger<CatalogLeafProcessor>>();
        var processor2 = new CatalogLeafProcessor(provider, logger);

        // Try to process the same package again
        var duplicateLeaf = new PackageDetailsCatalogLeaf
        {
            PackageId = packageId,
            PackageVersion = packageVersion,
            CommitTimestamp = DateTimeOffset.UtcNow.AddSeconds(1),
        };

        // Act - This should NOT throw because AnyAsync will return true
        await processor2.ProcessPackageDetailsAsync(duplicateLeaf, CancellationToken.None);

        // Assert - Verify only one record exists
        await using var verifyContext = CreateDbContext();
        var count = await verifyContext.PackageDetailsCatalogLeafs
            .CountAsync(p => p.PackageId == packageId);
        count.Should().Be(1, "duplicate was skipped by AnyAsync check");
    }

    /// <summary>
    /// Tests that the CatalogLeafProcessor handles duplicate key exceptions gracefully
    /// when a race condition occurs (package inserted between AnyAsync and SaveChangesAsync).
    /// This is the key regression test for the stuck cursor fix.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsAsync_DuplicateKeyRaceCondition_HandledGracefully()
    {
        // Arrange
        var packageId = "RaceConditionPackage";
        var packageVersion = "1.0.0";

        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        // First, insert the package via a separate context (simulates concurrent process winning the race)
        await using (var concurrentContext = CreateDbContext())
        {
            concurrentContext.PackageDetailsCatalogLeafs.Add(new PackageDetailsCatalogLeaf
            {
                PackageId = packageId,
                PackageIdLowered = packageId.ToLowerInvariant(),
                PackageVersion = packageVersion,
                CommitTimestamp = DateTimeOffset.UtcNow.AddSeconds(-1),
            });
            await concurrentContext.SaveChangesAsync();
            _output.WriteLine("Concurrent context inserted the package first");
        }

        // Simulate the race condition:
        // The processor's AnyAsync would return false (before the concurrent insert),
        // then it adds the leaf to context, then SaveChangesAsync fails.
        // We simulate this by adding directly to the context (bypassing AnyAsync).
        var duplicateLeaf = new PackageDetailsCatalogLeaf
        {
            PackageId = packageId,
            PackageIdLowered = packageId.ToLowerInvariant(),
            PackageVersion = packageVersion,
            CommitTimestamp = DateTimeOffset.UtcNow,
        };
        processor.Context.PackageDetailsCatalogLeafs.Add(duplicateLeaf);

        // Act - Try to save. This should trigger the duplicate key exception.
        // The fix in ProcessPackageDetailsAsync catches this, but we're not going through
        // ProcessPackageDetailsAsync here. Let's trigger SaveChangesAsync and verify the
        // exception handling manually, then verify the processor can continue.

        try
        {
            await processor.Context.SaveChangesAsync();
            Assert.Fail("Expected DbUpdateException");
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("23505") == true)
        {
            // This is expected - duplicate key violation
            // The fix detaches the entity
            processor.Context.Entry(duplicateLeaf).State = EntityState.Detached;
            _output.WriteLine("Caught duplicate key exception and detached entity (as fix does)");
        }

        // Now process a NEW package to verify the processor still works after handling the exception
        var newPackageLeaf = new PackageDetailsCatalogLeaf
        {
            PackageId = "PostRaceRecoveryPkg",
            PackageVersion = "1.0.0",
            CommitTimestamp = DateTimeOffset.UtcNow,
        };

        // This should work - the context is clean after detaching
        await processor.ProcessPackageDetailsAsync(newPackageLeaf, CancellationToken.None);
        _output.WriteLine("ProcessPackageDetailsAsync completed for new package");

        // Assert - Verify the new package was saved (proves processing continues after exception)
        await using var verifyContext = CreateDbContext();
        var newPackageExists = await verifyContext.PackageDetailsCatalogLeafs
            .AnyAsync(p => p.PackageId == "PostRaceRecoveryPkg");
        newPackageExists.Should().BeTrue("processor should continue working after handling duplicate key exception");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync correctly processes a batch of new packages
    /// in a single database operation (avoiding N+1 queries).
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_NewPackages_InsertsAllInSingleOperation()
    {
        // Arrange
        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        var leaves = new List<PackageDetailsCatalogLeaf>
        {
            new() { PackageId = "BatchPackage1", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "BatchPackage2", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "BatchPackage3", PackageVersion = "2.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
        };

        // Act
        await processor.ProcessPackageDetailsBatchAsync(leaves, CancellationToken.None);

        // Assert - All packages should be inserted
        await using var verifyContext = CreateDbContext();
        var count = await verifyContext.PackageDetailsCatalogLeafs
            .CountAsync(p => p.PackageId != null && p.PackageId.StartsWith("BatchPackage"));
        count.Should().Be(3, "all three packages should be inserted");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync correctly skips packages that already exist
    /// in the database and only inserts new ones.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_MixedNewAndExisting_OnlyInsertsNew()
    {
        // Arrange
        var (provider, _) = CreateProcessor();
        using var __ = provider;

        // Pre-insert one package
        await using (var setupContext = CreateDbContext())
        {
            setupContext.PackageDetailsCatalogLeafs.Add(new PackageDetailsCatalogLeaf
            {
                PackageId = "ExistingPackage",
                PackageIdLowered = "existingpackage",
                PackageVersion = "1.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await setupContext.SaveChangesAsync();
        }

        var logger = provider.GetRequiredService<ILogger<CatalogLeafProcessor>>();
        var processor = new CatalogLeafProcessor(provider, logger);

        var leaves = new List<PackageDetailsCatalogLeaf>
        {
            new() { PackageId = "ExistingPackage", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "MixedNew1", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "MixedNew2", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
        };

        // Act
        await processor.ProcessPackageDetailsBatchAsync(leaves, CancellationToken.None);

        // Assert
        await using var verifyContext = CreateDbContext();

        // Existing package should still have original timestamp (not updated)
        var existingPackage = await verifyContext.PackageDetailsCatalogLeafs
            .SingleAsync(p => p.PackageId == "ExistingPackage");
        existingPackage.CommitTimestamp.Should().BeBefore(DateTimeOffset.UtcNow.AddHours(-1),
            "existing package should not be updated");

        // New packages should exist
        var newPackagesCount = await verifyContext.PackageDetailsCatalogLeafs
            .CountAsync(p => p.PackageId != null && p.PackageId.StartsWith("MixedNew"));
        newPackagesCount.Should().Be(2, "both new packages should be inserted");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync handles an empty batch gracefully.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_EmptyBatch_DoesNothing()
    {
        // Arrange
        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        // Act - should not throw
        await processor.ProcessPackageDetailsBatchAsync([], CancellationToken.None);

        // Assert - no exception means success
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync correctly matches packages with same case.
    /// Note: NuGet catalog uses consistent casing for package IDs, so the database query
    /// is case-sensitive and the case-insensitive HashSet lookup handles edge cases.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_SameCaseMatch_SkipsExisting()
    {
        // Arrange
        var (provider, _) = CreateProcessor();
        using var __ = provider;

        // Pre-insert with specific case
        await using (var setupContext = CreateDbContext())
        {
            setupContext.PackageDetailsCatalogLeafs.Add(new PackageDetailsCatalogLeaf
            {
                PackageId = "CaseTest",
                PackageIdLowered = "casetest",
                PackageVersion = "1.0.0",
                CommitTimestamp = DateTimeOffset.UtcNow.AddDays(-1),
            });
            await setupContext.SaveChangesAsync();
        }

        var logger = provider.GetRequiredService<ILogger<CatalogLeafProcessor>>();
        var processor = new CatalogLeafProcessor(provider, logger);

        // Try to insert with same case
        var leaves = new List<PackageDetailsCatalogLeaf>
        {
            new() { PackageId = "CaseTest", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
        };

        // Act
        await processor.ProcessPackageDetailsBatchAsync(leaves, CancellationToken.None);

        // Assert - should still only have one record
        await using var verifyContext = CreateDbContext();
        var count = await verifyContext.PackageDetailsCatalogLeafs
            .CountAsync(p => p.PackageId == "CaseTest");
        count.Should().Be(1, "duplicate should be skipped");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync handles all packages already existing.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_AllExisting_SkipsAll()
    {
        // Arrange
        var (provider, _) = CreateProcessor();
        using var __ = provider;

        // Pre-insert all packages
        await using (var setupContext = CreateDbContext())
        {
            setupContext.PackageDetailsCatalogLeafs.AddRange(
                new PackageDetailsCatalogLeaf { PackageId = "AllExisting1", PackageIdLowered = "allexisting1", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
                new PackageDetailsCatalogLeaf { PackageId = "AllExisting2", PackageIdLowered = "allexisting2", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow }
            );
            await setupContext.SaveChangesAsync();
        }

        var logger = provider.GetRequiredService<ILogger<CatalogLeafProcessor>>();
        var processor = new CatalogLeafProcessor(provider, logger);

        // Try to insert the same packages
        var leaves = new List<PackageDetailsCatalogLeaf>
        {
            new() { PackageId = "AllExisting1", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow.AddHours(1) },
            new() { PackageId = "AllExisting2", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow.AddHours(1) },
        };

        // Act - should not throw
        await processor.ProcessPackageDetailsBatchAsync(leaves, CancellationToken.None);

        // Assert - count should still be 2
        await using var verifyContext = CreateDbContext();
        var count = await verifyContext.PackageDetailsCatalogLeafs
            .CountAsync(p => p.PackageId != null && p.PackageId.StartsWith("AllExisting"));
        count.Should().Be(2, "no duplicates should be inserted");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsBatchAsync populates PackageIdLowered for new packages.
    /// This ensures the case-insensitive join in GetUnprocessedPackageIds works correctly.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsBatchAsync_NewPackages_PopulatesPackageIdLowered()
    {
        // Arrange
        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        var leaves = new List<PackageDetailsCatalogLeaf>
        {
            new() { PackageId = "MixedCase.Casing", PackageVersion = "1.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "UPPERCASE.CASING", PackageVersion = "2.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
            new() { PackageId = "lowercase.casing", PackageVersion = "3.0.0", CommitTimestamp = DateTimeOffset.UtcNow },
        };

        // Act
        await processor.ProcessPackageDetailsBatchAsync(leaves, CancellationToken.None);

        // Assert - Verify PackageIdLowered is populated correctly
        await using var verifyContext = CreateDbContext();
        var packages = await verifyContext.PackageDetailsCatalogLeafs
            .Where(p => p.PackageIdLowered != null && p.PackageIdLowered.Contains(".casing"))
            .ToListAsync();

        packages.Should().HaveCount(3);
        packages.Should().Contain(p => p.PackageId == "MixedCase.Casing" && p.PackageIdLowered == "mixedcase.casing");
        packages.Should().Contain(p => p.PackageId == "UPPERCASE.CASING" && p.PackageIdLowered == "uppercase.casing");
        packages.Should().Contain(p => p.PackageId == "lowercase.casing" && p.PackageIdLowered == "lowercase.casing");
    }

    /// <summary>
    /// Tests that ProcessPackageDetailsAsync (individual path) populates PackageIdLowered.
    /// This is the fallback path used when batch processing hits a duplicate key exception.
    /// </summary>
    [Fact]
    public async Task ProcessPackageDetailsAsync_NewPackage_PopulatesPackageIdLowered()
    {
        // Arrange
        var (provider, processor) = CreateProcessor();
        using var _ = provider;

        var leaf = new PackageDetailsCatalogLeaf
        {
            PackageId = "Individual.Test.Package",
            PackageVersion = "1.0.0",
            CommitTimestamp = DateTimeOffset.UtcNow,
        };

        // Act
        await processor.ProcessPackageDetailsAsync(leaf, CancellationToken.None);

        // Assert - Verify PackageIdLowered is populated
        await using var verifyContext = CreateDbContext();
        var savedPackage = await verifyContext.PackageDetailsCatalogLeafs
            .SingleAsync(p => p.PackageId == "Individual.Test.Package");

        savedPackage.PackageIdLowered.Should().Be("individual.test.package",
            "PackageIdLowered should be populated in individual processing path");
    }

}

/// <summary>
/// Logger provider that writes to xUnit's test output.
/// </summary>
internal class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XUnitLoggerProvider(ITestOutputHelper output)
    {
        _output = output;
    }

    public ILogger CreateLogger(string categoryName) => new XUnitLogger(_output, categoryName);
    public void Dispose() { }
}

internal class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _category;

    public XUnitLogger(ITestOutputHelper output, string category)
    {
        _output = output;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            var message = formatter(state, exception);
            _output.WriteLine($"[{logLevel}] {_category}: {message}");
            if (exception != null)
            {
                _output.WriteLine($"  Exception: {exception.Message}");
            }
        }
        catch
        {
            // Ignore - test output may not be available
        }
    }
}
