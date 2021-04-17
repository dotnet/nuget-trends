using System;
using System.Net.Http;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;
using Sentry;

namespace NuGetTrends.Scheduler
{
    [DisableConcurrentExecution(timeoutInSeconds: 48 * 60 * 60)]
    public class NuGetCatalogImporter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CatalogCursorStore _cursorStore;
        private readonly CatalogLeafProcessor _catalogLeafProcessor;
        private readonly IHub _hub;
        private readonly ILoggerFactory _loggerFactory;

        public NuGetCatalogImporter(
            IHttpClientFactory httpClientFactory,
            CatalogCursorStore cursorStore,
            CatalogLeafProcessor catalogLeafProcessor,
            IHub hub,
            ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            _cursorStore = cursorStore;
            _catalogLeafProcessor = catalogLeafProcessor;
            _hub = hub;
            _loggerFactory = loggerFactory;
        }

        public async Task Import(IJobCancellationToken token)
        {
            var transaction = _hub.StartTransaction("import-catalog", "Import nuget catalog");
            var logger = _loggerFactory.CreateLogger<NuGetCatalogImporter>();

            logger.LogInformation("Starting importing catalog.");

            var httpClient = _httpClientFactory.CreateClient("nuget");
            var catalogClient = new CatalogClient(httpClient, _loggerFactory.CreateLogger<CatalogClient>());
            var settings = new CatalogProcessorSettings
            {
                DefaultMinCommitTimestamp = DateTimeOffset.MinValue, // Read everything
                ExcludeRedundantLeaves = false,
            };

            var catalogProcessor = new CatalogProcessor(
                _cursorStore,
                catalogClient,
                _catalogLeafProcessor,
                settings,
                _loggerFactory.CreateLogger<CatalogProcessor>());

            await catalogProcessor.ProcessAsync(token.ShutdownToken);

            logger.LogInformation("Finished importing catalog.");
            transaction.Finish(SpanStatus.Ok);
        }
    }
}
