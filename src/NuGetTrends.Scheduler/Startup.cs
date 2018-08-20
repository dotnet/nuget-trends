using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NuGetTrends.Data;
using Sentry.Extensibility;

namespace NuGetTrends.Scheduler
{
    public class Startup
    {
        private readonly IHostingEnvironment _hostingEnvironment;
        public IConfiguration Configuration { get; }

        public Startup(
            IConfiguration configuration,
            IHostingEnvironment hostingEnvironment)
        {
            Configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<ISentryEventExceptionProcessor, DbUpdateExceptionProcessor>();

            services
                .AddEntityFrameworkNpgsql()
                .AddDbContext<NuGetTrendsContext>(options =>
                {
                    options.UseNpgsql(Configuration.GetConnectionString("NuGetTrends"));
                    if (_hostingEnvironment.IsDevelopment())
                    {
                        options.EnableSensitiveDataLogging();
                    }
                });

            // TODO: Use Postgres storage instead:
            // Install: Hangfire.PostgreSql
            // Configure: config.UsePostgreSqlStorage(Configuration.GetConnectionString("HangfireConnection")
            services.AddHangfire(config => config.UseStorage(new MemoryStorage()));

            services.AddHttpClient("nuget"); // TODO: typed client? will be shared across all jobs

            services.AddScoped<CatalogCursorStore>();
            services.AddScoped<CatalogLeafProcessor>();
            services.AddScoped<NuGetCatalogImporter>();
        }

        public void Configure(IApplicationBuilder app)
        {
            if (_hostingEnvironment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            // TODO: access control
            app.UseHangfireDashboard("");
            app.UseHangfireServer();

            app.UseHttpsRedirection();

            app.ScheduleJobs();
        }
    }
}
