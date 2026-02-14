using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Ensures all JS and CSS resources are served from our own origin.
/// No external CDN requests should be made — all dependencies must be vendored.
/// </summary>
[Collection("Playwright")]
public class ExternalDependencyTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ExternalDependencyTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/packages/Sentry", "Package detail")]
    public async Task Page_ShouldNotLoadExternalJsOrCss(string path, string label)
    {
        var page = await _fixture.NewPageAsync();
        var serverUri = new Uri(_fixture.ServerUrl);
        var externalRequests = new List<string>();

        try
        {
            page.Request += (_, request) =>
            {
                var url = new Uri(request.Url);
                var isExternal = url.Host != serverUri.Host || url.Port != serverUri.Port;
                var isJsOrCss = request.ResourceType is "stylesheet" or "script";

                if (isExternal && isJsOrCss)
                {
                    externalRequests.Add($"[{request.ResourceType}] {request.Url}");
                    _output.WriteLine($"[external {request.ResourceType}] {request.Url}");
                }
            };

            var url = _fixture.ServerUrl + path;
            _output.WriteLine($"Loading {label} page: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for WASM hydration to catch any lazy-loaded scripts
            await page.WaitForFunctionAsync(
                "() => typeof Blazor !== 'undefined'",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });

            externalRequests.Should().BeEmpty(
                $"{label} page should not load JS or CSS from external CDNs — vendor all dependencies locally");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
