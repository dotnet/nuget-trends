using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;

namespace NuGetTrends.Api
{
    [Route("api/[controller]")]
    [ApiController]
    public class PackageController : ControllerBase
    {
        private readonly NuGetTrendsContext _context;

        public PackageController(NuGetTrendsContext context) => _context = context;

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<object>>> Search([FromQuery] string q, CancellationToken cancellationToken)
            => await _context.PackageDownloads
                .AsNoTracking()
                .Where(p => p.LatestDownloadCount != null
                            && p.PackageIdLowered.Contains(q.ToLower(CultureInfo.InvariantCulture)))
                .OrderByDescending(p => p.LatestDownloadCount)
                .Take(20)
                .Select(p => new
                {
                    p.PackageId,
                    p.LatestDownloadCount,
                    IconUrl = p.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg"
                })
                .ToListAsync(cancellationToken);

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

        [HttpGet("trend/{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetTrend(
            [FromRoute] string id,
            CancellationToken cancellationToken,
            [FromQuery] int months = 3,
            [FromQuery] int next = 1)
        {
            if (! await _context.PackageDownloads.
                AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken))
            {
                return NotFound();
            }

            var downloads = await _context.GetDailyDownloads(id, months);
            var xdata = Enumerable.Range(0, downloads.Count).Select(x => (double)x).ToArray();
            var ydata = downloads.Select(x => x.Count ?? 0d).ToArray();

            var p = MathNet.Numerics.Fit.Polynomial(xdata, ydata, 3); // TODO: order as param

            var allDates = downloads
                .Select(x => x.Week)
                .Concat(GetDateInterval(downloads[^1].Week.AddDays(7), downloads[^1].Week.AddMonths(next)));

            var data = new
            {
                Id = id,
                Downloads = allDates.Select((week, i) => new DailyDownloadResult
                {
                    Week = week,
                    Count = CalculateDownloadCount((double)i, p)
                })
            };

            return Ok(data);

            static IEnumerable<DateTime> GetDateInterval(DateTime from, DateTime to)
            {
                var current = from;
                while (current < to)
                {
                    yield return current;
                    current = current.AddDays(7);
                }
            }

            static long CalculateDownloadCount(double x, double[] p)
            {
                var result = 0L;

                for (var i = p.Length - 1; i >= 0; i--)
                {
                    result += (long)(p[i] * Math.Pow(x, i));
                }

                return result;
            }
        }
    }
}
