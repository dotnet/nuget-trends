using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MemoryStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Polly;
using Polly.Timeout;
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

        // ClickHouse connection string - supports both Aspire service discovery and manual config
        services.AddSingleton<IClickHouseService>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            // Aspire injects connection strings via ConnectionStrings__<name> environment variables
            var connString = config.GetConnectionString("clickhouse")
                ?? config.GetConnectionString("ClickHouse")
                ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
            var logger = sp.GetRequiredService<ILogger<ClickHouseService>>();
            return new ClickHouseService(connString, logger);
        });

        // RabbitMQ connection factory - supports both Aspire service discovery and manual config
        services.AddSingleton<IConnectionFactory>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();

            // Check for Aspire-injected connection string first (format: amqp://user:pass@host:port)
            var connectionString = config.GetConnectionString("rabbitmq");
            if (!string.IsNullOrEmpty(connectionString))
            {
                var factory = new ConnectionFactory
                {
                    Uri = new Uri(connectionString),
                    // For some reason you have to opt-in to have async code:
                    // If you don't set this, subscribing to Received with AsyncEventingBasicConsumer will silently fail.
                    DispatchConsumersAsync = true
                };
                return factory;
            }

            // Fall back to manual configuration
            var options = sp.GetRequiredService<IOptions<RabbitMqOptions>>().Value;
            return new ConnectionFactory
            {
                HostName = options.Hostname,
                Port = options.Port,
                Password = options.Password,
                UserName = options.Username,
                DispatchConsumersAsync = true
            };
        });

        // PostgreSQL - supports both Aspire service discovery and manual config
        services.AddDbContext<NuGetTrendsContext>((sp, options) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            // Aspire injects as "nugettrends" (database name), fallback to "NuGetTrends" for manual config
            var connString = config.GetConnectionString("nugettrends")
                ?? config.GetNuGetTrendsConnectionString();

            options.UseNpgsql(connString);

            if (hostingEnvironment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
            }
        });

        // TODO: Use Postgres storage instead:
        // Install: Hangfire.PostgreSql
        // Configure: config.UsePostgreSqlStorage(Configuration.GetConnectionString("HangfireConnection")
        services.AddHangfire(config => config
                .UseStorage(new MemoryStorage())
                .UseFilter(new SkipConcurrentExecutionFilter()))
            .AddSentry();
        services.AddHangfireServer();

        // Bind NuGet resilience options from configuration
        var resilienceOptions = new NuGetResilienceOptions();
        configuration.GetSection(NuGetResilienceOptions.SectionName).Bind(resilienceOptions);

        // Configure resilient HttpClient for NuGet API calls
        services.AddHttpClient("nuget")
            .AddResilienceHandler("nuget-resilience", builder =>
            {
                // Retry with exponential backoff
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = resilienceOptions.MaxRetryAttempts,
                    Delay = resilienceOptions.RetryDelay,
                    UseJitter = true,
                    BackoffType = DelayBackoffType.Exponential,
                    ShouldHandle = static args => ValueTask.FromResult(
                        args.Outcome.Exception is HttpRequestException or TaskCanceledException or TimeoutRejectedException
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
                        args.Outcome.Exception is HttpRequestException or TaskCanceledException or TimeoutRejectedException
                        || args.Outcome.Result?.StatusCode is
                            System.Net.HttpStatusCode.RequestTimeout or
                            System.Net.HttpStatusCode.TooManyRequests or
                            >= System.Net.HttpStatusCode.InternalServerError)
                });

                // Per-attempt timeout: retries use extended timeout
                var baseTimeout = resilienceOptions.Timeout;
                var retryTimeout = resilienceOptions.RetryTimeout;
                builder.AddTimeout(new HttpTimeoutStrategyOptions
                {
                    Timeout = baseTimeout,
                    TimeoutGenerator = args =>
                    {
                        // First attempt (0) uses base timeout, retries use extended timeout
                        var timeout = args.Context.Properties.TryGetValue(new ResiliencePropertyKey<int>("Polly.Retry.AttemptNumber"), out var attempt) && attempt > 0
                            ? retryTimeout
                            : baseTimeout;
                        return ValueTask.FromResult(timeout);
                    }
                });
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
