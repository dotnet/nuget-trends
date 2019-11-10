using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NuGetTrends.Data
{
    public class Program
    {
        public IConfiguration Configuration { get; }

        public Program(IConfiguration configuration) => Configuration = configuration;

        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseStartup<Program>();
                });

        public void ConfigureServices(IServiceCollection services)
            => services
                .AddEntityFrameworkNpgsql()
                .AddDbContext<NuGetTrendsContext>(o => o.UseNpgsql(Configuration.GetConnectionString("NuGetTrends")));

        public void Configure(IApplicationBuilder app) { }
    }
}
