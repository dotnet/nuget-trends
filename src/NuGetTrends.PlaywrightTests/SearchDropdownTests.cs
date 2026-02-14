using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Playwright-based browser tests for the search autocomplete dropdown.
/// These tests drive a real Chromium browser against a real ASP.NET Core server
/// backed by PostgreSQL and ClickHouse Testcontainers with seeded data.
/// </summary>
[Collection("Playwright")]
public class SearchDropdownTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SearchDropdownTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task HomePage_SearchForSentry_ShowsDropdownWithResult()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Navigate to the packages page (which has the search input)
            var packagesUrl = $"{_fixture.ServerUrl}/packages";
            _output.WriteLine($"Navigating to {packagesUrl}");
            await page.GotoAsync(packagesUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for Blazor WASM to hydrate — the page should become interactive.
            var searchInput = page.Locator("input.input.is-large");
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

            // Log page state before typing
            var title = await page.TitleAsync();
            _output.WriteLine($"Page title: {title}");

            // Type "sentry" in the search box
            _output.WriteLine("Typing 'sentry' in search input...");
            await searchInput.FillAsync("sentry");

            // Wait for the dropdown to appear (debounce is 300ms + API call time)
            var dropdown = page.Locator(".autocomplete-dropdown");
            try
            {
                await dropdown.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 10_000,
                });
            }
            catch (TimeoutException)
            {
                // Take a screenshot for debugging before failing
                var screenshotPath = Path.Combine(
                    Path.GetTempPath(), $"search-dropdown-fail-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                _output.WriteLine($"SCREENSHOT saved to: {screenshotPath}");

                // Log console errors
                var consoleErrors = new List<string>();
                page.Console += (_, msg) =>
                {
                    if (msg.Type == "error") consoleErrors.Add(msg.Text);
                };

                // Log all network requests/responses for debugging
                var apiResponse = await page.EvaluateAsync<string>(
                    "fetch('/api/package/search?q=sentry').then(r => r.text())");
                _output.WriteLine($"Direct fetch result: {apiResponse}");

                // Check if there are any JS errors on page
                var errors = await page.EvaluateAsync<string>(
                    "JSON.stringify(window.__blazorErrors || 'no errors captured')");
                _output.WriteLine($"Blazor errors: {errors}");

                throw new Exception(
                    $"Dropdown did not appear after typing 'sentry'. Screenshot: {screenshotPath}");
            }

            // Verify the dropdown has results
            var options = dropdown.Locator(".autocomplete-option");
            var count = await options.CountAsync();
            _output.WriteLine($"Dropdown visible with {count} option(s)");

            count.Should().BeGreaterThan(0, "dropdown should have at least one result");

            // Verify "Sentry" appears in the results
            var firstOption = options.First;
            var text = await firstOption.InnerTextAsync();
            _output.WriteLine($"First option text: {text}");

            text.Should().Contain("Sentry", "first result should be the Sentry package");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task HomePage_WasmLoads_SearchInputIsInteractive()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Navigate to packages page (which has the search input)
            await page.GotoAsync($"{_fixture.ServerUrl}/packages", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for WASM hydration by checking that the Blazor framework is connected
            _output.WriteLine("Waiting for Blazor WASM initialization...");

            // Wait for the search input to appear (confirms WASM hydration)
            var searchInput = page.Locator("input.input.is-large");
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

            // Check that blazor.web.js was loaded
            var blazorLoaded = await page.EvaluateAsync<bool>(
                "typeof Blazor !== 'undefined'");
            _output.WriteLine($"Blazor global available: {blazorLoaded}");
            await searchInput.FillAsync("test");

            var inputValue = await searchInput.InputValueAsync();
            inputValue.Should().Be("test", "input should accept typed text");

            // Check if the loading spinner appears (indicates Blazor event handling is active)
            // When the search fires, the control div should get the is-loading class
            await page.WaitForTimeoutAsync(500); // wait for debounce + start

            var controlDiv = page.Locator(".control.is-loading");
            var isLoading = await controlDiv.IsVisibleAsync();
            _output.WriteLine($"Loading indicator appeared: {isLoading}");

            // Take screenshot for visual verification
            var screenshotPath = Path.Combine(
                Path.GetTempPath(), $"wasm-loaded-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
            _output.WriteLine($"Screenshot: {screenshotPath}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SearchDropdown_SelectPackage_NavigatesToPackagesPage()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Navigate to packages page (which has the search input)
            await page.GotoAsync($"{_fixture.ServerUrl}/packages", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for search input to appear
            var searchInput = page.Locator("input.input.is-large");
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            await searchInput.FillAsync("sentry");

            // Wait for dropdown
            var dropdown = page.Locator(".autocomplete-dropdown");
            await dropdown.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000,
            });

            // Click the first result
            var firstOption = dropdown.Locator(".autocomplete-option").First;
            await firstOption.ClickAsync();

            // Should navigate to /packages?ids=Sentry&months=24
            await page.WaitForURLAsync(new Regex(@"packages\?ids="), new PageWaitForURLOptions
            {
                Timeout = 10_000
            });

            var url = page.Url;
            _output.WriteLine($"Navigated to: {url}");
            url.Should().Contain("ids=Sentry", "should navigate to packages page with Sentry after selecting a result");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
