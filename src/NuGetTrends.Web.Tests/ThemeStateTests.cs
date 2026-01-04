using Xunit;
using FluentAssertions;
using NuGetTrends.Web.Models;
using NuGetTrends.Web.Services;

namespace NuGetTrends.Web.Tests;

public class ThemeStateTests
{
    [Fact]
    public void Preference_DefaultsToSystem()
    {
        // Arrange & Act
        var state = new ThemeState();

        // Assert
        state.Preference.Should().Be(ThemePreference.System);
    }

    [Fact]
    public void ResolvedTheme_WhenSystemAndLightPreferred_ReturnsLight()
    {
        // Arrange
        var state = new ThemeState();
        state.SetSystemPreference(prefersDark: false);

        // Act & Assert
        state.ResolvedTheme.Should().Be("light");
        state.IsDark.Should().BeFalse();
    }

    [Fact]
    public void ResolvedTheme_WhenSystemAndDarkPreferred_ReturnsDark()
    {
        // Arrange
        var state = new ThemeState();
        state.SetSystemPreference(prefersDark: true);

        // Act & Assert
        state.ResolvedTheme.Should().Be("dark");
        state.IsDark.Should().BeTrue();
    }

    [Fact]
    public void ResolvedTheme_WhenLightPreference_ReturnsLightRegardlessOfSystem()
    {
        // Arrange
        var state = new ThemeState();
        state.SetSystemPreference(prefersDark: true);
        state.SetPreference(ThemePreference.Light);

        // Act & Assert
        state.ResolvedTheme.Should().Be("light");
        state.IsDark.Should().BeFalse();
    }

    [Fact]
    public void ResolvedTheme_WhenDarkPreference_ReturnsDarkRegardlessOfSystem()
    {
        // Arrange
        var state = new ThemeState();
        state.SetSystemPreference(prefersDark: false);
        state.SetPreference(ThemePreference.Dark);

        // Act & Assert
        state.ResolvedTheme.Should().Be("dark");
        state.IsDark.Should().BeTrue();
    }

    [Fact]
    public void SetPreference_RaisesThemeChangedEvent()
    {
        // Arrange
        var state = new ThemeState();
        string? newTheme = null;
        state.ThemeChanged += (_, theme) => newTheme = theme;

        // Act
        state.SetPreference(ThemePreference.Dark);

        // Assert
        newTheme.Should().Be("dark");
    }

    [Fact]
    public void SetPreference_WhenSameValue_DoesNotRaiseEvent()
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(ThemePreference.Light);
        bool eventRaised = false;
        state.ThemeChanged += (_, _) => eventRaised = true;

        // Act
        state.SetPreference(ThemePreference.Light);

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void SetSystemPreference_WhenSystemMode_RaisesThemeChangedEvent()
    {
        // Arrange
        var state = new ThemeState();
        state.SetSystemPreference(prefersDark: false);
        string? newTheme = null;
        state.ThemeChanged += (_, theme) => newTheme = theme;

        // Act
        state.SetSystemPreference(prefersDark: true);

        // Assert
        newTheme.Should().Be("dark");
    }

    [Fact]
    public void SetSystemPreference_WhenNotSystemMode_DoesNotRaiseEvent()
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(ThemePreference.Light);
        bool eventRaised = false;
        state.ThemeChanged += (_, _) => eventRaised = true;

        // Act
        state.SetSystemPreference(prefersDark: true);

        // Assert
        eventRaised.Should().BeFalse();
    }

    [Fact]
    public void CycleTheme_CyclesFromSystemToLight()
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(ThemePreference.System);

        // Act
        state.CycleTheme();

        // Assert
        state.Preference.Should().Be(ThemePreference.Light);
    }

    [Fact]
    public void CycleTheme_CyclesFromLightToDark()
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(ThemePreference.Light);

        // Act
        state.CycleTheme();

        // Assert
        state.Preference.Should().Be(ThemePreference.Dark);
    }

    [Fact]
    public void CycleTheme_CyclesFromDarkToSystem()
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(ThemePreference.Dark);

        // Act
        state.CycleTheme();

        // Assert
        state.Preference.Should().Be(ThemePreference.System);
    }

    [Theory]
    [InlineData("system", ThemePreference.System)]
    [InlineData("SYSTEM", ThemePreference.System)]
    [InlineData("light", ThemePreference.Light)]
    [InlineData("LIGHT", ThemePreference.Light)]
    [InlineData("dark", ThemePreference.Dark)]
    [InlineData("DARK", ThemePreference.Dark)]
    [InlineData(null, ThemePreference.System)]
    [InlineData("", ThemePreference.System)]
    [InlineData("invalid", ThemePreference.System)]
    public void LoadPreference_ParsesStoredValue(string? storedValue, ThemePreference expected)
    {
        // Arrange
        var state = new ThemeState();

        // Act
        state.LoadPreference(storedValue);

        // Assert
        state.Preference.Should().Be(expected);
    }

    [Theory]
    [InlineData(ThemePreference.System, "system")]
    [InlineData(ThemePreference.Light, "light")]
    [InlineData(ThemePreference.Dark, "dark")]
    public void GetPreferenceString_ReturnsLowercaseValue(ThemePreference preference, string expected)
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(preference);

        // Act
        var result = state.GetPreferenceString();

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(ThemePreference.System, "fa-desktop")]
    [InlineData(ThemePreference.Light, "fa-sun-o")]
    [InlineData(ThemePreference.Dark, "fa-moon-o")]
    public void CurrentIcon_ReturnsCorrectIcon(ThemePreference preference, string expected)
    {
        // Arrange
        var state = new ThemeState();
        state.SetPreference(preference);

        // Act
        var result = state.CurrentIcon;

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Tooltip_ContainsPreferenceName()
    {
        // Arrange
        var state = new ThemeState();

        // Act & Assert
        state.SetPreference(ThemePreference.System);
        state.Tooltip.Should().Contain("System");

        state.SetPreference(ThemePreference.Light);
        state.Tooltip.Should().Contain("Light");

        state.SetPreference(ThemePreference.Dark);
        state.Tooltip.Should().Contain("Dark");
    }
}
