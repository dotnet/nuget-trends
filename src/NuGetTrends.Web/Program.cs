using System.Reflection;
using Blazored.Toast;
using Microsoft.OpenApi.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Web;
using NuGetTrends.Web.Components;
using NuGetTrends.Web.Client.Services;
using Serilog;
using Swashbuckle.AspNetCore.SwaggerUI;
using SystemEnvironment = System.Environment;

const string Production = nameof(Production);
const string Testing = nameof(Testing);
var environment = SystemEnvironment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Production;

if (environment != Production && environment != Testing)
{
    Serilog.Debugging.SelfLog.Enable(Console.Error);
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// In Testing environment, use a minimal logger without Sentry
Log.Logger = environment == Testing
    ? new LoggerConfiguration()
        .MinimumLevel.Warning()
        .WriteTo.Console()
        .CreateLogger()
    : new LoggerConfiguration()
        .ReadFrom.Configuration(configuration)
        .CreateLogger();

try
{
    Log.Information("Starting.");

    var builder = WebApplication.CreateBuilder(args);

    // Add Aspire service defaults (OpenTelemetry, health checks, service discovery)
    builder.AddServiceDefaults();

    builder.Host.UseSerilog();
    builder.WebHost.UseConfiguration(configuration)
        .UseSentry(o =>
        {
            // Disable stacktrace attachment for log events - they only contain
            // system frames when logged from libraries like Polly/EF Core
            o.AttachStacktrace = false;
            // Mark ClickHouse driver frames as not in-app for cleaner stack traces
            o.AddInAppExclude("ClickHouse.Driver");
            o.SetBeforeSend(e =>
            {
                if (e.Message?.Formatted is { } msg && msg.Contains(
                        "An error occurred using the connection to database '\"nugettrends\"' on server"))
                {
                    e.Fingerprint = [msg];
                }

                return e;
            });
            o.CaptureFailedRequests = true;
            o.TracesSampler = context => context.CustomSamplingContext.TryGetValue("__HttpPath", out var path)
                                         && path is "/t"
                ? 0 // tunneling JS events
                : 1.0;
            o.AddExceptionFilterForType<OperationCanceledException>();
        });

    builder.Services.AddSentryTunneling(); // Add Sentry Tunneling to avoid ad-blockers.

    // Add Blazor services
    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents();

    // Add Blazored Toast
    builder.Services.AddBlazoredToast();

    // Add app state services (scoped for Blazor)
    builder.Services.AddScoped<LoadingState>();
    builder.Services.AddScoped<PackageState>();
    builder.Services.AddScoped<ThemeState>();

    // Add HttpClient for Blazor components to call the API
    builder.Services.AddScoped(sp =>
    {
        var navigationManager = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
        return new HttpClient { BaseAddress = new Uri(navigationManager.BaseUri) };
    });

    builder.Services.AddControllers();
    builder.Services.AddHttpClient();

    if (environment != Production)
    {
        // keep cors during development
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll",
                corsPolicyBuilder =>
                {
                    corsPolicyBuilder
                        .AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .SetPreflightMaxAge(TimeSpan.FromDays(1));
                });
        });
    }

    // Use Aspire's PostgreSQL integration for automatic connection string injection and health checks
    builder.AddNpgsqlDbContext<NuGetTrendsContext>("nugettrends", configureDbContextOptions: options =>
    {
        if (environment != Production)
        {
            options.EnableSensitiveDataLogging();
        }
    });

    // ClickHouse connection - parse connection info once as singleton
    builder.Services.AddSingleton(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("clickhouse")
            ?? config.GetConnectionString("ClickHouse")
            ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
        // Aspire injects endpoint URLs (http://host:port) - normalize to Key=Value format
        connString = ClickHouseConnectionInfo.NormalizeConnectionString(connString);
        return ClickHouseConnectionInfo.Parse(connString);
    });

    builder.Services.AddSingleton<IClickHouseService>(sp =>
    {
        var config = sp.GetRequiredService<IConfiguration>();
        var connString = config.GetConnectionString("clickhouse")
            ?? config.GetConnectionString("ClickHouse")
            ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
        // Aspire injects endpoint URLs (http://host:port) - normalize to Key=Value format
        connString = ClickHouseConnectionInfo.NormalizeConnectionString(connString);
        var logger = sp.GetRequiredService<ILogger<ClickHouseService>>();
        var connectionInfo = sp.GetRequiredService<ClickHouseConnectionInfo>();
        return new ClickHouseService(connString, logger, connectionInfo);
    });

    // Add caching services
    builder.Services.AddMemoryCache();
    builder.Services.AddScoped<ITrendingPackagesCache, TrendingPackagesCache>();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "NuGet Trends", Version = "v1" });
        var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
        {
            c.IncludeXmlComments(xmlPath);
        }
    });


    var app = builder.Build();

    // Map Aspire health check endpoints
    app.MapDefaultEndpoints();

    // Get app version from assembly (set via SourceRevisionId at build time)
    var appVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+').LastOrDefault() ?? "unknown";

    app.Use(async (context, next) =>
    {
        context.Response.OnStarting(() =>
        {
            // Sentry Browser Profiling
            // https://docs.sentry.io/platforms/javascript/profiling/
            context.Response.Headers.Append("Document-Policy", "js-profiling");
            // App version header
            context.Response.Headers.Append("X-Version", appVersion);
            return Task.CompletedTask;
        });
        await next();
    });

    app.UseStaticFiles();

    // Enable WebAssembly debugging in development
    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
    }

    app.UseRouting();
    app.UseSentryTracing();
    app.UseAntiforgery();

    if (app.Environment.IsDevelopment())
    {
        app.UseCors("AllowAll");
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "NuGet Trends");
            c.DocumentTitle = "NuGet Trends API";
            c.DocExpansion(DocExpansion.None);
        });
    }

    // Proxy Sentry events from the frontend to sentry.io
    // https://docs.sentry.io/platforms/javascript/troubleshooting/#using-the-tunnel-option
    // https://docs.sentry.io/platforms/dotnet/guides/aspnetcore/#tunnel
    app.UseSentryTunneling("/t");

    app.UseSwagger();

    app.MapControllers();

    // Map Blazor components
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(NuGetTrends.Web.Client._Imports).Assembly);

    app.Run();

    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

// Required for WebApplicationFactory in integration tests
public partial class Program { }
