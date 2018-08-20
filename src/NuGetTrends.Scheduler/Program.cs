using System;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace NuGetTrends.Scheduler
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateWebHostBuilder(args).Build().Run();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
            WebHost.CreateDefaultBuilder(args)
                .UseShutdownTimeout(TimeSpan.FromSeconds(30))
                .ConfigureAppConfiguration((_, c) => c.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true))
                .UseSentry()
                .UseStartup<Startup>();
    }
}
