using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Sentry.Extensions.Logging;

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
                .UseSentry(o =>
                {
                    o.Logging.Filters = o.Logging.Filters.Union(new[]
                    {
                        new DelegateLogEventFilter((category, level, eventId, exception)
                            => eventId.ToString() ==
                               "Microsoft.EntityFrameworkCore.Infrastructure.SensitiveDataLoggingEnabledWarning"
                               && string.Equals(
                                   category,
                                   "Microsoft.EntityFrameworkCore.Model.Validation",
                                   StringComparison.Ordinal))
                    }).ToArray();
                })
                .UseStartup<Startup>();
    }
}
