namespace NuGetTrends.Web;

/// <summary>
/// Head-to-head summary of two packages over their whole history on NuGet.
/// </summary>
public class PackageComparison
{
    /// <summary>
    /// Package with the most lifetime downloads.
    /// </summary>
    public required string LeaderId { get; init; }

    /// <summary>
    /// The other package in the comparison.
    /// </summary>
    public required string TrailingId { get; init; }

    /// <summary>
    /// How many times more lifetime downloads the leader has (e.g. 9.2 = 9.2x).
    /// </summary>
    public double DownloadRatio { get; init; }

    /// <summary>
    /// The widest the weekly gap between the two ever got.
    /// </summary>
    public double PeakRatio { get; init; }

    /// <summary>
    /// Package that has been tracked the longest.
    /// </summary>
    public required string OlderId { get; init; }

    /// <summary>
    /// How many more weeks of history the older package has.
    /// </summary>
    public int ExtraWeeksTracked { get; init; }
}
