using System.Reflection;
using Microsoft.OpenApi.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using NuGetTrends.Web;
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
            // Mark ClickHouse driver frames as not in-app for cleaner stack traces
            o.AddInAppExclude("ClickHouse.Driver");
            o.SetBeforeSend(e =>
            {
                // Ignore SPA default page middleware errors for POST requests
                // The SPA middleware doesn't support POST requests to index.html and this is expected behavior
                // See: https://nugettrends.sentry.io/issues/4968360400/
                // Note: We check SentryExceptions instead of Exception because when exceptions are
                // captured via middleware, the Exception property may be null - the exception data
                // is stored in the SentryExceptions collection instead.
                var firstException = e.SentryExceptions?.FirstOrDefault();
                if (firstException?.Type == "System.InvalidOperationException" &&
                    firstException.Value?.Contains("The SPA default page middleware could not return the default page") == true &&
                    e.Request?.Method == "POST")
                {
                    return null;
                }

                if (e.Message?.Formatted is { } msg && msg.Contains(
                        "An error occurred using the connection to database '\"nugettrends\"' on server"))
                {
                    e.Fingerprint = new[] { msg };
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
    builder.Services.AddControllers();
    builder.Services.AddHttpClient();

     // In production, the Angular files will be served from this directory
     builder.Services.AddSpaStaticFiles(c => c.RootPath = "Portal/dist");

     if (environment != Production)
     {
         // keep cors during development so we can still run the spa on Angular default port (4200)
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
         c.SwaggerDoc("v1", new OpenApiInfo {Title = "NuGet Trends", Version = "v1"});
         var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
         var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
         c.IncludeXmlComments(xmlPath);
     });


    var app = builder.Build();

    // Map Aspire health check endpoints
    app.MapDefaultEndpoints();

    // Get app version from assembly (set via SourceRevisionId at build time)
    var appVersion = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion?.Split('+').LastOrDefault() ?? "unknown";

    app.Use(async (context, next) => {
        context.Response.OnStarting(() => {
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
    if (!app.Environment.IsDevelopment())
    {
        app.UseSpaStaticFiles();
    }

    app.UseRouting();
    app.UseSentryTracing();

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
#pragma warning disable ASP0014 // Suggest using top level route registrations
    app.UseEndpoints(endpoints =>
    {
        endpoints.MapControllers();
    });
#pragma warning restore ASP0014

    app.UseSpa(spa =>
    {
        spa.Options.SourcePath = "Portal";
        if (app.Environment.IsDevelopment())
        {
            // use the external angular CLI server instead
            var spaDevUrl = app.Configuration["SPA_DEV_SERVER_URL"] ?? "http://localhost:4200";
            spa.UseProxyToSpaDevelopmentServer(spaDevUrl);
        }
    });

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
