using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Regression test: changing the date range selector should not create
/// duplicate datasets on the chart.
/// </summary>
[Collection("Playwright")]
public class DateRangeDuplicateTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public DateRangeDuplicateTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task ChangeDateRange_ShouldNotDuplicateDatasets()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Navigate to packages page (which has the search input)
            await page.GotoAsync($"{_fixture.ServerUrl}/packages", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            var searchInput = page.Locator("input.input.is-large");
            await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
            await searchInput.FillAsync("sentry");

            var dropdown = page.Locator(".autocomplete-dropdown");
            await dropdown.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 10_000,
            });

            // Select Sentry package
            await dropdown.Locator(".autocomplete-option").First.ClickAsync();

            // Wait for the chart page and chart to render
            // App navigates to /packages?ids=Sentry&months=24 (query params, not path segments)
            await page.WaitForURLAsync(new Regex(@"packages\?ids="), new PageWaitForURLOptions
            {
                Timeout = 10_000
            });
            // Wait for chart to render
            await page.Locator(".apexcharts-line-series .apexcharts-series").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Count datasets before changing the date range (ApexCharts renders SVG series)
            var seriesSelector = ".apexcharts-line-series .apexcharts-series";
            var datasetCountBefore = await page.Locator(seriesSelector).CountAsync();
            _output.WriteLine($"Datasets before period change: {datasetCountBefore}");
            datasetCountBefore.Should().Be(1, "should have exactly 1 dataset (Sentry)");

            // Change the date range selector
            var periodSelect = page.Locator("select#period");
            await periodSelect.SelectOptionAsync(new SelectOptionValue { Value = "3" });
            _output.WriteLine("Changed period to 3 months");

            // Wait for the chart to re-render after period change
            await page.Locator(".apexcharts-line-series .apexcharts-series").First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);

            // Count datasets after changing the date range
            var datasetCountAfter = await page.Locator(seriesSelector).CountAsync();
            _output.WriteLine($"Datasets after period change: {datasetCountAfter}");

            datasetCountAfter.Should().Be(1,
                "changing the date range should update the existing dataset, not add a duplicate");

            // Change again to be thorough
            await periodSelect.SelectOptionAsync(new SelectOptionValue { Value = "12" });
            _output.WriteLine("Changed period to 12 months");
            await page.Locator(seriesSelector).First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);

            var datasetCountFinal = await page.Locator(seriesSelector).CountAsync();
            _output.WriteLine($"Datasets after second period change: {datasetCountFinal}");

            datasetCountFinal.Should().Be(1,
                "dataset count should remain 1 after multiple period changes");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
