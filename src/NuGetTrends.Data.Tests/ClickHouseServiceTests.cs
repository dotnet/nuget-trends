using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Data.Tests.Infrastructure;
using Xunit;

namespace NuGetTrends.Data.Tests;

[Collection("ClickHouse")]
public class ClickHouseServiceTests : IAsyncLifetime
{
    private readonly ClickHouseFixture _fixture;
    private readonly ClickHouseService _sut;

    public ClickHouseServiceTests(ClickHouseFixture fixture)
    {
        _fixture = fixture;
        var options = Options.Create(new ClickHouseOptions
        {
            ConnectionString = fixture.ConnectionString
        });
        _sut = new ClickHouseService(options, NullLogger<ClickHouseService>.Instance);
    }

    public async Task InitializeAsync()
    {
        // Reset table before each test for isolation
        await _fixture.ResetTableAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InsertDailyDownloadsAsync_InsertsData_Successfully()
    {
        // Arrange
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("Sentry", DateOnly.FromDateTime(DateTime.UtcNow), 1000),
            ("Newtonsoft.Json", DateOnly.FromDateTime(DateTime.UtcNow), 2000),
        };

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var count = await _fixture.ExecuteScalarAsync<ulong>("SELECT count() FROM daily_downloads");
        count.Should().Be(2);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_WithEmptyList_DoesNothing()
    {
        // Arrange
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var count = await _fixture.ExecuteScalarAsync<ulong>("SELECT count() FROM daily_downloads");
        count.Should().Be(0);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_LowercasesPackageIds()
    {
        // Arrange
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("Sentry.AspNetCore", DateOnly.FromDateTime(DateTime.UtcNow), 1000),
            ("NEWTONSOFT.JSON", DateOnly.FromDateTime(DateTime.UtcNow), 2000),
        };

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var sentryExists = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'sentry.aspnetcore'");
        var newtonsoftExists = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'newtonsoft.json'");

        sentryExists.Should().Be(1);
        newtonsoftExists.Should().Be(1);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_HandlesLargeBatch()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = Enumerable.Range(1, 10_000)
            .Select(i => ($"package-{i}", today, (long)i * 100))
            .ToList();

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var count = await _fixture.ExecuteScalarAsync<ulong>("SELECT count() FROM daily_downloads");
        count.Should().Be(10_000);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_ReturnsAggregatedData()
    {
        // Arrange
        var packageId = "sentry";
        var baseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

        // Create 14 days of data
        for (var i = 0; i < 14; i++)
        {
            downloads.Add((packageId, baseDate.AddDays(i), 1000 + i * 100));
        }
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync(packageId, months: 1);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(r => r.Count.Should().BeGreaterThan(0));
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_WithNoData_ReturnsEmptyList()
    {
        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("nonexistent-package", months: 1);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_IsCaseInsensitive()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", today, 1000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act - query with different case
        var result = await _sut.GetWeeklyDownloadsAsync("SENTRY", months: 1);

        // Assert
        result.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetPackagesWithDownloadsForDateAsync_ReturnsCorrectPackages()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("package-today-1", today, 1000),
            ("package-today-2", today, 2000),
            ("package-yesterday", yesterday, 3000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetPackagesWithDownloadsForDateAsync(today);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("package-today-1");
        result.Should().Contain("package-today-2");
        result.Should().NotContain("package-yesterday");
    }

    [Fact]
    public async Task GetPackagesWithDownloadsForDateAsync_WithNoData_ReturnsEmptySet()
    {
        // Act
        var result = await _sut.GetPackagesWithDownloadsForDateAsync(DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_ReturnsUnprocessedOnly()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("processed-1", today, 1000),
            ("processed-2", today, 2000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        var packageIds = new List<string>
        {
            "Processed-1",   // Already processed (different case)
            "Processed-2",   // Already processed (different case)
            "Unprocessed-1", // Not processed
            "Unprocessed-2", // Not processed
        };

        // Act
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain("Unprocessed-1");
        result.Should().Contain("Unprocessed-2");
        result.Should().NotContain("Processed-1");
        result.Should().NotContain("Processed-2");
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_PreservesOriginalCase()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var packageIds = new List<string>
        {
            "Sentry.AspNetCore",
            "Newtonsoft.Json",
            "MICROSOFT.EXTENSIONS.LOGGING",
        };

        // Act - none are processed
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert - original case should be preserved
        result.Should().HaveCount(3);
        result.Should().Contain("Sentry.AspNetCore");
        result.Should().Contain("Newtonsoft.Json");
        result.Should().Contain("MICROSOFT.EXTENSIONS.LOGGING");
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_WithAllProcessed_ReturnsEmptyList()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", today, 1000),
            ("newtonsoft.json", today, 2000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        var packageIds = new List<string> { "Sentry", "Newtonsoft.Json" };

        // Act
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_WithNoneProcessed_ReturnsAll()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var packageIds = new List<string>
        {
            "Package-1",
            "Package-2",
            "Package-3",
        };

        // Act - no data in ClickHouse
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(packageIds);
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_WithEmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var packageIds = new List<string>();

        // Act
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_HandlesSpecialCharacters()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("my.package", today, 1000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        var packageIds = new List<string>
        {
            "My.Package",           // Already processed
            "Another-Package",      // With hyphen, not processed
            "Package_With_Under",   // With underscores, not processed
            "Package.With.Dots",    // With dots, not processed
        };

        // Act
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().HaveCount(3);
        result.Should().NotContain("My.Package");
        result.Should().Contain("Another-Package");
        result.Should().Contain("Package_With_Under");
        result.Should().Contain("Package.With.Dots");
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_HandlesLargeBatch()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Insert 5000 processed packages
        var processedDownloads = Enumerable.Range(1, 5000)
            .Select(i => ($"processed-{i}", today, (long)i * 100))
            .ToList();
        await _sut.InsertDailyDownloadsAsync(processedDownloads);

        // Check batch of 10000 packages (5000 processed + 5000 unprocessed)
        var packageIds = Enumerable.Range(1, 5000).Select(i => $"Processed-{i}")
            .Concat(Enumerable.Range(1, 5000).Select(i => $"Unprocessed-{i}"))
            .ToList();

        // Act
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().HaveCount(5000);
        result.Should().AllSatisfy(p => p.Should().StartWith("Unprocessed-"));
    }

    [Fact]
    public async Task GetUnprocessedPackagesAsync_ChecksDifferentDate()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        // Insert data for yesterday only
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", yesterday, 1000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        var packageIds = new List<string> { "Sentry" };

        // Act - check for today (should be unprocessed)
        var result = await _sut.GetUnprocessedPackagesAsync(packageIds, today);

        // Assert
        result.Should().HaveCount(1);
        result.Should().Contain("Sentry");
    }
}
