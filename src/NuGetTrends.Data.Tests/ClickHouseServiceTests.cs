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
        var connectionInfo = ClickHouseConnectionInfo.Parse(fixture.ConnectionString);
        _sut = new ClickHouseService(fixture.ConnectionString, NullLogger<ClickHouseService>.Instance, connectionInfo);
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
    public async Task InsertDailyDownloadsAsync_HandlesSpecialCharactersInPackageId()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("My.Package-Name_1.0", today, 1000),
            ("System.Text.Json", today, 2000),
            ("Microsoft.Extensions.DependencyInjection.Abstractions", today, 3000),
            ("Newtonsoft.Json", today, 4000),
        };

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var count = await _fixture.ExecuteScalarAsync<ulong>("SELECT count() FROM daily_downloads");
        count.Should().Be(4);

        var packageWithHyphen = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'my.package-name_1.0'");
        packageWithHyphen.Should().Be(1);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_HandlesZeroDownloadCount()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("new-package", today, 0),
        };

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var downloadCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT download_count FROM daily_downloads WHERE package_id = 'new-package'");
        downloadCount.Should().Be(0);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_HandlesVeryLargeDownloadCount()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var largeCount = 10_000_000_000L; // 10 billion
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("popular-package", today, largeCount),
        };

        // Act
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Assert
        var downloadCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT download_count FROM daily_downloads WHERE package_id = 'popular-package'");
        downloadCount.Should().Be((ulong)largeCount);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_DuplicateInsert_DeduplicatesAfterOptimize()
    {
        // Arrange - ReplacingMergeTree should deduplicate rows with same (package_id, date)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Insert first batch
        await _sut.InsertDailyDownloadsAsync([("sentry", today, 1000)]);

        // Insert duplicate (same package_id, date)
        await _sut.InsertDailyDownloadsAsync([("sentry", today, 2000)]);

        // Before OPTIMIZE, we might see both rows
        var countBeforeOptimize = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'sentry'");
        countBeforeOptimize.Should().BeGreaterThanOrEqualTo(1); // Could be 1 or 2 depending on timing

        // Act - Force deduplication
        await _fixture.OptimizeTableAsync();

        // Assert - After OPTIMIZE, should have exactly 1 row
        var countAfterOptimize = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'sentry'");
        countAfterOptimize.Should().Be(1);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_DuplicateInsert_KeepsLatestValue()
    {
        // Arrange - ReplacingMergeTree keeps the latest inserted row
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Insert first value
        await _sut.InsertDailyDownloadsAsync([("sentry", today, 1000)]);

        // Insert updated value (same package_id, date)
        await _sut.InsertDailyDownloadsAsync([("sentry", today, 2000)]);

        // Force deduplication
        await _fixture.OptimizeTableAsync();

        // Act
        var downloadCount = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT download_count FROM daily_downloads WHERE package_id = 'sentry'");

        // Assert - Should have the latest value (2000)
        downloadCount.Should().Be(2000);
    }

    [Fact]
    public async Task InsertDailyDownloadsAsync_DifferentDates_NotDeduplicated()
    {
        // Arrange - Different dates should NOT be deduplicated
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var yesterday = today.AddDays(-1);

        await _sut.InsertDailyDownloadsAsync([("sentry", today, 1000)]);
        await _sut.InsertDailyDownloadsAsync([("sentry", yesterday, 900)]);

        // Force any potential deduplication
        await _fixture.OptimizeTableAsync();

        // Act
        var count = await _fixture.ExecuteScalarAsync<ulong>(
            "SELECT count() FROM daily_downloads WHERE package_id = 'sentry'");

        // Assert - Both rows should exist (different dates)
        count.Should().Be(2);
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
    public async Task GetWeeklyDownloadsAsync_DoesNotIncludeOtherPackages()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", today, 1000),
            ("newtonsoft.json", today, 5000),
            ("other-package", today, 9000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert
        result.Should().HaveCount(1);
        result[0].Count.Should().Be(1000); // Only sentry's count
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_FiltersCorrectDateRange()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // Data within 1 month range
            ("sentry", today, 1000),
            ("sentry", today.AddDays(-7), 900),
            ("sentry", today.AddDays(-14), 800),
            ("sentry", today.AddDays(-21), 700),
            // Data outside 1 month range (should be excluded)
            ("sentry", today.AddDays(-60), 100),
            ("sentry", today.AddDays(-90), 50),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act - query for 1 month
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - should not include data from 60+ days ago
        result.Should().NotBeEmpty();
        result.Sum(r => r.Count ?? 0).Should().BeGreaterThanOrEqualTo(700); // At least the recent data
        result.Sum(r => r.Count ?? 0).Should().BeLessThan(700 + 800 + 900 + 1000 + 100 + 50); // Not all data
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_CalculatesWeeklyAverageCorrectly()
    {
        // Arrange - Insert data for a full week (Mon-Sun) with known values
        // Use a date far enough in the past to be a complete week
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        // Adjust to actual Monday
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();
        // Insert 7 days: 100, 200, 300, 400, 500, 600, 700 = sum 2800, avg = 400
        for (var i = 0; i < 7; i++)
        {
            downloads.Add(("sentry", monday.AddDays(i), (i + 1) * 100));
        }
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(400); // Average of 100+200+300+400+500+600+700 = 2800/7 = 400
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesPartialWeek()
    {
        // Arrange - Insert data for only 3 days of a week
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", monday, 100),
            ("sentry", monday.AddDays(1), 200),
            ("sentry", monday.AddDays(2), 300),
            // Only 3 days, not full week
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Average should be (100+200+300)/3 = 200
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(200);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_ReturnsResultsOrderedByWeek()
    {
        // Arrange
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", today, 1000),
            ("sentry", today.AddDays(-7), 900),
            ("sentry", today.AddDays(-14), 800),
            ("sentry", today.AddDays(-21), 700),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 2);

        // Assert - Results should be ordered by week ascending
        result.Should().HaveCountGreaterThan(1);
        result.Should().BeInAscendingOrder(r => r.Week);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesMultipleMonthsParameter()
    {
        // Arrange - Insert data spanning 6 months
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

        for (var i = 0; i < 180; i += 7) // Every week for ~6 months
        {
            downloads.Add(("sentry", today.AddDays(-i), 1000 - i));
        }
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act - Query for different month ranges
        var result1Month = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);
        var result3Months = await _sut.GetWeeklyDownloadsAsync("sentry", months: 3);
        var result6Months = await _sut.GetWeeklyDownloadsAsync("sentry", months: 6);

        // Assert - More months = more results
        result1Month.Count.Should().BeLessThanOrEqualTo(result3Months.Count);
        result3Months.Count.Should().BeLessThanOrEqualTo(result6Months.Count);
    }

    [Fact]
    public async Task FullPipeline_InsertThenQuery_ReturnsCorrectData()
    {
        // Arrange - Simulate the full pipeline: insert daily data, then query weekly
        var packageId = "Sentry.AspNetCore"; // Mixed case - should be lowercased
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Simulate 30 days of data
        var downloads = Enumerable.Range(0, 30)
            .Select(i => (packageId, today.AddDays(-i), 1000L + i * 10))
            .ToList();

        // Act - Insert
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act - Query (with different case)
        var result = await _sut.GetWeeklyDownloadsAsync("SENTRY.ASPNETCORE", months: 2);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(r =>
        {
            r.Week.Should().NotBe(default);
            r.Count.Should().BeGreaterThan(0);
        });
    }
}
