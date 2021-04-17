using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;

namespace NuGetTrends.Web
{
    [Route("api/package")]
    [ApiController]
    public class PackageController : ControllerBase
    {
        private readonly NuGetTrendsContext _context;

        public PackageController(NuGetTrendsContext context) => _context = context;

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<object>>> Search(
            [FromQuery] string q, CancellationToken cancellationToken)
        {
            return await _context.PackageDownloads
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
            if (! await _context.PackageDownloads.
                AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken))
            {
                return NotFound();
            }

            var downloads = await _context.GetDailyDownloads(id, months);
            var data = new
            {
                Id = id,
                Downloads = downloads
            };

            return Ok(data);
        }
    }
}
