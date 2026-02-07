using NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Client.Services;

/// <summary>
/// State management for packages displayed on the chart.
/// This service manages the flow of data between components similar to Angular's RxJS Subject pattern.
/// </summary>
public class PackageState
{
    private static readonly string[] ChartColors =
    [
        "#055499",
        "#ff5a00",
        "#9bca3c",
        "#e91365",
        "#9B5094",
        "#DB9D47"
    ];

    public const int MaxChartItems = 6;

    private readonly List<PackageColor> _packages = [];
    private readonly HashSet<string> _usedColors = new(StringComparer.Ordinal);
    private int _searchPeriod = SearchPeriods.Initial.Value;

    /// <summary>
    /// Current search period in months.
    /// </summary>
    public int SearchPeriod
    {
        get => _searchPeriod;
        set
        {
            if (_searchPeriod != value)
            {
                _searchPeriod = value;
                SearchPeriodChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>
    /// Current list of packages on the chart.
    /// </summary>
    public IReadOnlyList<PackageColor> Packages => _packages;

    /// <summary>
    /// Event raised when a package is added and should be plotted.
    /// </summary>
    public event EventHandler<PackageDownloadHistory>? PackagePlotted;

    /// <summary>
    /// Event raised when a package should be removed from the chart.
    /// </summary>
    public event EventHandler<string>? PackageRemoved;

    /// <summary>
    /// Event raised when the search period changes.
    /// </summary>
    public event EventHandler<int>? SearchPeriodChanged;

    /// <summary>
    /// Attempts to add a package to the chart.
    /// Returns the assigned color if successful, null if the chart is full or package already exists.
    /// </summary>
    public string? AddPackage(PackageDownloadHistory packageHistory)
    {
        if (_packages.Count >= MaxChartItems)
        {
            return null;
        }

        if (_packages.Any(p => p.Id.Equals(packageHistory.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var color = ChartColors.FirstOrDefault(c => !_usedColors.Contains(c));
        if (color == null)
        {
            return null;
        }

        _usedColors.Add(color);
        packageHistory.Color = color;
        _packages.Add(new PackageColor { Id = packageHistory.Id, Color = color });

        PackagePlotted?.Invoke(this, packageHistory);
        return color;
    }

    /// <summary>
    /// Updates a package with new data (e.g., after period change).
    /// </summary>
    public void UpdatePackage(PackageDownloadHistory packageHistory)
    {
        var existing = _packages.FirstOrDefault(p =>
            p.Id.Equals(packageHistory.Id, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            packageHistory.Color = existing.Color;
            PackagePlotted?.Invoke(this, packageHistory);
        }
    }

    /// <summary>
    /// Removes a package from the chart.
    /// </summary>
    public void RemovePackage(string packageId)
    {
        var package = _packages.FirstOrDefault(p =>
            p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));

        if (package != null)
        {
            _usedColors.Remove(package.Color);
            _packages.Remove(package);
            PackageRemoved?.Invoke(this, packageId);
        }
    }

    /// <summary>
    /// Gets the color assigned to a package.
    /// </summary>
    public string? GetPackageColor(string packageId)
    {
        return _packages.FirstOrDefault(p =>
            p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase))?.Color;
    }

    /// <summary>
    /// Checks if a package is already on the chart.
    /// </summary>
    public bool HasPackage(string packageId)
    {
        return _packages.Any(p =>
            p.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears all packages from the chart.
    /// </summary>
    public void Clear()
    {
        foreach (var package in _packages.ToList())
        {
            RemovePackage(package.Id);
        }
    }
}
