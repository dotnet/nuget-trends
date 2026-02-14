# AI Agent Instructions for NuGet Trends

This document provides guidance for AI agents working on the NuGet Trends codebase.

## Project Overview

NuGet Trends is a web application that tracks NuGet package download statistics and displays trends over time. The project consists of:

- **Frontend**: Blazor SSR + WebAssembly hybrid (two-project pattern)
- **Backend**: ASP.NET Core Web API (.NET)
- **Scheduler**: Hangfire background jobs for catalog import and download tracking
- **Databases**: PostgreSQL (metadata, search) + ClickHouse (time-series download data)
- **Message Queue**: RabbitMQ
- **Orchestration**: .NET Aspire (AppHost)

## Code Style

### EditorConfig

The project uses `.editorconfig` files to define code formatting rules. Always follow these conventions:

- **Indentation**: 4 spaces for C# files
- **Charset**: UTF-8
- See root `.editorconfig` for complete C# style rules

### Important Style Notes

1. **Never use `#region`/`#endregion`** in C# code. Keep code organized through proper class structure and small, focused methods instead.
2. Blazor components in the Client project (`NuGetTrends.Web.Client`) are interactive WASM components. The Server project (`NuGetTrends.Web`) only has `App.razor` and `Routes.razor`.

## Project Structure

```
src/
├── NuGet.Protocol.Catalog/         # NuGet catalog protocol client
├── NuGetTrends.Data/               # EF Core data layer + ClickHouse service
│   └── ClickHouse/
│       └── migrations/             # ClickHouse schema migrations (SQL)
├── NuGetTrends.Scheduler/          # Hangfire background jobs
├── NuGetTrends.AppHost/            # .NET Aspire orchestration
├── NuGetTrends.ServiceDefaults/    # Shared Aspire service defaults
├── NuGetTrends.Web/                # Server project (API controllers, SSR host)
│   └── Components/                 # App.razor, Routes.razor only
├── NuGetTrends.Web.Client/         # Client project (all interactive Blazor components)
│   ├── Pages/                      # Routable pages (Home, Packages)
│   ├── Layout/                     # MainLayout
│   ├── Shared/                     # Shared components
│   ├── Models/                     # DTOs and models
│   └── Services/                   # State services (PackageState, ThemeState, LoadingState)
├── NuGetTrends.Web.Tests/          # Unit tests
├── NuGetTrends.IntegrationTests/   # Integration tests
└── scripts/                        # Utility scripts (C# scripts preferred)
```

## Scripts

Utility and maintenance scripts live in the `scripts/` directory. **Prefer C# scripts** (`.cs` files using `#!/usr/bin/env dotnet` shebang with `#:package` directives) over shell scripts. This keeps tooling consistent with the rest of the codebase and works cross-platform.

Existing scripts:
- `seed-clickhouse-test-data.cs` — Seeds ClickHouse with test data for a single package
- `seed-local-test-data.cs` — Seeds both PostgreSQL and ClickHouse with test data for local development

## Common Tasks

### Running the Application

```bash
# Full stack via Aspire (PostgreSQL, ClickHouse, RabbitMQ, Web, Scheduler)
dotnet run --project src/NuGetTrends.AppHost

# Seed test data for local development
PG_CONNECTION_STRING="..." CH_CONNECTION_STRING="..." ./scripts/seed-local-test-data.cs
```

### Running Tests

```bash
dotnet test NuGetTrends.slnx
```

### Building

```bash
dotnet build NuGetTrends.slnx
```

## Technology Stack

- **Backend**: .NET 10, ASP.NET Core, Entity Framework Core, Hangfire, Sentry
- **Frontend**: Blazor SSR + WebAssembly hybrid, Blazored.Toast, Blazor-ApexCharts
- **Databases**: PostgreSQL (Npgsql), ClickHouse (ClickHouse.Driver)
- **Infrastructure**: .NET Aspire, Docker, Kubernetes (GKE)

## Adding New Pages

When adding a new routable page, follow this checklist:

### 1. SEO — Sitemap, Titles, Meta Tags, Canonical URL

- **Sitemap**: Add the new URL to `src/NuGetTrends.Web/wwwroot/sitemap.xml`.
- **Use the `<SeoHead>` component** (`Shared/SeoHead.razor`) which sets `<PageTitle>`, `<meta description>`, canonical URL, Open Graph, and Twitter Card tags in one place. Usage:
  ```razor
  <SeoHead Title="NuGet Trends - Your Page"
           Description="A unique description for this page."
           Path="/your-page" />
  ```
- The `<HeadOutlet>` component is already configured in `App.razor` with `InteractiveWebAssembly` render mode, so `<PageTitle>` and `<HeadContent>` work from client-side page components.
- Shared defaults (og:image, og:site_name, twitter:card, twitter:image) are set in `App.razor` and don't need to be repeated per page.

### 2. IL Trimming Compatibility

The WASM client project has IL trimming enabled (`PublishTrimmed=true`, `TrimMode=full`) in Release builds. This aggressively strips unused code and **will break the app in staging/production** if not handled:

- **JSON deserialization**: All HTTP response DTOs must be registered in `NuGetTrendsJsonContext` (`src/NuGetTrends.Web.Client/NuGetTrendsJsonContext.cs`) as `[JsonSerializable(typeof(YourType))]`. All `GetFromJsonAsync` calls must use the trim-safe `JsonTypeInfo<T>` overload (e.g., `GetFromJsonAsync(url, NuGetTrendsJsonContext.Default.YourType)`). Never use the generic overload without `JsonTypeInfo` — it relies on reflection that the trimmer removes.
- **Trimmer roots**: If a new component or third-party library is only referenced indirectly (e.g., via DI injection or cross-project `@rendermode`), the trimmer may strip it. Add it to `src/NuGetTrends.Web.Client/TrimmerRoots.xml`. See existing entries for `Routes` and `Blazored.Toast` as examples.
- **Test in Release mode**: Trimmer issues only surface in Release builds. Always verify with `dotnet publish -c Release` when adding new dependencies or DI registrations.

### 3. Playwright E2E Tests

Every new page must have Playwright tests. Tests live in `src/NuGetTrends.PlaywrightTests/`:

- **Page health**: Add the new route to the `[InlineData]` list in `PageHealthTests.cs` so it's checked for HTTP 4xx errors and JS console errors.
- **Functional tests**: Add a test class for page-specific behavior (e.g., `FrameworkPageTests.cs`, `ThemeToggleTests.cs`).
- **Test patterns**:
  - All test classes use `[Collection("Playwright")]` and inject `PlaywrightFixture`.
  - Use `PlaywrightFixture.WaitForWasmAsync(page)` to wait for Blazor WASM hydration instead of arbitrary `WaitForTimeoutAsync` delays.
  - After WASM hydration, wait for specific elements with `WaitForSelectorAsync` rather than blanket timeouts.
  - Use `page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle })` for initial navigation.
- **Fixture registration**: If the new page needs a new API controller, cache service, or DI registration, also add it to `PlaywrightFixture.ConfigureServices()`.
- **Seed data**: If the page displays data, ensure `DevelopmentDataSeeder` provides appropriate test data so tests have something to assert against.

### 4. Static Asset References

Use `@Assets["path"]` syntax in `App.razor` for all CSS/JS references so `MapStaticAssets()` serves fingerprinted, cache-busted URLs. Never use raw `/css/app.css` paths in the server-rendered HTML.

### 5. Blazor SSR + WASM Considerations

- New pages go in `src/NuGetTrends.Web.Client/Pages/` (the Client project), not the Server project.
- Pages are prerendered on the server (`prerender: true`) then hydrated on the client. Be aware that code in `OnInitializedAsync` runs **twice** (server + client). Use `[SupplyParameterFromQuery]` for URL state and check `OperatingSystem.IsBrowser()` if something should only run client-side.
- When modifying response headers in middleware, use `OnStarting` callbacks — Blazor SSR streams the response body, so headers are read-only after `await next()`.

## Sentry Observability

### Browser JS SDK

The Sentry Browser JS SDK is loaded in `App.razor` (conditional on `Sentry:Dsn` config being set) to catch errors even when WASM fails to boot. It includes Session Replay (100% on error, 0% normal). The `Blazor.start()` promise has a `.catch()` that reports WASM initialization failures to Sentry.

### Scheduler Metrics and Spans

All Hangfire jobs and background workers should be instrumented with Sentry:

- **Metrics**: Use `hub.Metrics.EmitCounter`, `hub.Metrics.EmitGauge`, and `hub.Metrics.EmitDistribution` to track key operational data (packages processed, queue sizes, job completions/failures). Use `SentrySdk.Experimental.Metrics` in classes that use the static API (e.g., `DailyDownloadWorker`).
- **Transactions and spans**: Wrap significant operations in Sentry transactions (`SentrySdk.StartTransaction`) with child spans (`transaction.StartChild` / `span.StartChild`) for sub-operations like DB queries, API calls, queue processing, and serialization. Finish spans and set status to `SpanStatus.Ok` or `SpanStatus.InternalError` as appropriate.
- **Naming conventions**: Use dot-separated hierarchical names — `scheduler.job.completed`, `scheduler.daily_download.packages_queued`, `worker.queue_latency`. Tag metrics with `job_name` for filtering.
- When adding a new Hangfire job, follow the pattern in `TfmAdoptionSnapshotRefresher.cs` and `DailyDownloadPackageIdPublisher.cs` for metrics emission.

### API Controllers

New API endpoints backing pages should consider adding Sentry breadcrumbs or spans for operations that could be slow or fail (e.g., ClickHouse queries, external API calls).
