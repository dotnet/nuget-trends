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
        private readonly IConfiguration _configuration;

        public Program(IConfiguration configuration) => _configuration = configuration;

        public static void Main(string[] args) => CreateHostBuilder(args).Build().Run();

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseStartup<Program>();
                });

        public void ConfigureServices(IServiceCollection services)
            => services
                .AddEntityFrameworkNpgsql()
                .AddDbContext<NuGetTrendsContext>(o => o.UseNpgsql(_configuration.GetConnectionString("NuGetTrends")));

        public void Configure(IApplicationBuilder app) { }
    }
}
