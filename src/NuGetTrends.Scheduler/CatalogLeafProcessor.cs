using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class CatalogLeafProcessor : ICatalogLeafProcessor
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<CatalogLeafProcessor> _logger;
        private int _counter;

        private IServiceScope _scope;
        private NuGetTrendsContext _context;

        public CatalogLeafProcessor(
                IServiceProvider provider,
                ILogger<CatalogLeafProcessor> logger)
        {
            _provider = provider;
            _logger = logger;

            _scope = _provider.CreateScope();
            _context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
        }

        private async Task Save(CancellationToken token)
        {
            _counter++;

            await _context.SaveChangesAsync(token);

            if (_counter == 100) // recycle the DbContext
            {
                _scope.Dispose();
                _scope = _provider.CreateScope();
                _context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
                _counter = 0;
            }
        }

        public async Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token)
        {
            var deletedItems = _context.PackageDetailsCatalogLeafs.Where(p =>
                p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion);

            var deleted = await deletedItems.ToListAsync(token);
            if (deleted.Count == 0)
            {
                _logger.LogDebug("Deleted event but not found with: {Id}, {Version}", leaf.PackageId, leaf.PackageVersion);
            }
            else
            {
                if (deleted.Count > 1)
                {
                    _logger.LogError("Expected 1 item but found {count} for {id} and {version}.",
                        deleted.Count,
                        leaf.PackageId,
                        leaf.PackageVersion);
                }

                foreach (var del in deleted)
                {
                    _context.PackageDetailsCatalogLeafs.Remove(del);
                }
                await Save(token);
            }
        }

        public async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
        {
            var exists = await _context.PackageDetailsCatalogLeafs.AnyAsync(
                p => p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion, token);

            if (!exists)
            {
                _context.PackageDetailsCatalogLeafs.Add(leaf);
                await Save(token);
            }
        }
    }
}
