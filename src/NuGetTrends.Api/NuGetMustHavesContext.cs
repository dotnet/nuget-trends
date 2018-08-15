using System;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace NuGetTrends.Api
{
    public class NuGetMustHavesContext : DbContext
    {
        public NuGetMustHavesContext(DbContextOptions<NuGetMustHavesContext> options)
            : base(options)
        { }

        public DbSet<NPackage> NPackages { get; set; }
        public DbSet<Downloads> Downloads { get; set; }
    }

    public class NPackage
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
        public string Version { get; set; }
        public int DownloadCount { get; set; }
        public string GalleryDetailsUrl { get; set; }
        public string IconUrl { get; set; }
    }

    public class Downloads
    {
        public int Id { get; set; }
        public int Count { get; set; }
        public DateTime Date { get; set; }
        [Column("Package_Id")]
        public int PackageId { get; set; }

    }
}
