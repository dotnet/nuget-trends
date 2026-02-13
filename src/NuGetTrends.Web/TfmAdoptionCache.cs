using System.Runtime.CompilerServices;
using Microsoft.Extensions.Caching.Memory;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Sentry;

namespace NuGetTrends.Web;

public class TfmAdoptionResponse
{
    public required List<TfmAdoptionSeriesDto> Series { get; init; }
}

public class TfmAdoptionSeriesDto
{
    public required string Tfm { get; init; }
    public required string Family { get; init; }
    public required List<TfmAdoptionPointDto> DataPoints { get; init; }
}

public class TfmAdoptionPointDto
{
    public DateOnly Month { get; init; }
    public uint CumulativeCount { get; init; }
    public uint NewCount { get; init; }
}

public class TfmFamilyGroupDto
{
    public required string Family { get; init; }
    public required List<string> Tfms { get; init; }
}

public interface ITfmAdoptionCache
{
    Task<TfmAdoptionResponse> GetAdoptionAsync(
        IReadOnlyList<string>? tfms = null,
        IReadOnlyList<string>? families = null,
        CancellationToken ct = default);

    Task<List<TfmFamilyGroupDto>> GetAvailableTfmsAsync(CancellationToken ct = default);
}

/// <summary>
/// Caches TFM adoption data to avoid ClickHouse queries on every request.
/// The snapshot only changes weekly, so we cache for 7 days in production.
/// Caches the FULL dataset and applies filtering on cached data (dataset is small).
/// </summary>
public class TfmAdoptionCache : ITfmAdoptionCache
{
    private readonly IClickHouseService _clickHouseService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TfmAdoptionCache> _logger;
    private readonly TimeSpan _cacheDuration;

    private const string AdoptionCacheKey = "tfm_adoption";
    private const string AvailableTfmsCacheKey = "tfm_available";

    public TfmAdoptionCache(
        IClickHouseService clickHouseService,
        IMemoryCache cache,
        ILogger<TfmAdoptionCache> logger,
        IHostEnvironment hostEnvironment)
    {
        _clickHouseService = clickHouseService;
        _cache = cache;
        _logger = logger;
        _cacheDuration = hostEnvironment.IsProduction()
            ? TimeSpan.FromDays(7)
            : TimeSpan.FromSeconds(30);
    }

    public async Task<TfmAdoptionResponse> GetAdoptionAsync(
        IReadOnlyList<string>? tfms = null,
        IReadOnlyList<string>? families = null,
        CancellationToken ct = default)
    {
        var span = StartCacheSpan("cache.get", AdoptionCacheKey);

        try
        {
            if (_cache.TryGetValue(AdoptionCacheKey, out TfmAdoptionResponse? cached) && cached != null)
            {
                span?.SetData("cache.hit", true);
                span?.Finish(SpanStatus.Ok);

                return FilterResponse(cached, tfms, families);
            }

            span?.SetData("cache.hit", false);
            span?.Finish(SpanStatus.Ok);

            _logger.LogDebug("Cache miss for TFM adoption, fetching from ClickHouse");

            var dataPoints = await _clickHouseService.GetTfmAdoptionFromSnapshotAsync(ct: ct);
            var response = BuildResponse(dataPoints);

            var putSpan = StartCacheSpan("cache.put", AdoptionCacheKey);
            try
            {
                _cache.Set(AdoptionCacheKey, response, new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration));
                putSpan?.SetData("cache.item_size", response.Series.Count);
                putSpan?.SetData("cache.ttl", (int)_cacheDuration.TotalSeconds);
                putSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                putSpan?.Finish(ex);
                _logger.LogWarning(ex, "Failed to cache TFM adoption data");
            }

            return FilterResponse(response, tfms, families);
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    public async Task<List<TfmFamilyGroupDto>> GetAvailableTfmsAsync(CancellationToken ct = default)
    {
        var span = StartCacheSpan("cache.get", AvailableTfmsCacheKey);

        try
        {
            if (_cache.TryGetValue(AvailableTfmsCacheKey, out List<TfmFamilyGroupDto>? cached) && cached != null)
            {
                span?.SetData("cache.hit", true);
                span?.Finish(SpanStatus.Ok);
                return cached;
            }

            span?.SetData("cache.hit", false);
            span?.Finish(SpanStatus.Ok);

            var groups = await _clickHouseService.GetAvailableTfmsAsync(ct: ct);
            var result = groups.Select(g => new TfmFamilyGroupDto
            {
                Family = g.Family,
                Tfms = g.Tfms
            }).ToList();

            var putSpan = StartCacheSpan("cache.put", AvailableTfmsCacheKey);
            try
            {
                _cache.Set(AvailableTfmsCacheKey, result, new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(_cacheDuration));
                putSpan?.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                putSpan?.Finish(ex);
                _logger.LogWarning(ex, "Failed to cache available TFMs");
            }

            return result;
        }
        catch (Exception ex)
        {
            span?.Finish(ex);
            throw;
        }
    }

    private static TfmAdoptionResponse BuildResponse(List<TfmAdoptionDataPoint> dataPoints)
    {
        var series = dataPoints
            .GroupBy(d => (d.Tfm, d.Family))
            .Select(g => new TfmAdoptionSeriesDto
            {
                Tfm = g.Key.Tfm,
                Family = g.Key.Family,
                DataPoints = g.OrderBy(d => d.Month).Select(d => new TfmAdoptionPointDto
                {
                    Month = d.Month,
                    CumulativeCount = d.CumulativePackageCount,
                    NewCount = d.NewPackageCount
                }).ToList()
            })
            .ToList();

        return new TfmAdoptionResponse { Series = series };
    }

    private static TfmAdoptionResponse FilterResponse(
        TfmAdoptionResponse response,
        IReadOnlyList<string>? tfms,
        IReadOnlyList<string>? families)
    {
        if (tfms is null or { Count: 0 } && families is null or { Count: 0 })
        {
            return response;
        }

        var filtered = response.Series.Where(s =>
        {
            if (tfms is { Count: > 0 } && !tfms.Contains(s.Tfm, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            if (families is { Count: > 0 } && !families.Contains(s.Family, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }).ToList();

        return new TfmAdoptionResponse { Series = filtered };
    }

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

        span.SetData("cache.key", cacheKey);
        span.SetData("code.filepath", TelemetryHelpers.GetRelativeFilePath(filePath));
        span.SetData("code.function", memberName);
        span.SetData("code.lineno", lineNumber);
        span.SetData("code.namespace", typeof(TfmAdoptionCache).FullName);

        return span;
    }
}
