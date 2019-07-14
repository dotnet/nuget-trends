using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        public async Task<object> GetDownloadHistory([FromRoute] string id, [FromQuery] int months = 3)
        {
            var downloads = await GetDailyDownloads(id, months);

            //var query = from p in _context.PackageDownloads.AsNoTracking()
            //    where p.PackageId == id
            //    select new
            //    {
            //        Id = p.PackageId,
            //        p.IconUrl,
            //        Downloads = from d in _context.DailyDownloads.AsNoTracking()
            //            where d.PackageId == p.PackageId && d.Date > DateTime.UtcNow.AddMonths(-months).Date
            //            select new {d.Date, d.DownloadCount} into dc
            //            let week = dc.Date.AddDays(-(int)dc.Date.DayOfWeek).Date
            //            group dc by week
            //            into dpw
            //            orderby dpw.Key
            //            select new
            //            {
            //                dpw.Key.Date,
            //                Count = dpw.Average(c => c.DownloadCount)
            //            } as object
            //    } as object;

            var data = new
            {
                Id = id,
                Downloads = downloads
            };

            return data;
        }

        public async Task<IList<DailyDownloadResult>> GetDailyDownloads(string packageId, int months)
        {
            var packageIdParam = new NpgsqlParameter("@packageId", packageId ?? (object)DBNull.Value);
            var monthsParam = new NpgsqlParameter("@months", months);

            var sql = @"
SELECT AVG(COALESCE(d.download_count, 0)) AS downloadcount,
	   DATE_TRUNC('day', (totalPeriod.day + CAST((-CAST(FLOOR(DATE_PART('dow', totalPeriod.day)) AS integer) || ' days') AS interval))) AS week
	  FROM 
	  (
		  SELECT day
			FROM   generate_series(
					 DATE_TRUNC('day', (NOW() AT TIME ZONE 'UTC' + CAST((-@months || ' months') AS interval)))
                     , DATE_TRUNC('day', (NOW() AT TIME ZONE 'UTC'))
                     , interval  '1 day') AS t(day)
	  ) AS totalPeriod
 LEFT JOIN daily_downloads AS d ON totalPeriod.day = d.date AND d.package_id = @packageId
	GROUP BY week
  ORDER BY week";

            var downloadsPerWeek = await _context.Query<DailyDownloadResult>().FromSql(sql, packageIdParam, monthsParam).ToListAsync();

            return downloadsPerWeek;
        }
    }
}
