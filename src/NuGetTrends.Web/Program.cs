using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi.Models;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Serilog;
using Shortr;
using Shortr.Npgsql;
using Swashbuckle.AspNetCore.SwaggerUI;
using SystemEnvironment = System.Environment;

const string Production = nameof(Production);
var environment = SystemEnvironment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Production;

if (environment != Production)
{
    Serilog.Debugging.SelfLog.Enable(Console.Error);
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

try
{
    Log.Information("Starting.");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseConfiguration(configuration)
        .UseSentry(o =>
        {
            o.SetBeforeSend(e =>
            {
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
                 builder =>
                 {
                     builder
                         .AllowAnyOrigin()
                         .AllowAnyMethod()
                         .AllowAnyHeader()
                         .SetPreflightMaxAge(TimeSpan.FromDays(1));
                 });
         });
     }

     builder.Services.AddDbContext<NuGetTrendsContext>(options =>
     {
         var connString = configuration.GetNuGetTrendsConnectionString();
         options.UseNpgsql(connString);
         if (environment != Production)
         {
             options.EnableSensitiveDataLogging();
         }
     });

     builder.Services.AddSingleton<IClickHouseService>(sp =>
     {
         var connString = configuration.GetConnectionString("ClickHouse")
             ?? throw new InvalidOperationException("ClickHouse connection string not configured.");
         var logger = sp.GetRequiredService<ILogger<ClickHouseService>>();
         return new ClickHouseService(connString, logger);
     });

     builder.Services.AddSwaggerGen(c =>
     {
         c.SwaggerDoc("v1", new OpenApiInfo {Title = "NuGet Trends", Version = "v1"});
         var xmlFile = Assembly.GetExecutingAssembly().GetName().Name + ".xml";
         var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
         c.IncludeXmlComments(xmlPath);
     });

     builder.Services.AddShortr();
     if (environment == Production)
     {
         builder.Services.Replace(ServiceDescriptor.Singleton<IShortrStore, NpgsqlShortrStore>());
         builder.Services.AddSingleton(_ => new NpgsqlShortrOptions
         {
             ConnectionString = configuration.GetNuGetTrendsConnectionString()
         });
     }

    var app = builder.Build();

    app.Use(async (context, next) => {
        context.Response.OnStarting(() => {
            // Sentry Browser Profiling
            // https://docs.sentry.io/platforms/javascript/profiling/
            context.Response.Headers.Append("Document-Policy", "js-profiling");
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
            spa.UseProxyToSpaDevelopmentServer("http://localhost:4200");
        }
    });

    app.UseShortr();

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
