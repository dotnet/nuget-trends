namespace NuGetTrends.Data;

/// <summary>
/// A single data point in the TFM adoption time series.
/// </summary>
public class TfmAdoptionDataPoint
{
    public required DateOnly Month { get; init; }
    public required string Tfm { get; init; }
    public required string Family { get; init; }
    public uint NewPackageCount { get; init; }
    public uint CumulativePackageCount { get; init; }
}

/// <summary>
/// Groups available TFMs by family for the filter dropdown.
/// </summary>
public class TfmFamilyGroup
{
    public required string Family { get; init; }
    public required List<string> Tfms { get; init; }
}
