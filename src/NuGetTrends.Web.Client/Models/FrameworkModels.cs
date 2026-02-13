namespace NuGetTrends.Web.Client.Models;

public record TfmAdoptionResponse
{
    public required List<TfmAdoptionSeries> Series { get; init; }
}

public record TfmAdoptionSeries
{
    public required string Tfm { get; init; }
    public required string Family { get; init; }
    public required IReadOnlyList<TfmAdoptionPoint> DataPoints { get; init; }
}

public record TfmAdoptionPoint
{
    public DateOnly Month { get; init; }
    public uint CumulativeCount { get; init; }
    public uint NewCount { get; init; }
}

public record TfmFamilyGroup
{
    public required string Family { get; init; }
    public required List<string> Tfms { get; init; }
}

public enum TfmViewMode
{
    Family,
    Individual
}

public enum TfmTimeMode
{
    Absolute,
    Relative
}
