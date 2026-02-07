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
        var now = DateTimeOffset.UtcNow;
        var releasesWindowStart = now.AddMonths(-12);
        var packageIdLowered = id.Trim().ToLower(CultureInfo.InvariantCulture);

        var packageDownload = await context.PackageDownloads
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PackageIdLowered == packageIdLowered, cancellationToken);

        // Aggregate package stats in SQL to avoid loading every catalog leaf into memory.
        var packageStats = await context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                TotalVersionCount = g.Count(),
                StableVersionCount = g.Count(p => !p.IsPrerelease),
                PrereleaseVersionCount = g.Count(p => p.IsPrerelease),
                ReleasesInLast12Months = g.Count(p => p.Created >= releasesWindowStart),
                FirstVersionPublishedUtc = g.Min(p => (DateTimeOffset?)p.Created),
                LastCatalogCommitUtc = g.Max(p => (DateTimeOffset?)p.CommitTimestamp)
            })
            .FirstOrDefaultAsync(cancellationToken);

        var latestLeaf = await context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .AsSplitQuery()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .OrderByDescending(p => p.Created)
            .ThenByDescending(p => p.CommitTimestamp)
            .Include(p => p.DependencyGroups)
            .ThenInclude(g => g.Dependencies)
            .FirstOrDefaultAsync(cancellationToken);

        var listingState = await context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .Select(p => new PackageListingState
            {
                Listed = p.Listed,
                Published = p.Published
            })
            .ToListAsync(cancellationToken);

        var allFrameworkNames = await context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .SelectMany(p => p.DependencyGroups.Select(g => g.TargetFramework))
            .ToListAsync(cancellationToken);

        var distinctDependencyCount = await context.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .Where(p => p.PackageIdLowered == packageIdLowered)
            .SelectMany(p => p.DependencyGroups)
            .SelectMany(g => g.Dependencies)
            .Where(d => !string.IsNullOrWhiteSpace(d.DependencyId))
            .Select(d => d.DependencyId!.ToLower())
            .Distinct()
            .CountAsync(cancellationToken);

        if (packageDownload == null && packageStats == null && latestLeaf == null)
        {
            return NotFound();
        }

        var canonicalPackageId = packageDownload?.PackageId
            ?? latestLeaf?.PackageId
            ?? id.Trim();

        var allFrameworks = allFrameworkNames.Select(NormalizeTargetFramework).ToList();
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

        var listedVersionCount = listingState.Count(IsListed);
        var totalVersionCount = packageStats?.TotalVersionCount ?? 0;
        var latestVersion = latestLeaf?.PackageVersion;
        var latestVersionPublishedUtc = latestLeaf?.Created;
        var firstVersionPublishedUtc = packageStats?.FirstVersionPublishedUtc;
        var lastCatalogCommitUtc = packageStats?.LastCatalogCommitUtc;

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
            LatestDownloadCountCheckedUtc = packageDownload == null
                ? null
                : ToDateTimeOffsetUtc(packageDownload.LatestDownloadCountCheckedUtc),
            TotalVersionCount = totalVersionCount,
            StableVersionCount = packageStats?.StableVersionCount ?? 0,
            PrereleaseVersionCount = packageStats?.PrereleaseVersionCount ?? 0,
            ListedVersionCount = listedVersionCount,
            UnlistedVersionCount = Math.Max(0, totalVersionCount - listedVersionCount),
            ReleasesInLast12Months = packageStats?.ReleasesInLast12Months ?? 0,
            SupportedTargetFrameworkCount = allFrameworks
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(),
            LatestVersionTargetFrameworkCount = latestVersionFrameworks.Count,
            DistinctDependencyCount = distinctDependencyCount,
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

    private static bool IsListed(PackageListingState packageVersion)
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

    private static DateTimeOffset ToDateTimeOffsetUtc(DateTime value)
    {
        var utcDateTime = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

        return new DateTimeOffset(utcDateTime);
    }

    private sealed class PackageListingState
    {
        public bool? Listed { get; init; }
        public DateTimeOffset Published { get; init; }
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
    public DateTimeOffset? LatestDownloadCountCheckedUtc { get; init; }
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
