using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace NuGetTrends.Data;

/// <summary>
/// Data for upserting a package download record.
/// </summary>
public record PackageDownloadUpsert(
    string PackageId,
    long DownloadCount,
    DateTime CheckedUtc,
    string? IconUrl);

public static class NuGetTrendsContextExtensions
{
    /// <summary>
    /// Gets package IDs that haven't been checked today.
    /// Splits into two efficient queries to avoid a slow LEFT JOIN on the 11M+ row catalog table:
    /// 1. Existing packages not checked today (from package_downloads - ~495K rows, indexed)
    /// 2. New packages not yet in package_downloads (NOT EXISTS subquery with indexed lookup)
    /// </summary>
    /// <param name="context">DbContext to query.</param>
    /// <param name="todayUtc">Today's date in UTC (typically DateTime.UtcNow.Date).</param>
    /// <returns>Queryable of distinct package IDs that need processing.</returns>
    public static IQueryable<string> GetUnprocessedPackageIds(this NuGetTrendsContext context, DateTime todayUtc)
    {
        // Fast: scans package_downloads (~495K rows) with index on latest_download_count_checked_utc
        var uncheckedExisting = context.PackageDownloads
            .Where(pd => pd.LatestDownloadCountCheckedUtc < todayUtc)
            .Select(pd => pd.PackageId);

        // New packages in catalog but not yet in package_downloads
        // Uses NOT EXISTS with indexed lookup on package_id_lowered
        var newPackages = context.PackageDetailsCatalogLeafs
            .Where(leaf => leaf.PackageId != null
                && !context.PackageDownloads
                    .Any(pd => pd.PackageIdLowered == leaf.PackageIdLowered))
            .Select(leaf => leaf.PackageId!)
            .Distinct();

        return uncheckedExisting.Union(newPackages);
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

    /// <summary>
    /// Performs a batch upsert of package download records using PostgreSQL's
    /// INSERT ... ON CONFLICT DO UPDATE (UPSERT) for optimal performance.
    /// </summary>
    /// <param name="context">DbContext to execute the upsert.</param>
    /// <param name="packages">Collection of package download data to upsert.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows affected.</returns>
    /// <remarks>
    /// This replaces the previous pattern of SELECT + INSERT/UPDATE per package,
    /// reducing database round-trips from N+1 to 1 for a batch of N packages.
    /// </remarks>
    public static async Task<int> UpsertPackageDownloadsAsync(
        this NuGetTrendsContext context,
        IReadOnlyList<PackageDownloadUpsert> packages,
        CancellationToken ct = default)
    {
        if (packages.Count == 0)
        {
            return 0;
        }

        // Build parameterized query for batch upsert
        // Using numbered parameters ($1, $2, etc.) for Npgsql
        var sql = new StringBuilder();
        sql.AppendLine("""
            INSERT INTO package_downloads (package_id, package_id_lowered, latest_download_count, latest_download_count_checked_utc, icon_url)
            VALUES
            """);

        var parameters = new List<object>();
        var paramIndex = 1;

        for (var i = 0; i < packages.Count; i++)
        {
            if (i > 0)
            {
                sql.AppendLine(",");
            }

            sql.Append($"(@p{paramIndex}, @p{paramIndex + 1}, @p{paramIndex + 2}, @p{paramIndex + 3}, @p{paramIndex + 4})");

            var pkg = packages[i];
            parameters.Add(new NpgsqlParameter($"p{paramIndex}", pkg.PackageId));
            parameters.Add(new NpgsqlParameter($"p{paramIndex + 1}", pkg.PackageId.ToLowerInvariant()));
            parameters.Add(new NpgsqlParameter($"p{paramIndex + 2}", pkg.DownloadCount));
            parameters.Add(new NpgsqlParameter($"p{paramIndex + 3}", pkg.CheckedUtc));
            parameters.Add(new NpgsqlParameter($"p{paramIndex + 4}", (object?)pkg.IconUrl ?? DBNull.Value));

            paramIndex += 5;
        }

        sql.AppendLine();
        sql.AppendLine("""
            ON CONFLICT (package_id_lowered) DO UPDATE SET
                package_id = EXCLUDED.package_id,
                latest_download_count = EXCLUDED.latest_download_count,
                latest_download_count_checked_utc = EXCLUDED.latest_download_count_checked_utc,
                icon_url = EXCLUDED.icon_url
            """);

        return await context.Database.ExecuteSqlRawAsync(sql.ToString(), parameters, ct);
    }
}
