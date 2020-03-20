using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NuGet.Protocol.Catalog;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public abstract class CatalogLeafProcessor : ICatalogLeafProcessor
    {
        private readonly IServiceProvider _provider;
        private readonly ILogger<CatalogLeafProcessor> _logger;

        private IServiceScope _scope;

        protected CatalogLeafProcessor(
                IServiceProvider provider,
                ILogger<CatalogLeafProcessor> logger)
        {
            _provider = provider;
            _logger = logger;

            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
            Context.ChangeTracker.AutoDetectChangesEnabled = false;
        }

        protected NuGetTrendsContext Context { get; set; }

        protected async Task Save(CancellationToken token)
        {
            try
            {
                await Context.SaveChangesAsync(token);
            }
            finally
            {
                _scope.Dispose();
                _scope = _provider.CreateScope();
                Context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
                Context.ChangeTracker.AutoDetectChangesEnabled = false;
            }
        }

        public abstract Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token);
        public abstract Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token);
    }
}
