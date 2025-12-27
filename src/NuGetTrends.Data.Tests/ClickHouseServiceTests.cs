using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
        _sut = new ClickHouseService(fixture.ConnectionString, NullLogger<ClickHouseService>.Instance);
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
}
