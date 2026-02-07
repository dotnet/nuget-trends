using Blazored.Toast;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NuGetTrends.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

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

await builder.Build().RunAsync();
