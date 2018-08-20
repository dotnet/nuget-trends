using System;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Catalog.Models;

namespace NuGetTrends.Data
{
    public class NuGetTrendsContext : BasePostgresContext
    {
        public NuGetTrendsContext(DbContextOptions<NuGetTrendsContext> options)
            : base(options)
        { }

        public DbSet<PackageRegistration> PackageRegistrations { get; set; }
        public DbSet<PackageDetailsCatalogLeaf> PackageDetailsCatalogLeafs { get; set; }
        public DbSet<Cursor> Cursors { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .Entity<Cursor>()
                .HasData(new Cursor
                {
                    Id = "Catalog",
                    Value = DateTimeOffset.MinValue
                });

            modelBuilder
                .Entity<PackageDetailsCatalogLeaf>()
                .HasIndex(p => new { p.PackageId, p.PackageVersion })
                .IsUnique();

            modelBuilder
                .Entity<PackageDetailsCatalogLeaf>()
                .HasMany(p => p.DependencyGroups)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<PackageDependencyGroup>()
                .HasMany(p => p.Dependencies)
                .WithOne()
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<PackageDependency>()
                .HasIndex(p => p.DependencyId);
        }
    }

    public class PackageRegistration
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
    }

    public class Cursor
    {
        public string Id { get; set; }
        // https://github.com/npgsql/Npgsql.EntityFrameworkCore.PostgreSQL/issues/303
        public DateTimeOffset Value { get; set; }
    }
}
