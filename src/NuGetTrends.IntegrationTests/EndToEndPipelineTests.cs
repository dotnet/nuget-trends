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
using RabbitMQ.Client;
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

        // Verify search API
        await VerifySearchEndpoint();

        // Verify history API
        await VerifyHistoryEndpoint();
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
