using Sentry.Extensions.Logging;
using Serilog;
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
                    .UseSentry(o =>
                    {
                        // Disable stacktrace attachment for log events - they only contain
                        // system frames when logged from libraries like Polly/EF Core
                        o.AttachStacktrace = false;
                        o.SetBeforeSend(e =>
                        {
                            if (e.Message?.Formatted is {} msg && msg.Contains(
                                    "An error occurred using the connection to database '\"nugettrends\"' on server"))
                            {
                                e.Fingerprint = new []{msg};
                            }
                            return e;
                        });
                        o.CaptureFailedRequests = true;
                        o.AddExceptionFilterForType<OperationCanceledException>();
                        o.AddExceptionFilterForType<ConcurrentExecutionSkippedException>();
                        o.AddLogEntryFilter((category, level, eventId, exception)
                            => eventId.ToString() ==
                               "Microsoft.EntityFrameworkCore.Infrastructure.SensitiveDataLoggingEnabledWarning"
                               && string.Equals(
                                   category,
                                   "Microsoft.EntityFrameworkCore.Model.Validation",
                                   StringComparison.Ordinal));
                        // Filter out EF Core transaction and command error logs - these are duplicates
                        // of the actual DbUpdateException which is captured separately.
                        // TransactionError (20205) and CommandError (20102) are logged by EF Core
                        // but don't include the exception, making them noise in Sentry.
                        o.AddLogEntryFilter((category, _, eventId, _)
                            => eventId.Id == 20205 && string.Equals(
                                   category,
                                   "Microsoft.EntityFrameworkCore.Database.Transaction",
                                   StringComparison.Ordinal));
                        o.AddLogEntryFilter((category, _, eventId, _)
                            => eventId.Id == 20102 && string.Equals(
                                   category,
                                   "Microsoft.EntityFrameworkCore.Database.Command",
                                   StringComparison.Ordinal));
                    })
                    .UseStartup<Startup>();
            });
}
