using ApexCharts;
using Blazored.Toast;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NuGetTrends.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.UseSentry(options =>
{
    options.Dsn = "https://fa6e5ca9a2e9347861919ad11c3b8d8c@sentry.garcia.in/3";
    options.TracesSampleRate = 1.0;
    options.AddExceptionFilterForType<OperationCanceledException>();
});
builder.Logging.AddSentry(o => o.InitializeSdk = false);

// Loading state (scoped – one per circuit)
builder.Services.AddScoped<LoadingState>();

// HttpClient with LoadingStateHandler
builder.Services.AddScoped<LoadingStateHandler>();
builder.Services.AddScoped(sp =>
{
    var loadingHandler = sp.GetRequiredService<LoadingStateHandler>();
    loadingHandler.InnerHandler = new HttpClientHandler();
    return new HttpClient(loadingHandler)
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    };
});

// App state services
builder.Services.AddScoped<PackageState>();
builder.Services.AddScoped<ThemeState>();

// Blazored Toast
builder.Services.AddBlazoredToast();

// ApexCharts
builder.Services.AddApexCharts();

await builder.Build().RunAsync();
