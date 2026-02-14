using System.Text.RegularExpressions;
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
            // Start at the packages page (which has the search input)
            await page.GotoAsync($"{_fixture.ServerUrl}/packages", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for search input
            var searchInput = page.Locator("input.input.is-large");
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

            var homeUrl = page.Url;
            _output.WriteLine($"Start URL: {homeUrl}");

            // Search and select a package
            await searchInput.FillAsync("sentry");

            var dropdown = page.Locator(".autocomplete-dropdown");
            await dropdown.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000,
            });
            await dropdown.Locator(".autocomplete-option").First.ClickAsync();

            // Wait for navigation to package detail page (/packages?ids=Sentry&months=24)
            await page.WaitForURLAsync(new Regex(@"packages\?ids=Sentry"), new PageWaitForURLOptions
            {
                Timeout = 10_000
            });
            await page.WaitForTimeoutAsync(1_000);

            var detailUrl = page.Url;
            _output.WriteLine($"Detail URL: {detailUrl}");
            detailUrl.Should().Contain("ids=Sentry");

            // Press back — should return to /packages (the search page)
            await page.GoBackAsync(new PageGoBackOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(1_000);

            var backUrl = page.Url;
            _output.WriteLine($"After back: {backUrl}");
            backUrl.Should().NotContain("ids=Sentry",
                "back button should return from detail page");

            // Press forward — should go back to the detail page
            await page.GoForwardAsync(new PageGoForwardOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(1_000);

            var forwardUrl = page.Url;
            _output.WriteLine($"After forward: {forwardUrl}");
            forwardUrl.Should().Contain("ids=Sentry",
                "forward button should return to package detail page");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
