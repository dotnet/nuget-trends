# AI Agent Instructions for NuGet Trends

This document provides guidance for AI agents working on the NuGet Trends codebase.

## Project Overview

NuGet Trends is a web application that tracks NuGet package download statistics and displays trends over time. The project consists of:

- **Backend**: ASP.NET Core Web API and Hangfire scheduler (.NET)
- **Frontend**: Angular SPA (TypeScript)
- **Database**: PostgreSQL
- **Message Queue**: RabbitMQ

## Code Style

### EditorConfig

The project uses `.editorconfig` files to define code formatting rules. Always follow these conventions:

#### .NET Backend (`src/` directory)
- **Indentation**: 4 spaces for C# files
- **Charset**: UTF-8
- See root `.editorconfig` for complete C# style rules

#### Angular Frontend (`src/NuGetTrends.Web/Portal/`)
- **Indentation**: 2 spaces for all files (TypeScript, HTML, SCSS, JSON)
- **Charset**: UTF-8
- **Final newline**: Required
- **Trailing whitespace**: Trimmed (except Markdown)

### Important Style Notes

1. **Do not change indentation style** when editing files. The Portal uses 2-space indentation consistently.
2. When adding new Angular components, use the existing NgModule pattern with `standalone: false` in the `@Component` decorator.
3. For tests, components go in `declarations`, not `imports` (since they are not standalone).
4. **Never use `#region`/`#endregion`** in C# code. Keep code organized through proper class structure and small, focused methods instead.

## Project Structure

```
src/
├── NuGet.Protocol.Catalog/     # NuGet catalog protocol client
├── NuGetTrends.Data/           # EF Core data layer
├── NuGetTrends.Scheduler/      # Hangfire background jobs
└── NuGetTrends.Web/
    ├── Portal/                 # Angular frontend
    │   ├── src/app/
    │   │   ├── core/          # Core services and components
    │   │   ├── home/          # Home page module
    │   │   ├── packages/      # Packages chart module
    │   │   └── shared/        # Shared components and services
    │   └── .editorconfig      # Frontend-specific editor config
    └── *.cs                   # ASP.NET Core API controllers
```

## Common Tasks

### Running the Application

```bash
# Backend (from src/NuGetTrends.Web)
dotnet run

# Frontend (from src/NuGetTrends.Web/Portal)
npm install
npm start
```

### Running Tests

```bash
# Backend tests
dotnet test

# Frontend tests
cd src/NuGetTrends.Web/Portal
npm run test-no-watch
```

### Building

```bash
# Frontend production build
cd src/NuGetTrends.Web/Portal
npm run build
```

## Technology Stack

- **Backend**: .NET 10, ASP.NET Core, Entity Framework Core, Hangfire, Sentry
- **Frontend**: Angular 19, Angular Material, Chart.js, RxJS, ngx-toastr
- **Database**: PostgreSQL with Npgsql
- **Infrastructure**: Docker, Kubernetes (GKE)
