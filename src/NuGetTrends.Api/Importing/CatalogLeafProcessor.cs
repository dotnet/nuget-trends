using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;

namespace NuGetTrends.Api.Importing
{
    public class CatalogLeafProcessor : ICatalogLeafProcessor
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<CatalogLeafProcessor> _logger;

        public CatalogLeafProcessor(
            IServiceProvider provider,
            ILogger<CatalogLeafProcessor> logger)
        {
            _provider = provider;
            _logger = logger;
        }

        public Task<bool> ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf)
        {
            _logger.LogWarning("Deleted: {leaf}", leaf);
            return Task.FromResult(true);
        }

        public async Task<bool> ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf)
        {
            using (var context = _provider.GetRequiredService<NuGetTrendsContext>())
            {
                await context.PackageDetailsCatalogLeafs.AddAsync(leaf);
            }
            return true;
        }
    }
}
