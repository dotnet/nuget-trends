namespace NuGetTrends.Data.ClickHouse;

/// <summary>
/// Service for interacting with ClickHouse for daily download data.
/// </summary>
public interface IClickHouseService
{
    /// <summary>
    /// Batch insert daily downloads. Package IDs are automatically lowercased.
    /// </summary>
    /// <param name="downloads">Collection of downloads to insert</param>
    /// <param name="ct">Cancellation token</param>
    Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default);

    /// <summary>
    /// Get weekly download aggregations for a package.
    /// </summary>
    /// <param name="packageId">Package ID (will be lowercased for query)</param>
    /// <param name="months">Number of months to look back</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>List of weekly download results</returns>
    Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId,
        int months,
        CancellationToken ct = default);

    /// <summary>
    /// Get package IDs that have downloads recorded for a specific date.
    /// Returns lowercase package IDs.
    /// </summary>
    /// <param name="date">Date to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Set of lowercase package IDs</returns>
    Task<HashSet<string>> GetPackagesWithDownloadsForDateAsync(
        DateOnly date,
        CancellationToken ct = default);

    /// <summary>
    /// From a batch of package IDs, returns those that do NOT have downloads recorded for a specific date.
    /// This is memory-efficient for large datasets as it processes in batches.
    /// </summary>
    /// <param name="packageIds">Batch of package IDs to check (will be lowercased)</param>
    /// <param name="date">Date to check</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Set of package IDs (original case) that are not yet processed for the date</returns>
    Task<List<string>> GetUnprocessedPackagesAsync(
        IReadOnlyList<string> packageIds,
        DateOnly date,
        CancellationToken ct = default);
}
