using NuGetTrends.Web.Client.Models;

namespace NuGetTrends.Web.Client.Services;

/// <summary>
/// Manages theme state and preferences.
/// Works with JS interop on the client side for localStorage and CSS class management.
/// </summary>
public class ThemeState
{
    private ThemePreference _preference = ThemePreference.System;
    private bool _systemPrefersDark;

    /// <summary>
    /// Current theme preference (system/light/dark).
    /// </summary>
    public ThemePreference Preference => _preference;

    /// <summary>
    /// Resolved theme based on preference and system settings.
    /// </summary>
    public string ResolvedTheme => _preference switch
    {
        ThemePreference.Light => "light",
        ThemePreference.Dark => "dark",
        _ => _systemPrefersDark ? "dark" : "light"
    };

    /// <summary>
    /// Whether the current resolved theme is dark.
    /// </summary>
    public bool IsDark => ResolvedTheme == "dark";

    /// <summary>
    /// Event raised when the theme changes.
    /// </summary>
    public event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Gets the icon class for the current preference.
    /// </summary>
    public string CurrentIcon => _preference switch
    {
        ThemePreference.System => "fa-desktop",
        ThemePreference.Light => "fa-sun-o",
        ThemePreference.Dark => "fa-moon-o",
        _ => "fa-desktop"
    };

    /// <summary>
    /// Gets the tooltip text for the current preference.
    /// </summary>
    public string Tooltip => _preference switch
    {
        ThemePreference.System => "Theme: System (click to change)",
        ThemePreference.Light => "Theme: Light (click to change)",
        ThemePreference.Dark => "Theme: Dark (click to change)",
        _ => "Theme: System (click to change)"
    };

    /// <summary>
    /// Sets the theme preference.
    /// </summary>
    public void SetPreference(ThemePreference preference)
    {
        if (_preference != preference)
        {
            _preference = preference;
            ThemeChanged?.Invoke(this, ResolvedTheme);
        }
    }

    /// <summary>
    /// Updates the system theme preference.
    /// </summary>
    public void SetSystemPreference(bool prefersDark)
    {
        if (_systemPrefersDark != prefersDark)
        {
            _systemPrefersDark = prefersDark;
            if (_preference == ThemePreference.System)
            {
                ThemeChanged?.Invoke(this, ResolvedTheme);
            }
        }
    }

    /// <summary>
    /// Cycles through theme preferences: system -> light -> dark -> system.
    /// </summary>
    public void CycleTheme()
    {
        var next = _preference switch
        {
            ThemePreference.System => ThemePreference.Light,
            ThemePreference.Light => ThemePreference.Dark,
            ThemePreference.Dark => ThemePreference.System,
            _ => ThemePreference.System
        };
        SetPreference(next);
    }

    /// <summary>
    /// Loads the preference from a stored string value.
    /// </summary>
    public void LoadPreference(string? storedValue)
    {
        _preference = storedValue?.ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            _ => ThemePreference.System
        };
    }

    /// <summary>
    /// Gets the string value for storage.
    /// </summary>
    public string GetPreferenceString() => _preference.ToString().ToLowerInvariant();
}
