using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NuGetTrends.Web.Client.Models;
using NuGetTrends.Web.Client.Services;
using NuGetTrends.Web.Client.Shared;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class ThemeToggleTests : TestContext
{
    private readonly ThemeState _themeState = new();

    public ThemeToggleTests()
    {
        Services.AddSingleton(_themeState);
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Render_ShowsSystemIconByDefault()
    {
        var cut = RenderComponent<ThemeToggle>();

        cut.Find("i").ClassList.Should().Contain("fa-desktop");
    }

    [Fact]
    public void Click_CyclesThemeAndSavesPreference()
    {
        var cut = RenderComponent<ThemeToggle>();

        cut.Find("button").Click();

        _themeState.Preference.Should().Be(ThemePreference.Light);
        JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "themeInterop.setPreference" && i.Arguments[0]!.ToString() == "light");
    }

    [Fact]
    public void Click_AppliesThemeViaJsInterop()
    {
        var cut = RenderComponent<ThemeToggle>();

        cut.Find("button").Click();

        JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "themeInterop.applyTheme" && i.Arguments[0]!.ToString() == "light");
    }

    [Fact]
    public void OnSystemPreferenceChanged_WhenSystemMode_AppliesTheme()
    {
        var cut = RenderComponent<ThemeToggle>();

        // Simulate OS switching to dark
        cut.InvokeAsync(() => cut.Instance.OnSystemPreferenceChanged(prefersDark: true));

        JSInterop.Invocations.Should().Contain(i =>
            i.Identifier == "themeInterop.applyTheme" && i.Arguments[0]!.ToString() == "dark");
    }

    [Fact]
    public void OnSystemPreferenceChanged_WhenSystemMode_TogglingBackAppliesLight()
    {
        var cut = RenderComponent<ThemeToggle>();

        // OS goes dark then back to light
        cut.InvokeAsync(() => cut.Instance.OnSystemPreferenceChanged(prefersDark: true));
        cut.InvokeAsync(() => cut.Instance.OnSystemPreferenceChanged(prefersDark: false));

        var applyInvocations = JSInterop.Invocations
            .Where(i => i.Identifier == "themeInterop.applyTheme")
            .Select(i => i.Arguments[0]!.ToString())
            .ToList();

        // Should have applied both dark and light themes, ending on light
        applyInvocations.Should().Contain("dark");
        applyInvocations.Last().Should().Be("light");
    }

    [Fact]
    public void OnSystemPreferenceChanged_WhenManualTheme_StillAppliesManualTheme()
    {
        var cut = RenderComponent<ThemeToggle>();

        // User manually selects Light
        cut.Find("button").Click(); // System -> Light
        _themeState.Preference.Should().Be(ThemePreference.Light);

        // Clear invocations to isolate the system preference change
        var countBefore = JSInterop.Invocations
            .Count(i => i.Identifier == "themeInterop.applyTheme");

        // OS switches to dark, but user chose Light
        cut.InvokeAsync(() => cut.Instance.OnSystemPreferenceChanged(prefersDark: true));

        var applyAfter = JSInterop.Invocations
            .Where(i => i.Identifier == "themeInterop.applyTheme")
            .Select(i => i.Arguments[0]!.ToString())
            .ToList();

        // The last applied theme should still be "light" (the manual preference)
        applyAfter.Last().Should().Be("light");
    }

    [Fact]
    public void FullCycle_SystemToDarkToLightBackToSystem()
    {
        var cut = RenderComponent<ThemeToggle>();

        // System -> Light -> Dark -> System
        cut.Find("button").Click();
        _themeState.Preference.Should().Be(ThemePreference.Light);

        cut.Find("button").Click();
        _themeState.Preference.Should().Be(ThemePreference.Dark);

        cut.Find("button").Click();
        _themeState.Preference.Should().Be(ThemePreference.System);
    }
}
