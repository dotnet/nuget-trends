using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates key pages render correctly at mobile viewport sizes
/// without JS errors or layout breakage.
/// </summary>
[Collection("Playwright")]
public class MobileViewportTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public MobileViewportTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/packages/Sentry", "Package detail")]
    public async Task MobileViewport_ShouldRenderWithoutErrors(string path, string label)
    {
        var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 667 },
            IsMobile = true,
            UserAgent = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15"
        });
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        var failedRequests = new List<string>();

        try
        {
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                {
                    consoleErrors.Add(msg.Text);
                    _output.WriteLine($"[console error] {msg.Text}");
                }
            };

            page.Response += (_, response) =>
            {
                if (response.Status >= 400)
                {
                    failedRequests.Add($"{response.Status} {response.Url}");
                    _output.WriteLine($"[HTTP {response.Status}] {response.Url}");
                }
            };

            var url = _fixture.ServerUrl + path;
            _output.WriteLine($"Loading {label} at 375x667 mobile viewport: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            await page.WaitForWasmInteractivityAsync();

            // Verify no JS errors or failed requests
            consoleErrors.Should().BeEmpty(
                $"{label} page should have no JS errors at mobile viewport");
            failedRequests.Should().BeEmpty(
                $"{label} page should have no failed requests at mobile viewport");

            // Verify key elements are visible and not overflowing
            var logo = page.Locator("img[alt='NuGet Trends brand logo']").First;
            (await logo.IsVisibleAsync()).Should().BeTrue(
                "logo should be visible on mobile");

            var searchInput = page.Locator("input.input.is-large");
            (await searchInput.IsVisibleAsync()).Should().BeTrue(
                "search input should be visible on mobile");

            // Check that nothing is horizontally overflowing
            // (ApexCharts uses responsive SVG, so chart pages are included)
            var hasOverflow = await page.EvaluateAsync<bool>(
                "document.documentElement.scrollWidth > document.documentElement.clientWidth");
            _output.WriteLine($"Horizontal overflow: {hasOverflow}");
            hasOverflow.Should().BeFalse(
                $"{label} page should not have horizontal overflow at mobile viewport");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task MobileViewport_SearchWorks()
    {
        var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 375, Height = 667 },
            IsMobile = true,
        });
        var page = await context.NewPageAsync();

        try
        {
            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForWasmInteractivityAsync();

            var searchInput = page.Locator("input.input.is-large");
            await searchInput.FillAsync("sentry");

            await page.WaitForSearchDropdownAsync();
            var dropdown = page.Locator(".autocomplete-dropdown");

            var count = await dropdown.Locator(".autocomplete-option").CountAsync();
            _output.WriteLine($"Dropdown options on mobile: {count}");
            count.Should().BeGreaterThan(0, "search dropdown should work on mobile");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }
}
