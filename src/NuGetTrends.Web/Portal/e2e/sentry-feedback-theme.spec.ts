import { test, expect, Page } from '@playwright/test';

/** Locate the Sentry feedback Shadow DOM host. */
function sentryHost(page: Page) {
  return page.locator('#sentry-feedback');
}

/** Check whether the Sentry feedback trigger button is visible inside the Shadow DOM. */
async function isSentryTriggerVisible(page: Page): Promise<boolean> {
  return page.evaluate(() => {
    const host = document.querySelector('#sentry-feedback');
    if (!host?.shadowRoot) return false;
    const trigger = host.shadowRoot.querySelector('[aria-label="Give Feedback"]')
      ?? host.shadowRoot.querySelector('button');
    if (!trigger) return false;
    const rect = (trigger as HTMLElement).getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  });
}

/** Get the computed value of a CSS custom property on the Sentry feedback host element. */
async function getSentryHostCssVar(page: Page, varName: string): Promise<string> {
  return page.evaluate((name) => {
    const host = document.querySelector('#sentry-feedback');
    if (!host) return '';
    return getComputedStyle(host).getPropertyValue(name).trim();
  }, varName);
}

/** Remove the webpack dev server overlay iframe that intercepts pointer events. */
async function removeWebpackOverlay(page: Page) {
  await page.evaluate(() => {
    const iframe = document.getElementById('webpack-dev-server-client-overlay');
    iframe?.remove();
  });
}

/** Click the theme toggle button and wait for the expected body class. */
async function toggleToTheme(page: Page, expectedClass: string) {
  await page.locator('.theme-toggle-btn').click();
  await expect(page.locator('body')).toHaveClass(new RegExp(expectedClass), { timeout: 3000 });
}

// ---------------------------------------------------------------------------

test.describe('Sentry Feedback Widget – Theme Integration', () => {

  test.beforeEach(async ({ page }) => {
    // Clear saved theme so each test starts from "system" default
    await page.addInitScript(() => localStorage.removeItem('nuget-trends-theme'));
    await page.goto('/');
    // Wait for Sentry feedback widget to be injected into the DOM
    await expect(sentryHost(page)).toBeAttached({ timeout: 10_000 });
    // Remove the webpack dev server overlay that intercepts clicks
    await removeWebpackOverlay(page);
  });

  test('feedback widget is present on initial page load', async ({ page }) => {
    const visible = await isSentryTriggerVisible(page);
    expect(visible).toBe(true);
  });

  test('feedback widget remains visible after toggling to light theme', async ({ page }) => {
    await toggleToTheme(page, 'light-theme');

    await expect(sentryHost(page)).toBeAttached();
    expect(await isSentryTriggerVisible(page)).toBe(true);
  });

  test('feedback widget remains visible after toggling to dark theme', async ({ page }) => {
    await toggleToTheme(page, 'light-theme');
    await toggleToTheme(page, 'dark-theme');

    await expect(sentryHost(page)).toBeAttached();
    expect(await isSentryTriggerVisible(page)).toBe(true);
  });

  test('feedback widget survives a full theme cycle', async ({ page }) => {
    // system -> light
    await toggleToTheme(page, 'light-theme');
    await expect(sentryHost(page)).toBeAttached();
    expect(await isSentryTriggerVisible(page)).toBe(true);

    // light -> dark
    await toggleToTheme(page, 'dark-theme');
    await expect(sentryHost(page)).toBeAttached();
    expect(await isSentryTriggerVisible(page)).toBe(true);

    // dark -> system (class depends on OS preference, just check widget survives)
    await page.locator('.theme-toggle-btn').click();
    await expect(sentryHost(page)).toBeAttached();
    expect(await isSentryTriggerVisible(page)).toBe(true);
  });

  test('clicking the page body does NOT open the feedback dialog', async ({ page }) => {
    await page.locator('body').click({ position: { x: 300, y: 300 } });

    // The dialog lives inside the Shadow DOM so we must use page.evaluate.
    // Use expect.poll instead of waitForTimeout to avoid flaky timing.
    await expect.poll(async () => page.evaluate(() => {
      const host = document.querySelector('#sentry-feedback');
      if (!host?.shadowRoot) return false;
      const dialog = host.shadowRoot.querySelector('dialog[open]')
        ?? host.shadowRoot.querySelector('[class*="dialog"]');
      if (!dialog) return false;
      const rect = (dialog as HTMLElement).getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    }), { timeout: 1000 }).toBe(false);
  });

  test('clicking body does NOT open feedback dialog after theme toggle', async ({ page }) => {
    await toggleToTheme(page, 'light-theme');

    await page.locator('body').click({ position: { x: 300, y: 300 } });

    await expect.poll(async () => page.evaluate(() => {
      const host = document.querySelector('#sentry-feedback');
      if (!host?.shadowRoot) return false;
      const dialog = host.shadowRoot.querySelector('dialog[open]')
        ?? host.shadowRoot.querySelector('[class*="dialog"]');
      if (!dialog) return false;
      const rect = (dialog as HTMLElement).getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    }), { timeout: 1000 }).toBe(false);
  });

  test('widget uses dark accent color when app is in dark theme', async ({ page }) => {
    await toggleToTheme(page, 'light-theme');
    await toggleToTheme(page, 'dark-theme');

    const accent = await getSentryHostCssVar(page, '--accent-background');
    expect(accent).toBe('#4a9fd4');
  });

  test('widget uses light accent color when app is in light theme', async ({ page }) => {
    await toggleToTheme(page, 'light-theme');

    const accent = await getSentryHostCssVar(page, '--accent-background');
    expect(accent).toBe('#215C84');
  });
});
