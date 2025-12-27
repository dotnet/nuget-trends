using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetTrends.Data;
using NuGetTrends.Data.ClickHouse;

namespace NuGetTrends.IntegrationTests.Infrastructure;

/// <summary>
/// Custom WebApplicationFactory for E2E integration tests.
/// Replaces the database connections with the Testcontainer instances (PostgreSQL and ClickHouse).
/// </summary>
public class NuGetTrendsWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly IntegrationTestFixture _fixture;

    public NuGetTrendsWebApplicationFactory(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Override configuration with test-specific values
        // This runs AFTER appsettings.Testing.json is loaded, so our values take precedence
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Disable Sentry by not providing a DSN
                ["Sentry:Dsn"] = "",
                // Override PostgreSQL connection string
                ["ConnectionStrings:NuGetTrends"] = _fixture.PostgresConnectionString,
                // Override ClickHouse connection string
                ["ConnectionStrings:ClickHouse"] = _fixture.ClickHouseConnectionString
            });
        });

        // ConfigureTestServices runs AFTER the app's ConfigureServices, so our registrations win
        builder.ConfigureTestServices(services =>
        {
            // Remove all DbContext-related registrations
            var descriptorsToRemove = services.Where(d =>
                d.ServiceType == typeof(DbContextOptions<NuGetTrendsContext>) ||
                d.ServiceType == typeof(NuGetTrendsContext) ||
                d.ServiceType == typeof(IClickHouseService) ||
                d.ServiceType.FullName?.Contains("DbContextOptions") == true).ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            // Re-add DbContext with Testcontainer connection string
            services.AddDbContext<NuGetTrendsContext>(options =>
            {
                options.UseNpgsql(_fixture.PostgresConnectionString);
            });

            // Re-add ClickHouseService with Testcontainer connection string
            services.AddSingleton<IClickHouseService>(sp =>
                new ClickHouseService(
                    _fixture.ClickHouseConnectionString,
                    NullLogger<ClickHouseService>.Instance));
        });
    }
}
