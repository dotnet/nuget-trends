using System.Runtime.CompilerServices;
using Blazored.Toast;
using ClickHouse.Driver.ADO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;
using Testcontainers.ClickHouse;
using Testcontainers.PostgreSql;
using Xunit;

namespace NuGetTrends.PlaywrightTests.Infrastructure;

/// <summary>
/// Shared fixture that manages:
/// - PostgreSQL + ClickHouse Testcontainers
/// - ASP.NET Core Kestrel host on a real TCP port
/// - Seed data for the "Sentry" package
/// - Playwright Chromium browser instance
/// </summary>
public class PlaywrightFixture : IAsyncLifetime
{
    private const string ClickHouseImage = "clickhouse/clickhouse-server:25.11.5";
    private const string ClickHouseDatabase = "nugettrends";
    private const string ClickHouseUser = "clickhouse";
    private const string ClickHousePass = "clickhouse";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    private readonly ClickHouseContainer _clickHouse = new ClickHouseBuilder()
        .WithImage(ClickHouseImage)
        .WithUsername(ClickHouseUser)
        .WithPassword(ClickHousePass)
        .Build();

    private WebApplication? _app;

    public string ServerUrl { get; private set; } = null!;
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    private string ClickHouseConnectionString =>
        $"Host={_clickHouse.Hostname};Port={_clickHouse.GetMappedPublicPort(8123)};Database={ClickHouseDatabase};Username={ClickHouseUser};Password={ClickHousePass}";

    public async Task InitializeAsync()
    {
        // 1. Start containers in parallel
        await Task.WhenAll(
            _postgres.StartAsync(),
            _clickHouse.StartAsync());

        // 2. Run ClickHouse migrations
        await ApplyClickHouseMigrationsAsync();

        // 3. Start the real web application on a random Kestrel port.
        //    We reuse the app's own Program.cs startup by setting env vars
        //    that the app reads for connection strings, then building as normal.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://127.0.0.1:0");
        Environment.SetEnvironmentVariable("ConnectionStrings__NuGetTrends", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__nugettrends", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("ConnectionStrings__clickhouse", ClickHouseConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__ClickHouse", ClickHouseConnectionString);
        Environment.SetEnvironmentVariable("Sentry__Dsn", "");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = GetWebProjectPath(),
            WebRootPath = Path.Combine(GetWebProjectPath(), "wwwroot"),
            // Must be "Development" so StaticWebAssetsLoader runs and serves _framework/blazor.web.js
            EnvironmentName = "Development",
            // Must match the Web project name so the static web assets manifest is found
            ApplicationName = "NuGetTrends.Web",
        });

        // Copy the exact same service configuration from the real app's Program.cs
        ConfigureServices(builder);

        _app = builder.Build();
        ConfigureMiddleware(_app);

        await _app.StartAsync();

        // Capture the assigned port
        ServerUrl = _app.Urls.First();

        // 4. Run EF migrations and seed data
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NuGetTrendsContext>();
            await db.Database.MigrateAsync();
            DevelopmentDataSeeder.SeedPostgresIfEmpty(db);
        }

        var chService = _app.Services.GetRequiredService<IClickHouseService>();
        await DevelopmentDataSeeder.SeedClickHouseIfEmptyAsync(chService);

        // 5. Install and launch Playwright
        Microsoft.Playwright.Program.Main(["install", "chromium"]);
        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.CloseAsync();
        Playwright?.Dispose();
        if (_app != null) await _app.StopAsync();
        _app?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        await Task.WhenAll(
            _postgres.DisposeAsync().AsTask(),
            _clickHouse.DisposeAsync().AsTask());
    }

    public async Task<IPage> NewPageAsync(Action<string>? log = null)
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        if (log != null)
            page.Console += (_, msg) => log($"[browser {msg.Type}] {msg.Text}");
        return page;
    }

    /// <summary>
    /// Mirrors the service configuration from the real Program.cs,
    /// but with test-specific overrides (no Sentry, Testcontainer DBs).
    /// </summary>
    private void ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddRazorComponents()
            .AddInteractiveWebAssemblyComponents();

        builder.Services.AddBlazoredToast();
        builder.Services.AddScoped<NuGetTrends.Web.Client.Services.LoadingState>();
        builder.Services.AddScoped<NuGetTrends.Web.Client.Services.PackageState>();
        builder.Services.AddScoped<NuGetTrends.Web.Client.Services.ThemeState>();

        builder.Services.AddScoped(sp =>
        {
            var nav = sp.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
            return new HttpClient { BaseAddress = new Uri(nav.BaseUri) };
        });

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(NuGetTrends.Web.PackageController).Assembly);

        builder.Services.AddDbContext<NuGetTrendsContext>(o =>
            o.UseNpgsql(_postgres.GetConnectionString()));

        var connInfo = ClickHouseConnectionInfo.Parse(ClickHouseConnectionString);
        builder.Services.AddSingleton(connInfo);
        builder.Services.AddSingleton<IClickHouseService>(_ =>
            new ClickHouseService(ClickHouseConnectionString,
                NullLogger<ClickHouseService>.Instance, connInfo));

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<NuGetTrends.Web.ITrendingPackagesCache, NuGetTrends.Web.TrendingPackagesCache>();
    }

    private static void ConfigureMiddleware(WebApplication app)
    {
        app.UseStaticFiles();
        app.MapStaticAssets();
        app.UseRouting();
        app.UseAntiforgery();
        app.MapControllers();
        app.MapRazorComponents<NuGetTrends.Web.Components.App>()
            .AddInteractiveWebAssemblyRenderMode()
            .AddAdditionalAssemblies(typeof(NuGetTrends.Web.Client._Imports).Assembly);
    }

    private static string GetWebProjectPath([CallerFilePath] string callerFilePath = "")
    {
        var dir = Path.GetDirectoryName(callerFilePath)!;
        return Path.GetFullPath(Path.Combine(dir, "..", "..", "NuGetTrends.Web"));
    }

    private async Task ApplyClickHouseMigrationsAsync()
    {
        var adminConnStr =
            $"Host={_clickHouse.Hostname};Port={_clickHouse.GetMappedPublicPort(8123)};Username={ClickHouseUser};Password={ClickHousePass}";

        await using var conn = new ClickHouseConnection(adminConnStr);
        await conn.OpenAsync();

        foreach (var script in GetClickHouseMigrationScripts())
        {
            foreach (var stmt in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (string.IsNullOrWhiteSpace(stmt)) continue;
                var lines = stmt.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (!lines.Any(l => { var t = l.Trim(); return !string.IsNullOrEmpty(t) && !t.StartsWith("--"); }))
                    continue;

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = stmt;
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }

    private static List<string> GetClickHouseMigrationScripts([CallerFilePath] string callerFilePath = "")
    {
        var dir = Path.GetDirectoryName(callerFilePath)!;
        var migrationsDir = Path.GetFullPath(
            Path.Combine(dir, "..", "..", "NuGetTrends.Data", "ClickHouse", "migrations"));

        return Directory.GetFiles(migrationsDir, "*.sql")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(File.ReadAllText)
            .ToList();
    }
}

[CollectionDefinition("Playwright")]
public class PlaywrightCollection : ICollectionFixture<PlaywrightFixture> { }
