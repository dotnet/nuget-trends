using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Polly;
using RabbitMQ.Client;
using Sentry.Extensibility;

namespace NuGetTrends.Scheduler;

public class Startup(
    IConfiguration configuration,
    IWebHostEnvironment hostingEnvironment)
{
    public void ConfigureServices(IServiceCollection services)
    {
        // NuGet API availability tracking - shared state for all jobs and workers
        services.AddSingleton<NuGetAvailabilityState>();

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

        // Configure resilient HttpClient for NuGet API calls
        services.AddHttpClient("nuget")
            .AddResilienceHandler("nuget-resilience", builder =>
            {
                // Retry with exponential backoff: 2 attempts total (1 initial + 1 retry)
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 2,
                    Delay = TimeSpan.FromSeconds(2),
                    UseJitter = true,
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TaskCanceledException
                        || args.Outcome.Result?.StatusCode is
                            System.Net.HttpStatusCode.RequestTimeout or
                            System.Net.HttpStatusCode.TooManyRequests or
                            >= System.Net.HttpStatusCode.InternalServerError)
                });

                // Circuit breaker: open after repeated failures
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    MinimumThroughput = 3,
                    BreakDuration = TimeSpan.FromSeconds(30),
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TaskCanceledException
                        || args.Outcome.Result?.StatusCode is
                            System.Net.HttpStatusCode.RequestTimeout or
                            System.Net.HttpStatusCode.TooManyRequests or
                            >= System.Net.HttpStatusCode.InternalServerError)
                });

                // Overall timeout for a single request attempt
                builder.AddTimeout(TimeSpan.FromSeconds(30));
            });

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
