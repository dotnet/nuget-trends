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
    public async Task GetWeeklyDownloadsAsync_HandlesPartialWeek_ThreeDays()
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
    public async Task GetWeeklyDownloadsAsync_HandlesPartialWeek_SingleDay()
    {
        // Arrange - Insert data for only 1 day of a week (edge case: worker only ran once)
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", monday.AddDays(3), 500), // Only Thursday
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Average of single value is that value
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(500);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesPartialWeek_TwoDays()
    {
        // Arrange - Insert data for only 2 days of a week
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", monday, 100),       // Monday
            ("sentry", monday.AddDays(4), 300), // Friday
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Average should be (100+300)/2 = 200
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(200);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesPartialWeek_FiveDays()
    {
        // Arrange - Insert data for 5 days of a week (weekend missing)
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", monday, 100),           // Monday
            ("sentry", monday.AddDays(1), 200), // Tuesday
            ("sentry", monday.AddDays(2), 300), // Wednesday
            ("sentry", monday.AddDays(3), 400), // Thursday
            ("sentry", monday.AddDays(4), 500), // Friday
            // Saturday and Sunday missing
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Average should be (100+200+300+400+500)/5 = 300
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(300);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesNonConsecutiveDays()
    {
        // Arrange - Insert data for non-consecutive days (simulates worker failures)
        var monday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-14));
        while (monday.DayOfWeek != DayOfWeek.Monday)
        {
            monday = monday.AddDays(-1);
        }

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ("sentry", monday, 100),           // Monday
            // Tuesday missing (worker failed)
            ("sentry", monday.AddDays(2), 300), // Wednesday
            // Thursday missing (worker failed)
            ("sentry", monday.AddDays(4), 500), // Friday
            // Weekend missing
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Average should be (100+300+500)/3 = 300
        result.Should().NotBeEmpty();
        var weekResult = result.FirstOrDefault(r => r.Week.Date == monday.ToDateTime(TimeOnly.MinValue));
        weekResult.Should().NotBeNull();
        weekResult!.Count.Should().Be(300);
    }

    [Fact]
    public async Task GetWeeklyDownloadsAsync_HandlesDifferentDayCountsAcrossWeeks()
    {
        // Arrange - Different weeks have different numbers of days (realistic scenario)
        var monday1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-21));
        while (monday1.DayOfWeek != DayOfWeek.Monday)
        {
            monday1 = monday1.AddDays(-1);
        }
        var monday2 = monday1.AddDays(7);

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // Week 1: Only 2 days of data, avg = (100+200)/2 = 150
            ("sentry", monday1, 100),
            ("sentry", monday1.AddDays(1), 200),

            // Week 2: Full 7 days of data, avg = (100+200+300+400+500+600+700)/7 = 400
            ("sentry", monday2, 100),
            ("sentry", monday2.AddDays(1), 200),
            ("sentry", monday2.AddDays(2), 300),
            ("sentry", monday2.AddDays(3), 400),
            ("sentry", monday2.AddDays(4), 500),
            ("sentry", monday2.AddDays(5), 600),
            ("sentry", monday2.AddDays(6), 700),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);

        // Act
        var result = await _sut.GetWeeklyDownloadsAsync("sentry", months: 1);

        // Assert - Each week should have its own correct average
        result.Should().HaveCount(2);

        var week1Result = result.FirstOrDefault(r => r.Week.Date == monday1.ToDateTime(TimeOnly.MinValue));
        week1Result.Should().NotBeNull();
        week1Result!.Count.Should().Be(150);

        var week2Result = result.FirstOrDefault(r => r.Week.Date == monday2.ToDateTime(TimeOnly.MinValue));
        week2Result.Should().NotBeNull();
        week2Result!.Count.Should().Be(400);
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

    [Fact]
    public async Task GetTrendingPackagesAsync_WithNoData_ReturnsEmptyList()
    {
        // Arrange - Ensure package_first_seen is populated (will be empty since no data)
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 10, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_FiltersPackagesWithoutPreviousWeekData()
    {
        // Arrange - Insert data only for current week (no previous week comparison possible)
        var monday = GetLastWeekMonday();
        var packageId = $"new-package-{Guid.NewGuid():N}";
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            (packageId, monday, 5000),
            (packageId, monday.AddDays(1), 5000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Our package should not appear (no previous week data for comparison)
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().BeNull();
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_FiltersByMinDownloads()
    {
        // Arrange - Insert data for both weeks but below minimum threshold
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var packageId = $"tiny-package-{Guid.NewGuid():N}";

        // The query computes weekly total as: avgMerge(daily) * 7
        // So a daily value of 100 becomes weekly 700.
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // Small package: daily 50 -> weekly 350, daily 100 -> weekly 700
            (packageId, previousMonday, 50),
            (packageId, currentMonday, 100),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act - Set minimum to 1000 (which is > 700 weekly)
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 1000, maxPackageAgeMonths: 12);

        // Assert - Package should be filtered out (700 weekly < 1000 threshold)
        result.Where(p => p.PackageId == packageId.ToLowerInvariant()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_ReturnsTrendingPackagesSortedByGrowthRate()
    {
        // Arrange - Insert data for multiple packages with different growth rates
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var suffix = Guid.NewGuid().ToString("N");
        var packageA = $"package-a-{suffix}";
        var packageB = $"package-b-{suffix}";
        var packageC = $"package-c-{suffix}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // Package A: 50% growth (1000 -> 1500)
            (packageA, previousMonday, 1000),
            (packageA, currentMonday, 1500),

            // Package B: 100% growth (1000 -> 2000) - highest growth
            (packageB, previousMonday, 1000),
            (packageB, currentMonday, 2000),

            // Package C: 25% growth (2000 -> 2500)
            (packageC, previousMonday, 2000),
            (packageC, currentMonday, 2500),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Filter to our packages and verify sort order by growth rate descending
        var ourPackages = result.Where(p => p.PackageId.EndsWith(suffix)).ToList();
        ourPackages.Should().HaveCount(3);
        ourPackages[0].PackageId.Should().Be(packageB.ToLowerInvariant()); // 100% growth
        ourPackages[1].PackageId.Should().Be(packageA.ToLowerInvariant()); // 50% growth
        ourPackages[2].PackageId.Should().Be(packageC.ToLowerInvariant()); // 25% growth
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_RespectsLimit()
    {
        // Arrange - Insert many packages with unique suffix
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var suffix = Guid.NewGuid().ToString("N");

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();
        for (var i = 1; i <= 20; i++)
        {
            var packageId = $"package-{i}-{suffix}";
            downloads.Add((packageId, previousMonday, 1000));
            downloads.Add((packageId, currentMonday, 1000 + i * 100)); // Increasing growth rates
        }
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act - Request only 5 packages
        var result = await _sut.GetTrendingPackagesAsync(limit: 5, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Should return at most 5 packages (limit is respected)
        result.Should().HaveCountLessOrEqualTo(5);
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_FiltersByPackageAge()
    {
        // Arrange - Insert packages with different ages
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var longAgo = currentMonday.AddMonths(-18); // 18 months ago
        var suffix = Guid.NewGuid().ToString("N");
        var oldPackageId = $"old-package-{suffix}";
        var newPackageId = $"new-package-{suffix}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // Old package (first seen 18 months ago) - should be excluded with 12 month filter
            (oldPackageId, longAgo, 500),
            (oldPackageId, previousMonday, 1000),
            (oldPackageId, currentMonday, 2000), // 100% growth

            // New package (first seen this week) - should be included
            (newPackageId, previousMonday, 1000),
            (newPackageId, currentMonday, 1500), // 50% growth
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act - Filter to packages up to 12 months old
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Only new package should appear (old package filtered by age)
        var ourPackages = result.Where(p => p.PackageId.EndsWith(suffix)).ToList();
        ourPackages.Should().HaveCount(1);
        ourPackages[0].PackageId.Should().Be(newPackageId.ToLowerInvariant());
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_CalculatesGrowthRateCorrectly()
    {
        // Arrange - Insert data with known growth
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var packageId = $"test-package-{Guid.NewGuid():N}";

        // Insert daily download counts. The query calculates weekly totals as:
        // avgMerge(download_avg) * 7 - so a single day's value gets multiplied by 7.
        // To get predictable weekly totals, we insert values that account for this.
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            // 50% growth: daily avg 2000 -> 3000 becomes weekly 14000 -> 21000
            (packageId, previousMonday, 2000),
            (packageId, currentMonday, 3000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - find our specific package and verify growth rate
        // The absolute values are daily * 7, but the growth rate should be preserved
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().NotBeNull();
        package!.WeekDownloads.Should().Be(3000 * 7); // daily avg * 7
        package.ComparisonWeekDownloads.Should().Be(2000 * 7); // daily avg * 7
        package.GrowthRate.Should().BeApproximately(0.5, 0.01); // 50% growth preserved
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_LowercasesPackageIds()
    {
        // Arrange - Insert with mixed case and unique suffix
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var suffix = Guid.NewGuid().ToString("N");
        var packageId = $"Sentry.AspNetCore.{suffix}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            (packageId, previousMonday, 1000),
            (packageId, currentMonday, 1500),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Package ID should be lowercased in results
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().NotBeNull();
        package!.PackageId.Should().Be(packageId.ToLowerInvariant());
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_HandlesZeroGrowth()
    {
        // Arrange - Package with zero growth should still appear if it meets criteria
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var packageId = $"stable-package-{Guid.NewGuid():N}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            (packageId, previousMonday, 5000),
            (packageId, currentMonday, 5000), // 0% growth
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Should appear with 0% growth
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().NotBeNull();
        package!.GrowthRate.Should().Be(0);
    }

    [Fact]
    public async Task GetTrendingPackagesAsync_HandlesNegativeGrowth()
    {
        // Arrange - Package with declining downloads
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var packageId = $"declining-package-{Guid.NewGuid():N}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            (packageId, previousMonday, 10000),
            (packageId, currentMonday, 8000), // -20% growth
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.GetTrendingPackagesAsync(limit: 100, minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert - Should appear with negative growth
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().NotBeNull();
        package!.GrowthRate.Should().BeApproximately(-0.2, 0.01); // -20% growth
    }

    [Fact]
    public async Task ComputeTrendingPackagesAsync_WithTwoWeeksOfData_ReturnsResults()
    {
        // Arrange - Need data for both last week and two weeks ago
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var suffix = Guid.NewGuid().ToString("N");
        var packageId = $"compute-test-{suffix}";

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            (packageId, previousMonday, 1000),
            (packageId, currentMonday, 2000), // 100% growth
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act
        var result = await _sut.ComputeTrendingPackagesAsync(minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert
        var package = result.FirstOrDefault(p => p.PackageId == packageId.ToLowerInvariant());
        package.Should().NotBeNull();
        package!.WeekDownloads.Should().Be(2000 * 7);
        package.ComparisonWeekDownloads.Should().Be(1000 * 7);
    }

    [Fact]
    public async Task ComputeTrendingPackagesAsync_WithNoData_ReturnsEmpty()
    {
        // Act
        var result = await _sut.ComputeTrendingPackagesAsync(minWeeklyDownloads: 100, maxPackageAgeMonths: 12);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task InsertTrendingPackagesSnapshotAsync_RoundTrips_EnrichmentColumns()
    {
        // Arrange - Create enriched packages and insert directly
        var week = GetLastWeekMonday();
        var packages = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "newtonsoft.json",
                Week = week,
                WeekDownloads = 21000,
                ComparisonWeekDownloads = 14000,
                PackageIdOriginal = "Newtonsoft.Json",
                IconUrl = "https://www.nuget.org/Content/gallery/img/newtonsoft.svg",
                GitHubUrl = "https://github.com/JamesNK/Newtonsoft.Json"
            },
            new()
            {
                PackageId = "sentry",
                Week = week,
                WeekDownloads = 7000,
                ComparisonWeekDownloads = 5000,
                PackageIdOriginal = "Sentry",
                IconUrl = "https://sentry.io/icon.png",
                GitHubUrl = "https://github.com/getsentry/sentry-dotnet"
            }
        };

        // Act - Insert
        var inserted = await _sut.InsertTrendingPackagesSnapshotAsync(packages);

        // Assert - Insert count
        inserted.Should().Be(2);

        // Act - Read back via snapshot query
        var snapshot = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 10);

        // Assert - Enrichment columns round-trip correctly
        snapshot.Should().HaveCount(2);

        var newtonsoft = snapshot.First(p => p.PackageId == "newtonsoft.json");
        newtonsoft.PackageIdOriginal.Should().Be("Newtonsoft.Json");
        newtonsoft.IconUrl.Should().Be("https://www.nuget.org/Content/gallery/img/newtonsoft.svg");
        newtonsoft.GitHubUrl.Should().Be("https://github.com/JamesNK/Newtonsoft.Json");
        newtonsoft.WeekDownloads.Should().Be(21000);
        newtonsoft.ComparisonWeekDownloads.Should().Be(14000);

        var sentry = snapshot.First(p => p.PackageId == "sentry");
        sentry.PackageIdOriginal.Should().Be("Sentry");
        sentry.IconUrl.Should().Be("https://sentry.io/icon.png");
        sentry.GitHubUrl.Should().Be("https://github.com/getsentry/sentry-dotnet");
    }

    [Fact]
    public async Task InsertTrendingPackagesSnapshotAsync_DeletesExistingWeekOnRetry()
    {
        // Arrange - Insert an initial batch
        var week = GetLastWeekMonday();
        var initialPackages = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "old-package",
                Week = week,
                WeekDownloads = 1000,
                ComparisonWeekDownloads = 500,
                PackageIdOriginal = "Old.Package",
                IconUrl = "",
                GitHubUrl = ""
            }
        };
        await _sut.InsertTrendingPackagesSnapshotAsync(initialPackages);

        // Act - Re-insert with different data (simulates retry)
        var retryPackages = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "new-package",
                Week = week,
                WeekDownloads = 2000,
                ComparisonWeekDownloads = 1000,
                PackageIdOriginal = "New.Package",
                IconUrl = "",
                GitHubUrl = ""
            }
        };
        await _sut.InsertTrendingPackagesSnapshotAsync(retryPackages);

        // Assert - Should only have the retry data (old was deleted)
        // ClickHouse ALTER DELETE is async; poll until the mutation is reflected
        IReadOnlyList<TrendingPackage> snapshot;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            snapshot = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 10);
            var hasNew = snapshot.Any(p => p.PackageId == "new-package");
            var hasOld = snapshot.Any(p => p.PackageId == "old-package");

            if (hasNew && !hasOld)
                break;

            if (DateTime.UtcNow >= deadline)
                break;

            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }

        // After DELETE + INSERT, we should see only the retry package
        var packageIds = snapshot.Select(p => p.PackageId).ToList();
        packageIds.Should().Contain("new-package");
        packageIds.Should().NotContain("old-package");
    }

    [Fact]
    public async Task InsertTrendingPackagesSnapshotAsync_EmptyList_ReturnsZero()
    {
        // Act
        var result = await _sut.InsertTrendingPackagesSnapshotAsync([]);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task GetTrendingPackagesFromSnapshotAsync_EmptyTable_ReturnsEmpty()
    {
        // Act - Snapshot table is empty after reset
        var result = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 10);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendingPackagesFromSnapshotAsync_OrdersByGrowthRateDescending()
    {
        // Arrange - Insert packages with different growth rates
        var week = GetLastWeekMonday();
        var packages = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "low-growth",
                Week = week,
                WeekDownloads = 1250,
                ComparisonWeekDownloads = 1000, // 25% growth
                PackageIdOriginal = "low-growth",
                IconUrl = "",
                GitHubUrl = ""
            },
            new()
            {
                PackageId = "high-growth",
                Week = week,
                WeekDownloads = 2000,
                ComparisonWeekDownloads = 1000, // 100% growth
                PackageIdOriginal = "high-growth",
                IconUrl = "",
                GitHubUrl = ""
            },
            new()
            {
                PackageId = "medium-growth",
                Week = week,
                WeekDownloads = 1500,
                ComparisonWeekDownloads = 1000, // 50% growth
                PackageIdOriginal = "medium-growth",
                IconUrl = "",
                GitHubUrl = ""
            }
        };
        await _sut.InsertTrendingPackagesSnapshotAsync(packages);

        // Act
        var result = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 10);

        // Assert - Should be ordered by growth rate descending
        result.Should().HaveCount(3);
        result[0].PackageId.Should().Be("high-growth");   // 100%
        result[1].PackageId.Should().Be("medium-growth");  // 50%
        result[2].PackageId.Should().Be("low-growth");     // 25%
    }

    [Fact]
    public async Task GetTrendingPackagesFromSnapshotAsync_EmptyEnrichment_FallsBackToPackageId()
    {
        // Arrange - Insert with empty enrichment (simulates pre-enrichment data)
        var week = GetLastWeekMonday();
        var packages = new List<TrendingPackage>
        {
            new()
            {
                PackageId = "some.package",
                Week = week,
                WeekDownloads = 2000,
                ComparisonWeekDownloads = 1000,
                PackageIdOriginal = "", // Empty - should fall back to package_id
                IconUrl = "",
                GitHubUrl = ""
            }
        };
        await _sut.InsertTrendingPackagesSnapshotAsync(packages);

        // Act
        var result = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 10);

        // Assert - PackageIdOriginal should fall back to PackageId when empty
        result.Should().HaveCount(1);
        result[0].PackageIdOriginal.Should().Be("some.package");
    }

    [Fact]
    public async Task FullSnapshotPipeline_Compute_Insert_Read_RoundTrip()
    {
        // Arrange - Set up data for trending computation
        var currentMonday = GetLastWeekMonday();
        var previousMonday = currentMonday.AddDays(-7);
        var suffix = Guid.NewGuid().ToString("N");

        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>
        {
            ($"pipeline-pkg-{suffix}", previousMonday, 1000),
            ($"pipeline-pkg-{suffix}", currentMonday, 2000),
        };
        await _sut.InsertDailyDownloadsAsync(downloads);
        await _fixture.PopulatePackageFirstSeenAsync();

        // Act - Step 1: Compute trending
        var computed = await _sut.ComputeTrendingPackagesAsync(minWeeklyDownloads: 100, maxPackageAgeMonths: 12);
        computed.Should().NotBeEmpty();

        // Act - Step 2: Add enrichment data (simulates scheduler enrichment)
        var enriched = computed.Select(p => new TrendingPackage
        {
            PackageId = p.PackageId,
            Week = p.Week,
            WeekDownloads = p.WeekDownloads,
            ComparisonWeekDownloads = p.ComparisonWeekDownloads,
            PackageIdOriginal = "Pipeline.Pkg",
            IconUrl = "https://example.com/icon.png",
            GitHubUrl = "https://github.com/test/repo"
        }).ToList();

        // Act - Step 3: Insert enriched snapshot
        var insertCount = await _sut.InsertTrendingPackagesSnapshotAsync(enriched);
        insertCount.Should().BeGreaterThan(0);

        // Act - Step 4: Read snapshot
        var snapshot = await _sut.GetTrendingPackagesFromSnapshotAsync(limit: 100);

        // Assert - Full pipeline round-trip
        var pkg = snapshot.FirstOrDefault(p => p.PackageId.Contains(suffix));
        pkg.Should().NotBeNull("Package from compute step should appear in snapshot");
        pkg!.PackageIdOriginal.Should().Be("Pipeline.Pkg");
        pkg.IconUrl.Should().Be("https://example.com/icon.png");
        pkg.GitHubUrl.Should().Be("https://github.com/test/repo");
        pkg.WeekDownloads.Should().Be(2000 * 7);
        pkg.ComparisonWeekDownloads.Should().Be(1000 * 7);
    }

    /// <summary>
    /// Gets the Monday of last week as a DateOnly.
    /// This matches the ClickHouse query: toMonday(today() - INTERVAL 1 WEEK)
    /// which means "go back 7 days from today, then find that week's Monday".
    /// </summary>
    private static DateOnly GetLastWeekMonday()
    {
        // Match ClickHouse logic: toMonday(today() - INTERVAL 1 WEEK)
        var oneWeekAgo = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        // Go back to Monday of that week
        while (oneWeekAgo.DayOfWeek != DayOfWeek.Monday)
        {
            oneWeekAgo = oneWeekAgo.AddDays(-1);
        }
        return oneWeekAgo;
    }
}
