using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates that non-existent routes show the NotFound page with a way back home.
/// </summary>
[Collection("Playwright")]
public class NotFoundPageTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public NotFoundPageTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("/this-does-not-exist")]
    [InlineData("/some/random/deep/path")]
    public async Task NonExistentRoute_ShowsNotFoundPage(string path)
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            var url = _fixture.ServerUrl + path;
            _output.WriteLine($"Navigating to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for page to render
            await page.Locator("h1:has-text('Page not found')").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // Verify the "Page not found" heading appears
            var heading = page.Locator("h1:has-text('Page not found')");
            var isVisible = await heading.IsVisibleAsync();
            _output.WriteLine($"'Page not found' visible: {isVisible}");
            isVisible.Should().BeTrue("non-existent routes should show the NotFound page");

            // Verify the Go Home link exists and points to /
            var goHomeLink = page.Locator("a:has-text('Go Home')");
            var href = await goHomeLink.GetAttributeAsync("href");
            _output.WriteLine($"Go Home href: {href}");
            href.Should().Be("/", "Go Home link should point to the root");

            // Click Go Home and verify navigation
            await goHomeLink.ClickAsync();
            await page.WaitForURLAsync($"{_fixture.ServerUrl}/", new PageWaitForURLOptions
            {
                Timeout = 10_000
            });

            var finalUrl = page.Url;
            _output.WriteLine($"After Go Home: {finalUrl}");
            finalUrl.Should().EndWith("/", "clicking Go Home should navigate to the home page");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
