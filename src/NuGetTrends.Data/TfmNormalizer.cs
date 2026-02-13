using System.Text.RegularExpressions;

namespace NuGetTrends.Data;

/// <summary>
/// Normalizes raw target_framework strings to short-form TFMs and classifies into families.
/// </summary>
public static partial class TfmNormalizer
{
    public record TfmInfo(string ShortName, string Family);

    // Regex for long-form: .NETFramework,Version=v4.5 or .NETFramework4.5
    [GeneratedRegex(@"^\.NETFramework[,\s]*(?:Version=v?)?(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetFrameworkLongFormRegex();

    // Regex for long-form: .NETStandard,Version=v2.0 or .NETStandard2.0
    [GeneratedRegex(@"^\.NETStandard[,\s]*(?:Version=v?)?(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetStandardLongFormRegex();

    // Regex for long-form: .NETCoreApp,Version=v3.1 or .NETCoreApp3.1
    [GeneratedRegex(@"^\.NETCoreApp[,\s]*(?:Version=v?)?(\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetCoreAppLongFormRegex();

    // Regex for short-form: net45, net451, net472 (no dots, 2-3 digits)
    [GeneratedRegex(@"^net(\d{2,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex NetFrameworkShortFormRegex();

    // Regex for netstandard short-form: netstandard1.0, netstandard2.1
    [GeneratedRegex(@"^netstandard(\d+\.\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetStandardShortFormRegex();

    // Regex for netcoreapp short-form: netcoreapp1.0, netcoreapp3.1
    [GeneratedRegex(@"^netcoreapp(\d+\.\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetCoreAppShortFormRegex();

    // Regex for modern .NET short-form: net5.0, net6.0, net8.0, net11.0-preview1
    [GeneratedRegex(@"^net(\d+\.\d+)(?:-.*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex ModernNetShortFormRegex();

    /// <summary>
    /// Normalizes a raw target framework string to a short-form TFM and classifies into a family.
    /// Returns null for unrecognizable frameworks (Xamarin, portable, etc.).
    /// </summary>
    public static TfmInfo? Normalize(string? rawTfm)
    {
        if (string.IsNullOrWhiteSpace(rawTfm))
        {
            return null;
        }

        rawTfm = rawTfm.Trim();

        // Try long-form .NETCoreApp (must check before .NETFramework due to .NET 5+ mapping)
        var match = NetCoreAppLongFormRegex().Match(rawTfm);
        if (match.Success)
        {
            return NormalizeNetCoreAppVersion(match.Groups[1].Value);
        }

        // Try long-form .NETFramework
        match = NetFrameworkLongFormRegex().Match(rawTfm);
        if (match.Success)
        {
            var version = match.Groups[1].Value.Replace(".", "");
            return new TfmInfo($"net{version}", ".NET Framework");
        }

        // Try long-form .NETStandard
        match = NetStandardLongFormRegex().Match(rawTfm);
        if (match.Success)
        {
            return new TfmInfo($"netstandard{match.Groups[1].Value}", ".NET Standard");
        }

        // Try short-form netstandard
        match = NetStandardShortFormRegex().Match(rawTfm);
        if (match.Success)
        {
            return new TfmInfo($"netstandard{match.Groups[1].Value}", ".NET Standard");
        }

        // Try short-form netcoreapp
        match = NetCoreAppShortFormRegex().Match(rawTfm);
        if (match.Success)
        {
            return NormalizeNetCoreAppVersion(match.Groups[1].Value);
        }

        // Try modern .NET short-form (net5.0+, including preview suffixes)
        match = ModernNetShortFormRegex().Match(rawTfm);
        if (match.Success)
        {
            var version = match.Groups[1].Value;
            if (TryParseVersion(version, out var major, out _) && major >= 5)
            {
                return new TfmInfo($"net{version}", ".NET");
            }
        }

        // Try short-form .NET Framework (net45, net472)
        match = NetFrameworkShortFormRegex().Match(rawTfm);
        if (match.Success)
        {
            return new TfmInfo($"net{match.Groups[1].Value}", ".NET Framework");
        }

        // Unrecognizable (Xamarin, portable, UAP, etc.)
        return null;
    }

    private static TfmInfo NormalizeNetCoreAppVersion(string version)
    {
        // .NETCoreApp,Version=v5.0 and above -> net5.0 (".NET")
        // NuGet uses NETCoreApp internally for .NET 5+
        if (TryParseVersion(version, out var major, out _) && major >= 5)
        {
            return new TfmInfo($"net{version}", ".NET");
        }

        return new TfmInfo($"netcoreapp{version}", ".NET Core");
    }

    private static bool TryParseVersion(string version, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        var parts = version.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out major))
        {
            if (parts.Length >= 2)
            {
                int.TryParse(parts[1], out minor);
            }
            return true;
        }
        return false;
    }
}
