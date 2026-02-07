using System.Runtime.CompilerServices;

namespace NuGetTrends.Data;

/// <summary>
/// Helper methods for Sentry telemetry with query source support.
/// </summary>
public static class TelemetryHelpers
{
    /// <summary>
    /// Converts an absolute file path to a repo-relative path for Sentry query source.
    /// </summary>
    /// <param name="absolutePath">The absolute file path from <see cref="CallerFilePathAttribute"/>.</param>
    /// <returns>A path relative to the repository root (starting with "src/").</returns>
    public static string GetRelativeFilePath(string absolutePath)
    {
        // Find "src/" in the path and return from there
        // e.g., "/Users/bruno/git/nuget-trends/src/NuGetTrends.Data/..." -> "src/NuGetTrends.Data/..."
        const string srcMarker = "src/";
        var normalizedPath = absolutePath.Replace('\\', '/');
        var srcIndex = normalizedPath.IndexOf(srcMarker, StringComparison.OrdinalIgnoreCase);

        if (srcIndex >= 0)
        {
            return normalizedPath[srcIndex..];
        }

        // Fallback: just return the file name
        return Path.GetFileName(absolutePath);
    }

    /// <summary>
    /// Sets query source attributes on a span for Sentry's Queries module.
    /// Uses OpenTelemetry semantic conventions for code location.
    /// </summary>
    /// <typeparam name="T">The type of the class where the span is created (used for namespace).</typeparam>
    /// <param name="span">The span to set attributes on.</param>
    /// <param name="filePath">Automatically populated by the compiler.</param>
    /// <param name="memberName">Automatically populated by the compiler.</param>
    /// <param name="lineNumber">Automatically populated by the compiler.</param>
    public static void SetQuerySource<T>(
        ISpan span,
        [CallerFilePath] string filePath = "",
        [CallerMemberName] string memberName = "",
        [CallerLineNumber] int lineNumber = 0)
    {
        span.SetData("code.filepath", GetRelativeFilePath(filePath));
        span.SetData("code.function", memberName);
        span.SetData("code.lineno", lineNumber);
        span.SetData("code.namespace", typeof(T).FullName);
    }
}
