#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# Verify IL trimming doesn't break Blazor WASM rendering
# ============================================================================
# Publishes the Web project with trimming enabled, starts the binary, and
# uses a headless Playwright browser to verify Blazor components render
# without CtorNotLocated or other trimming-related errors.
#
# Usage:
#   ./scripts/verify-trimming.sh                  # publish + test
#   ./scripts/verify-trimming.sh /path/to/publish  # test existing publish dir
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
PORT=5556
PUBLISH_DIR="${1:-$REPO_ROOT/artifacts/publish/trimming-check}"

# 1. Publish with trimming (unless a pre-built dir was provided)
if [ $# -eq 0 ]; then
  echo "==> Publishing with IL trimming..."
  rm -rf "$PUBLISH_DIR"
  dotnet publish "$REPO_ROOT/src/NuGetTrends.Web/NuGetTrends.Web.csproj" \
    -c Release -o "$PUBLISH_DIR" -p:SentryUploadSymbols=false --nologo -v quiet
  echo "    Published to $PUBLISH_DIR"
fi

# 2. Ensure Playwright is available
if ! node -e "require('playwright')" 2>/dev/null; then
  echo "==> Installing Playwright..."
  npm install --no-save --silent playwright 2>/dev/null
  npx playwright install chromium 2>/dev/null
fi

# 3. Start the published app
echo "==> Starting published app on port $PORT..."
kill "$(lsof -ti :"$PORT")" 2>/dev/null || true
sleep 1

pushd "$PUBLISH_DIR" > /dev/null
ASPNETCORE_URLS="http://127.0.0.1:$PORT" \
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__NuGetTrends="Host=localhost;Database=nugettrends;" \
ConnectionStrings__clickhouse="Host=localhost;" \
Sentry__Dsn="" \
  dotnet NuGetTrends.Web.dll &
APP_PID=$!
popd > /dev/null

cleanup() { kill "$APP_PID" 2>/dev/null || true; }
trap cleanup EXIT

# Wait for server
for i in $(seq 1 30); do
  if curl -sf "http://127.0.0.1:$PORT/" > /dev/null 2>&1; then
    echo "    Server started after ${i}s"
    break
  fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then
    echo "FAIL: Server process exited unexpectedly"
    exit 1
  fi
  sleep 1
done

if ! curl -sf "http://127.0.0.1:$PORT/" > /dev/null 2>&1; then
  echo "FAIL: Server failed to start within 30s"
  exit 1
fi

# 4. Verify Blazor renders with Playwright
echo "==> Verifying Blazor WASM renders..."
node --input-type=commonjs - <<SCRIPT
const { chromium } = require('playwright');

(async () => {
  const errors = [];
  const browser = await chromium.launch();
  const page = await browser.newPage();

  page.on('console', msg => {
    if (msg.type() === 'error') errors.push(msg.text());
  });

  await page.goto('http://127.0.0.1:$PORT/', { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(5000);

  const title = await page.title();
  console.log('    Page title:', title);

  if (title === 'Not found') {
    console.error('FAIL: Page title is "Not found" — Blazor components failed to render');
    errors.push('Page title is "Not found"');
  }

  const blazorLoaded = await page.evaluate(() => typeof Blazor !== 'undefined');
  console.log('    Blazor loaded:', blazorLoaded);
  if (!blazorLoaded) errors.push('Blazor did not load');

  const criticalErrors = errors.filter(e =>
    e.includes('CtorNotLocated') || e.includes('Unhandled exception rendering'));
  if (criticalErrors.length > 0) {
    console.error('FAIL: Critical rendering errors detected:');
    criticalErrors.forEach(e => console.error('  ', e.substring(0, 200)));
  }

  await browser.close();

  if (criticalErrors.length > 0) {
    process.exit(1);
  }

  console.log('PASS: Blazor WASM rendered successfully after IL trimming');
})();
SCRIPT
