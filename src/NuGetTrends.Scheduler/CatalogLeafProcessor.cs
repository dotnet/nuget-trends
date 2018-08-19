using System;
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

        private async ValueTask Save(CancellationToken token)
        {
            _counter++;

            if (_counter == 100) // Save and recycle the DbContext
            {
                await _context.SaveChangesAsync(token);
                _scope.Dispose();
                _scope = _provider.CreateScope();
                _context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
                _counter = 0;
            }
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
                await Save(token);
            }
        }

        public async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
        {
            _context.PackageDetailsCatalogLeafs.Add(leaf);
            await Save(token);
        }
    }
}
