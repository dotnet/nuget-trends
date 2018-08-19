using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;

namespace NuGetTrends.Scheduler
{
    public class NuGetCatalogImporter
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CatalogCursorStore _cursorStore;
        private readonly CatalogLeafProcessor _catalogLeafProcessor;
        private readonly ILoggerFactory _loggerFactory;

        public NuGetCatalogImporter(
            IHttpClientFactory httpClientFactory,
            CatalogCursorStore cursorStore,
            CatalogLeafProcessor catalogLeafProcessor,
            ILoggerFactory loggerFactory)
        {
            _httpClientFactory = httpClientFactory;
            _cursorStore = cursorStore;
            _catalogLeafProcessor = catalogLeafProcessor;
            _loggerFactory = loggerFactory;
        }

        public async Task Import()
        {
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

            bool success;
            do
            {
                success = await catalogProcessor.ProcessAsync();
                if (!success)
                {
                    Console.WriteLine("Processing the catalog leafs failed. Retrying.");
                }
            }
            while (!success);
        }
    }
}
