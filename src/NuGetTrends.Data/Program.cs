using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace NuGetTrends.Data
{
    public class Program
    {
        public IConfiguration Configuration { get; }

        public Program(IConfiguration configuration) => Configuration = configuration;

        public static void Main(string[] args) => BuildWebHost(args).Run();

        public static IWebHost BuildWebHost(string[] args)
            => WebHost.CreateDefaultBuilder(args)
                .UseStartup<Program>()
                .Build();

        public void ConfigureServices(IServiceCollection services)
            => services
                .AddEntityFrameworkNpgsql()
                .AddDbContext<NuGetTrendsContext>(o => o.UseNpgsql(Configuration.GetConnectionString("NuGetTrends")));

        public void Configure(IApplicationBuilder app) { }
    }
}
