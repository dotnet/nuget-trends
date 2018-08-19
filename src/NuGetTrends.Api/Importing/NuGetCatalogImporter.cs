using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;

namespace NuGetTrends.Api.Importing
{
    public class NuGetCatalogImporter
    {
        private readonly CatalogCursorStore _cursorStore;
        private readonly CatalogLeafProcessor _catalogLeafProcessor;
        private readonly ILoggerFactory _loggerFactory;

        public NuGetCatalogImporter(
            CatalogCursorStore cursorStore,
            CatalogLeafProcessor catalogLeafProcessor,
            ILoggerFactory loggerFactory)
        {
            _cursorStore = cursorStore;
            _catalogLeafProcessor = catalogLeafProcessor;
            _loggerFactory = loggerFactory;
        }

        public async Task Import()
        {
            using (var httpClient = new HttpClient())
            {
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
}
