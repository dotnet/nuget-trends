using ApexCharts;

namespace NuGetTrends.Web.Client.Shared;

/// <summary>
/// Shared ApexCharts option fragments for the line charts (package download trends and
/// framework adoption). Both charts build their options from these so they render with the
/// same look and, in particular, the same stroke curve. Defining these in one place is what
/// keeps the two charts from drifting apart again.
/// </summary>
public static class LineChartDefaults
{
    private const string LabelFontSize = "13px";

    public static string GridColor(bool isDark) => isDark ? "rgba(255, 255, 255, 0.1)" : "rgba(0, 0, 0, 0.1)";

    public static string TextColor(bool isDark) => isDark ? "#a0a0a0" : "#666666";

    public static Chart Chart(bool isDark) => new()
    {
        Toolbar = new Toolbar { Show = false },
        Zoom = new Zoom { Enabled = false },
        Animations = new Animations
        {
            Enabled = true,
            Easing = Easing.Easeinout,
            Speed = 400,
            AnimateGradually = new AnimateGradually { Enabled = false },
        },
        Background = "transparent",
        FontFamily = "inherit",
        ForeColor = TextColor(isDark),
    };

    /// <param name="size">Marker radius. Use 0 to hide markers on dense series; they still appear on hover.</param>
    public static Markers Markers(int size) => new()
    {
        Size = size,
        Hover = new MarkersHover { SizeOffset = size > 0 ? 3 : 5 },
        StrokeWidth = new Size(size > 0 ? 1 : 0),
        StrokeColors = new Color("#fff"),
        Shape = MarkerShape.Circle,
    };

    /// <summary>
    /// Monotone cubic interpolation never overshoots between data points, so cumulative
    /// series never appear to dip or spike where the data does not (#461).
    /// </summary>
    public static Stroke Stroke() => new()
    {
        Curve = Curve.MonotoneCubic,
        Width = 2,
    };

    public static Grid Grid(bool isDark) => new()
    {
        BorderColor = GridColor(isDark),
    };

    public static AxisLabelStyle AxisLabelStyle(bool isDark) => new()
    {
        Colors = new Color(TextColor(isDark)),
        FontSize = LabelFontSize,
    };

    public static AxisBorder AxisBorder(bool isDark) => new() { Color = GridColor(isDark) };

    public static AxisTicks AxisTicks(bool isDark) => new() { Color = GridColor(isDark) };
}
