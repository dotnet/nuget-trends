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
    /// In CI, WASM initialization can take 60-120 seconds due to:
    /// - Testcontainers overhead
    /// - WASM assembly downloads
    /// - JIT compilation
    /// </summary>
    public static async Task WaitForWasmInteractivityAsync(this IPage page, int timeoutMs = 120_000)
    {
        // Wait for the search input to be present and ready
        var searchInput = page.Locator("input.input.is-large");
        await searchInput.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = timeoutMs
        });

        // Additional wait for WASM to fully initialize after SSR hydration completes.
        // The search input may be present during SSR but not yet interactive with WASM event handlers.
        // This fixed wait ensures the Blazor WASM runtime has finished attaching event handlers
        // to the DOM elements. Without this, typing into inputs may not trigger WASM-side events.
        // Increased for CI environments where WASM initialization is significantly slower.
        await page.WaitForTimeoutAsync(5_000);
    }

    /// <summary>
    /// Wait for the search dropdown to appear after typing.
    /// Accounts for debounce delay (300ms) + API call + render time.
    /// </summary>
    public static async Task WaitForSearchDropdownAsync(this IPage page, int timeoutMs = 30_000)
    {
        var dropdown = page.Locator(".autocomplete-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = timeoutMs
        });
    }
}
