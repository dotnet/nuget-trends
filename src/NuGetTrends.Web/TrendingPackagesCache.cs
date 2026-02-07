using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Sentry;

namespace NuGetTrends.Web;

/// <summary>
/// API response for trending packages endpoint.
/// </summary>
public class TrendingPackagesResponse
{
    /// <summary>
    /// The week this data represents (Monday of the week, ISO 8601 format).
    /// This is always the most recently completed week, not the current partial week.
    /// </summary>
    public required DateOnly Week { get; init; }

    /// <summary>
    /// List of trending packages for this week.
    /// </summary>
    public required List<TrendingPackageDto> Packages { get; init; }
}

/// <summary>
/// DTO for a single trending package.
/// </summary>
public class TrendingPackageDto
{
    /// <summary>
    /// Package ID (original casing from PostgreSQL).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Total downloads for the reported week.
    /// </summary>
    public long DownloadCount { get; init; }

    /// <summary>
    /// Week-over-week growth rate (e.g., 0.25 = 25% growth).
    /// </summary>
    public double? GrowthRate { get; init; }

    /// <summary>
    /// Package icon URL.
    /// </summary>
    public string IconUrl { get; init; } = "https://www.nuget.org/Content/gallery/img/default-package-icon.svg";

    /// <summary>
    /// GitHub repository URL if available.
    /// </summary>
    public string? GitHubUrl { get; init; }
}

/// <summary>
/// Caches trending packages to avoid ClickHouse queries on every request.
/// The snapshot data is refreshed weekly, so we cache for 7 days.
/// Instrumented with Sentry's Caches module conventions.
/// </summary>
public interface ITrendingPackagesCache
{
    /// <summary>
    /// Gets trending packages response, using cached data if available.
    /// </summary>
    Task<TrendingPackagesResponse> GetTrendingPackagesAsync(
        int limit = 10,
        CancellationToken ct = default);
}

public class TrendingPackagesCache : ITrendingPackagesCache
{
    private readonly IClickHouseService _clickHouseService;
    private readonly NuGetTrendsContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TrendingPackagesCache> _logger;
    private readonly TimeSpan _cacheDuration;

    // Cache configuration - data is refreshed weekly, so cache for 7 days
    private const string CacheKey = "trending_packages";

    // Trending query configuration - favor newer packages
    private const long MinWeeklyDownloads = 1000;
    private const int MaxPackageAgeMonths = 12;
    private const int MaxCachedResults = 100; // Cache more than we serve to support different limit values

    public TrendingPackagesCache(
        IClickHouseService clickHouseService,
        NuGetTrendsContext dbContext,
        IMemoryCache cache,
        ILogger<TrendingPackagesCache> logger,
        IHostEnvironment hostEnvironment)
    {
        _clickHouseService = clickHouseService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
        _cacheDuration = hostEnvironment.IsProduction()
            ? TimeSpan.FromDays(7)
            : TimeSpan.FromSeconds(30);
    }

    public async Task<TrendingPackagesResponse> GetTrendingPackagesAsync(
        int limit = 10,
        CancellationToken ct = default)
    {
        var span = StartCacheSpan("cache.get", CacheKey);

        try
        {
            if (_cache.TryGetValue(CacheKey, out TrendingPackagesResponse? cached) && cached != null)
            {
                span?.SetData("cache.hit", true);
                span?.SetData("cache.item_size", cached.Packages.Count);
                span?.Finish(SpanStatus.Ok);

                _logger.LogDebug("Cache hit for trending packages, returning {Count} of {Total} results",
                    Math.Min(limit, cached.Packages.Count), cached.Packages.Count);

                return new TrendingPackagesResponse
                {
                    Week = cached.Week,
                    Packages = cached.Packages.Take(limit).ToList()
                };
            }

            span?.SetData("cache.hit", false);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Cache miss for trending packages, fetching from ClickHouse");

            // Fetch fresh data
            var freshData = await FetchTrendingPackagesAsync(ct);

            // Cache the results
            var putSpan = StartCacheSpan("cache.put", CacheKey);
            try
            {
                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration);

                _cache.Set(CacheKey, freshData, cacheOptions);

                putSpan?.SetData("cache.item_size", freshData.Packages.Count);
                putSpan?.SetData("cache.ttl", (int)_cacheDuration.TotalSeconds);
                putSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                putSpan?.Finish(ex);
                // Don't fail the request if caching fails
                _logger.LogWarning(ex, "Failed to cache trending packages");
            }

            return new TrendingPackagesResponse
            {
                Week = freshData.Week,
                Packages = freshData.Packages.Take(limit).ToList()
            };
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    private async Task<TrendingPackagesResponse> FetchTrendingPackagesAsync(CancellationToken ct)
    {
        // Get trending packages from pre-computed ClickHouse snapshot (fast, milliseconds)
        // Falls back to real-time query if snapshot is empty (first run or stale data)
        var trendingPackages = await _clickHouseService.GetTrendingPackagesFromSnapshotAsync(
            limit: MaxCachedResults,
            ct: ct);

        if (trendingPackages.Count == 0)
        {
            _logger.LogWarning("No trending packages in snapshot, falling back to real-time query");
            trendingPackages = await _clickHouseService.GetTrendingPackagesAsync(
                limit: MaxCachedResults,
                minWeeklyDownloads: MinWeeklyDownloads,
                maxPackageAgeMonths: MaxPackageAgeMonths,
                ct: ct);
        }

        if (trendingPackages.Count == 0)
        {
            // Return empty response with a default week (last Monday)
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var daysFromMonday = ((int)today.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var lastMonday = today.AddDays(-daysFromMonday - 7); // Previous week's Monday

            return new TrendingPackagesResponse
            {
                Week = lastMonday,
                Packages = []
            };
        }

        // Get the week from the first package (all packages have the same week)
        var dataWeek = trendingPackages[0].Week;

        // Get package metadata from PostgreSQL (icon URLs, original casing)
        var packageIds = trendingPackages.Select(p => p.PackageId).ToList();
        var packageMetadata = await _dbContext.PackageDownloads
            .Where(p => packageIds.Contains(p.PackageIdLowered))
            .Select(p => new
            {
                p.PackageId,
                p.PackageIdLowered,
                p.IconUrl
            })
            .ToListAsync(ct);

        // Get GitHub URLs from catalog data if available
        var catalogData = await _dbContext.PackageDetailsCatalogLeafs
            .Where(c => c.PackageId != null && packageIds.Contains(c.PackageId.ToLower()))
            .Select(c => new
            {
                PackageIdLowered = c.PackageId!.ToLower(),
                c.ProjectUrl
            })
            .ToListAsync(ct);

        var metadataLookup = packageMetadata.ToDictionary(p => p.PackageIdLowered);
        var catalogLookup = catalogData
            .GroupBy(c => c.PackageIdLowered)
            .ToDictionary(g => g.Key, g => g.First());

        var packages = trendingPackages.Select(tp =>
        {
            var hasMetadata = metadataLookup.TryGetValue(tp.PackageId, out var metadata);
            var hasCatalog = catalogLookup.TryGetValue(tp.PackageId, out var catalog);

            // Extract GitHub URL from project URL
            string? gitHubUrl = null;
            if (hasCatalog)
            {
                gitHubUrl = ExtractGitHubUrl(catalog!.ProjectUrl);
            }

            return new TrendingPackageDto
            {
                PackageId = hasMetadata ? metadata!.PackageId : tp.PackageId,
                DownloadCount = tp.WeekDownloads,
                GrowthRate = tp.GrowthRate,
                IconUrl = metadata?.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg",
                GitHubUrl = gitHubUrl
            };
        }).ToList();

        return new TrendingPackagesResponse
        {
            Week = dataWeek,
            Packages = packages
        };
    }

    /// <summary>
    /// Extracts a GitHub URL from a project or repository URL.
    /// Returns null if not a GitHub URL.
    /// </summary>
    internal static string? ExtractGitHubUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Return just the repo URL (remove .git suffix and any deep paths)
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2)
        {
            var owner = segments[0];
            var repo = segments[1].Replace(".git", "", StringComparison.OrdinalIgnoreCase);
            return $"https://github.com/{owner}/{repo}";
        }

        return null;
    }

    /// <summary>
    /// Starts a cache span following Sentry's Caches module conventions.
    /// </summary>
    private static ISpan? StartCacheSpan(
        string operation,
        string cacheKey,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        var span = SentrySdk.GetSpan()?.StartChild(operation, cacheKey);

        if (span == null)
        {
            return null;
        }

        // Required for Sentry Caches module
        span.SetData("cache.key", cacheKey);

        // Query source attributes
        span.SetData("code.filepath", TelemetryHelpers.GetRelativeFilePath(filePath));
        span.SetData("code.function", memberName);
        span.SetData("code.lineno", lineNumber);
        span.SetData("code.namespace", typeof(TrendingPackagesCache).FullName);

        return span;
    }
}
