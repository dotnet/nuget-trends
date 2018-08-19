using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class CatalogLeafProcessor : ICatalogLeafProcessor
    {
        private readonly NuGetTrendsContext _context;
        private readonly ILogger<CatalogLeafProcessor> _logger;

        public CatalogLeafProcessor(
            NuGetTrendsContext context,
            ILogger<CatalogLeafProcessor> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<bool> ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf)
        {
            _logger.LogWarning("Deleted: {leaf}", leaf);
            return Task.FromResult(true);
        }

        public async Task<bool> ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf)
        {
            await _context.PackageDetailsCatalogLeafs.AddAsync(leaf);
            return true;
        }
    }
}
