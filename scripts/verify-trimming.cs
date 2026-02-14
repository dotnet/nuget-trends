#!/usr/bin/env dotnet

#:package Microsoft.Playwright@1.52.0
#:property ManagePackageVersionsCentrally=false
#:property PublishAot=false

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

// ============================================================================
// Verify IL trimming doesn't break Blazor WASM rendering
// ============================================================================
// Publishes the Web project with trimming enabled, starts the binary, and
// uses a headless Playwright browser to verify Blazor components render
// without CtorNotLocated or other trimming-related errors.
//
// Usage:
//   ./scripts/verify-trimming.cs                    # publish + test
//   ./scripts/verify-trimming.cs /path/to/publish   # test existing publish dir
// ============================================================================

const int port = 5556;
var scriptDir = Path.GetDirectoryName(AppContext.BaseDirectory
    .Split(".dotnet-script")
    .First()
    .TrimEnd(Path.DirectorySeparatorChar)) ?? Environment.CurrentDirectory;

// Resolve repo root: walk up from CWD until we find NuGetTrends.slnx
var repoRoot = Environment.CurrentDirectory;
while (repoRoot != null && !File.Exists(Path.Combine(repoRoot, "NuGetTrends.slnx")))
    repoRoot = Path.GetDirectoryName(repoRoot);
if (repoRoot == null)
{
    Console.Error.WriteLine("Could not find NuGetTrends.slnx. Run from the repo root.");
    return 1;
}

var publishDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(repoRoot, "artifacts", "publish", "trimming-check");

// 1. Publish with trimming (unless a pre-built dir was provided)
if (args.Length == 0)
{
    Console.WriteLine("==> Publishing with IL trimming...");
    if (Directory.Exists(publishDir)) Directory.Delete(publishDir, true);

    var publishResult = Run("dotnet",
        $"publish {Path.Combine(repoRoot, "src/NuGetTrends.Web/NuGetTrends.Web.csproj")} " +
        $"-c Release -o \"{publishDir}\" -p:SentryUploadSymbols=false --nologo -v quiet");
    if (publishResult != 0) { Console.Error.WriteLine("FAIL: dotnet publish failed"); return 1; }
    Console.WriteLine($"    Published to {publishDir}");
}

if (!File.Exists(Path.Combine(publishDir, "NuGetTrends.Web.dll")))
{
    Console.Error.WriteLine($"FAIL: NuGetTrends.Web.dll not found in {publishDir}");
    return 1;
}

// 2. Install Playwright browsers
Microsoft.Playwright.Program.Main(["install", "chromium"]);

// 3. Start the published app
Console.WriteLine($"==> Starting published app on port {port}...");

var appProcess = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = "NuGetTrends.Web.dll",
        WorkingDirectory = publishDir,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    }
};
appProcess.StartInfo.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
appProcess.StartInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
appProcess.StartInfo.Environment["ConnectionStrings__NuGetTrends"] = "Host=localhost;Database=nugettrends;";
appProcess.StartInfo.Environment["ConnectionStrings__nugettrends"] = "Host=localhost;Database=nugettrends;";
appProcess.StartInfo.Environment["ConnectionStrings__clickhouse"] = "Host=localhost;";
appProcess.StartInfo.Environment["ConnectionStrings__ClickHouse"] = "Host=localhost;";
appProcess.StartInfo.Environment["Sentry__Dsn"] = "";
appProcess.Start();

try
{
    // Wait for server to start (up to 30s)
    using var httpClient = new HttpClient();
    var started = false;
    for (var i = 1; i <= 30; i++)
    {
        if (appProcess.HasExited) { Console.Error.WriteLine("FAIL: Server process exited unexpectedly"); return 1; }
        try
        {
            var resp = await httpClient.GetAsync($"http://127.0.0.1:{port}/");
            if (resp.IsSuccessStatusCode) { Console.WriteLine($"    Server started after {i}s"); started = true; break; }
        }
        catch { /* server not ready yet */ }
        await Task.Delay(1000);
    }
    if (!started) { Console.Error.WriteLine("FAIL: Server failed to start within 30s"); return 1; }

    // 4. Verify Blazor renders with Playwright
    Console.WriteLine("==> Verifying Blazor WASM renders...");
    var errors = new System.Collections.Generic.List<string>();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
    var page = await browser.NewPageAsync();

    page.Console += (_, msg) =>
    {
        if (msg.Type == "error") errors.Add(msg.Text);
    };

    await page.GotoAsync($"http://127.0.0.1:{port}/", new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 30_000 });
    await page.WaitForFunctionAsync(
        "() => typeof Blazor !== 'undefined'",
        null,
        new PageWaitForFunctionOptions { Timeout = 15_000 });
    await page.WaitForTimeoutAsync(500); // hydration buffer

    var title = await page.TitleAsync();
    Console.WriteLine($"    Page title: {title}");

    if (title == "Not found")
    {
        Console.Error.WriteLine("FAIL: Page title is \"Not found\" — Blazor components failed to render");
        return 1;
    }

    var blazorLoaded = await page.EvaluateAsync<bool>("() => typeof Blazor !== 'undefined'");
    Console.WriteLine($"    Blazor loaded: {blazorLoaded}");
    if (!blazorLoaded)
    {
        Console.Error.WriteLine("FAIL: Blazor did not load");
        return 1;
    }

    var criticalErrors = errors.Where(e => e.Contains("CtorNotLocated") || e.Contains("Unhandled exception rendering")).ToList();
    if (criticalErrors.Count > 0)
    {
        Console.Error.WriteLine("FAIL: Critical rendering errors detected:");
        foreach (var e in criticalErrors) Console.Error.WriteLine($"  {e}");
        return 1;
    }

    Console.WriteLine("PASS: Blazor WASM rendered successfully after IL trimming");
    return 0;
}
finally
{
    if (!appProcess.HasExited) { appProcess.Kill(entireProcessTree: true); appProcess.WaitForExit(5000); }
}

static int Run(string command, string arguments)
{
    var p = Process.Start(new ProcessStartInfo { FileName = command, Arguments = arguments, UseShellExecute = false });
    p!.WaitForExit();
    return p.ExitCode;
}
