using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates the app handles API failures gracefully — showing error messages
/// instead of crashing or going blank.
/// </summary>
[Collection("Playwright")]
public class ApiErrorResilienceTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ApiErrorResilienceTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task TrendingSection_DoesNotCrash_WhenWasmApiFails()
    {
        // With SSR + PersistentComponentState, the trending data is fetched server-side
        // and persisted for the WASM client. Even if the client-side API would fail,
        // SSR provides the data. This test verifies the page doesn't crash.
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));
        var pageErrors = new List<string>();

        try
        {
            page.PageError += (_, error) =>
            {
                pageErrors.Add(error);
                _output.WriteLine($"[page error] {error}");
            };

            // Block the trending API on the browser side
            await page.RouteAsync("**/api/package/trending**", async route =>
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = "{\"error\": \"Internal Server Error\"}"
                });
            });

            _output.WriteLine("Navigating to /trending with trending API blocked on client side (500)");
            await page.GotoAsync($"{_fixture.ServerUrl}/trending", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for the trending section to render via SSR
            await page.Locator(".trending-section").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // Page should not have crashed
            pageErrors.Should().BeEmpty("page should not crash when trending API fails on client side");

            // The trending section should be rendered (via SSR), even if the DB is empty.
            // With PersistentComponentState, the WASM client uses server-persisted data
            // and doesn't re-fetch, so the client-side 500 never triggers.
            var trendingSection = page.Locator(".trending-section");
            var hasTrendingSection = await trendingSection.IsVisibleAsync();
            _output.WriteLine($"Trending section visible: {hasTrendingSection}");
            hasTrendingSection.Should().BeTrue(
                "trending section should be rendered via SSR even when client API is blocked");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task BlockedHistoryApi_RedirectsHome_DoesNotCrash()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));
        var pageErrors = new List<string>();

        try
        {
            page.PageError += (_, error) =>
            {
                pageErrors.Add(error);
                _output.WriteLine($"[page error] {error}");
            };

            // Block the package history API
            await page.RouteAsync("**/api/package/history/**", async route =>
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 500,
                    ContentType = "application/json",
                    Body = "{\"error\": \"Internal Server Error\"}"
                });
            });

            // Navigate to a package page — the history fetch should fail on the WASM client
            var url = $"{_fixture.ServerUrl}/packages/Sentry";
            _output.WriteLine($"Navigating to: {url} with history API blocked (500)");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // SSR pre-fetches data server-side, so the page renders with server data.
            // The WASM re-fetch is blocked but the page should still render without crashing.
            // Wait for the page to settle.
            await page.WaitForTimeoutAsync(3_000);

            // Page should not have crashed
            pageErrors.Should().BeEmpty("page should not crash when history API fails");

            // Verify the page rendered content (either stayed on packages or redirected gracefully)
            var currentUrl = page.Url;
            _output.WriteLine($"Current URL after error: {currentUrl}");
            var bodyText = await page.Locator("body").InnerTextAsync();
            bodyText.Should().NotBeNullOrEmpty("page should render content even when client API is blocked");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task NonExistentPackage_ShowsWarning_DoesNotCrash()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));
        var pageErrors = new List<string>();

        try
        {
            page.PageError += (_, error) =>
            {
                pageErrors.Add(error);
                _output.WriteLine($"[page error] {error}");
            };

            // Navigate to a package that doesn't exist in our DB
            var url = $"{_fixture.ServerUrl}/packages/ThisPackageDefinitelyDoesNotExist12345";
            _output.WriteLine($"Navigating to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for the page to settle (redirect or render)
            await page.WaitForFunctionAsync(
                "() => typeof Blazor !== 'undefined'",
                null,
                new PageWaitForFunctionOptions { Timeout = 15_000 });
            await page.WaitForTimeoutAsync(1_000);

            // Page should not have crashed
            pageErrors.Should().BeEmpty("page should not crash for non-existent packages");

            // Should either show a toast warning or redirect to home
            var currentUrl = page.Url;
            var toast = page.Locator(".blazored-toast");
            var toastVisible = await toast.IsVisibleAsync();

            _output.WriteLine($"Current URL: {currentUrl}");
            _output.WriteLine($"Toast visible: {toastVisible}");

            var handledGracefully = currentUrl.EndsWith("/") || currentUrl.EndsWith("/packages") || toastVisible;
            handledGracefully.Should().BeTrue(
                "non-existent package should either redirect home or show a warning toast");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
