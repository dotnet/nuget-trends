using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using RabbitMQ.Client;
using Sentry.Extensibility;

namespace NuGetTrends.Scheduler;

public class Startup(
    IConfiguration configuration,
    IWebHostEnvironment hostingEnvironment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<DailyDownloadWorker>();

        services.AddSingleton<INuGetSearchService, NuGetSearchService>();
        services.AddTransient<ISentryEventExceptionProcessor, DbUpdateExceptionProcessor>();

        services.Configure<DailyDownloadWorkerOptions>(configuration.GetSection("DailyDownloadWorker"));
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.Configure<BackgroundJobServerOptions>(configuration.GetSection("Hangfire"));
        services.AddSingleton<IClickHouseService>(sp =>
        {
            var connString = configuration.GetConnectionString("ClickHouse")
                ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
            var logger = sp.GetRequiredService<ILogger<ClickHouseService>>();
            return new ClickHouseService(connString, logger);
        });

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
                    .UseNpgsql(configuration.GetNuGetTrendsConnectionString());

                if (hostingEnvironment.IsDevelopment())
                {
                    options.EnableSensitiveDataLogging();
                }
            });

        // TODO: Use Postgres storage instead:
        // Install: Hangfire.PostgreSql
        // Configure: config.UsePostgreSqlStorage(Configuration.GetConnectionString("HangfireConnection")
        services.AddHangfire(config => config.UseStorage(new MemoryStorage()))
            .AddSentry();
        services.AddHangfireServer();

        services.AddHttpClient("nuget"); // TODO: typed client? will be shared across all jobs

        services.AddScoped<CatalogCursorStore>();
        services.AddScoped<CatalogLeafProcessor>();
        services.AddScoped<NuGetCatalogImporter>();
    }

    public void Configure(IApplicationBuilder app)
    {
        if (hostingEnvironment.IsDevelopment())
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

        app.ScheduleJobs();
    }
}
