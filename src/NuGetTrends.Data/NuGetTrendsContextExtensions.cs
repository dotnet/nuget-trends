using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NuGetTrends.Data;

public static class NuGetTrendsContextExtensions
{
    /// <summary>
    /// Gets package IDs that haven't been checked today.
    /// Uses a LEFT JOIN to find packages in the catalog that either:
    /// 1. Don't exist in package_downloads yet (new packages), OR
    /// 2. Were last checked before today (LatestDownloadCountCheckedUtc &lt; today)
    /// </summary>
    /// <param name="context">DbContext to query.</param>
    /// <param name="todayUtc">Today's date in UTC (typically DateTime.UtcNow.Date).</param>
    /// <returns>Queryable of distinct package IDs that need processing.</returns>
    public static IQueryable<string> GetUnprocessedPackageIds(this NuGetTrendsContext context, DateTime todayUtc)
    {
        return (from leaf in context.PackageDetailsCatalogLeafs
                join pd in context.PackageDownloads
                    on leaf.PackageId!.ToLower() equals pd.PackageIdLowered into downloads
                from pd in downloads.DefaultIfEmpty()
                where pd == null || pd.LatestDownloadCountCheckedUtc < todayUtc
                select leaf.PackageId)
            .Distinct()
            .Where(p => p != null)!;
    }

    /// <summary>
    /// Get daily download number grouped by week for the packageId
    /// </summary>
    /// <param name="context">DbContext to query.</param>
    /// <param name="packageId">The package id to lookup.</param>
    /// <param name="months">How many months back to query.</param>
    /// <returns>List of download count grouped by week.</returns>
    public static Task<List<DailyDownloadResult>> GetDailyDownloads(this NuGetTrendsContext context, string packageId, int months)
    {
        const string sql = @"
SELECT AVG(COALESCE(d.download_count, NULL)) AS download_count,
	   DATE_TRUNC('day', (totalPeriod.day + CAST((-CAST(FLOOR(DATE_PART('dow', totalPeriod.day)) AS integer) || ' days') AS interval))) AS week
	  FROM
	  (
		  SELECT day
			FROM generate_series(
					 DATE_TRUNC('day', (NOW() AT TIME ZONE 'UTC' + CAST((-@months || ' months') AS interval)))
                     , DATE_TRUNC('day', (NOW() AT TIME ZONE 'UTC'))
                     , interval  '1 day') AS t(day)
	  ) AS totalPeriod
 LEFT JOIN daily_downloads AS d ON totalPeriod.day = d.date AND d.package_id = @packageId
	GROUP BY week
  ORDER BY week;";

        var packageIdParam = new NpgsqlParameter("@packageId", packageId ?? (object)DBNull.Value);
        var monthsParam = new NpgsqlParameter("@months", months);

        return context
            .Set<DailyDownloadResult>()
            // ReSharper disable FormatStringProblem - Drunk ReSharper
            .FromSqlRaw(sql, packageIdParam, monthsParam)
            // ReSharper restore FormatStringProblem
            .ToListAsync();
    }
}
