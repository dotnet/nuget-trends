namespace NuGetTrends.Web.Client.Models;

/// <summary>
/// Search result from the package search API.
/// </summary>
public record PackageSearchResult
{
    public required string PackageId { get; init; }
    public long DownloadCount { get; init; }
    public string IconUrl { get; init; } = "https://www.nuget.org/Content/gallery/img/default-package-icon.svg";
}

/// <summary>
/// Download history for a package.
/// </summary>
public record PackageDownloadHistory
{
    public required string Id { get; init; }
    public required IReadOnlyList<DownloadStats> Downloads { get; init; }
    public string? Color { get; set; }
}

/// <summary>
/// Weekly download statistics.
/// </summary>
public record DownloadStats
{
    public DateTime Week { get; init; }
    public long? Count { get; init; }
}

/// <summary>
/// Single data point for the ApexCharts trend line.
/// </summary>
public record DownloadDataPoint
{
    public DateTime Timestamp { get; init; }
    public decimal Count { get; init; }
}

/// <summary>
/// Trending package from the API.
/// </summary>
public record TrendingPackage
{
    public required string PackageId { get; init; }
    public long DownloadCount { get; init; }
    public double? GrowthRate { get; init; }
    public string IconUrl { get; init; } = "https://www.nuget.org/Content/gallery/img/default-package-icon.svg";
    public string? GitHubUrl { get; init; }
}

/// <summary>
/// Package with its assigned chart color.
/// </summary>
public record PackageColor
{
    public required string Id { get; init; }
    public required string Color { get; init; }
}

/// <summary>
/// Available search periods.
/// </summary>
public record SearchPeriod
{
    public required string Text { get; init; }
    public required int Value { get; init; }
}

/// <summary>
/// Helper class for managing search periods.
/// </summary>
public static class SearchPeriods
{
    // NuGet Trends has data starting from January 2012
    private static readonly DateTime DataStartDate = new(2012, 1, 1);

    private static int CalculateAllTimeMonths()
    {
        var now = DateTime.UtcNow;
        return (now.Year - DataStartDate.Year) * 12 + (now.Month - DataStartDate.Month);
    }

    public static readonly IReadOnlyList<SearchPeriod> Default =
    [
        new() { Value = 3, Text = "3 months" },
        new() { Value = 6, Text = "6 months" },
        new() { Value = 12, Text = "1 year" },
        new() { Value = 24, Text = "2 years" },
        new() { Value = 60, Text = "5 years" },
        new() { Value = 120, Text = "10 years" },
        new() { Value = CalculateAllTimeMonths(), Text = "All time" }
    ];

    public static readonly SearchPeriod Initial = Default[3]; // 2 years
}

/// <summary>
/// Theme preference options.
/// </summary>
public enum ThemePreference
{
    System,
    Light,
    Dark
}
