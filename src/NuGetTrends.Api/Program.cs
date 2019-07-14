using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Sentry.Protocol;

namespace NuGetTrends.Api
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseKestrel(c => c.AddServerHeader = false)
                .ConfigureAppConfiguration((b, c) =>
                {
                    if (b.HostingEnvironment.IsDevelopment())
                    {
                        c.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
                    }
                })
                .UseSentry()
                .UseStartup<Startup>();
    }
}
