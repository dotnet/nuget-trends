using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

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
                .UseSentry(o => o.Debug = true)
                .UseStartup<Startup>();
    }
}
