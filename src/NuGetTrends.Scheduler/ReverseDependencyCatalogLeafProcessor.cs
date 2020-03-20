using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using NuGet.Protocol.Catalog.Models;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler
{
    public class ReverseDependencyCatalogLeafProcessor : CatalogLeafProcessor
    {
        private readonly ILogger<CatalogLeafProcessor> _logger;

        public ReverseDependencyCatalogLeafProcessor(
            IServiceProvider provider,
            ILogger<CatalogLeafProcessor> logger)
            : base(provider, logger) =>
            _logger = logger;

        public override Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token)
            => Task.CompletedTask;

        public override async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
        {
            var dependencies = from dg in leaf.DependencyGroups
                from d in dg.Dependencies
                where d?.DependencyId is {} && d.Range is {}
                select new ReversePackageDependency
                {
                    TargetFramework = dg.TargetFramework ?? string.Empty,
                    PackageId = leaf.PackageId!,
                    PackageVersion = leaf.PackageVersion!,
                    DependencyPackageIdLowered = d.DependencyId!.ToLowerInvariant(),
                    DependencyRange = d.Range!
                };

            try
            {
                Context.ReversePackageDependencies.AddRange(dependencies);
                await Save(token);
            }
            catch (Exception e) when (
                e is DbUpdateException && e.InnerException is PostgresException pex && pex.SqlState == "23505"
                // TODO: Detach all entities as we add them? We don't need entity tracking here.
                || e is InvalidOperationException && e.Message.StartsWith("The instance of entity type 'ReversePackageDependency' cannot be tracked because another instance with the key value"))
            {
                // Good news. We've got that in already...
            }
        }
    }
}
