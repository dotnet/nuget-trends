using Hangfire;
using Hangfire.Server;
using NuGet.Protocol.Catalog;
using Polly.CircuitBreaker;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 48 * 60 * 60)]
[AutomaticRetry(Attempts = 1, OnAttemptsExceeded = AttemptsExceededAction.Delete)]
public class NuGetCatalogImporter(
    IHttpClientFactory httpClientFactory,
    CatalogCursorStore cursorStore,
    CatalogLeafProcessor catalogLeafProcessor,
    NuGetAvailabilityState availabilityState,
    IHub hub,
    ILoggerFactory loggerFactory)
{
    // Hangfire's [DisableConcurrentExecution] uses a distributed lock keyed by method arguments.
    // However, with MemoryStorage and recurring jobs, the lock doesn't prevent concurrent execution
    // when a new scheduled job is triggered while a previous job (or its retry) is still running.
    // This static semaphore ensures only one catalog import runs at a time within this process.
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    private readonly ILogger<NuGetCatalogImporter> _logger = loggerFactory.CreateLogger<NuGetCatalogImporter>();

    public async Task Import(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        // Start a new, independent transaction with its own trace ID
        // This ensures catalog imports are not linked to other jobs' traces
        using var _ = hub.PushScope();
        var transactionContext = new TransactionContext(
            name: "import-catalog",
            operation: "catalog.import",
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
            JobScheduleConfig.CatalogImporter.MonitorSlug,
            CheckInStatus.InProgress,
            configureMonitorOptions: options =>
            {
                options.Interval(JobScheduleConfig.CatalogImporter.IntervalHours, SentryMonitorInterval.Hour);
                options.CheckInMargin = TimeSpan.FromMinutes(JobScheduleConfig.CatalogImporter.CheckInMarginMinutes);
                options.MaxRuntime = TimeSpan.FromMinutes(JobScheduleConfig.CatalogImporter.MaxRuntimeMinutes);
                options.TimeZone = "Etc/UTC";
                options.FailureIssueThreshold = JobScheduleConfig.CatalogImporter.FailureIssueThreshold;
            });

        try
        {
            if (!await ImportLock.WaitAsync(TimeSpan.Zero))
            {
                _logger.LogWarning("Job {JobId}: Skipping catalog import - another import is already in progress", jobId);
                transaction.Finish(SpanStatus.Aborted);
                hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Ok, checkInId); // Skipped is OK, not an error
                hub.Metrics.EmitCounter<int>("scheduler.job.skipped", 1,
                    [new("job", "catalog-importer"), new("reason", "concurrent")]);
                throw new ConcurrentExecutionSkippedException(
                    $"Job {jobId}: Catalog import skipped - another import is already in progress");
            }

            try
            {
                // Check NuGet availability before starting - skip if recently unavailable
                if (!availabilityState.IsAvailable)
                {
                    _logger.LogWarning(
                        "Job {JobId}: Skipping catalog import - NuGet API marked unavailable since {UnavailableSince}. Will retry after cooldown.",
                        jobId, availabilityState.UnavailableSince);
                    transaction.Finish(SpanStatus.Unavailable);
                    hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Ok, checkInId); // Skipped is OK, not an error
                    hub.Metrics.EmitCounter<int>("scheduler.job.skipped", 1,
                        [new("job", "catalog-importer"), new("reason", "nuget_unavailable")]);
                    return;
                }

                _logger.LogInformation("Job {JobId}: Starting importing catalog.", jobId);

                var processingSpan = transaction.StartChild("catalog.process", "Process NuGet catalog");
                try
                {
                    var httpClient = httpClientFactory.CreateClient("nuget");
                    var catalogClient = new CatalogClient(httpClient, loggerFactory.CreateLogger<CatalogClient>());
                    var settings = new CatalogProcessorSettings
                    {
                        DefaultMinCommitTimestamp = DateTimeOffset.MinValue, // Read everything
                        ExcludeRedundantLeaves = false,
                    };

                    var catalogProcessor = new CatalogProcessor(
                        cursorStore,
                        catalogClient,
                        catalogLeafProcessor,
                        settings,
                        loggerFactory.CreateLogger<CatalogProcessor>());

                    await catalogProcessor.ProcessAsync(token.ShutdownToken);

                    // Success - ensure availability state is marked as available
                    availabilityState.MarkAvailable();

                    _logger.LogInformation("Job {JobId}: Finished importing catalog.", jobId);
                    processingSpan.Finish(SpanStatus.Ok);
                    transaction.Finish(SpanStatus.Ok);
                    hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Ok, checkInId);

                    hub.Metrics.EmitCounter<int>("scheduler.job.completed", 1,
                        [new("job", "catalog-importer"), new("status", "ok")]);
                }
                catch (HttpRequestException e)
                {
                    // HTTP failure after resilience retries exhausted - mark NuGet as unavailable
                    availabilityState.MarkUnavailable(e);
                    processingSpan.Finish(e);
                    transaction.Finish(e);
                    hub.CaptureException(e);
                    hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Error, checkInId);
                    hub.Metrics.EmitCounter<int>("scheduler.job.completed", 1,
                        [new("job", "catalog-importer"), new("status", "error"), new("error_type", "http")]);
                    throw;
                }
                catch (BrokenCircuitException e)
                {
                    // Circuit breaker is open - NuGet API has been failing consistently
                    availabilityState.MarkUnavailable(e);
                    processingSpan.Finish(e);
                    transaction.Finish(e);
                    hub.CaptureException(e);
                    hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Error, checkInId);
                    hub.Metrics.EmitCounter<int>("scheduler.job.completed", 1,
                        [new("job", "catalog-importer"), new("status", "error"), new("error_type", "circuit_breaker")]);
                    throw;
                }
                catch (Exception e)
                {
                    processingSpan.Finish(e);
                    transaction.Finish(e);
                    hub.CaptureException(e);
                    hub.CaptureCheckIn(JobScheduleConfig.CatalogImporter.MonitorSlug, CheckInStatus.Error, checkInId);
                    hub.Metrics.EmitCounter<int>("scheduler.job.completed", 1,
                        [new("job", "catalog-importer"), new("status", "error"), new("error_type", "unknown")]);
                    throw;
                }
            }
            finally
            {
                ImportLock.Release();
            }
        }
        finally
        {
            await hub.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
