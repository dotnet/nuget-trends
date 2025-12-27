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
    IClickHouseService clickHouseService) : ControllerBase
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

    [HttpGet("history/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDownloadHistory(
        [FromRoute] string id,
        CancellationToken cancellationToken,
        [FromQuery] int months = 3)
    {
        // Validate package exists in PostgreSQL
        if (!await context.PackageDownloads
                .AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken))
        {
            return NotFound();
        }

        // Query ClickHouse for download history
        var downloads = await clickHouseService.GetWeeklyDownloadsAsync(id, months, cancellationToken);
        var data = new
        {
            Id = id,
            Downloads = downloads
        };

        return Ok(data);
    }
}
