using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Regression test: the correct theme class must be present on body
/// before WASM hydrates, preventing a flash of wrong theme.
/// </summary>
[Collection("Playwright")]
public class ThemeFlashTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ThemeFlashTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task DarkPreference_ShouldApplyDarkThemeBeforeWasmHydrates()
    {
        var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            // Set dark preference in localStorage before navigating
            await context.AddInitScriptAsync("""
                window.localStorage.setItem('nuget-trends-theme', 'dark');
            """);

            // Capture the body class as soon as DOM is ready (before WASM)
            string? earlyBodyClass = null;
            page.DOMContentLoaded += async (_, _) =>
            {
                try
                {
                    earlyBodyClass = await page.EvaluateAsync<string>(
                        "document.body.className");
                }
                catch { /* page might navigate away */ }
            };

            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            // Also check immediately after goto returns
            var bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"Body class at DOMContentLoaded: {earlyBodyClass}");
            _output.WriteLine($"Body class after goto: {bodyClass}");

            // The dark-theme class must be present from the inline script,
            // not added later by WASM
            (earlyBodyClass ?? bodyClass).Should().Contain("dark-theme",
                "dark-theme class should be applied by inline script before WASM hydrates");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task LightPreference_ShouldApplyLightThemeBeforeWasmHydrates()
    {
        var context = await _fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        try
        {
            await context.AddInitScriptAsync("""
                window.localStorage.setItem('nuget-trends-theme', 'light');
            """);

            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            var bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"Body class: {bodyClass}");

            bodyClass.Should().Contain("light-theme",
                "light-theme class should be applied by inline script before WASM hydrates");
            bodyClass.Should().NotContain("dark-theme");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }

    [Fact]
    public async Task SystemPreference_DarkMode_ShouldApplyDarkThemeBeforeWasmHydrates()
    {
        // Simulate system dark mode preference with no stored preference
        var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ColorScheme = ColorScheme.Dark
        });
        var page = await context.NewPageAsync();

        try
        {
            // Clear any stored preference so it falls back to system
            await context.AddInitScriptAsync("""
                window.localStorage.removeItem('nuget-trends-theme');
            """);

            await page.GotoAsync(_fixture.ServerUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

            var bodyClass = await page.EvaluateAsync<string>("document.body.className");
            _output.WriteLine($"Body class: {bodyClass}");

            bodyClass.Should().Contain("dark-theme",
                "system dark mode should result in dark-theme class from inline script");
        }
        finally
        {
            await page.CloseAsync();
            await context.CloseAsync();
        }
    }
}
