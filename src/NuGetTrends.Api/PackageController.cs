using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
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
        public async Task<ActionResult<IEnumerable<object>>> Search([FromQuery] string q)
            => await _context.PackageDownloads
                .AsNoTracking()
                .Where(p => p.PackageIdLowered.Contains(q.ToLower(CultureInfo.InvariantCulture)))
                .OrderByDescending(p => p.LatestDownloadCount)
                .Take(20)
                .Select(p => new
                {
                    p.PackageId,
                    p.LatestDownloadCount,
                    IconUrl = p.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg"
                })
                .ToListAsync();

        [HttpGet("history/{id}")]
        public Task<object> GetDownloadHistory([FromRoute] string id, [FromQuery] int months = 3)
        {
            var query = from p in _context.PackageDownloads.AsNoTracking()
                where p.PackageId == id
                select new
                {
                    Id = p.PackageId,
                    p.IconUrl,
                    Downloads = from d in _context.DailyDownloads.AsNoTracking()
                        where d.PackageId == p.PackageId
                              && d.Date > DateTime.UtcNow.AddMonths(-months).Date
                        select new {d.Date, d.DownloadCount}
                        into dc
                        let week = dc.Date.AddDays(-(int)dc.Date.DayOfWeek).Date
                        group dc by week
                        into dpw
                        orderby dpw.Key
                        select new
                        {
                            dpw.Key.Date,
                            Count = dpw.Average(c => c.DownloadCount)
                        } as object
                } as object;

            return query.FirstOrDefaultAsync();
        }
    }
}
