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
    /// The snapshot is refreshed weekly by <see cref="ComputeTrendingPackagesAsync"/> + <see cref="InsertTrendingPackagesSnapshotAsync"/>.
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
    /// Compute trending packages from ClickHouse (expensive query) and return them.
    /// Does NOT insert into the snapshot table — the caller is responsible for enriching
    /// and then inserting via <see cref="InsertTrendingPackagesSnapshotAsync"/>.
    /// Call <see cref="UpdatePackageFirstSeenAsync"/> before this to ensure new packages are included.
    /// </summary>
    /// <param name="minWeeklyDownloads">Minimum weekly downloads to filter noise</param>
    /// <param name="maxPackageAgeMonths">Maximum age of package in months (filters to newer packages)</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>List of trending packages (not yet enriched with metadata)</returns>
    Task<List<TrendingPackage>> ComputeTrendingPackagesAsync(
        long minWeeklyDownloads = 1000,
        int maxPackageAgeMonths = 12,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Batch-insert fully-enriched trending packages into the snapshot table.
    /// The packages should already have enrichment data (icon_url, github_url, package_id_original)
    /// populated by the caller (typically from PostgreSQL).
    /// </summary>
    /// <param name="packages">Enriched trending packages to insert</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>Number of rows inserted</returns>
    Task<int> InsertTrendingPackagesSnapshotAsync(
        List<TrendingPackage> packages,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Updates the package_first_seen table with any missing packages from all weeks.
    /// Self-healing: scans all weekly_downloads data, so it catches up after pipeline gaps.
    /// This must be called BEFORE <see cref="ComputeTrendingPackagesAsync"/> to ensure
    /// newly published packages are included in the trending calculation.
    /// The operation is idempotent - packages already tracked are skipped.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <param name="parentSpan">Optional parent span for Sentry tracing</param>
    /// <returns>Number of new packages added</returns>
    Task<int> UpdatePackageFirstSeenAsync(
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Runs all pending ClickHouse migrations from .sql files.
    /// Creates a migration tracking table and only runs migrations that haven't been applied yet.
    /// This is called automatically on scheduler startup.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A task representing the async operation</returns>
    Task RunMigrationsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets TFM adoption data from the pre-computed snapshot table.
    /// Optionally filters by specific TFMs or families.
    /// </summary>
    Task<List<TfmAdoptionDataPoint>> GetTfmAdoptionFromSnapshotAsync(
        IReadOnlyList<string>? tfms = null,
        IReadOnlyList<string>? families = null,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Batch-inserts TFM adoption data points into the snapshot table.
    /// Deletes existing rows for the target months before inserting.
    /// </summary>
    Task<int> InsertTfmAdoptionSnapshotAsync(
        List<TfmAdoptionDataPoint> dataPoints,
        CancellationToken ct = default,
        ISpan? parentSpan = null);

    /// <summary>
    /// Gets the distinct TFMs available in the snapshot table, grouped by family.
    /// </summary>
    Task<List<TfmFamilyGroup>> GetAvailableTfmsAsync(
        CancellationToken ct = default,
        ISpan? parentSpan = null);
}
