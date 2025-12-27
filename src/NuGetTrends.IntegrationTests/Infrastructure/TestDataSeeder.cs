using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;

namespace NuGetTrends.IntegrationTests.Infrastructure;

/// <summary>
/// Seeds deterministic historical download data for testing.
/// Each package gets predictable download counts based on its index.
/// Daily downloads are stored in ClickHouse, package metadata in PostgreSQL.
/// </summary>
public static class TestDataSeeder
{
    /// <summary>
    /// Number of days of historical data to seed (excluding today).
    /// </summary>
    public const int DaysOfHistory = 30;

    /// <summary>
    /// Daily increment in download count.
    /// </summary>
    public const int DailyIncrement = 100;

    /// <summary>
    /// Gets the base download count for a package at the given index.
    /// </summary>
    public static long GetBaseDownloadCount(int packageIndex)
        => (packageIndex + 1) * 10000L;

    /// <summary>
    /// Gets the expected download count for a package on a specific day.
    /// </summary>
    /// <param name="packageIndex">Zero-based index of the package.</param>
    /// <param name="daysAgo">Number of days ago (1 = yesterday, 30 = 30 days ago).</param>
    public static long GetExpectedDownloadCount(int packageIndex, int daysAgo)
        => GetBaseDownloadCount(packageIndex) + (DaysOfHistory - daysAgo) * DailyIncrement;

    /// <summary>
    /// Seeds historical download data for the given packages.
    /// Creates 30 days of history with deterministic download counts.
    /// Daily downloads go to ClickHouse, package metadata to PostgreSQL.
    /// Also marks packages as "pending" by setting their last check to yesterday.
    /// </summary>
    public static async Task SeedHistoricalDownloadsAsync(
        NuGetTrendsContext context,
        IClickHouseService clickHouseService,
        IReadOnlyList<CatalogPackageInfo> packages)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var baseCount = GetBaseDownloadCount(i);

            // Prepare daily_downloads for past 30 days (excluding today) -> ClickHouse
            for (var daysAgo = DaysOfHistory; daysAgo >= 1; daysAgo--)
            {
                var downloadCount = baseCount + (DaysOfHistory - daysAgo) * DailyIncrement;
                downloads.Add((package.PackageId, today.AddDays(-daysAgo), downloadCount));
            }

            // Seed package_downloads (checked yesterday = pending for today) -> PostgreSQL
            // This makes the package appear in pending_packages_daily_downloads view
            context.PackageDownloads.Add(new PackageDownload
            {
                PackageId = package.PackageId,
                PackageIdLowered = package.PackageId.ToLowerInvariant(),
                LatestDownloadCount = baseCount + (DaysOfHistory - 1) * DailyIncrement,
                LatestDownloadCountCheckedUtc = DateTime.UtcNow.Date.AddDays(-1)
            });
        }

        // Batch insert to ClickHouse
        await clickHouseService.InsertDailyDownloadsAsync(downloads);

        // Save PostgreSQL changes
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Calculates the expected weekly download averages for a package.
    /// Used for asserting API responses.
    /// </summary>
    /// <param name="packageIndex">Zero-based index of the package.</param>
    /// <param name="daysOfHistory">Number of days of history (default 30).</param>
    /// <returns>List of expected weekly averages, ordered by week (oldest first).</returns>
    public static List<ExpectedWeeklyDownload> CalculateExpectedWeeklyAverages(
        int packageIndex,
        int daysOfHistory = DaysOfHistory)
    {
        var today = DateTime.UtcNow.Date;
        var weeklyData = new Dictionary<DateTime, List<long>>();

        // Group daily downloads by week (Monday)
        for (var daysAgo = daysOfHistory; daysAgo >= 1; daysAgo--)
        {
            var date = today.AddDays(-daysAgo);
            var weekStart = GetMondayOfWeek(date);
            var downloadCount = GetExpectedDownloadCount(packageIndex, daysAgo);

            if (!weeklyData.ContainsKey(weekStart))
            {
                weeklyData[weekStart] = [];
            }

            weeklyData[weekStart].Add(downloadCount);
        }

        // Calculate averages
        return weeklyData
            .OrderBy(kv => kv.Key)
            .Select(kv => new ExpectedWeeklyDownload(
                kv.Key,
                (long)Math.Round(kv.Value.Average())))
            .ToList();
    }

    /// <summary>
    /// Gets the Monday of the week for a given date.
    /// </summary>
    private static DateTime GetMondayOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.AddDays(-diff).Date;
    }
}

/// <summary>
/// Expected weekly download data for assertions.
/// </summary>
public record ExpectedWeeklyDownload(DateTime Week, long AverageDownloadCount);
