using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates the /frameworks page loads, renders the TFM adoption chart,
/// and that the view/time toggles and filter work correctly.
/// </summary>
[Collection("Playwright")]
public class FrameworkPageTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public FrameworkPageTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public async Task FrameworksPage_LoadsAndRendersChart()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));
        var failedRequests = new List<string>();
        var consoleErrors = new List<string>();

        try
        {
            page.Response += (_, response) =>
            {
                if (response.Status >= 400)
                {
                    var entry = $"{response.Status} {response.Request.Method} {response.Url}";
                    failedRequests.Add(entry);
                    _output.WriteLine($"[HTTP {response.Status}] {response.Request.Method} {response.Url}");
                }
            };

            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                {
                    consoleErrors.Add(msg.Text);
                    _output.WriteLine($"[console error] {msg.Text}");
                }
            };

            var url = $"{_fixture.ServerUrl}/frameworks";
            _output.WriteLine($"Navigating to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for chart to render
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // No HTTP errors or JS errors
            failedRequests.Should().BeEmpty("frameworks page should load without HTTP errors");
            consoleErrors.Should().BeEmpty("frameworks page should have no JS errors");

            // The ApexCharts chart should render with at least one series
            var seriesCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Chart series count: {seriesCount}");
            seriesCount.Should().BeGreaterThan(0, "chart should render with TFM adoption data");

            // The title should be visible
            var title = page.Locator("h2:text('Target Framework Adoption')");
            await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_ViewToggle_SwitchesToFamilyView()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await page.GotoAsync($"{_fixture.ServerUrl}/frameworks", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            // Wait for chart to render
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // Verify chart is rendered in Individual mode (default)
            var individualCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Individual series count: {individualCount}");

            // Click "Family" toggle button
            var familyButton = page.Locator("button:text('Family')");
            await familyButton.ClickAsync();
            // Wait for chart to re-render after toggling view
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Family view should aggregate series - fewer lines than individual
            var familyCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Family series count: {familyCount}");

            familyCount.Should().BeGreaterThan(0, "family view should render series");
            familyCount.Should().BeLessThanOrEqualTo(individualCount,
                "family view should have fewer or equal series than individual view");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_TimeToggle_SwitchesToRelativeMode()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await page.GotoAsync($"{_fixture.ServerUrl}/frameworks", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            // Wait for chart to render
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            // Click "Relative" time toggle
            var relativeButton = page.Locator("button:text('Relative')");
            await relativeButton.ClickAsync();
            // Wait for chart to re-render in relative mode
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

            // Chart should still render
            var seriesCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Series count in relative mode: {seriesCount}");
            seriesCount.Should().BeGreaterThan(0, "chart should render in relative time mode");

            // The X-axis should show numeric labels (not dates)
            // In relative mode the axis title says "Months since first appearance"
            var axisTitle = page.Locator("text=Months since first appearance");
            (await axisTitle.CountAsync()).Should().BeGreaterThan(0,
                "relative mode should show 'Months since first appearance' axis label");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_Filter_ChangesChartSeries()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await page.GotoAsync($"{_fixture.ServerUrl}/frameworks", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            // Wait for chart to render
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            await seriesLocator.First.WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

            var initialCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Initial series count: {initialCount}");

            // Open the filter dropdown
            var filterButton = page.Locator("button:has-text('TFMs selected')");
            await filterButton.ClickAsync();

            // Click "Clear" on a family to remove some TFMs
            var clearLink = page.Locator("a:text('Clear')").First;
            await clearLink.ClickAsync();

            // Close dropdown by clicking backdrop
            var backdrop = page.Locator(".tfm-filter-backdrop");
            if (await backdrop.CountAsync() > 0)
            {
                await backdrop.ClickAsync();
            }
            // Wait for chart to re-render after filter change
            await page.WaitForTimeoutAsync(1_000);

            var afterCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Series count after clearing a family: {afterCount}");

            afterCount.Should().BeLessThan(initialCount,
                "clearing a family from the filter should reduce chart series");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_ClickingToggles_UpdatesUrlQueryString()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            await page.GotoAsync($"{_fixture.ServerUrl}/frameworks", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(5_000);

            // Default URL should have tfms but not view= or time= (defaults are omitted)
            var initialUrl = page.Url;
            _output.WriteLine($"Initial URL: {initialUrl}");
            initialUrl.Should().Contain("tfms=");
            initialUrl.Should().NotContain("view=");
            initialUrl.Should().NotContain("time=");

            // Click "Family" toggle
            await page.Locator("button:text('Family')").ClickAsync();
            await page.WaitForTimeoutAsync(1_000);

            var afterFamilyUrl = page.Url;
            _output.WriteLine($"After Family toggle URL: {afterFamilyUrl}");
            afterFamilyUrl.Should().Contain("view=family");

            // Click "Relative" time toggle
            await page.Locator("button:text('Relative')").ClickAsync();
            await page.WaitForTimeoutAsync(1_000);

            var afterRelativeUrl = page.Url;
            _output.WriteLine($"After Relative toggle URL: {afterRelativeUrl}");
            afterRelativeUrl.Should().Contain("view=family");
            afterRelativeUrl.Should().Contain("time=relative");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_DeepLink_WithViewAndTime_RestoresState()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            var url = $"{_fixture.ServerUrl}/frameworks?view=family&time=relative&tfms=net8.0&tfms=net6.0";
            _output.WriteLine($"Navigating directly to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(5_000);

            // Chart should render
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            var seriesCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Series count: {seriesCount}");

            // In family view, net8.0 and net6.0 both belong to ".NET" so should be 1 family series
            seriesCount.Should().BeGreaterThan(0, "chart should render from deep link");

            // The "Family" button should be active (has is-primary class)
            var familyButton = page.Locator("button:text('Family')");
            var familyClass = await familyButton.GetAttributeAsync("class") ?? "";
            _output.WriteLine($"Family button class: {familyClass}");
            familyClass.Should().Contain("is-primary", "Family view should be active from URL");

            // The "Relative" button should be active
            var relativeButton = page.Locator("button:text('Relative')");
            var relativeClass = await relativeButton.GetAttributeAsync("class") ?? "";
            _output.WriteLine($"Relative button class: {relativeClass}");
            relativeClass.Should().Contain("is-primary", "Relative time should be active from URL");

            // Relative mode should show "Months since first appearance"
            var axisTitle = page.Locator("text=Months since first appearance");
            (await axisTitle.CountAsync()).Should().BeGreaterThan(0,
                "relative mode from deep link should show axis label");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FrameworksPage_DeepLink_WithSpecificTfms_SelectsOnlyThose()
    {
        var page = await _fixture.NewPageAsync(msg => _output.WriteLine(msg));

        try
        {
            // Select only 2 specific TFMs via URL
            var url = $"{_fixture.ServerUrl}/frameworks?tfms=net8.0&tfms=net9.0";
            _output.WriteLine($"Navigating directly to: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });
            await page.WaitForTimeoutAsync(5_000);

            // Should render exactly 2 series (individual view, one per TFM)
            var seriesLocator = page.Locator(".apexcharts-line-series .apexcharts-series");
            var seriesCount = await seriesLocator.CountAsync();
            _output.WriteLine($"Series count: {seriesCount}");
            seriesCount.Should().Be(2, "deep link with 2 TFMs should render exactly 2 series");

            // Verify the URL still contains only the 2 TFMs
            var currentUrl = page.Url;
            _output.WriteLine($"Current URL: {currentUrl}");
            currentUrl.Should().Contain("tfms=net8.0");
            currentUrl.Should().Contain("tfms=net9.0");

            // Verify the filter shows "2 TFMs selected"
            var filterButton = page.Locator("button:has-text('TFMs selected')");
            var filterText = await filterButton.TextContentAsync();
            _output.WriteLine($"Filter button text: {filterText}");
            filterText.Should().Contain("2 TFMs selected");
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
