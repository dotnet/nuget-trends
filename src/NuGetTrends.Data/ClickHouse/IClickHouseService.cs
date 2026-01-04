using Sentry;

namespace NuGetTrends.Data.ClickHouse;

/// <summary>
/// Service for interacting with ClickHouse for daily download data.
/// </summary>
public interface IClickHouseService
{
    /// <summary>
    /// Batch insert daily downloads. Package IDs are automatically lowercased.
    /// Duplicate inserts for the same (package_id, date) are handled by ReplacingMergeTree.
    /// </summary>
    /// <param name="downloads">Collection of downloads to insert</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Get weekly download aggregations for a package.
    /// </summary>
    /// <param name="packageId">Package ID (will be lowercased for query)</param>
    /// <param name="months">Number of months to look back</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>List of weekly download results</returns>
    Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId,
        int months,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Get trending packages based on week-over-week growth rate.
    /// Favors newer packages by filtering to packages first seen within the specified age limit.
    /// </summary>
    /// <param name="limit">Maximum number of packages to return (1-100)</param>
    /// <param name="minWeeklyDownloads">Minimum weekly downloads to filter noise</param>
    /// <param name="maxPackageAgeMonths">Maximum age of package in months (filters to newer packages)</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>List of trending packages sorted by growth rate descending</returns>
    Task<List<TrendingPackage>> GetTrendingPackagesAsync(
        int limit = 10,
        long minWeeklyDownloads = 1000,
        int maxPackageAgeMonths = 12,
        CancellationToken ct = default,
        ISpan? parentSpan = null);
}
