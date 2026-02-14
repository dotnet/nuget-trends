using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Tests that clicking the theme toggle button cycles through themes
/// and the body class updates accordingly.
/// </summary>
[Collection("Playwright")]
public class ThemeToggleTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ThemeToggleTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ThemeToggle_Click_CyclesTheme()
    {
        var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            // Start with light theme
            await context.AddInitScriptAsync("""
                window.localStorage.setItem('nuget-trends-theme', 'light');
            """);

            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for WASM hydration
            await page.WaitForTimeoutAsync(5_000);

            var bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"Initial body class: {bodyClass}");
            bodyClass.Should().Contain("light-theme");

            // Click the theme toggle button
            var toggleButton = page.Locator(".theme-toggle-btn");
            await toggleButton.ClickAsync();
            await page.WaitForTimeoutAsync(500);

            bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"After first click: {bodyClass}");
            bodyClass.Should().Contain("dark-theme", "clicking toggle should switch from light to dark");

            // Click again
            await toggleButton.ClickAsync();
            await page.WaitForTimeoutAsync(500);

            bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"After second click: {bodyClass}");
            // After dark, it should cycle to system (which depends on browser preference)
            // In Playwright headless, no system preference is set, so it defaults to light
            bodyClass.Should().NotContain("dark-theme",
                "clicking toggle again should cycle away from dark theme");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task ThemeToggle_PersistsPreference()
    {
        var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            // Start fresh (no stored preference)
            await context.AddInitScriptAsync("""
                window.localStorage.removeItem('nuget-trends-theme');
            """);

            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(5_000);

            // Click toggle to change theme
            var toggleButton = page.Locator(".theme-toggle-btn");
            await toggleButton.ClickAsync();
            await page.WaitForTimeoutAsync(500);

            // Read the stored preference
            var storedPreference = await page.EvaluateAsync<string?>(
                "window.localStorage.getItem('nuget-trends-theme')");
            _output.WriteLine($"Stored preference after toggle: {storedPreference}");
            storedPreference.Should().NotBeNullOrEmpty(
                "theme preference should be persisted to localStorage after toggling");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }
}
