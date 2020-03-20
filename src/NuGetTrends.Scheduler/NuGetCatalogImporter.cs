using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Hangfire;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;

namespace NuGetTrends.Scheduler
{
    [DisableConcurrentExecution(timeoutInSeconds: 48 * 60 * 60)]
    public class NuGetCatalogImporter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CatalogCursorStore _cursorStore;
        private readonly IEnumerable<ICatalogLeafProcessor> _catalogLeafProcessors;
        private readonly ILoggerFactory _loggerFactory;

        public NuGetCatalogImporter(
            IHttpClientFactory httpClientFactory,
            CatalogCursorStore cursorStore,
            IEnumerable<ICatalogLeafProcessor> catalogLeafProcessors,
            ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            _cursorStore = cursorStore;
            _catalogLeafProcessors = catalogLeafProcessors;
            _loggerFactory = loggerFactory;
        }

        public async Task Import(IJobCancellationToken token)
        {
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
                _catalogLeafProcessors,
                settings,
                _loggerFactory.CreateLogger<CatalogProcessor>());

            await catalogProcessor.ProcessAsync(token.ShutdownToken);

            logger.LogInformation("Finished importing catalog.");
        }
    }
}
