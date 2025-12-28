using Hangfire;
using Hangfire.Server;
using NuGet.Protocol.Catalog;
using Sentry.Hangfire;

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

    [SentryMonitorSlug("NuGetCatalogImporter.Import")]
    public async Task Import(IJobCancellationToken token, PerformContext? context)
    {
        var jobId = context?.BackgroundJob?.Id ?? "unknown";

        // Start transaction immediately - wraps entire job execution for full observability
        using var _ = hub.PushScope();
        var transaction = hub.StartTransaction("import-catalog", "catalog.import");
        hub.ConfigureScope(s =>
        {
            s.Transaction = transaction;
            s.SetTag("jobId", jobId);
        });

        try
        {
            if (!await ImportLock.WaitAsync(TimeSpan.Zero))
            {
                _logger.LogWarning("Job {JobId}: Skipping catalog import - another import is already in progress", jobId);
                transaction.Finish(SpanStatus.Aborted);
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
                }
                catch (HttpRequestException e)
                {
                    // HTTP failure after resilience retries exhausted - mark NuGet as unavailable
                    availabilityState.MarkUnavailable(e);
                    processingSpan.Finish(e);
                    transaction.Finish(e);
                    throw;
                }
                catch (Exception e)
                {
                    processingSpan.Finish(e);
                    transaction.Finish(e);
                    SentrySdk.CaptureException(e);
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
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
