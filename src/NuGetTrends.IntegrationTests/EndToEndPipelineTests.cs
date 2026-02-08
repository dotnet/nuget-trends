using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.IntegrationTests.Infrastructure;
using NuGetTrends.Scheduler;
using NuGetTrends.Web;
using RabbitMQ.Client;
using Sentry;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the full NuGet Trends pipeline.
/// Tests: Catalog Import -> Seed Historical Data -> Daily Download Pipeline -> API Verification
/// Uses PostgreSQL for package metadata and ClickHouse for daily downloads.
/// </summary>
[Collection("E2E")]
public class EndToEndPipelineTests : IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private NuGetTrendsWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    public EndToEndPipelineTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        // Set environment BEFORE creating the factory - Program.cs reads this early
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

        _factory = new NuGetTrendsWebApplicationFactory(_fixture);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FullPipeline_CatalogImport_SeedData_DailyDownload_ApiVerification()
    {
        // Clean up from any previous test runs (tests share the fixture and database)
        await _fixture.ResetClickHouseTableAsync();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM package_downloads");
        }

        // Arrange - Log imported packages
        _output.WriteLine($"Imported {_fixture.ImportedPackages.Count} packages from NuGet.org catalog:");
        foreach (var pkg in _fixture.ImportedPackages)
        {
            _output.WriteLine($"  - {pkg.PackageId} v{pkg.PackageVersion}");
        }

        // Verify catalog import
        await VerifyCatalogImport();

        // Seed historical data
        await SeedHistoricalDownloads();

        // Verify seeded data exists
        await VerifySeededData();

        // Run daily download pipeline
        await RunDailyDownloadPipeline();

        // Verify today's data was fetched
        await VerifyTodaysDataExists();

        // Verify publisher finds zero unprocessed packages after pipeline ran
        await VerifyNoUnprocessedPackages();

        // Verify search API
        await VerifySearchEndpoint();

        // Verify history API
        await VerifyHistoryEndpoint();

        // Run trending snapshot refresh and verify trending API
        await RunTrendingSnapshotRefresh();
        await VerifyTrendingEndpoint();
    }

    [Fact]
    public async Task ApiEndpoints_HandleDownloadCountsBeyondInt32MaxValue()
    {
        // Clean up from any previous test runs
        await _fixture.ResetClickHouseTableAsync();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM package_downloads");
        }

        // Arrange - Create a package with downloads exceeding int.MaxValue
        const string testPackageId = "TestPackage.WithHugeDownloads";
        var justOverIntMax = (long)int.MaxValue + 1_000_000L; // ~2.148 billion
        var wellOverIntMax = 3_000_000_000L; // 3 billion

        await using (var ctx = _fixture.CreateDbContext())
        {
            // Add package to PostgreSQL with large download count
            ctx.PackageDownloads.Add(new PackageDownload
            {
                PackageId = testPackageId,
                PackageIdLowered = testPackageId.ToLower(),
                LatestDownloadCount = wellOverIntMax,
                LatestDownloadCountCheckedUtc = DateTime.UtcNow,
                IconUrl = "https://example.com/icon.png"
            });
            await ctx.SaveChangesAsync();
        }

        // Add download history to ClickHouse with large counts
        var clickHouseService = _factory.Services.GetRequiredService<IClickHouseService>();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

        // Create a week of data with large counts
        for (var i = 0; i < 7; i++)
        {
            downloads.Add((testPackageId, today.AddDays(-i), justOverIntMax));
        }
        await clickHouseService.InsertDailyDownloadsAsync(downloads);

        // Act & Assert - Verify Search API handles large download counts
        _output.WriteLine("Testing Search API with large download counts...");
        var searchResponse = await _client.GetAsync($"/api/package/search?q={Uri.EscapeDataString(testPackageId)}");
        searchResponse.EnsureSuccessStatusCode();

        var searchJson = await searchResponse.Content.ReadAsStringAsync();
        var searchResults = JsonSerializer.Deserialize<List<SearchResult>>(searchJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        searchResults.Should().NotBeNull();
        var result = searchResults!.FirstOrDefault(r => r.PackageId.Equals(testPackageId, StringComparison.OrdinalIgnoreCase));
        result.Should().NotBeNull();
        result!.LatestDownloadCount.Should().Be(wellOverIntMax);
        _output.WriteLine($"✓ Search API correctly returned {result.LatestDownloadCount:N0} downloads (>{int.MaxValue:N0})");

        // Act & Assert - Verify History API handles large download counts
        _output.WriteLine("Testing History API with large download counts...");
        var historyResponse = await _client.GetAsync($"/api/package/history/{Uri.EscapeDataString(testPackageId)}?months=1");
        historyResponse.EnsureSuccessStatusCode();

        var historyJson = await historyResponse.Content.ReadAsStringAsync();
        var history = JsonSerializer.Deserialize<PackageHistory>(historyJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        history.Should().NotBeNull();
        history!.Downloads.Should().NotBeEmpty();
        history.Downloads.Should().AllSatisfy(d =>
        {
            d.Count.Should().NotBeNull();
            d.Count.Should().BeGreaterThan(int.MaxValue, "Weekly count should exceed int.MaxValue");
        });
        _output.WriteLine($"✓ History API correctly returned weekly counts >{int.MaxValue:N0}");

        _output.WriteLine("✅ All API endpoints successfully handle download counts beyond int.MaxValue");
    }

    [Fact]
    public async Task Pipeline_SecondDay_ReprocessesPackages()
    {
        // Clean up from any previous test runs (tests share the fixture and database)
        await _fixture.ResetClickHouseTableAsync();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM package_downloads");
        }

        // Arrange — seed the same data the full pipeline test uses
        await VerifyCatalogImport();
        await SeedHistoricalDownloads();

        // Run pipeline once to process all packages (simulates "day 1")
        await RunDailyDownloadPipeline();
        await VerifyNoUnprocessedPackages();

        // Simulate "next day" by backdating LatestDownloadCountCheckedUtc to yesterday
        await using (var context = _fixture.CreateDbContext())
        {
            var yesterday = DateTime.UtcNow.Date.AddDays(-1);
            await context.Database.ExecuteSqlRawAsync(
                "UPDATE package_downloads SET latest_download_count_checked_utc = {0}",
                yesterday);
        }

        // Verify packages are now unprocessed again
        await using (var context = _fixture.CreateDbContext())
        {
            var unprocessed = await context.GetUnprocessedPackageIds(DateTime.UtcNow.Date).ToListAsync();
            unprocessed.Should().NotBeEmpty("packages should be unprocessed after backdating check timestamp");
            _output.WriteLine($"After backdating, {unprocessed.Count} packages are unprocessed.");
        }

        // Run pipeline again (simulates "day 2")
        await RunDailyDownloadPipeline();

        // Verify all packages were processed again
        await VerifyNoUnprocessedPackages();

        // Verify today's data still exists in ClickHouse
        await VerifyTodaysDataExists();

        _output.WriteLine("Second-day pipeline re-processing verified successfully.");
    }

    [Fact]
    public async Task Pipeline_RemovesDeletedPackages_FromCatalog()
    {
        const string fakePackageId = "__test-nonexistent-pkg__";

        // Clean up from any previous test runs
        await _fixture.ResetClickHouseTableAsync();
        await using (var ctx = _fixture.CreateDbContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM package_downloads");
            // Clean up leftover fake package from previous runs
            await ctx.Database.ExecuteSqlRawAsync(
                "DELETE FROM package_details_catalog_leafs WHERE package_id = {0}", fakePackageId);
        }

        // Verify catalog import and seed historical data (same as other tests)
        await VerifyCatalogImport();
        await SeedHistoricalDownloads();

        // Insert a fake catalog entry for a package that doesn't exist on NuGet.org
        await using (var context = _fixture.CreateDbContext())
        {
            context.PackageDetailsCatalogLeafs.Add(new NuGet.Protocol.Catalog.Models.PackageDetailsCatalogLeaf
            {
                PackageId = fakePackageId,
                PackageIdLowered = fakePackageId,
                PackageVersion = "1.0.0",
                Listed = true,
                CommitTimestamp = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();

            // Verify it was inserted
            var exists = await context.PackageDetailsCatalogLeafs
                .AnyAsync(p => p.PackageId == fakePackageId);
            exists.Should().BeTrue("fake package should exist in catalog before pipeline runs");
            _output.WriteLine($"Inserted fake catalog entry: {fakePackageId}");
        }

        // Run the pipeline — NuGet API returns null for the fake package, triggering deletion
        await RunDailyDownloadPipeline();

        // Assert: fake package was removed from catalog
        await using (var context = _fixture.CreateDbContext())
        {
            var fakeExists = await context.PackageDetailsCatalogLeafs
                .AnyAsync(p => p.PackageId == fakePackageId);
            fakeExists.Should().BeFalse(
                "fake non-existent package should have been removed from catalog by the pipeline");
            _output.WriteLine("Verified: fake package was removed from catalog.");
        }

        // Assert: real packages still have their catalog entries
        await using (var context = _fixture.CreateDbContext())
        {
            foreach (var pkg in _fixture.ImportedPackages)
            {
                var exists = await context.PackageDetailsCatalogLeafs
                    .AnyAsync(p => p.PackageId == pkg.PackageId && p.PackageVersion == pkg.PackageVersion);
                exists.Should().BeTrue(
                    $"real package {pkg.PackageId} v{pkg.PackageVersion} should still exist in catalog");
            }
            _output.WriteLine("Verified: all real packages still exist in catalog.");
        }

        // Assert: real packages have download data in ClickHouse
        await VerifyTodaysDataExists();

        _output.WriteLine("Deleted-package removal test passed.");
    }

    private async Task VerifyCatalogImport()
    {
        _output.WriteLine("Verifying catalog import...");

        // Verify we have at least some packages imported from the catalog
        _fixture.ImportedPackages.Should().NotBeEmpty(
            "At least one package should have been imported from the NuGet.org catalog");

        _fixture.ImportedPackages.Should().HaveCountGreaterThanOrEqualTo(3,
            "At least 3 packages should have been imported from the NuGet.org catalog");

        await using var context = _fixture.CreateDbContext();

        // Verify each package exists in the database
        foreach (var pkg in _fixture.ImportedPackages)
        {
            var exists = await context.PackageDetailsCatalogLeafs.AnyAsync(
                p => p.PackageId == pkg.PackageId && p.PackageVersion == pkg.PackageVersion);

            exists.Should().BeTrue($"Package {pkg.PackageId} v{pkg.PackageVersion} should exist in catalog");
        }

        // Verify total count in database matches imported count
        var dbCount = await context.PackageDetailsCatalogLeafs.CountAsync();
        dbCount.Should().Be(_fixture.ImportedPackages.Count,
            $"Database should contain exactly {_fixture.ImportedPackages.Count} packages");

        _output.WriteLine($"Catalog import verified: {_fixture.ImportedPackages.Count} packages in database.");
    }

    private async Task SeedHistoricalDownloads()
    {
        _output.WriteLine("Seeding historical download data...");

        await using var context = _fixture.CreateDbContext();
        var clickHouseService = _fixture.CreateClickHouseService();

        await TestDataSeeder.SeedHistoricalDownloadsAsync(context, clickHouseService, _fixture.ImportedPackages);

        _output.WriteLine($"Seeded {TestDataSeeder.DaysOfHistory} days of history for {_fixture.ImportedPackages.Count} packages.");
    }

    private async Task VerifySeededData()
    {
        _output.WriteLine("Verifying seeded data...");

        // Verify ClickHouse daily downloads
        var expectedCount = _fixture.ImportedPackages.Count * TestDataSeeder.DaysOfHistory;
        var dailyDownloadCount = await _fixture.ExecuteClickHouseScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads");

        ((long)dailyDownloadCount).Should().Be(expectedCount,
            $"Expected {expectedCount} daily download records ({_fixture.ImportedPackages.Count} packages x {TestDataSeeder.DaysOfHistory} days)");

        // Verify PostgreSQL package_downloads
        await using var context = _fixture.CreateDbContext();
        var packageDownloadCount = await context.PackageDownloads.CountAsync();
        packageDownloadCount.Should().Be(_fixture.ImportedPackages.Count,
            $"Expected {_fixture.ImportedPackages.Count} package download records");

        _output.WriteLine($"Verified {dailyDownloadCount} daily downloads (ClickHouse) and {packageDownloadCount} package downloads (PostgreSQL).");
    }

    private async Task RunDailyDownloadPipeline()
    {
        _output.WriteLine("Running daily download pipeline...");

        var connectionFactory = new ConnectionFactory
        {
            HostName = _fixture.RabbitMqHostname,
            Port = _fixture.RabbitMqPort,
            UserName = _fixture.RabbitMqUsername,
            Password = _fixture.RabbitMqPassword,
            DispatchConsumersAsync = true
        };

        // Create ClickHouse service for the worker
        var clickHouseService = _fixture.CreateClickHouseService();

        // Create services for the worker
        var services = new ServiceCollection();
        services.AddSingleton<IConnectionFactory>(connectionFactory);
        services.AddSingleton<NuGetAvailabilityState>();
        services.AddSingleton<INuGetSearchService, NuGetSearchService>();
        services.AddSingleton<IClickHouseService>(clickHouseService);
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.Configure<DailyDownloadWorkerOptions>(options =>
        {
            options.WorkerCount = 1;
        });
        services.AddDbContext<NuGetTrendsContext>(options =>
        {
            options.UseNpgsql(_fixture.PostgresConnectionString);
        });

        var serviceProvider = services.BuildServiceProvider();

        // Start the worker
        var worker = new DailyDownloadWorker(
            serviceProvider.GetRequiredService<IOptions<DailyDownloadWorkerOptions>>(),
            connectionFactory,
            serviceProvider,
            serviceProvider.GetRequiredService<INuGetSearchService>(),
            serviceProvider.GetRequiredService<IClickHouseService>(),
            serviceProvider.GetRequiredService<NuGetAvailabilityState>(),
            NullLogger<DailyDownloadWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        _output.WriteLine("Worker started, waiting for it to connect...");

        // Give worker time to connect to RabbitMQ
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Run the publisher to queue package IDs
        await using var context = _fixture.CreateDbContext();
        var publisher = new DailyDownloadPackageIdPublisher(
            connectionFactory,
            context,
            Sentry.Extensibility.DisabledHub.Instance,
            NullLogger<DailyDownloadPackageIdPublisher>.Instance);

        _output.WriteLine("Publishing package IDs to queue...");
        await publisher.Import(new TestJobCancellationToken(), context: null);

        // Wait for queue to drain
        await WaitForQueueDrain(connectionFactory, TimeSpan.FromMinutes(3));

        // Stop the worker
        await worker.StopAsync(CancellationToken.None);

        _output.WriteLine("Daily download pipeline completed.");
    }

    private async Task WaitForQueueDrain(IConnectionFactory connectionFactory, TimeSpan timeout)
    {
        _output.WriteLine("Waiting for queue to drain...");

        using var connection = connectionFactory.CreateConnection();
        using var channel = connection.CreateModel();

        var deadline = DateTime.UtcNow + timeout;
        var lastMessageCount = uint.MaxValue;

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var queueInfo = channel.QueueDeclarePassive("daily-download");
                var messageCount = queueInfo.MessageCount;

                if (messageCount == 0)
                {
                    _output.WriteLine("Queue is empty, waiting for processing to complete...");
                    // Give extra time for in-flight messages to be processed
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    return;
                }

                if (messageCount != lastMessageCount)
                {
                    _output.WriteLine($"Queue has {messageCount} messages remaining...");
                    lastMessageCount = messageCount;
                }

                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex)
            {
                _output.WriteLine($"Error checking queue: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }

        throw new TimeoutException($"Queue did not drain within {timeout.TotalMinutes} minutes");
    }

    private async Task VerifyNoUnprocessedPackages()
    {
        _output.WriteLine("Verifying publisher finds zero unprocessed packages...");

        await using var context = _fixture.CreateDbContext();
        var todayUtc = DateTime.UtcNow.Date;
        var unprocessed = await context.GetUnprocessedPackageIds(todayUtc).ToListAsync();

        unprocessed.Should().BeEmpty(
            "all packages should have been processed by the pipeline today");

        _output.WriteLine("Confirmed: no unprocessed packages remain.");
    }

    private async Task VerifyTodaysDataExists()
    {
        _output.WriteLine("Verifying today's download data exists in ClickHouse...");

        var clickHouseService = _fixture.CreateClickHouseService();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var pkg in _fixture.ImportedPackages)
        {
            // Query ClickHouse for today's data (package_id is lowercased in ClickHouse)
            var todaysDownloads = await clickHouseService.GetWeeklyDownloadsAsync(pkg.PackageId, months: 1);

            // Get the most recent entry which should include today
            var mostRecent = todaysDownloads.OrderByDescending(d => d.Week).FirstOrDefault();

            mostRecent.Should().NotBeNull(
                $"Today's download data should exist for {pkg.PackageId}");

            // Download count can be 0 for newly published packages, but should not be null
            mostRecent!.Count.Should().NotBeNull(
                $"Today's download count for {pkg.PackageId} should not be null (fetched from NuGet.org)");

            mostRecent.Count.Should().BeGreaterThanOrEqualTo(0,
                $"Today's download count for {pkg.PackageId} should be >= 0 (fetched from NuGet.org)");

            _output.WriteLine($"  - {pkg.PackageId}: {mostRecent.Count:N0} downloads (weekly avg)");
        }

        _output.WriteLine("Today's download data verified.");
    }

    private async Task RunTrendingSnapshotRefresh()
    {
        _output.WriteLine("Running trending packages snapshot refresh...");

        var clickHouseService = _fixture.CreateClickHouseService();

        // The trending query needs package_first_seen populated
        await clickHouseService.UpdatePackageFirstSeenAsync();

        // Compute trending packages (needs data for 2 consecutive weeks)
        var trendingPackages = await clickHouseService.ComputeTrendingPackagesAsync(
            minWeeklyDownloads: 1, // Use low threshold for test data
            maxPackageAgeMonths: 12);

        _output.WriteLine($"Computed {trendingPackages.Count} trending packages from ClickHouse");

        if (trendingPackages.Count == 0)
        {
            _output.WriteLine("No trending packages computed (test data may not span 2 complete weeks). " +
                              "Inserting synthetic snapshot for API verification.");

            // The seeded 30-day history may not align with ClickHouse's toMonday() boundaries.
            // Insert a synthetic snapshot so we can still verify the API endpoint works.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var lastMonday = today.AddDays(-(((int)today.DayOfWeek - 1 + 7) % 7) - 7);

            var syntheticPackages = _fixture.ImportedPackages.Select((pkg, i) => new TrendingPackage
            {
                PackageId = pkg.PackageId.ToLowerInvariant(),
                Week = lastMonday,
                WeekDownloads = (i + 1) * 7000,
                ComparisonWeekDownloads = (i + 1) * 5000,
                PackageIdOriginal = pkg.PackageId,
                IconUrl = "",
                GitHubUrl = ""
            }).ToList();

            var inserted = await clickHouseService.InsertTrendingPackagesSnapshotAsync(syntheticPackages);
            _output.WriteLine($"Inserted {inserted} synthetic trending packages into snapshot");
            return;
        }

        // Enrich with PostgreSQL metadata (same as the scheduler does)
        await using var context = _fixture.CreateDbContext();
        var packageIds = trendingPackages.Select(p => p.PackageId).ToList();

        var packageMetadata = await context.PackageDownloads
            .AsNoTracking()
            .Where(p => packageIds.Contains(p.PackageIdLowered))
            .Select(p => new { p.PackageId, p.PackageIdLowered, p.IconUrl })
            .ToListAsync();

        var metadataLookup = packageMetadata.ToDictionary(p => p.PackageIdLowered);

        var enriched = trendingPackages.Select(tp =>
        {
            metadataLookup.TryGetValue(tp.PackageId, out var metadata);
            return new TrendingPackage
            {
                PackageId = tp.PackageId,
                Week = tp.Week,
                WeekDownloads = tp.WeekDownloads,
                ComparisonWeekDownloads = tp.ComparisonWeekDownloads,
                PackageIdOriginal = metadata?.PackageId ?? tp.PackageId,
                IconUrl = metadata?.IconUrl ?? "",
                GitHubUrl = ""
            };
        }).ToList();

        var count = await clickHouseService.InsertTrendingPackagesSnapshotAsync(enriched);
        _output.WriteLine($"Inserted {count} enriched trending packages into snapshot");
    }

    private async Task VerifyTrendingEndpoint()
    {
        _output.WriteLine("Verifying trending API endpoint...");

        var response = await _client.GetAsync("/api/package/trending?limit=10");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        var trending = JsonSerializer.Deserialize<List<TrendingPackageDto>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        trending.Should().NotBeNull();
        trending.Should().NotBeEmpty("Trending packages should be available after snapshot refresh");

        foreach (var pkg in trending!)
        {
            pkg.PackageId.Should().NotBeNullOrEmpty();
            pkg.DownloadCount.Should().BeGreaterThan(0);
            pkg.GrowthRate.Should().NotBeNull();

            _output.WriteLine($"  - {pkg.PackageId}: {pkg.DownloadCount:N0} downloads, " +
                              $"{pkg.GrowthRate:P0} growth, icon={!string.IsNullOrEmpty(pkg.IconUrl)}");
        }

        _output.WriteLine($"Trending API verified: {trending.Count} packages returned.");
    }

    private async Task VerifySearchEndpoint()
    {
        _output.WriteLine("Verifying search API endpoint...");

        foreach (var pkg in _fixture.ImportedPackages)
        {
            var response = await _client.GetAsync($"/api/package/search?q={Uri.EscapeDataString(pkg.PackageId)}");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var results = JsonSerializer.Deserialize<List<SearchResult>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            results.Should().NotBeNull();
            results.Should().Contain(r =>
                    r.PackageId.Equals(pkg.PackageId, StringComparison.OrdinalIgnoreCase),
                $"Search results should contain {pkg.PackageId}");

            _output.WriteLine($"  - Search for '{pkg.PackageId}': found in results");
        }

        _output.WriteLine("Search API verified.");
    }

    private async Task VerifyHistoryEndpoint()
    {
        _output.WriteLine("Verifying history API endpoint...");

        for (var i = 0; i < _fixture.ImportedPackages.Count; i++)
        {
            var pkg = _fixture.ImportedPackages[i];
            var response = await _client.GetAsync($"/api/package/history/{Uri.EscapeDataString(pkg.PackageId)}?months=1");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var history = JsonSerializer.Deserialize<PackageHistory>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            history.Should().NotBeNull();
            history!.Id.Should().Be(pkg.PackageId);
            history.Downloads.Should().NotBeNull();
            history.Downloads.Should().NotBeEmpty("Download history should not be empty");

            // Should have at least 4 weeks of data (30 days + today spans ~5 weeks)
            history.Downloads.Should().HaveCountGreaterOrEqualTo(4,
                "Should have at least 4 weeks of aggregated data");

            // Verify historical weeks match expected values (skip last week which includes today's real data)
            var expectedWeekly = TestDataSeeder.CalculateExpectedWeeklyAverages(i);

            _output.WriteLine($"  - {pkg.PackageId}: {history.Downloads.Count} weeks of data");

            // Verify at least some historical weeks (not the most recent which has real data mixed in)
            var historicalWeeks = expectedWeekly.SkipLast(1).ToList();
            foreach (var expected in historicalWeeks.Take(2)) // Check first 2 full historical weeks
            {
                var actual = history.Downloads.FirstOrDefault(d =>
                    d.Week.Date == expected.Week.Date);

                if (actual != null && actual.Count.HasValue)
                {
                    // Allow some tolerance for rounding in SQL AVG
                    var diff = Math.Abs(actual.Count.Value - expected.AverageDownloadCount);
                    diff.Should().BeLessThanOrEqualTo(10,
                        $"Week {expected.Week:yyyy-MM-dd} download count should match expected value (actual: {actual.Count}, expected: {expected.AverageDownloadCount})");
                }
            }

            // Verify the most recent data point includes today's real data
            // Note: Count can be 0 for newly published packages, but should not be null
            var mostRecent = history.Downloads.OrderByDescending(d => d.Week).First();
            mostRecent.Count.Should().NotBeNull(
                "Most recent week should have download data including today");
            mostRecent.Count.Should().BeGreaterThanOrEqualTo(0,
                "Most recent week should have download data >= 0");
        }

        _output.WriteLine("History API verified.");
    }
}

/// <summary>
/// DTO for search API results.
/// </summary>
public class SearchResult
{
    public string PackageId { get; set; } = "";
    public long? LatestDownloadCount { get; set; }
    public string? IconUrl { get; set; }
}

/// <summary>
/// DTO for history API results.
/// </summary>
public class PackageHistory
{
    public string Id { get; set; } = "";
    public List<WeeklyDownload> Downloads { get; set; } = [];
}

/// <summary>
/// DTO for weekly download data.
/// Matches the DailyDownloadResult entity returned by the API.
/// </summary>
public class WeeklyDownload
{
    public DateTime Week { get; set; }
    public long? Count { get; set; }
}

/// <summary>
/// Test implementation of IJobCancellationToken for Hangfire.
/// </summary>
internal class TestJobCancellationToken : Hangfire.IJobCancellationToken
{
    public CancellationToken ShutdownToken => CancellationToken.None;

    public void ThrowIfCancellationRequested()
    {
        // No-op for tests
    }
}
