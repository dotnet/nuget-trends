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
dotnet test NuGetTrends.sln
```

### Building

```bash
dotnet build NuGetTrends.sln
```

## Technology Stack

- **Backend**: .NET 10, ASP.NET Core, Entity Framework Core, Hangfire, Sentry
- **Frontend**: Blazor SSR + WebAssembly hybrid, Blazored.Toast, Chart.js (via JS interop)
- **Databases**: PostgreSQL (Npgsql), ClickHouse (ClickHouse.Driver)
- **Infrastructure**: .NET Aspire, Docker, Kubernetes (GKE)
