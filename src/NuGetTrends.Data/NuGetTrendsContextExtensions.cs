using System.Text;
using Microsoft.EntityFrameworkCore;

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
                    on leaf.PackageIdLowered equals pd.PackageIdLowered into downloads
                from pd in downloads.DefaultIfEmpty()
                where pd == null || pd.LatestDownloadCountCheckedUtc < todayUtc
                select leaf.PackageId)
            .Distinct()
            .Where(p => p != null)!;
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
