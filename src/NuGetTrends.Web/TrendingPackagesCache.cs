using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Sentry;

namespace NuGetTrends.Web;

/// <summary>
/// DTO for trending package API response.
/// </summary>
public class TrendingPackageDto
{
    /// <summary>
    /// Package ID (original casing from PostgreSQL).
    /// </summary>
    public required string PackageId { get; init; }

    /// <summary>
    /// Current week average downloads.
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
/// Caches trending packages to avoid expensive ClickHouse queries on every request.
/// Instrumented with Sentry's Caches module conventions.
/// </summary>
public interface ITrendingPackagesCache
{
    /// <summary>
    /// Gets trending packages, using cached data if available.
    /// </summary>
    Task<List<TrendingPackageDto>> GetTrendingPackagesAsync(
        int limit = 10,
        CancellationToken ct = default);
}

public class TrendingPackagesCache : ITrendingPackagesCache
{
    private readonly IClickHouseService _clickHouseService;
    private readonly NuGetTrendsContext _dbContext;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TrendingPackagesCache> _logger;

    // Cache configuration
    private const string CacheKey = "trending_packages";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    // Trending query configuration - favor newer packages
    private const long MinWeeklyDownloads = 1000;
    private const int MaxPackageAgeMonths = 12;
    private const int MaxCachedResults = 100; // Cache more than we serve to support different limit values

    public TrendingPackagesCache(
        IClickHouseService clickHouseService,
        NuGetTrendsContext dbContext,
        IMemoryCache cache,
        ILogger<TrendingPackagesCache> logger)
    {
        _clickHouseService = clickHouseService;
        _dbContext = dbContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<TrendingPackageDto>> GetTrendingPackagesAsync(
        int limit = 10,
        CancellationToken ct = default)
    {
        var span = StartCacheSpan("cache.get", CacheKey);

        try
        {
            if (_cache.TryGetValue(CacheKey, out List<TrendingPackageDto>? cached) && cached != null)
            {
                span?.SetData("cache.hit", true);
                span?.SetData("cache.item_size", cached.Count);
                span?.Finish(SpanStatus.Ok);

                _logger.LogDebug("Cache hit for trending packages, returning {Count} of {Total} results",
                    Math.Min(limit, cached.Count), cached.Count);

                return cached.Take(limit).ToList();
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
                    .SetAbsoluteExpiration(CacheDuration);

                _cache.Set(CacheKey, freshData, cacheOptions);

                putSpan?.SetData("cache.item_size", freshData.Count);
                putSpan?.SetData("cache.ttl", (int)CacheDuration.TotalSeconds);
                putSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                putSpan?.Finish(ex);
                // Don't fail the request if caching fails
                _logger.LogWarning(ex, "Failed to cache trending packages");
            }

            return freshData.Take(limit).ToList();
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    private async Task<List<TrendingPackageDto>> FetchTrendingPackagesAsync(CancellationToken ct)
    {
        // Get trending packages from ClickHouse
        var trendingPackages = await _clickHouseService.GetTrendingPackagesAsync(
            limit: MaxCachedResults,
            minWeeklyDownloads: MinWeeklyDownloads,
            maxPackageAgeMonths: MaxPackageAgeMonths,
            ct: ct);

        if (trendingPackages.Count == 0)
        {
            return [];
        }

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
        // Using PackageDetailsCatalogLeafs instead of Set<> for better type inference
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

        return trendingPackages.Select(tp =>
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
                DownloadCount = tp.CurrentWeekDownloads,
                GrowthRate = tp.GrowthRate,
                IconUrl = metadata?.IconUrl ?? "https://www.nuget.org/Content/gallery/img/default-package-icon.svg",
                GitHubUrl = gitHubUrl
            };
        }).ToList();
    }

    /// <summary>
    /// Extracts a GitHub URL from a project or repository URL.
    /// Returns null if not a GitHub URL.
    /// </summary>
    private static string? ExtractGitHubUrl(string? url)
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
