using ApexCharts;
using FluentAssertions;
using NuGetTrends.Web.Client.Shared;
using Xunit;

namespace NuGetTrends.Web.Tests;

public class LineChartDefaultsTests
{
    [Fact]
    public void Stroke_UsesMonotoneCubicCurve()
    {
        // Cumulative download series must never appear to decrease between points (#461).
        LineChartDefaults.Stroke().Curve.Should().Equal(Curve.MonotoneCubic);
    }

    [Fact]
    public void Chart_HidesToolbarAndDisablesZoom()
    {
        var chart = LineChartDefaults.Chart(isDark: false);

        chart.Toolbar!.Show.Should().BeFalse();
        chart.Zoom!.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(4, 3, 1)]
    [InlineData(0, 5, 0)]
    public void Markers_HiddenMarkersGrowMoreOnHover(int size, int expectedHoverOffset, int expectedStrokeWidth)
    {
        var markers = LineChartDefaults.Markers(size);

        markers.Size.Should().Equal(size);
        markers.Hover!.SizeOffset.Should().Be(expectedHoverOffset);
        markers.StrokeWidth.Should().Equal(expectedStrokeWidth);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThemeDependentColors_AreConsistentAcrossFragments(bool isDark)
    {
        LineChartDefaults.Grid(isDark).BorderColor.Should().Be(LineChartDefaults.GridColor(isDark));
        LineChartDefaults.AxisBorder(isDark).Color.Should().Be(LineChartDefaults.GridColor(isDark));
        LineChartDefaults.AxisTicks(isDark).Color.Should().Be(LineChartDefaults.GridColor(isDark));
        LineChartDefaults.Chart(isDark).ForeColor.Should().Be(LineChartDefaults.TextColor(isDark));
    }
}
