using Hangfire;
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
    private readonly ILogger<NuGetCatalogImporter> _logger = loggerFactory.CreateLogger<NuGetCatalogImporter>();

    [SentryMonitorSlug("NuGetCatalogImporter.Import")]
    public async Task Import(IJobCancellationToken token)
    {
        // Check NuGet availability before starting - skip if recently unavailable
        if (!availabilityState.IsAvailable)
        {
            _logger.LogWarning(
                "Skipping catalog import - NuGet API marked unavailable since {UnavailableSince}. Will retry after cooldown.",
                availabilityState.UnavailableSince);
            return;
        }

        using var _ = hub.PushScope();
        var transaction = hub.StartTransaction("import-catalog", "catalog.import");
        hub.ConfigureScope(s => s.Transaction = transaction);

        _logger.LogInformation("Starting importing catalog.");
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

            _logger.LogInformation("Finished importing catalog.");
            transaction.Finish(SpanStatus.Ok);
        }
        catch (HttpRequestException e)
        {
            // HTTP failure after resilience retries exhausted - mark NuGet as unavailable
            availabilityState.MarkUnavailable(e);
            transaction.Finish(e);
            throw;
        }
        catch (Exception e)
        {
            transaction.Finish(e);
            SentrySdk.CaptureException(e);
            throw;
        }
        finally
        {
            await SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));
        }
    }
}
