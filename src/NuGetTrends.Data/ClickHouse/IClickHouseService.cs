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
    /// NOTE: This method performs an expensive real-time query. For production use,
    /// prefer <see cref="GetTrendingPackagesFromSnapshotAsync"/> which reads from pre-computed data.
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

    /// <summary>
    /// Get trending packages from the pre-computed snapshot table.
    /// This is fast (milliseconds) because it reads pre-computed data.
    /// The snapshot is refreshed weekly by <see cref="RefreshTrendingPackagesSnapshotAsync"/>.
    /// </summary>
    /// <param name="limit">Maximum number of packages to return</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>List of trending packages from the latest snapshot, or empty if no snapshot exists</returns>
    Task<List<TrendingPackage>> GetTrendingPackagesFromSnapshotAsync(
        int limit = 100,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Refresh the trending packages snapshot by computing and storing current trending data.
    /// This runs the expensive query once and stores results in trending_packages_snapshot table.
    /// Should be called weekly (e.g., Monday morning) via a scheduled job.
    /// Call <see cref="UpdatePackageFirstSeenAsync"/> before this to ensure new packages are included.
    /// </summary>
    /// <param name="minWeeklyDownloads">Minimum weekly downloads to filter noise</param>
    /// <param name="maxPackageAgeMonths">Maximum age of package in months (filters to newer packages)</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>Number of trending packages computed and stored</returns>
    Task<int> RefreshTrendingPackagesSnapshotAsync(
        long minWeeklyDownloads = 1000,
        int maxPackageAgeMonths = 12,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Updates the package_first_seen table with new packages from last week.
    /// This must be called BEFORE <see cref="RefreshTrendingPackagesSnapshotAsync"/> to ensure
    /// newly published packages are included in the trending calculation.
    /// The operation is idempotent - packages already tracked are skipped.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>Number of new packages added</returns>
    Task<int> UpdatePackageFirstSeenAsync(
        CancellationToken ct = default,
        ISpan? parentSpan = null);
}
