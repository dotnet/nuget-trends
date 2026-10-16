using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Sentry;

namespace NuGetTrends.Scheduler;

/// <summary>
/// Hangfire job that turns silent pipeline failures into Sentry errors. In October 2026 ClickHouse ran out of
/// disk, the daily download consumers died, and for five days nothing failed: the publisher kept succeeding
/// while its messages expired unprocessed.
/// Each failed check is captured as its own Sentry error, fingerprinted per check, so it opens (or regresses)
/// one issue per problem. The cron check-in tracks that the checks ran: a missed check-in means the scheduler is down.
/// </summary>
[DisableConcurrentExecution(timeoutInSeconds: 60 * 10)]
[AutomaticRetry(Attempts = 0)] // Runs again within the hour
// ReSharper disable once ClassNeverInstantiated.Global - DI
public class PipelineHealthCheck(
    IConnectionFactory connectionFactory,
    IClickHouseService clickHouseService,
    NuGetTrendsContext dbContext,
    IOptions<DailyDownloadWorkerOptions> workerOptions,
    IHub hub,
    ILogger<PipelineHealthCheck> logger)
{
    // A message carries 1,000 package IDs (~0.2% of all packages) and a healthy run leaves almost none unchecked.
    internal const double MaxUncheckedRatio = 0.01;
    internal const int MinUncheckedToReport = 2_000;

    internal const double MinClickHouseFreeRatio = 0.20;
    internal const ulong MinClickHouseFreeBytes = 30UL * 1024 * 1024 * 1024;

    public async Task Run(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        using var _ = hub.PushScope();
        var transactionContext = new TransactionContext(
            name: "scheduler-pipeline-health",
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

        var checkInId = hub.CaptureCheckIn(
            JobScheduleConfig.PipelineHealthCheck.MonitorSlug,
            CheckInStatus.InProgress,
            configureMonitorOptions: options =>
            {
                options.Interval(JobScheduleConfig.PipelineHealthCheck.IntervalHours, SentryMonitorInterval.Hour);
                options.CheckInMargin = TimeSpan.FromMinutes(JobScheduleConfig.PipelineHealthCheck.CheckInMarginMinutes);
                options.MaxRuntime = TimeSpan.FromMinutes(JobScheduleConfig.PipelineHealthCheck.MaxRuntimeMinutes);
                options.TimeZone = "Etc/UTC";
                options.FailureIssueThreshold = JobScheduleConfig.PipelineHealthCheck.FailureIssueThreshold;
            });

        var ct = token.ShutdownToken;
        var checksFailedToRun = 0;
        try
        {
            checksFailedToRun += await RunCheck("queue-consumers", transaction, () => CheckQueueConsumers());
            checksFailedToRun += await RunCheck("clickhouse-disk", transaction, () => CheckClickHouseDisk(transaction, ct));
            checksFailedToRun += await RunCheck("daily-downloads", transaction, () => CheckDailyDownloads(ct));

            var status = checksFailedToRun == 0 ? CheckInStatus.Ok : CheckInStatus.Error;
            transaction.Finish(checksFailedToRun == 0 ? SpanStatus.Ok : SpanStatus.InternalError);
            hub.CaptureCheckIn(JobScheduleConfig.PipelineHealthCheck.MonitorSlug, status, checkInId);
        }
        catch (OperationCanceledException)
        {
            transaction.Finish(SpanStatus.Cancelled);
            hub.CaptureCheckIn(JobScheduleConfig.PipelineHealthCheck.MonitorSlug, CheckInStatus.Error, checkInId);
            throw;
        }
        finally
        {
            await hub.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Runs one check. Problems it finds are reported by the check itself; this only handles the check
    /// being unable to run (e.g. a dependency is unreachable), which is reported too.
    /// </summary>
    /// <returns>1 if the check couldn't run, otherwise 0.</returns>
    private async Task<int> RunCheck(string name, ITransactionTracer transaction, Func<Task> check)
    {
        var span = transaction.StartChild("health.check", name);
        try
        {
            await check();
            span.Finish(SpanStatus.Ok);
            return 0;
        }
        catch (OperationCanceledException)
        {
            span.Finish(SpanStatus.Cancelled);
            throw;
        }
        catch (Exception e)
        {
            span.Finish(e);
            logger.LogError(e, "Pipeline health check '{Check}' could not run.", name);
            return 1;
        }
    }

    private Task CheckQueueConsumers()
    {
        using var connection = connectionFactory.CreateConnection("scheduler-pipeline-health");
        using var channel = connection.CreateModel();

        QueueDeclareOk queue;
        try
        {
            queue = channel.QueueDeclarePassive(DailyDownloadWorker.QueueName);
        }
        catch (OperationInterruptedException e) when (e.ShutdownReason?.ReplyCode == 404)
        {
            // The worker declares the queue when it connects.
            ReportUnhealthy("queue-consumers",
                $"Queue '{DailyDownloadWorker.QueueName}' does not exist: the daily download worker never connected.");
            return Task.CompletedTask;
        }

        hub.Metrics.EmitGauge<int>("scheduler.health.queue_consumers", (int)queue.ConsumerCount,
            MeasurementUnit.None, [new("queue", DailyDownloadWorker.QueueName)]);

        var expected = workerOptions.Value.WorkerCount;
        if (queue.ConsumerCount == 0)
        {
            ReportUnhealthy("queue-consumers",
                $"No consumers on queue '{DailyDownloadWorker.QueueName}' ({queue.MessageCount} messages waiting). " +
                "Daily downloads are not being processed and queued messages expire after 12 hours.");
        }
        else if (queue.ConsumerCount < expected)
        {
            ReportUnhealthy("queue-consumers-degraded",
                $"Only {queue.ConsumerCount} of {expected} consumers on queue '{DailyDownloadWorker.QueueName}'.",
                SentryLevel.Warning);
        }

        return Task.CompletedTask;
    }

    private async Task CheckClickHouseDisk(ITransactionTracer transaction, CancellationToken ct)
    {
        var disk = await clickHouseService.GetDiskUsageAsync(ct, transaction);

        hub.Metrics.EmitGauge<double>("scheduler.health.clickhouse_disk_free_ratio", disk.FreeRatio,
            MeasurementUnit.None, [new("disk", disk.Name)]);

        if (IsDiskLow(disk))
        {
            ReportUnhealthy("clickhouse-disk",
                $"ClickHouse disk '{disk.Name}' is low on space: {ToGiB(disk.FreeBytes):N1} GiB free of " +
                $"{ToGiB(disk.TotalBytes):N1} GiB ({disk.FreeRatio:P0}). Inserts fail once it is exhausted.");
        }
    }

    private async Task CheckDailyDownloads(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        if (now.Hour < JobScheduleConfig.PipelineHealthCheck.DailyDownloadsDeadlineHourUtc)
        {
            return; // Tonight's run hasn't had time to finish yet
        }

        var (uncheckedCount, total) = await dbContext.GetDailyDownloadProgressAsync(now.Date, ct);

        hub.Metrics.EmitGauge<int>("scheduler.health.daily_downloads_unchecked", uncheckedCount, MeasurementUnit.None);

        if (IsDailyDownloadIncomplete(uncheckedCount, total))
        {
            ReportUnhealthy("daily-downloads",
                $"{uncheckedCount:N0} of {total:N0} packages ({(double)uncheckedCount / total:P1}) have no download count for " +
                $"{now:yyyy-MM-dd} at {now:HH:mm} UTC; the run is published at " +
                $"{JobScheduleConfig.DailyDownloadPublisher.RunAtHourUtc:00}:00 UTC.");
        }
    }

    internal static bool IsDiskLow(ClickHouseDiskUsage disk) =>
        disk.FreeRatio < MinClickHouseFreeRatio || disk.FreeBytes < MinClickHouseFreeBytes;

    internal static bool IsDailyDownloadIncomplete(int uncheckedCount, int total) =>
        uncheckedCount >= MinUncheckedToReport && uncheckedCount > total * MaxUncheckedRatio;

    private void ReportUnhealthy(string check, string message, SentryLevel level = SentryLevel.Error)
    {
        // Captured directly (not via LogError) to control the fingerprint: one issue per check.
        logger.LogWarning("Pipeline health check '{Check}' failed: {Message}", check, message);
        hub.CaptureMessage(message, scope =>
        {
            scope.SetFingerprint(["pipeline-health", check]);
            scope.SetTag("health_check", check);
        }, level);
    }

    private static double ToGiB(ulong bytes) => bytes / (1024.0 * 1024 * 1024);
}
