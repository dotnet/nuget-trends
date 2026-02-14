# Contributing

Please raise an issue to discuss changes before raising large PRs.

## Requirements

- .NET SDK 10
- Docker (for Aspire-managed containers: PostgreSQL, RabbitMQ, ClickHouse)

## Running Locally with .NET Aspire

The project uses [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) for local development orchestration. A single command starts all services:

```bash
dotnet run --project src/NuGetTrends.AppHost
```

This starts:
- **PostgreSQL** with PgAdmin
- **RabbitMQ** with Management UI
- **ClickHouse** (with migrations auto-applied)
- **NuGetTrends.Web** (Blazor app + API)
- **NuGetTrends.Scheduler** (Hangfire jobs)

### Aspire Dashboard

The Aspire dashboard URL is displayed in the terminal when the AppHost starts. It provides:
- Real-time view of all services and their status
- Centralized logs from all services
- Distributed traces
- Metrics

Click on any service URL in the Resources table to open it.

### Key URLs (via Aspire Dashboard)

| Service | Description |
|---------|-------------|
| Web | Blazor app and API |
| Scheduler | Hangfire dashboard for background jobs |
| PgAdmin | PostgreSQL administration |
| RabbitMQ Management | Message queue management |

> Note: Ports are dynamically assigned by Aspire. Check the dashboard's Resources table for actual URLs.

### Database Migrations

EF Core migrations are **automatically applied** when the Scheduler starts in Development mode. No manual migration steps needed.

### Getting Data

After starting the services, you can either:

1. Run the _catalog importer_ job from the Hangfire dashboard to import the NuGet catalog
2. Run the _daily download importer_ for a few days to get download statistics

### Debugging

When running from an IDE (Rider, Visual Studio, VS Code):

1. Set `NuGetTrends.AppHost` as the startup project
2. Press F5 to start debugging
3. Breakpoints in any project (Web, Scheduler, Data) will work automatically

To attach to a running instance:
1. Start the AppHost from terminal: `dotnet run --project src/NuGetTrends.AppHost`
2. Use your IDE's "Attach to Process" feature
3. Select `NuGetTrends.Web` or `NuGetTrends.Scheduler`

## Background Jobs

Background jobs are managed using [Hangfire](https://github.com/HangfireIO/Hangfire) in the `NuGetTrends.Scheduler` project. Access the dashboard by clicking the Scheduler URL in the Aspire dashboard.

Jobs are scheduled to run at intervals, but you can trigger them on demand from the Hangfire dashboard.

### Daily Download Pipeline Architecture

The system collects daily download statistics for all NuGet packages (~400K+) using a distributed pipeline:

```
+---------------------------------------------------------------------------+
|                              PUBLISHER JOB                                |
|                     (DailyDownloadPackageIdPublisher)                     |
|                                                                           |
|  1. Query PostgreSQL: packages not yet checked today                      |
|     - JOIN package_details_catalog_leafs with package_downloads           |
|     - Filter: LatestDownloadCountCheckedUtc < today                       |
|  2. Stream results (avoid loading 400K+ IDs into memory)                  |
|  3. Batch into groups of 25, serialize with MessagePack                   |
|  4. Publish to RabbitMQ queue: "daily-download"                           |
+---------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------+
|                              RABBITMQ                                     |
|                                                                           |
|  Queue: daily-download (durable, 12h message TTL)                         |
|  Messages: batches of 25 package IDs (MessagePack serialized)             |
+---------------------------------------------------------------------------+
                                      |
                                      v
+---------------------------------------------------------------------------+
|                              WORKER(S)                                    |
|                         (DailyDownloadWorker)                             |
|                                                                           |
|  1. Consume batch from RabbitMQ                                           |
|  2. Parallel async requests to nuget.org API (fetch download counts)      |
|  3. Dual write:                                                           |
|     - ClickHouse: bulk insert to daily_downloads (time-series history)    |
|     - PostgreSQL: update package_downloads (latest count + timestamp)     |
+---------------------------------------------------------------------------+
```

**Key files:**
- Publisher: [`src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs`](src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs)
- Worker: [`src/NuGetTrends.Scheduler/DailyDownloadWorker.cs`](src/NuGetTrends.Scheduler/DailyDownloadWorker.cs)
- ClickHouse service: [`src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs`](src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs)
- ClickHouse schema: [`src/NuGetTrends.Data/ClickHouse/migrations/`](src/NuGetTrends.Data/ClickHouse/migrations/)

**Deduplication strategy:**
- **Primary**: PostgreSQL `LatestDownloadCountCheckedUtc` filters already-processed packages at publish time
- **Safety net**: ClickHouse uses `ReplacingMergeTree` engine which deduplicates rows by `(package_id, date)`

**Configuration:**
- Worker count: `DailyDownloadWorker:WorkerCount` in appsettings.json
- ClickHouse connection: `ConnectionStrings:ClickHouse` in appsettings.json

## Website

The website is a Blazor SSR + WebAssembly app with an ASP.NET Core API backend:

- **Blazor App (server + components)**: `src/NuGetTrends.Web/`
- **Blazor Client (WASM components)**: `src/NuGetTrends.Web.Client/`
- **Vendored JS/CSS libs**: `src/NuGetTrends.Web/wwwroot/lib/`

When running via Aspire, all services start automatically.

### SEO files

When adding new public routes, update these files in `src/NuGetTrends.Web/wwwroot/`:

- **`sitemap.xml`** — add new `<url>` entries for crawlers
- **`robots.txt`** — update if routes need to be blocked from indexing

## Production Deployment

Production deployment is **not affected** by the Aspire setup. The AppHost and ServiceDefaults projects are for local development only.

Production continues to use:
- Terraform for infrastructure
- Kubernetes (GKE) for orchestration
- Manual connection strings via appsettings/environment variables

## Note on .NET SDK Version

We lock the .NET SDK version via `global.json` to have a reference version and avoid surprises during CI.
If you don't have that exact version, usually anything with that major works just fine.
If you just want to quickly build, try deleting `global.json`.

---

Please note we have a code of conduct, please follow it in all your interactions with the project.
