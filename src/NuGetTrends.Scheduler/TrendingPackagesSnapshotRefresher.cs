using Hangfire;
using Hangfire.Server;
using NuGetTrends.Data.ClickHouse;
using Sentry;
using Sentry.Hangfire;

namespace NuGetTrends.Scheduler;

/// <summary>
/// Hangfire job that refreshes the pre-computed trending packages snapshot in ClickHouse.
/// This runs the expensive trending packages query once and stores results for fast retrieval.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 10)] // 10 minutes max
[AutomaticRetry(Attempts = 2, DelaysInSeconds = [60, 300])] // Retry after 1 min, then 5 min
public class TrendingPackagesSnapshotRefresher(
    IClickHouseService clickHouseService,
    IHub hub,
    ILogger<TrendingPackagesSnapshotRefresher> logger)
{
    // Configuration for trending packages query
    private const long MinWeeklyDownloads = 1000;
    private const int MaxPackageAgeMonths = 12;

    [SentryMonitorSlug("TrendingPackagesSnapshotRefresher.Refresh")]
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

        try
        {
            logger.LogInformation("Job {JobId}: Starting trending packages snapshot refresh", jobId);

            var refreshSpan = transaction.StartChild("clickhouse.refresh", "Refresh trending packages snapshot");
            refreshSpan.SetData("min_weekly_downloads", MinWeeklyDownloads);
            refreshSpan.SetData("max_package_age_months", MaxPackageAgeMonths);

            var count = await clickHouseService.RefreshTrendingPackagesSnapshotAsync(
                minWeeklyDownloads: MinWeeklyDownloads,
                maxPackageAgeMonths: MaxPackageAgeMonths,
                ct: token.ShutdownToken,
                parentSpan: refreshSpan);

            refreshSpan.SetData("packages_count", count);
            refreshSpan.Finish(SpanStatus.Ok);

            logger.LogInformation("Job {JobId}: Trending packages snapshot refreshed with {Count} packages", jobId, count);

            transaction.Finish(SpanStatus.Ok);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Job {JobId}: Trending packages snapshot refresh was cancelled", jobId);
            transaction.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job {JobId}: Failed to refresh trending packages snapshot", jobId);
            transaction.Finish(ex);
            throw;
        }
        finally
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
