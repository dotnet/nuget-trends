using Sentry.Extensions.Logging;
using Serilog;
using Sentry;
using SystemEnvironment = System.Environment;

namespace NuGetTrends.Scheduler;

public class Program
{
    private const string Production = nameof(Production);
    private static readonly string Environment
        = SystemEnvironment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Production;

    public static IConfiguration Configuration { get; private set; } = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{Environment}.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

    public static int Main(string[] args)
    {
        if (Environment != Production)
        {
            Serilog.Debugging.SelfLog.Enable(Console.Error);
        }

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(Configuration)
            .CreateLogger();

        try
        {
            Log.Information("Starting.");

            CreateHostBuilder(args).Build().Run();

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
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder
                    .UseKestrel()
                    .UseConfiguration(Configuration)
                    .UseSentry(o =>
                    {
                        o.CaptureFailedRequests = true;
                        o.AddExceptionFilterForType<OperationCanceledException>();
                        o.AddLogEntryFilter((category, level, eventId, exception)
                            => eventId.ToString() ==
                               "Microsoft.EntityFrameworkCore.Infrastructure.SensitiveDataLoggingEnabledWarning"
                               && string.Equals(
                                   category,
                                   "Microsoft.EntityFrameworkCore.Model.Validation",
                                   StringComparison.Ordinal));
                        if (Environment != Production)
                        {
                            o.EnableSpotlight = true;
                        }
                    })
                    .UseStartup<Startup>();
            });
}
