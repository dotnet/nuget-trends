namespace NuGetTrends.Data;

/// <summary>
/// Represents a trending package with week-over-week growth data from ClickHouse.
/// </summary>
public class TrendingPackage
{
    /// <summary>
    /// Package ID (lowercased as stored in ClickHouse).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Average downloads for the current week.
    /// </summary>
    public long CurrentWeekDownloads { get; init; }

    /// <summary>
    /// Average downloads for the previous week.
    /// </summary>
    public long PreviousWeekDownloads { get; init; }

    /// <summary>
    /// Week-over-week growth rate as a decimal (e.g., 0.25 = 25% growth).
    /// Null if previous week had zero downloads.
    /// </summary>
    public double? GrowthRate => PreviousWeekDownloads > 0
        ? (double)(CurrentWeekDownloads - PreviousWeekDownloads) / PreviousWeekDownloads
        : null;
}
