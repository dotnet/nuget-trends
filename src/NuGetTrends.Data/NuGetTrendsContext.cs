using System;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Catalog.Models;

namespace NuGetTrends.Data
{
    public class PackageDownload
    {
        public string PackageId { get; set; } = null!;
        public string PackageIdLowered { get; set; } = null!;
        public long? LatestDownloadCount { get; set; }
        public DateTime LatestDownloadCountCheckedUtc { get; set; }
        public string? IconUrl { get; set; }
    }

    public class NuGetTrendsContext : BasePostgresContext
    {
        public NuGetTrendsContext(DbContextOptions<NuGetTrendsContext> options)
            : base(options)
        { }

        public DbSet<PackageDetailsCatalogLeaf> PackageDetailsCatalogLeafs { get; set; } = null!;
        public DbSet<Cursor> Cursors { get; set; } = null!;
        public DbSet<DailyDownload> DailyDownloads { get; set; } = null!;
        public DbSet<PackageDownload> PackageDownloads { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Opt-out of the default identity-columns strategy for auto increment
            // TODO: Consider changing and dealing with the migrations later
            modelBuilder.UseSerialColumns();

            // This is still not working in EF 5 (table is still being created), but calling ToView now is worse
            // as it fails in our custom FixSnakeCaseNames since it expects the object to exist in the db.
            // TODO: Will manually remove the table from the migration - Follow GH issues later to see what's new
            modelBuilder.Entity<DailyDownloadResult>().HasNoKey();

            modelBuilder.Entity<DailyDownload>()
                .HasKey(k => new { k.PackageId, k.Date });

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
                .HasIndex(p => p.PackageId);

            modelBuilder
                .Entity<PackageDetailsCatalogLeaf>()
                .HasMany(p => p.DependencyGroups)
                .WithOne().HasConstraintName("fk_package_dependency_group_package_details_catalog_leafs_id") //so it doesn't get truncated
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<PackageDependencyGroup>()
                .HasMany(p => p.Dependencies)
                .WithOne().HasConstraintName("fk_package_dependency_package_dependency_group_id") //so it doesn't get truncated
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder
                .Entity<PackageDependency>()
                .HasIndex(p => p.DependencyId);

            modelBuilder
                .Entity<PackageDownload>()
                .HasKey(c => c.PackageId);

            modelBuilder
                .Entity<PackageDownload>()
                .HasIndex(c => c.PackageIdLowered)
                .IsUnique();

            modelBuilder.Entity<PackageDownload>()
                .Property(b => b.PackageIdLowered)
                .IsRequired();
        }
    }
}
