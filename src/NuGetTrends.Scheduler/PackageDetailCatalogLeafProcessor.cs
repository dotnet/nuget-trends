using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NuGet.Protocol.Catalog.Models;

namespace NuGetTrends.Scheduler
{
    public class PackageDetailCatalogLeafProcessor : CatalogLeafProcessor
    {
        private readonly ILogger<CatalogLeafProcessor> _logger;

        public PackageDetailCatalogLeafProcessor(
            IServiceProvider provider,
            ILogger<CatalogLeafProcessor> logger)
            : base(provider, logger)
        {
            _logger = logger;
        }

        public override async Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token)
        {
            var deletedItems = Context.PackageDetailsCatalogLeafs.Where(p =>
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
                    Context.PackageDetailsCatalogLeafs.Remove(del);
                }
                await Save(token);
            }
        }

        public override async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
        {
            var exists = await Context.PackageDetailsCatalogLeafs.AnyAsync(
                p => p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion, token);

            if (!exists)
            {
                Context.PackageDetailsCatalogLeafs.Add(leaf);
                await Save(token);
            }
        }
    }
}
