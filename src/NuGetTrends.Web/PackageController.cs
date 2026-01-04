using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
                IconUrl = p.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg"
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
