using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class CatalogLeafProcessor : ICatalogLeafProcessor
    {
        private readonly NuGetTrendsContext _context;
        private readonly ILogger<CatalogLeafProcessor> _logger;
        private int _counter;

        public CatalogLeafProcessor(
                NuGetTrendsContext context,
                ILogger<CatalogLeafProcessor> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token)
        {
            var deleted = await _context.PackageDetailsCatalogLeafs.FirstOrDefaultAsync(p =>
                p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion, token);

            if (deleted == null)
            {
                _logger.LogError("Deleted event but not found with: {Id}, {Version}", leaf.PackageId, leaf.PackageVersion);
            }
            else
            {
                _context.PackageDetailsCatalogLeafs.Remove(deleted);
                await _context.SaveChangesAsync(token);

                _logger.LogInformation("Deleted: {leaf}", leaf);
            }
        }

        public async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
        {
            _context.PackageDetailsCatalogLeafs.Add(leaf);
            _counter++;

            if (_counter == 100)
            {
                await _context.SaveChangesAsync(token);
                _counter = 0;
            }
        }
    }
}
