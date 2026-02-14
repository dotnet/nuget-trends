using System.Net;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NuGetTrends.Web.Client.Pages;
using NuGetTrends.Web.Client.Services;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class FrameworksPageTests : TestContext
{
    // Raw JSON avoids namespace collision between server and client TfmAdoptionResponse types
    private const string AdoptionJson = """
        {
            "series": [
                { "tfm": "net8.0", "family": ".NET", "dataPoints": [{ "month": "2024-01-01", "cumulativeCount": 5000, "newCount": 500 }] },
                { "tfm": "net6.0", "family": ".NET", "dataPoints": [{ "month": "2024-01-01", "cumulativeCount": 3000, "newCount": 100 }] },
                { "tfm": "net9.0", "family": ".NET", "dataPoints": [{ "month": "2024-01-01", "cumulativeCount": 1000, "newCount": 800 }] },
                { "tfm": "netstandard2.0", "family": ".NET Standard", "dataPoints": [{ "month": "2024-01-01", "cumulativeCount": 4000, "newCount": 50 }] }
            ]
        }
        """;

    private const string AvailableJson = """
        [
            { "family": ".NET", "tfms": ["net6.0", "net8.0", "net9.0"] },
            { "family": ".NET Standard", "tfms": ["netstandard2.0"] }
        ]
        """;

    public FrameworksPageTests()
    {
        Services.AddSingleton(new ThemeState());
        Services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        var handler = new MockHttpHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/api/framework/adoption"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(AdoptionJson, System.Text.Encoding.UTF8, "application/json")
                };
            }
            if (request.RequestUri.AbsolutePath.EndsWith("/api/framework/available"))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(AvailableJson, System.Text.Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost") };
        Services.AddSingleton(httpClient);

        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void WaitForDataLoaded(NavigationManager nav)
    {
        // Data is loaded when UpdateUrl has run, which sets tfms= in the URL
        // Use a simple spin-wait since bUnit's WaitForState needs a rendered component
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!nav.Uri.Contains("tfms=") && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(50);
        }
    }

    [Fact]
    public void DefaultLoad_SetsQueryStringWithSelectedTfms()
    {
        RenderComponent<Frameworks>();

        var nav = Services.GetRequiredService<NavigationManager>();
        WaitForDataLoaded(nav);

        nav.Uri.Should().Contain("tfms=");
    }

    [Fact]
    public void DefaultLoad_OmitsDefaultViewAndTimeFromUrl()
    {
        RenderComponent<Frameworks>();

        var nav = Services.GetRequiredService<NavigationManager>();
        WaitForDataLoaded(nav);

        nav.Uri.Should().NotContain("view=", "default view mode (individual) should be omitted");
        nav.Uri.Should().NotContain("time=", "default time mode (absolute) should be omitted");
    }

    [Fact]
    public void DefaultLoad_SelectsAllTfmsWhenFewerThanTen()
    {
        RenderComponent<Frameworks>();

        var nav = Services.GetRequiredService<NavigationManager>();
        WaitForDataLoaded(nav);

        var uri = new Uri(nav.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var tfms = query.GetValues("tfms");

        // Test data has 4 TFMs, all should be selected (default is top 10)
        tfms.Should().HaveCount(4);
    }

    [Fact]
    public void NavigateWithViewFamily_PreservesViewModeInUrl()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?view=family");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        nav.Uri.Should().Contain("view=family");
    }

    [Fact]
    public void NavigateWithTimeRelative_PreservesTimeModeInUrl()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?time=relative");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        nav.Uri.Should().Contain("time=relative");
    }

    [Fact]
    public void NavigateWithSpecificTfms_SelectsOnlyThoseTfms()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?tfms=net8.0&tfms=net9.0");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        var uri = new Uri(nav.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var tfms = query.GetValues("tfms");

        tfms.Should().BeEquivalentTo(["net8.0", "net9.0"]);
    }

    [Fact]
    public void NavigateWithInvalidTfms_FallsBackToDefaults()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?tfms=invalid1&tfms=invalid2");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        var uri = new Uri(nav.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var tfms = query.GetValues("tfms");

        tfms.Should().HaveCountGreaterThan(0);
        tfms.Should().NotContain("invalid1");
    }

    [Fact]
    public void NavigateWithAllParams_RestoresFullState()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?view=family&time=relative&tfms=net8.0&tfms=netstandard2.0");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        nav.Uri.Should().Contain("view=family");
        nav.Uri.Should().Contain("time=relative");

        var uri = new Uri(nav.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var tfms = query.GetValues("tfms");
        tfms.Should().BeEquivalentTo(["net8.0", "netstandard2.0"]);
    }

    [Fact]
    public void NavigateWithCaseInsensitiveParams_ParsesCorrectly()
    {
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/frameworks?view=FAMILY&time=RELATIVE");

        RenderComponent<Frameworks>();
        WaitForDataLoaded(nav);

        // UpdateUrl normalizes to lowercase
        nav.Uri.Should().Contain("view=family");
        nav.Uri.Should().Contain("time=relative");
    }

    private class MockHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
