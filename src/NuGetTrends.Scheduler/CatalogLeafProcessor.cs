using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Catalog;
using NuGet.Protocol.Catalog.Models;
using Npgsql;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler;

public class CatalogLeafProcessor : ICatalogLeafProcessor
{
    /// <summary>
    /// PostgreSQL error code for unique_violation (duplicate key).
    /// See: https://www.postgresql.org/docs/current/errcodes-appendix.html
    /// </summary>
    private const string PostgresUniqueViolationCode = "23505";

    private readonly IServiceProvider _provider;
    private readonly ILogger<CatalogLeafProcessor> _logger;
    private int _counter;

    private IServiceScope _scope;
    internal NuGetTrendsContext Context;

    public CatalogLeafProcessor(
        IServiceProvider provider,
        ILogger<CatalogLeafProcessor> logger)
    {
        _provider = provider;
        _logger = logger;

        _scope = _provider.CreateScope();
        Context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
    }

    private async Task Save(CancellationToken token)
    {
        await Context.SaveChangesAsync(token);

        if (++_counter == 100) // recycle the DbContext
        {
            _scope.Dispose();
            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
            _counter = 0;
        }
    }

    public async Task ProcessPackageDeleteAsync(PackageDeleteCatalogLeaf leaf, CancellationToken token)
    {
        var deletedItems = Context.PackageDetailsCatalogLeafs.Where(p =>
            p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion);

        var deleted = await deletedItems.ToListAsync(token);
        if (deleted.Count == 0)
        {
            _logger.LogDebug("Deleted event but not found for '{id}' and '{version}'", leaf.PackageId, leaf.PackageVersion);
        }
        else
        {
            if (deleted.Count > 1)
            {
                _logger.LogError("Expected 1 item but found '{count}' for '{id}' and '{version}'.",
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

    public async Task ProcessPackageDetailsAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
    {
        var exists = await Context.PackageDetailsCatalogLeafs.AnyAsync(
            p => p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion, token);

        if (!exists)
        {
            Context.PackageDetailsCatalogLeafs.Add(leaf);
            try
            {
                await Save(token);
            }
            catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
            {
                // Race condition: another process inserted the same package between our AnyAsync check
                // and SaveChangesAsync. This is harmless - the package exists, which is what we wanted.
                // Detach the entity to prevent cascading failures on subsequent saves.
                Context.Entry(leaf).State = EntityState.Detached;

                _logger.LogDebug(
                    "Package {PackageId} v{PackageVersion} already exists (concurrent insert), skipping.",
                    leaf.PackageId,
                    leaf.PackageVersion);
            }
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresUniqueViolationCode };
    }
}
