using Microsoft.EntityFrameworkCore;

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
}
