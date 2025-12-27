# Contributing

Please raise an issue to discuss changes before raising large PRs.

### Requirements

- .NET SDK 10
- Docker, Compose (for the dependencies, PostgreSQL, RabbitMQ, ClickHouse)
- Node.js 20.10.0 (might also work with higher versions)
- NPM


### Run dependant services

We want to get a script to bootstrap the development environment but it's not here yet.
That means now you'll need to:

1. Run `docker-compose up` to get the services.

And either:

2. Restore a [DB backup to get some data](#database-backup-for-contributors), like the packages and some download number.

**Or:**

2. Run _Entity Framework_ migrations to get the database schema in place.
3. Run the _catalog importer_ from scratch.
4. Run the _daily download importer_ for a few days to get some data in.

### Run the jobs

Background jobs are done using [_Hangfire_](https://github.com/HangfireIO/Hangfire). It lives in the
_NuGetTrends.Scheduler_ project. Once you run it (i.e: `dotnet run`) its dashboard will be made available through [http://localhost:5003/](http://localhost:5003/).

The jobs are scheduled to run at an interval. You can browse the dashboard and trigger the jobs on demand though.

One of the jobs is to download the [NuGet's catalog](https://docs.microsoft.com/en-us/nuget/api/catalog-resource).
The second is to hit the nuget.org's API and get the current number of downloads for each package and store in the database.

### Daily Download Pipeline Architecture

The system collects daily download statistics for all NuGet packages (~400K+) using a distributed pipeline:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              PUBLISHER JOB                                  │
│                     (DailyDownloadPackageIdPublisher)                       │
│                                                                             │
│  1. Query PostgreSQL: packages not yet checked today                        │
│     - JOIN package_details_catalog_leafs with package_downloads             │
│     - Filter: LatestDownloadCountCheckedUtc < today                         │
│  2. Stream results (avoid loading 400K+ IDs into memory)                    │
│  3. Batch into groups of 25, serialize with MessagePack                     │
│  4. Publish to RabbitMQ queue: "daily-download"                             │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              RABBITMQ                                       │
│                                                                             │
│  Queue: daily-download (durable, 12h message TTL)                           │
│  Messages: batches of 25 package IDs (MessagePack serialized)               │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              WORKER(S)                                      │
│                         (DailyDownloadWorker)                               │
│                                                                             │
│  1. Consume batch from RabbitMQ                                             │
│  2. Parallel async requests to nuget.org API (fetch download counts)        │
│  3. Dual write:                                                             │
│     - ClickHouse: bulk insert to daily_downloads (time-series history)      │
│     - PostgreSQL: update package_downloads (latest count + timestamp)       │
└─────────────────────────────────────────────────────────────────────────────┘
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

### Website

The website is composed by two parts: An Angular SPA and an ASP.NET Core API. The API is at the root of `src/NuGetTrends.Web/` and the SPA is in the `Portal` sub-folder.

To run it locally:

**SPA** (src/NuGetTrends.Web/Portal)
1. Install the SPA dependencies: `npm install` (only the first time)
2. Run the SPA: `ng serve`

**API** (src/NuGetTrends.Web)
2. Run the API with `dotnet run`

The app can be browsed at:
- `http://localhost:5100`
- `https://localhost:5001`
- `http://localhost:4200` (default `ng serve` port)

> Note: You might need to see how to [install/trust the ASP.NET Core HTTPS development certificates](https://docs.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-5.0&tabs=visual-studio#trust-the-aspnet-core-https-development-certificate-on-windows-and-macos) (not required but nice to have)

### Database backup for contributors

We host a DB backup with the latest month's download counts. This will help you see some data on and fill the charts when developing.
The backup can be grabbed here: [https://contrib.nugettrends.com/nuget-trends-contrib.dump](https://contrib.nugettrends.com/nuget-trends-contrib.dump) and is a compressed _postgres_ database.

You can pull the repo and run `docker-compose up` at the root of the project to get the required services.
That will give you an empty _postgres_ database though.

For example, [with `pg_restore`](https://command-not-found.com/pg_restore).

```sh
# the db password hard coded in docker-compose.yml and appsettings.json config files:
export PGPASSWORD=PUg2rt6Pp8Arx7Z9FbgJLFvxEL7pZ2

# create an empty db
createdb nugettrends -U postgres -h localhost -p 5432

# restore backup
pg_restore -U postgres -d nugettrends -h localhost -p 5432 nuget-trends-contrib.dump
```

### Note to .NET SDK version and global.json

We lock the .NET SDK version via `global.json` to have a reference version and avoid surprises during CI.
If you don't have that exact version, usually anything with that major works just fine.
If you just want to quickly build, try just deleting `global.json`.

Please note we have a code of conduct, please follow it in all your interactions with the project.
