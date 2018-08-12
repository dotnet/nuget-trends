using Microsoft.EntityFrameworkCore;

namespace NuGetTrends.Api
{
    public class NuGetMustHavesContext : DbContext
    {
        public NuGetMustHavesContext(DbContextOptions<NuGetMustHavesContext> options)
            : base(options)
        { }

        public DbSet<NPackage> NPackages { get; set; }
    }

    public class NPackage
    {
        public int Id { get; set; }
        public string PackageId { get; set; }
        public string Version { get; set; }
        public int DownloadCount { get; set; }
        public string GalleryDetailsUrl { get; set; }
    }
}
