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
}
