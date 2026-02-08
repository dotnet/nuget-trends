using Hangfire;
using Hangfire.Server;
using Microsoft.EntityFrameworkCore;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Sentry;

namespace NuGetTrends.Scheduler;

/// <summary>
/// Hangfire job that refreshes the pre-computed trending packages snapshot in ClickHouse.
/// Computes trending data from ClickHouse, enriches with metadata from PostgreSQL
/// (icon URLs, GitHub URLs, original-cased package IDs), then batch-inserts the
/// enriched data back into ClickHouse for fast retrieval by the web app.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 10)] // 10 minutes max
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])] // Retry after 1 min, then 5 min
public class TrendingPackagesSnapshotRefresher(
    IClickHouseService clickHouseService,
    NuGetTrendsContext dbContext,
    IHub hub,
    ILogger<TrendingPackagesSnapshotRefresher> logger)
{
    // Configuration for trending packages query
    private const long MinWeeklyDownloads = 1000;
    private const int MaxPackageAgeMonths = 12;

    public async Task Refresh(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        // Start a new, independent transaction
        using var _ = hub.PushScope();
        var transactionContext = new TransactionContext(
            name: "trending-packages-snapshot-refresh",
            operation: "job",
            traceId: SentryId.Create(),
            spanId: SpanId.Create(),
            parentSpanId: null,
            isSampled: true);
        var transaction = hub.StartTransaction(transactionContext);
        hub.ConfigureScope(s =>
        {
            s.Transaction = transaction;
            s.SetTag("jobId", jobId);
        });

        // Start Sentry cron check-in with monitor upsert (auto-creates/updates monitor config)
        // Placed inside scope so check-in is bound to the same trace ID
        var checkInId = hub.CaptureCheckIn(
            JobScheduleConfig.TrendingSnapshotRefresher.MonitorSlug,
            CheckInStatus.InProgress,
            configureMonitorOptions: options =>
            {
                options.Interval(1, SentryMonitorInterval.Week);
                options.CheckInMargin = TimeSpan.FromMinutes(JobScheduleConfig.TrendingSnapshotRefresher.CheckInMarginMinutes);
                options.MaxRuntime = TimeSpan.FromMinutes(JobScheduleConfig.TrendingSnapshotRefresher.MaxRuntimeMinutes);
                options.TimeZone = "Etc/UTC";
                options.FailureIssueThreshold = JobScheduleConfig.TrendingSnapshotRefresher.FailureIssueThreshold;
            });

        try
        {
            logger.LogInformation("Job {JobId}: Starting trending packages snapshot refresh", jobId);

            // Step 1: Update package_first_seen with any missing packages (self-healing)
            // Scans all weeks, so it catches up after pipeline gaps
            var firstSeenSpan = transaction.StartChild("clickhouse.update_first_seen", "Update package_first_seen");
            var newPackages = await clickHouseService.UpdatePackageFirstSeenAsync(
                ct: token.ShutdownToken,
                parentSpan: firstSeenSpan);
            firstSeenSpan.SetData("new_packages_count", newPackages);
            firstSeenSpan.Finish(SpanStatus.Ok);

            logger.LogInformation("Job {JobId}: Backfilled {NewPackages} missing packages into package_first_seen", jobId, newPackages);

            // Step 2: Compute trending packages from ClickHouse (expensive query)
            var computeSpan = transaction.StartChild("clickhouse.compute_trending", "Compute trending packages");
            computeSpan.SetData("min_weekly_downloads", MinWeeklyDownloads);
            computeSpan.SetData("max_package_age_months", MaxPackageAgeMonths);
            List<TrendingPackage> trendingPackages;
            try
            {
                trendingPackages = await clickHouseService.ComputeTrendingPackagesAsync(
                    minWeeklyDownloads: MinWeeklyDownloads,
                    maxPackageAgeMonths: MaxPackageAgeMonths,
                    ct: token.ShutdownToken,
                    parentSpan: computeSpan);
                computeSpan.SetData("packages_count", trendingPackages.Count);
                computeSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                computeSpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: Computed {Count} trending packages from ClickHouse", jobId, trendingPackages.Count);

            // Step 3: Enrich with metadata from PostgreSQL (icon URLs, GitHub URLs, original casing)
            var enrichSpan = transaction.StartChild("postgres.enrich_metadata", "Enrich trending packages with PostgreSQL metadata");
            List<TrendingPackage> enrichedPackages;
            try
            {
                enrichedPackages = await EnrichWithPostgresMetadataAsync(trendingPackages, token.ShutdownToken);
                enrichSpan.SetData("enriched_count", enrichedPackages.Count);
                enrichSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                enrichSpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: Enriched {Count} trending packages with PostgreSQL metadata", jobId, enrichedPackages.Count);

            // Step 4: Batch-insert enriched data into ClickHouse snapshot table
            var insertSpan = transaction.StartChild("clickhouse.insert_snapshot", "Insert enriched trending packages snapshot");
            int count;
            try
            {
                count = await clickHouseService.InsertTrendingPackagesSnapshotAsync(
                    enrichedPackages,
                    ct: token.ShutdownToken,
                    parentSpan: insertSpan);
                insertSpan.SetData("packages_count", count);
                insertSpan.Finish(SpanStatus.Ok);
            }
            catch (Exception ex)
            {
                insertSpan.Finish(ex);
                throw;
            }

            logger.LogInformation("Job {JobId}: Trending packages snapshot refreshed with {Count} enriched packages", jobId, count);

            transaction.Finish(SpanStatus.Ok);
            hub.CaptureCheckIn(JobScheduleConfig.TrendingSnapshotRefresher.MonitorSlug, CheckInStatus.Ok, checkInId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Job {JobId}: Trending packages snapshot refresh was cancelled", jobId);
            transaction.Finish(SpanStatus.Cancelled);
            hub.CaptureCheckIn(JobScheduleConfig.TrendingSnapshotRefresher.MonitorSlug, CheckInStatus.Error, checkInId);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId}: Failed to refresh trending packages snapshot", jobId);
            transaction.Finish(ex);
            hub.CaptureException(ex);
            hub.CaptureCheckIn(JobScheduleConfig.TrendingSnapshotRefresher.MonitorSlug, CheckInStatus.Error, checkInId);
            throw;
        }
        finally
        {
            await hub.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Enriches trending packages with metadata from PostgreSQL:
    /// - Original-cased package ID (from package_downloads)
    /// - Icon URL (from package_downloads)
    /// - GitHub URL (extracted from project_url in package_details_catalog_leafs)
    /// </summary>
    private async Task<List<TrendingPackage>> EnrichWithPostgresMetadataAsync(
        List<TrendingPackage> packages,
        CancellationToken ct)
    {
        if (packages.Count == 0)
        {
            return packages;
        }

        var packageIds = packages.Select(p => p.PackageId).ToList();

        // Get original-cased package IDs and icon URLs
        var packageMetadata = await dbContext.PackageDownloads
            .AsNoTracking()
            .Where(p => packageIds.Contains(p.PackageIdLowered))
            .Select(p => new { p.PackageId, p.PackageIdLowered, p.IconUrl })
            .ToListAsync(ct);

        // Get project URLs for GitHub extraction
        var catalogData = await dbContext.PackageDetailsCatalogLeafs
            .AsNoTracking()
            .Where(c => c.PackageId != null && packageIds.Contains(c.PackageIdLowered))
            .Select(c => new { c.PackageIdLowered, c.ProjectUrl })
            .ToListAsync(ct);

        var metadataLookup = packageMetadata.ToDictionary(p => p.PackageIdLowered);
        var catalogLookup = catalogData
            .GroupBy(c => c.PackageIdLowered)
            .ToDictionary(g => g.Key, g => g.First());

        return packages.Select(tp =>
        {
            var hasMetadata = metadataLookup.TryGetValue(tp.PackageId, out var metadata);
            var hasCatalog = catalogLookup.TryGetValue(tp.PackageId, out var catalog);

            return new TrendingPackage
            {
                PackageId = tp.PackageId,
                Week = tp.Week,
                WeekDownloads = tp.WeekDownloads,
                ComparisonWeekDownloads = tp.ComparisonWeekDownloads,
                PackageIdOriginal = hasMetadata ? metadata!.PackageId : tp.PackageId,
                IconUrl = metadata?.IconUrl ?? "",
                GitHubUrl = hasCatalog ? ExtractGitHubUrl(catalog!.ProjectUrl) ?? "" : ""
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
}
