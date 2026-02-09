using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates that shared URLs (deep links) work correctly when navigated to directly.
/// This exercises the SSR prerender → WASM handoff with URL parameters.
/// </summary>
[Collection("Playwright")]
public class DeepLinkTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DeepLinkTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task DirectUrl_WithPackageAndPeriod_LoadsChartCorrectly()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            var url = $"{_fixture.ServerUrl}/packages?ids=Sentry&months=3";
            _output.WriteLine($"Navigating directly to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for WASM hydration
            await page.WaitForTimeoutAsync(5_000);

            // Verify the chart rendered with the Sentry dataset
            var datasetCount = await page.EvaluateAsync<int>(
                "window.chartInterop.chart?.data.datasets.length ?? 0");
            _output.WriteLine($"Dataset count: {datasetCount}");
            datasetCount.Should().Be(1, "chart should have exactly 1 dataset from the deep link");

            var datasetLabel = await page.EvaluateAsync<string>(
                "window.chartInterop.chart?.data.datasets[0]?.label ?? ''");
            _output.WriteLine($"Dataset label: {datasetLabel}");
            datasetLabel.Should().Be("Sentry", "dataset should be the Sentry package from URL");

            // Verify the period selector shows the correct value
            var selectedPeriod = await page.Locator("select#period").InputValueAsync();
            _output.WriteLine($"Selected period: {selectedPeriod}");
            selectedPeriod.Should().Be("3", "period selector should reflect months=3 from URL");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task DirectUrl_PackagePathFormat_LoadsCorrectly()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Test the /packages/{id} path format
            var url = $"{_fixture.ServerUrl}/packages/Sentry";
            _output.WriteLine($"Navigating directly to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            await page.WaitForTimeoutAsync(5_000);

            var datasetCount = await page.EvaluateAsync<int>(
                "window.chartInterop.chart?.data.datasets.length ?? 0");
            _output.WriteLine($"Dataset count: {datasetCount}");
            datasetCount.Should().Be(1, "chart should load the package from the URL path");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
