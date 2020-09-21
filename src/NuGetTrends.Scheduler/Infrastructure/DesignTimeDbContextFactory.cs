using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using NuGetTrends.Data;

namespace NuGetTrends.Scheduler.Infrastructure
{
    public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NuGetTrendsContext>
    {
        public NuGetTrendsContext CreateDbContext(string[] args)
        {
            // Load	the	settings from the project which	contains the connection	string
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();

            var builder = new DbContextOptionsBuilder<NuGetTrendsContext>();
            builder
                .UseNpgsql(configuration.GetConnectionString("NuGetTrends"));

            return new NuGetTrendsContext(builder.Options);
        }
    }
}
