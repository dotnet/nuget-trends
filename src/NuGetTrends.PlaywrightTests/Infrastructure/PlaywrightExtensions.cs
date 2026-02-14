using Microsoft.Playwright;

namespace NuGetTrends.PlaywrightTests.Infrastructure;

/// <summary>
/// Extension methods for Playwright operations with CI-appropriate timeouts.
/// CI environments (especially with Testcontainers + WASM) are significantly slower
/// than local development, so these helpers use longer timeouts.
/// </summary>
public static class PlaywrightExtensions
{
    /// <summary>
    /// Wait for Blazor WASM to fully hydrate and become interactive.
    /// In CI, WASM initialization can take 30-60 seconds due to:
    /// - Testcontainers overhead
    /// - WASM assembly downloads
    /// - JIT compilation
    /// </summary>
    public static async Task WaitForWasmInteractivityAsync(this IPage page, int timeoutMs = 60_000)
    {
        // Wait for the search input to be present and ready
        var searchInput = page.Locator("input.input.is-large");
        await searchInput.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = timeoutMs
        });

        // Additional wait for WASM to fully initialize after SSR hydration
        await page.WaitForTimeoutAsync(3_000);
    }

    /// <summary>
    /// Wait for the search dropdown to appear after typing.
    /// Accounts for debounce delay (300ms) + API call + render time.
    /// </summary>
    public static async Task WaitForSearchDropdownAsync(this IPage page, int timeoutMs = 20_000)
    {
        var dropdown = page.Locator(".autocomplete-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }
}
