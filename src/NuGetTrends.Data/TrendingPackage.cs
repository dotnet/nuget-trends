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
    /// The week this data represents (Monday of the week).
    /// This is the most recently completed week, not the current partial week.
    /// </summary>
    public DateOnly Week { get; init; }

    /// <summary>
    /// Total downloads for the reported week.
    /// </summary>
    public long WeekDownloads { get; init; }

    /// <summary>
    /// Total downloads for the comparison week (week before the reported week).
    /// Used to calculate growth rate.
    /// </summary>
    public long ComparisonWeekDownloads { get; init; }

    /// <summary>
    /// Week-over-week growth rate as a decimal (e.g., 0.25 = 25% growth).
    /// Null if comparison week had zero downloads.
    /// </summary>
    public double? GrowthRate => ComparisonWeekDownloads > 0
        ? (double)(WeekDownloads - ComparisonWeekDownloads) / ComparisonWeekDownloads
        : null;
}
