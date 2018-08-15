using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query.Expressions;

namespace NuGetTrends.Api.Packages
{
    [Route("api/[controller]")]
    [ApiController]
    public class PackageController : ControllerBase
    {
        private readonly NuGetMustHavesContext _context;

        public PackageController(NuGetMustHavesContext context) => _context = context;

        [HttpGet("search")]
        public async Task<ActionResult<IEnumerable<object>>> Search([FromQuery] string q)
            => await _context.NPackages
                .AsNoTracking()
                .Where(p => p.PackageId.Contains(q))
                .OrderByDescending(p => p.DownloadCount)
                .Take(100)
                .Select(p => new
                {
                    p.PackageId,
                    p.DownloadCount,
                    p.IconUrl
                })
                .ToListAsync();

        [HttpGet("history/{id}")]
        public Task<object> GetDownloadHistory([FromRoute] string id, [FromQuery] int months = 3)
        {
            var query = from p in _context.NPackages.AsNoTracking()
                where p.PackageId == id
                select new
                {
                    Id = p.PackageId,
                    p.IconUrl,
                    Downloads = from d in _context.Downloads.AsNoTracking()
                        where d.PackageId == p.Id
                              && d.Date > DateTime.UtcNow.AddMonths(-months).Date
                        select new {d.Date, d.Count}
                        into dc
                        let week = dc.Date.AddDays(-(int)dc.Date.DayOfWeek).Date
                        group dc by week
                        into dpw
                        orderby dpw.Key descending
                        select new
                        {
                            dpw.Key.Date,
                            Count = dpw.Sum(c => c.Count)
                        } as object
                } as object;

            return query.FirstOrDefaultAsync();
        }
    }
}
