using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates that ApexCharts renders with the correct configuration.
/// Catches regressions where chart options (toolbar, zoom, Y-axis formatting)
/// silently revert to defaults — e.g. due to IL trimming stripping serialization metadata.
/// </summary>
[Collection("Playwright")]
public class ChartRenderingTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ChartRenderingTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task Chart_ToolbarAndZoom_ShouldBeHidden()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await NavigateToChartAsync(page);

            // The toolbar should either not exist or be hidden (display:none / visibility:hidden)
            var toolbar = page.Locator(".apexcharts-toolbar");
            var toolbarCount = await toolbar.CountAsync();

            if (toolbarCount > 0)
            {
                var isVisible = await toolbar.IsVisibleAsync();
                _output.WriteLine($"Toolbar exists: {toolbarCount > 0}, visible: {isVisible}");
                isVisible.Should().BeFalse(
                    "chart toolbar should be hidden (Toolbar.Show = false). " +
                    "If this fails in Release/published builds, check that Blazor-ApexCharts " +
                    "is preserved in TrimmerRoots.xml");
            }

            // Zoom buttons should not be visible
            var zoomIn = page.Locator(".apexcharts-zoomin-icon");
            if (await zoomIn.CountAsync() > 0)
            {
                var zoomVisible = await zoomIn.IsVisibleAsync();
                zoomVisible.Should().BeFalse("zoom buttons should be hidden (Zoom.Enabled = false)");
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Chart_YAxisLabels_ShouldUseAbbreviatedFormat()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await NavigateToChartAsync(page);

            var yLabels = await page.Locator(".apexcharts-yaxis-label tspan")
                .AllTextContentsAsync();

            _output.WriteLine($"Y-axis labels: {string.Join(", ", yLabels)}");

            yLabels.Should().NotBeEmpty("chart should have Y-axis labels");

            // Labels should use abbreviated format (K, M, B) not raw numbers
            foreach (var label in yLabels)
            {
                var trimmed = label.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                // Should NOT be a raw large number like "50000000"
                if (long.TryParse(trimmed.Replace(",", ""), out var numericValue) && numericValue >= 10_000)
                {
                    Assert.Fail(
                        $"Y-axis label '{trimmed}' is a raw number >= 10,000. " +
                        "Expected abbreviated format (e.g. '50M', '100K'). " +
                        "This usually means the custom Y-axis formatter was stripped by IL trimming. " +
                        "Check that Blazor-ApexCharts is preserved in TrimmerRoots.xml");
                }
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task Chart_XAxisLabels_ShouldUseShortDateFormat()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await NavigateToChartAsync(page);

            var xLabels = await page.Locator(".apexcharts-xaxis-label tspan")
                .AllTextContentsAsync();

            _output.WriteLine($"X-axis labels: {string.Join(", ", xLabels)}");

            xLabels.Should().NotBeEmpty("chart should have X-axis labels");

            // Labels should NOT be full ISO timestamps like "2025-10-06T00:00:00Z"
            foreach (var label in xLabels)
            {
                var trimmed = label.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                trimmed.Should().NotContain("T00:00:00",
                    $"X-axis label '{trimmed}' looks like an ISO timestamp. " +
                    "Expected short date format (e.g. 'Oct 2025'). " +
                    "This usually means the DatetimeFormatter config was stripped by IL trimming");
            }
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    private async Task NavigateToChartAsync(IPage page)
    {
        var url = $"{_fixture.ServerUrl}/packages?ids=Sentry&months=3";
        _output.WriteLine($"Navigating to: {url}");

        await page.GotoAsync(url, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle
        });

        // Wait for the chart SVG to render
        await page.Locator(".apexcharts-line-series .apexcharts-series").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        // Wait for axis labels to be populated (ApexCharts renders labels after series)
        await page.Locator(".apexcharts-yaxis-label tspan").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5_000 });
        await page.Locator(".apexcharts-xaxis-label tspan").First.WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 5_000 });
    }
}
