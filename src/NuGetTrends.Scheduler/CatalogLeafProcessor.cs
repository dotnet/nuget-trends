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
    
    /// <summary>
    /// PostgreSQL error code for not_null_violation.
    /// See: https://www.postgresql.org/docs/current/errcodes-appendix.html
    /// </summary>
    private const string PostgresNotNullViolationCode = "23502";
    
    /// <summary>
    /// PostgreSQL error code prefix for integrity constraint violations (class 23).
    /// Includes: unique_violation (23505), not_null_violation (23502), foreign_key_violation (23503),
    /// check_violation (23514), exclusion_violation (23P01), etc.
    /// See: https://www.postgresql.org/docs/current/errcodes-appendix.html
    /// </summary>
    private const string PostgresConstraintViolationPrefix = "23";

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
        await ProcessPackageDetailsBatchAsync([leaf], token);
    }

    public async Task ProcessPackageDetailsBatchAsync(IReadOnlyList<PackageDetailsCatalogLeaf> leaves, CancellationToken token)
    {
        if (leaves.Count == 0)
        {
            return;
        }

        // Extract all package IDs from the batch to filter the database query
        var packageIds = leaves.Select(l => l.PackageId).Distinct().ToList();

        // Single query to find which packages already exist in the database
        // We filter by PackageId first (which can use an index), then do the version check in memory
        var existingPackages = await Context.PackageDetailsCatalogLeafs
            .Where(p => packageIds.Contains(p.PackageId))
            .Select(p => new { p.PackageId, p.PackageVersion })
            .ToListAsync(token);

        var existingSet = existingPackages
            .Select(p => new PackageKey(p.PackageId, p.PackageVersion))
            .ToHashSet();

        // Add only leaves that don't already exist
        var newLeaves = leaves
            .Where(l => !existingSet.Contains(new PackageKey(l.PackageId, l.PackageVersion)))
            .ToList();

        if (newLeaves.Count == 0)
        {
            _logger.LogDebug("All {Count} packages in batch already exist, skipping.", leaves.Count);
            return;
        }

        _logger.LogDebug("Adding {NewCount} new packages out of {TotalCount} in batch.", newLeaves.Count, leaves.Count);

        foreach (var leaf in newLeaves)
        {
            if (string.IsNullOrWhiteSpace(leaf.PackageId))
            {
                throw new InvalidOperationException(
                    "PackageId must be set and non-empty before inserting a PackageDetailsCatalogLeaf. " +
                    "The NuGet catalog leaf should always provide a valid PackageId.");
            }

            leaf.PackageIdLowered = leaf.PackageId.ToLowerInvariant();
        }

        Context.PackageDetailsCatalogLeafs.AddRange(newLeaves);

        try
        {
            await Save(token);
        }
        catch (DbUpdateException ex)
        {
            // Only handle constraint violations (23xxx codes).
            // Non-constraint errors (timeouts, connection errors, deadlocks) should fail the job.
            if (!IsConstraintViolationException(ex))
            {
                // Detach entities to prevent cascading failures, then rethrow
                foreach (var leaf in newLeaves)
                {
                    Context.Entry(leaf).State = EntityState.Detached;
                }
                throw;
            }

            // Constraint violation - fall back to individual processing for partial success
            var isDuplicateKey = IsDuplicateKeyException(ex);
            var isNotNull = IsNotNullViolationException(ex);
            
            if (isDuplicateKey)
            {
                _logger.LogDebug("Concurrent insert detected in batch, falling back to individual processing.");
            }
            else if (isNotNull)
            {
                _logger.LogWarning(ex, "NOT NULL constraint violation in batch, falling back to individual processing.");
            }
            else
            {
                _logger.LogWarning(ex, "Database constraint violation in batch, falling back to individual processing.");
            }

            // Detach all entities we tried to add to prevent cascading failures
            foreach (var leaf in newLeaves)
            {
                Context.Entry(leaf).State = EntityState.Detached;
            }

            // Process each leaf individually to handle partial success
            foreach (var leaf in newLeaves)
            {
                await ProcessPackageDetailsIndividualAsync(leaf, token);
            }
        }
    }

    private async Task ProcessPackageDetailsIndividualAsync(PackageDetailsCatalogLeaf leaf, CancellationToken token)
    {
        var exists = await Context.PackageDetailsCatalogLeafs.AnyAsync(
            p => p.PackageId == leaf.PackageId && p.PackageVersion == leaf.PackageVersion, token);

        if (!exists)
        {
            if (string.IsNullOrWhiteSpace(leaf.PackageId))
            {
                throw new InvalidOperationException(
                    "PackageId must be set and non-empty before inserting a PackageDetailsCatalogLeaf. " +
                    "The NuGet catalog leaf should always provide a valid PackageId.");
            }

            leaf.PackageIdLowered = leaf.PackageId.ToLowerInvariant();
            
            Context.PackageDetailsCatalogLeafs.Add(leaf);
            try
            {
                await Save(token);
            }
            catch (DbUpdateException ex)
            {
                // Detach entity to prevent cascading failures
                Context.Entry(leaf).State = EntityState.Detached;

                if (IsDuplicateKeyException(ex))
                {
                    // Race condition: another process inserted the same package between our AnyAsync check
                    // and SaveChangesAsync. This is harmless - the package exists, which is what we wanted.
                    _logger.LogDebug(
                        "Package {PackageId} v{PackageVersion} already exists (concurrent insert), skipping.",
                        leaf.PackageId,
                        leaf.PackageVersion);
                }
                else if (IsNotNullViolationException(ex))
                {
                    _logger.LogError(ex,
                        "NOT NULL constraint violation for package {PackageId} v{PackageVersion}. " +
                        "PackageIdLowered={PackageIdLowered}",
                        leaf.PackageId,
                        leaf.PackageVersion,
                        leaf.PackageIdLowered);
                }
                else if (IsConstraintViolationException(ex))
                {
                    // Other constraint violations (foreign key, check constraint, etc.)
                    _logger.LogError(ex,
                        "Database constraint violation for package {PackageId} v{PackageVersion}",
                        leaf.PackageId,
                        leaf.PackageVersion);
                }
                else
                {
                    // Non-constraint errors (timeouts, connection errors, deadlocks) should fail the job
                    throw;
                }
            }
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresUniqueViolationCode };
    }

    private static bool IsNotNullViolationException(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException { SqlState: PostgresNotNullViolationCode };
    }

    private static bool IsConstraintViolationException(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pgEx && 
               pgEx.SqlState?.StartsWith(PostgresConstraintViolationPrefix, StringComparison.Ordinal) == true;
    }

    /// <summary>
    /// Case-insensitive package key for efficient lookup in HashSet.
    /// </summary>
    private readonly record struct PackageKey
    {
        public string PackageId { get; }
        public string PackageVersion { get; }

        public PackageKey(string? packageId, string? packageVersion)
        {
            PackageId = packageId ?? "";
            PackageVersion = packageVersion ?? "";
        }

        public bool Equals(PackageKey other) =>
            StringComparer.OrdinalIgnoreCase.Equals(PackageId, other.PackageId) &&
            StringComparer.OrdinalIgnoreCase.Equals(PackageVersion, other.PackageVersion);

        public override int GetHashCode() =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(PackageId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(PackageVersion));
    }
}
