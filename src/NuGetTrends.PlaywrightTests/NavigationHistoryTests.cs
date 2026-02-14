using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates browser back/forward navigation works correctly with Blazor's
/// client-side routing. SPA navigation bugs are the #1 migration regression.
/// </summary>
[Collection("Playwright")]
public class NavigationHistoryTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public NavigationHistoryTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task BackButton_FromPackages_ReturnsToHome()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Start at home
            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForWasmInteractivityAsync();

            var homeUrl = page.Url;
            _output.WriteLine($"Home URL: {homeUrl}");

            // Search and select a package
            var searchInput = page.Locator("input.input.is-large");
            await searchInput.FillAsync("sentry");

            await page.WaitForSearchDropdownAsync();
            var dropdown = page.Locator(".autocomplete-dropdown");
            await dropdown.Locator(".autocomplete-option").First.ClickAsync();

            // Wait for packages page
            await page.WaitForURLAsync("**/packages**", new PageWaitForURLOptions
            {
                Timeout = 10_000
            });
            await page.WaitForTimeoutAsync(2_000);

            var packagesUrl = page.Url;
            _output.WriteLine($"Packages URL: {packagesUrl}");
            packagesUrl.Should().Contain("/packages");

            // Press back
            await page.GoBackAsync(new PageGoBackOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(2_000);

            var backUrl = page.Url;
            _output.WriteLine($"After back: {backUrl}");
            backUrl.Should().NotContain("/packages",
                "back button should return to home page");

            // Press forward
            await page.GoForwardAsync(new PageGoForwardOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(2_000);

            var forwardUrl = page.Url;
            _output.WriteLine($"After forward: {forwardUrl}");
            forwardUrl.Should().Contain("/packages",
                "forward button should return to packages page");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
