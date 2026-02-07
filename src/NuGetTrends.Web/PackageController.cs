using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;

namespace NuGetTrends.Web;

[Route("api/package")]
[ApiController]
public class PackageController(
    NuGetTrendsContext context,
    IClickHouseService clickHouseService,
    ITrendingPackagesCache trendingPackagesCache,
    ILogger<PackageController> logger) : ControllerBase
{
    private const string DefaultPackageIconUrl = "https://www.nuget.org/Content/gallery/img/default-package-icon.svg";

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<object>>> Search(
        [FromQuery] string q, CancellationToken cancellationToken)
    {
        return await context.PackageDownloads
            .Where(p => p.LatestDownloadCount != null
                        && p.PackageIdLowered.Contains(q.Trim().ToLower(CultureInfo.InvariantCulture)))
            .OrderByDescending(p => p.LatestDownloadCount)
            .Take(20)
            .Select(p => new
            {
                p.PackageId,
                p.LatestDownloadCount,
                IconUrl = p.IconUrl ?? DefaultPackageIconUrl
            })
            .ToListAsync(cancellationToken);
    }

    private const int MaxMonthsAllowed = 240;

    [HttpGet("history/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDownloadHistory(
        [FromRoute] string id,
        CancellationToken cancellationToken,
        [FromQuery] int months = 3)
    {
        // Validate months parameter to prevent memory-intensive queries
        if (months < 1 || months > MaxMonthsAllowed)
        {
            return BadRequest($"The 'months' parameter must be between 1 and {MaxMonthsAllowed}.");
        }

        // Query ClickHouse first (happy path: 1 query instead of 2)
        var downloads = await clickHouseService.GetWeeklyDownloadsAsync(id, months, cancellationToken);

        if (downloads.Count == 0)
        {
            // Check if package exists in PostgreSQL
            var exists = await context.PackageDownloads
                .AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken);

            if (!exists)
            {
                return NotFound();
            }

            // Package exists but no download history - likely just imported from catalog
            logger.LogWarning("Package '{PackageId}' exists in PostgreSQL but has no download history in ClickHouse", id);
        }

        return Ok(new { Id = id, Downloads = downloads });
    }

    [HttpGet("details/{id}")]
    [ProducesResponseType(typeof(PackageDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PackageDetailsDto>> GetDetails(
        [FromRoute] string id,
        CancellationToken cancellationToken)
    {
        var packageIdLowered = id.Trim().ToLower(CultureInfo.InvariantCulture);

        var packageDownloadTask = context.PackageDownloads
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageIdLowered == packageIdLowered, cancellationToken);

        var packageLeavesTask = context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .AsSplitQuery()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .Include(p => p.DependencyGroups)
            .ThenInclude(g => g.Dependencies)
            .ToListAsync(cancellationToken);

        await Task.WhenAll(packageDownloadTask, packageLeavesTask);
        var packageDownload = packageDownloadTask.Result;
        var packageLeaves = packageLeavesTask.Result;

        if (packageDownload == null && packageLeaves.Count == 0)
        {
            return NotFound();
        }

        var canonicalPackageId = packageDownload?.PackageId
            ?? packageLeaves
                .Select(p => p.PackageId)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p))
            ?? id.Trim();

        var latestLeaf = packageLeaves
            .OrderByDescending(p => p.Created)
            .ThenByDescending(p => p.CommitTimestamp)
            .FirstOrDefault();

        var now = DateTimeOffset.UtcNow;
        var allDependencyGroups = packageLeaves.SelectMany(p => p.DependencyGroups).ToList();
        var allFrameworks = allDependencyGroups
            .Select(g => NormalizeTargetFramework(g.TargetFramework))
            .ToList();
        var latestVersionFrameworks = latestLeaf?.DependencyGroups
                .Select(g => NormalizeTargetFramework(g.TargetFramework))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList()
            ?? [];

        var topTargetFrameworks = allFrameworks
            .GroupBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(g => new TargetFrameworkSupportDto
            {
                Framework = g.Key,
                VersionCount = g.Count()
            })
            .OrderByDescending(g => g.VersionCount)
            .ThenBy(g => g.Framework, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        var listedVersionCount = packageLeaves.Count(IsListed);
        var latestVersion = latestLeaf?.PackageVersion;
        var latestVersionPublishedUtc = latestLeaf?.Created;
        var firstVersionPublishedUtc = packageLeaves.Count != 0
            ? packageLeaves.Min(p => p.Created)
            : (DateTimeOffset?)null;
        var lastCatalogCommitUtc = packageLeaves.Count != 0
            ? packageLeaves.Max(p => p.CommitTimestamp)
            : (DateTimeOffset?)null;

        return Ok(new PackageDetailsDto
        {
            PackageId = canonicalPackageId,
            Title = latestLeaf?.Title,
            Summary = latestLeaf?.Summary,
            Description = latestLeaf?.Description,
            Authors = latestLeaf?.Authors,
            LatestVersion = latestVersion,
            LatestVersionPublishedUtc = latestVersionPublishedUtc,
            LatestVersionAgeDays = latestVersionPublishedUtc == null
                ? null
                : (int?)Math.Max(0, (now - latestVersionPublishedUtc.Value).TotalDays),
            FirstVersionPublishedUtc = firstVersionPublishedUtc,
            LastCatalogCommitUtc = lastCatalogCommitUtc,
            LastCatalogCommitAgeDays = lastCatalogCommitUtc == null
                ? null
                : (int?)Math.Max(0, (now - lastCatalogCommitUtc.Value).TotalDays),
            LatestDownloadCount = packageDownload?.LatestDownloadCount,
            LatestDownloadCountCheckedUtc = packageDownload?.LatestDownloadCountCheckedUtc,
            TotalVersionCount = packageLeaves.Count,
            StableVersionCount = packageLeaves.Count(p => !p.IsPrerelease),
            PrereleaseVersionCount = packageLeaves.Count(p => p.IsPrerelease),
            ListedVersionCount = listedVersionCount,
            UnlistedVersionCount = packageLeaves.Count - listedVersionCount,
            ReleasesInLast12Months = packageLeaves.Count(p => p.Created >= now.AddMonths(-12)),
            SupportedTargetFrameworkCount = allFrameworks
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            LatestVersionTargetFrameworkCount = latestVersionFrameworks.Count,
            DistinctDependencyCount = allDependencyGroups
                .SelectMany(g => g.Dependencies)
                .Select(d => d.DependencyId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            LatestPackageSizeBytes = latestLeaf?.PackageSize,
            IconUrl = packageDownload?.IconUrl ?? latestLeaf?.IconUrl ?? DefaultPackageIconUrl,
            ProjectUrl = latestLeaf?.ProjectUrl,
            LicenseUrl = latestLeaf?.LicenseUrl,
            NuGetUrl = $"https://www.nuget.org/packages/{Uri.EscapeDataString(canonicalPackageId)}",
            NuGetInfoUrl = latestVersion is { Length: > 0 }
                ? $"https://nuget.info/packages/{Uri.EscapeDataString(canonicalPackageId)}/{Uri.EscapeDataString(latestVersion)}"
                : $"https://nuget.info/packages/{Uri.EscapeDataString(canonicalPackageId)}",
            Tags = latestLeaf?.Tags?
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Take(20)
                .ToList() ?? [],
            LatestVersionTargetFrameworks = latestVersionFrameworks,
            TopTargetFrameworks = topTargetFrameworks
        });
    }

    private static bool IsListed(PackageDetailsCatalogLeaf packageVersion)
    {
        if (packageVersion.Listed.HasValue)
        {
            return packageVersion.Listed.Value;
        }

        return packageVersion.Published.Year != 1900;
    }

    private static string NormalizeTargetFramework(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework))
        {
            return "any";
        }

        return framework.Trim();
    }

    private const int MaxTrendingLimit = 100;

    /// <summary>
    /// Get trending packages based on week-over-week growth rate.
    /// Returns packages that are relatively new (up to 1 year old) with significant downloads.
    /// </summary>
    /// <param name="limit">Maximum number of packages to return (1-100, default: 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of trending packages with growth metrics</returns>
    [HttpGet("trending")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<TrendingPackageDto>>> GetTrending(
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (limit < 1 || limit > MaxTrendingLimit)
        {
            return BadRequest($"The 'limit' parameter must be between 1 and {MaxTrendingLimit}.");
        }

        var trending = await trendingPackagesCache.GetTrendingPackagesAsync(limit, cancellationToken);
        return Ok(trending.Packages);
    }
}

public sealed class PackageDetailsDto
{
    public required string PackageId { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string? LatestVersion { get; init; }
    public DateTimeOffset? LatestVersionPublishedUtc { get; init; }
    public int? LatestVersionAgeDays { get; init; }
    public DateTimeOffset? FirstVersionPublishedUtc { get; init; }
    public DateTimeOffset? LastCatalogCommitUtc { get; init; }
    public int? LastCatalogCommitAgeDays { get; init; }
    public long? LatestDownloadCount { get; init; }
    public DateTime? LatestDownloadCountCheckedUtc { get; init; }
    public int TotalVersionCount { get; init; }
    public int StableVersionCount { get; init; }
    public int PrereleaseVersionCount { get; init; }
    public int ListedVersionCount { get; init; }
    public int UnlistedVersionCount { get; init; }
    public int ReleasesInLast12Months { get; init; }
    public int SupportedTargetFrameworkCount { get; init; }
    public int LatestVersionTargetFrameworkCount { get; init; }
    public int DistinctDependencyCount { get; init; }
    public long? LatestPackageSizeBytes { get; init; }
    public string IconUrl { get; init; } = "https://www.nuget.org/Content/gallery/img/default-package-icon.svg";
    public string? ProjectUrl { get; init; }
    public string? LicenseUrl { get; init; }
    public required string NuGetUrl { get; init; }
    public required string NuGetInfoUrl { get; init; }
    public IReadOnlyList<TargetFrameworkSupportDto> TopTargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> LatestVersionTargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
}

public sealed class TargetFrameworkSupportDto
{
    public required string Framework { get; init; }
    public int VersionCount { get; init; }
}
