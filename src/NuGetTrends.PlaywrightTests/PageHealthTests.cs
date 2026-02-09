using FluentAssertions;
using Microsoft.Playwright;
using NuGetTrends.PlaywrightTests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace NuGetTrends.PlaywrightTests;

/// <summary>
/// Validates pages load without 404s on sub-resources or JavaScript errors in the console.
/// </summary>
[Collection("Playwright")]
public class PageHealthTests
{
    private readonly PlaywrightFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PageHealthTests(PlaywrightFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Theory]
    [InlineData("/", "Home")]
    [InlineData("/packages/Sentry", "Package detail")]
    public async Task Page_ShouldHaveNo404sOrJsErrors(string path, string label)
    {
        var page = await _fixture.NewPageAsync();
        var failedRequests = new List<string>();
        var consoleErrors = new List<string>();

        try
        {
            page.RequestFailed += (_, request) =>
            {
                failedRequests.Add($"{request.Method} {request.Url} - {request.Failure}");
                _output.WriteLine($"[request failed] {request.Method} {request.Url} - {request.Failure}");
            };

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

            page.PageError += (_, error) =>
            {
                consoleErrors.Add(error);
                _output.WriteLine($"[page error] {error}");
            };

            var url = _fixture.ServerUrl + path;
            _output.WriteLine($"Loading {label} page: {url}");

            await page.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            // Wait for WASM hydration
            await page.WaitForTimeoutAsync(5_000);

            failedRequests.Should().BeEmpty(
                $"{label} page should load all resources without HTTP errors");

            consoleErrors.Should().BeEmpty(
                $"{label} page should have no JavaScript errors in the console");
        }
        finally
        {
            if (failedRequests.Count > 0 || consoleErrors.Count > 0)
            {
                var screenshotPath = Path.Combine(
                    Path.GetTempPath(), $"page-health-{label.Replace(' ', '-')}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                _output.WriteLine($"Screenshot saved: {screenshotPath}");
            }

            await page.CloseAsync();
        }
    }
}
