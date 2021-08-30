using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using RabbitMQ.Client;
using Sentry.Extensibility;

namespace NuGetTrends.Scheduler
{
    public class Startup
    {
        private readonly IWebHostEnvironment _hostingEnvironment;
        private readonly IConfiguration _configuration;

        public Startup(
            IConfiguration configuration,
            IWebHostEnvironment hostingEnvironment)
        {
            _configuration = configuration;
            _hostingEnvironment = hostingEnvironment;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            services.AddHostedService<DailyDownloadWorker>();

            services.AddSingleton<INuGetSearchService, NuGetSearchService>();
            services.AddTransient<ISentryEventExceptionProcessor, DbUpdateExceptionProcessor>();

            services.Configure<DailyDownloadWorkerOptions>(_configuration.GetSection("DailyDownloadWorker"));
            services.Configure<RabbitMqOptions>(_configuration.GetSection("RabbitMq"));
            services.Configure<BackgroundJobServerOptions>(_configuration.GetSection("Hangfire"));

            services.AddSingleton<IConnectionFactory>(c =>
            {
                var options = c.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
                var factory = new ConnectionFactory
                {
                    HostName = options.Hostname,
                    Port = options.Port,
                    Password = options.Password,
                    UserName = options.Username,
                    // For some reason you have to opt-in to have async code:
                    // If you don't set this, subscribing to Received with AsyncEventingBasicConsumer will silently fail.
                    // DefaultConsumer doesn't fire either!
                    DispatchConsumersAsync = true
                };

                return factory;
            });

            services
                .AddDbContext<NuGetTrendsContext>(options =>
                {
                    options
                        .UseNpgsql(_configuration.GetConnectionString("NuGetTrends"));

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

            app.UseHangfireDashboard(
                pathMatch: "",
                options: new DashboardOptions
                {
                    Authorization = new IDashboardAuthorizationFilter[]
                    {
                        // Process not expected to be exposed to the internet
                        new PublicAccessDashboardAuthorizationFilter()
                    }
                });

            var hangfireOptions = app.ApplicationServices.GetRequiredService<IOptions<BackgroundJobServerOptions>>().Value;
            app.UseHangfireServer(hangfireOptions);

            app.ScheduleJobs();
        }
    }
}
