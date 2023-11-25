using Hangfire;
using NuGet.Protocol.Catalog;
using Sentry;

namespace NuGetTrends.Scheduler;

[DisableConcurrentExecution(timeoutInSeconds: 48 * 60 * 60)]
public class NuGetCatalogImporter(
    IHttpClientFactory httpClientFactory,
    CatalogCursorStore cursorStore,
    CatalogLeafProcessor catalogLeafProcessor,
    IHub hub,
    ILoggerFactory loggerFactory)
{
    public async Task Import(IJobCancellationToken token)
    {
        using var _ = hub.PushScope();
        var transaction = hub.StartTransaction("import-catalog", "catalog.import");
        hub.ConfigureScope(s => s.Transaction = transaction);
        var logger = loggerFactory.CreateLogger<NuGetCatalogImporter>();
        logger.LogInformation("Starting importing catalog.");
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

            logger.LogInformation("Finished importing catalog.");
            transaction.Finish(SpanStatus.Ok);
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
