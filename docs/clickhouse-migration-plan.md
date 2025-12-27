# ClickHouse Migration Plan for Daily Download Data

## Overview

This document outlines the plan to migrate the `daily_downloads` time-series data from PostgreSQL to ClickHouse. This migration will improve query performance for historical download trends while keeping PostgreSQL for relational data (package metadata, dependencies, etc.).

## Current State

### Data Model
- **Table**: `daily_downloads` in PostgreSQL
- **Schema**: `(package_id TEXT, date TIMESTAMP, download_count BIGINT)`
- **Primary Key**: Composite `(package_id, date)`
- **Data Pattern**: One row per package per day

### Data Flow
1. **Insert**: `DailyDownloadWorker` consumes from RabbitMQ, fetches download counts from NuGet API, inserts via EF Core
2. **Query**: `PackageController.GetDownloadHistory()` runs raw SQL aggregating by week using `DATE_TRUNC`, `AVG()`, and `generate_series()`

### Key Files
| Purpose | File |
|---------|------|
| Entity | `src/NuGetTrends.Data/DailyDownload.cs` |
| Query | `src/NuGetTrends.Data/NuGetTrendsContextExtensions.cs` |
| Insert | `src/NuGetTrends.Scheduler/DailyDownloadWorker.cs` |
| API | `src/NuGetTrends.Web/PackageController.cs` |
| Pending check | `src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs` |

---

## Target Architecture

### What Stays in PostgreSQL
- `package_details_catalog_leafs` - NuGet catalog metadata
- `package_downloads` - Latest download count (used for search, stores original case)
- `package_dependency_group`, `package_dependency` - Dependency graph
- `cursors` - Catalog sync cursor

### What Moves to ClickHouse
- `daily_downloads` - Historical download time-series data

### Architecture Diagram
```
┌─────────────────────────────────────────────────────────────────────┐
│                          PostgreSQL                                 │
│  - package_details_catalog_leafs (NuGet catalog metadata)           │
│  - package_downloads (latest count + LatestDownloadCountCheckedUtc) │
│  - package_dependency_group, package_dependency                     │
│  - cursors                                                          │
└─────────────────────────────────────────────────────────────────────┘
                              │
        Publisher queries     │    Worker updates
        unprocessed packages  │    package_downloads
                              ▼
┌─────────────────────────────────────────────────────────────────────┐
│                          ClickHouse                                 │
│                                                                     │
│  daily_downloads                                                    │
│  ├── ENGINE = ReplacingMergeTree()  (deduplicates by ORDER BY key) │
│  ├── PARTITION BY toYear(date)                                     │
│  └── ORDER BY (package_id, date)                                   │
│                                                                     │
│  Key Design: package_id stored LOWERCASE for case-insensitive      │
│  searching. Original case retrieved from PostgreSQL.                │
│                                                                     │
│  Query: Aggregate on-the-fly (no materialized views)               │
└─────────────────────────────────────────────────────────────────────┘
```

For the complete data pipeline architecture (publisher, RabbitMQ, workers), see [CONTRIBUTING.md](../CONTRIBUTING.md#daily-download-pipeline-architecture).

---

## ClickHouse Schema

### Design Decisions

| Decision | Rationale |
|----------|-----------|
| `package_id` stored **lowercase only** | Enables case-insensitive queries without `LOWER()` function. Original case is available from PostgreSQL `package_downloads.PackageId` |
| `String` for package_id | NuGet has 400K+ packages, exceeding LowCardinality's recommended 10K threshold. Native string compression with ZSTD is efficient enough. |
| `Date` instead of `DateTime` | Daily granularity is sufficient, saves storage |
| `UInt64` for download_count | Unsigned, matches expected values |
| `PARTITION BY toYear(date)` | Yearly partitions for optional data management (e.g., DROP PARTITION to remove old years). Yearly preferred over monthly to avoid INSERT issues with max_partitions_per_insert_block limit. |
| `ORDER BY (package_id, date)` | Optimizes the primary query pattern (filter by package_id, range on date) |
| `ReplacingMergeTree` engine | Deduplicates rows with same `(package_id, date)` during background merges - safety net for duplicate inserts |
| No materialized views | Single-package queries are fast enough without pre-aggregation |
| Keep all data forever | No retention policy, preserve full history |

### Schema Definition

See [`src/NuGetTrends.Data/ClickHouse/migrations/2024-12-26-1-init.sql`](../src/NuGetTrends.Data/ClickHouse/migrations/2024-12-26-1-init.sql) for the full schema.

Key points:
- `ENGINE = ReplacingMergeTree()` - deduplicates rows with same `(package_id, date)` during background merges
- `PARTITION BY toYear(date)` - yearly partitions for optional data management
- `ORDER BY (package_id, date)` - optimizes filter by package_id + range on date
- Package IDs stored **lowercase** for case-insensitive queries

### Example Queries

**Weekly aggregation (replaces current PostgreSQL query):**
```sql
SELECT 
    toMonday(date) AS week,
    avg(download_count) AS download_count
FROM nugettrends.daily_downloads
WHERE package_id = lower('Sentry')  -- Always query with lowercase
  AND date >= today() - INTERVAL 12 MONTH
GROUP BY week
ORDER BY week;
```

**Daily granularity (for short time ranges):**
```sql
SELECT date, download_count
FROM nugettrends.daily_downloads
WHERE package_id = lower('Sentry')
  AND date >= today() - INTERVAL 30 DAY
ORDER BY date;
```

**Monthly aggregation (for 10+ year queries):**
```sql
SELECT 
    toStartOfMonth(date) AS month,
    avg(download_count) AS download_count
FROM nugettrends.daily_downloads
WHERE package_id = lower('Sentry')
  AND date >= today() - INTERVAL 10 YEAR
GROUP BY month
ORDER BY month;
```

**Check packages processed today:**
```sql
SELECT DISTINCT package_id
FROM nugettrends.daily_downloads
WHERE date = today();
```

---

## Schema Migrations

ClickHouse migrations are managed via versioned SQL files in `src/NuGetTrends.Data/ClickHouse/migrations/`.

### Naming Convention
```
YYYY-MM-DD-N-description.sql
```
- `YYYY-MM-DD`: Date the migration was created
- `N`: Sequence number for that day (1, 2, 3...)
- `description`: Brief description of changes

### Example
```
2024-12-26-1-init.sql           # Initial schema
2024-12-27-1-add-index.sql      # Add secondary index
```

### Applying Migrations

**Local Development (Docker):**
- SQL files in `migrations/` are automatically executed on container startup via docker-entrypoint

**Production:**
- Run migrations manually via `clickhouse-client` or CI pipeline
- Track applied migrations manually (or via a simple `schema_migrations` table if needed later)

---

## Code Changes

All code changes have been implemented. See the key files:

| Component | File |
|-----------|------|
| ClickHouse Service Interface | [`src/NuGetTrends.Data/ClickHouse/IClickHouseService.cs`](../src/NuGetTrends.Data/ClickHouse/IClickHouseService.cs) |
| ClickHouse Service Implementation | [`src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs`](../src/NuGetTrends.Data/ClickHouse/ClickHouseService.cs) |
| Worker (batch insert to ClickHouse) | [`src/NuGetTrends.Scheduler/DailyDownloadWorker.cs`](../src/NuGetTrends.Scheduler/DailyDownloadWorker.cs) |
| Publisher (PostgreSQL join filter) | [`src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs`](../src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs) |
| API Controller | [`src/NuGetTrends.Web/PackageController.cs`](../src/NuGetTrends.Web/PackageController.cs) |
| DI Registration (Scheduler) | [`src/NuGetTrends.Scheduler/Startup.cs`](../src/NuGetTrends.Scheduler/Startup.cs) |
| DI Registration (Web) | [`src/NuGetTrends.Web/Program.cs`](../src/NuGetTrends.Web/Program.cs) |

### Configuration

Connection string is configured under `ConnectionStrings:ClickHouse` in appsettings.json:

```json
{
  "ConnectionStrings": {
    "NuGetTrends": "Host=localhost;Database=nugettrends;...",
    "ClickHouse": "Host=localhost;Port=8123;Database=nugettrends"
  }
}
```

### Deduplication Strategy

The publisher uses a PostgreSQL LEFT JOIN to filter packages where `LatestDownloadCountCheckedUtc < today`. This prevents re-processing packages that have already been checked today.

As a safety net, ClickHouse uses `ReplacingMergeTree` engine which deduplicates rows with the same `(package_id, date)` during background merges.

See [CONTRIBUTING.md](../CONTRIBUTING.md#daily-download-pipeline-architecture) for the complete pipeline architecture diagram.

### Cleanup (Phase 5)

After migration is complete:
- Remove `DailyDownload` entity from `NuGetTrendsContext`
- Remove `NuGetTrendsContextExtensions.GetDailyDownloads()`
- Create EF migration to drop `daily_downloads` table and `pending_packages_daily_downloads` view

---

## Local Development Setup

### Docker Compose

ClickHouse is included in `docker-compose.yml`:

```yaml
clickhouse:
  image: clickhouse/clickhouse-server:24
  restart: "no"
  environment:
    CLICKHOUSE_DB: nugettrends
    CLICKHOUSE_USER: default
    CLICKHOUSE_PASSWORD: ""
  ports:
    - "8123:8123"   # HTTP interface
    - "9000:9000"   # Native interface
  volumes:
    - clickhouse-data:/var/lib/clickhouse
    - ./src/NuGetTrends.Data/ClickHouse/migrations:/docker-entrypoint-initdb.d
```

### Running Locally

```bash
# Start all services including ClickHouse
docker-compose up -d

# Verify ClickHouse is running
curl http://localhost:8123/ping

# Connect to ClickHouse CLI
docker-compose exec clickhouse clickhouse-client

# Run a test query
SELECT count() FROM daily_downloads;
```

---

## Data Migration

Use the streaming migration script: [`scripts/migrate-daily-downloads-to-clickhouse.cs`](../scripts/README.md)

The script streams the entire PostgreSQL table in a single query, applies `LOWER()` to normalize package IDs, and batch-inserts to ClickHouse. See the script README for usage details.

---

## Migration Steps

### Phase 1: Infrastructure Setup
- [x] Add ClickHouse to docker-compose.yml
- [x] Create migrations directory with init SQL
- [ ] Deploy ClickHouse to Kubernetes (production)
- [ ] Configure secrets/connection strings

### Phase 2: Code Implementation
- [x] Add `ClickHouse.Client` NuGet package
- [x] Implement `IClickHouseService` and `ClickHouseService`
- [x] Register in DI containers (Scheduler and Web)
- [x] Add ClickHouse configuration to appsettings.json files (using `ConnectionStrings:ClickHouse`)
- [x] Modify `DailyDownloadWorker` for batch inserts to ClickHouse
- [x] Modify `PackageController` to read from ClickHouse
- [x] Modify `DailyDownloadPackageIdPublisher` to use PostgreSQL JOIN filter (replaces ClickHouse check)
- [x] Use `ReplacingMergeTree` engine for deduplication safety net
- [x] Write integration tests (ClickHouseServiceTests with Testcontainers)

### Phase 3: Data Migration
- [x] Run streaming migration script (`scripts/migrate-daily-downloads-to-clickhouse.cs`)
- [ ] Verify row counts match
- [ ] Spot-check data integrity for sample packages

### Phase 4: Deployment & Cutover
- [ ] Deploy updated Scheduler (writes to ClickHouse)
- [ ] Verify new data is being inserted
- [ ] Deploy updated Web (reads from ClickHouse)
- [ ] Monitor for errors
- [ ] Compare query results between old and new for validation

### Phase 5: Cleanup
- [ ] Remove `daily_downloads` table from PostgreSQL
- [ ] Drop `pending_packages_daily_downloads` view
- [ ] Remove EF Core entity and migration code
- [ ] Update documentation

---

## Rollback Plan

If issues occur after cutover:

1. **Revert code**: Deploy previous versions of Scheduler and Web
2. **Data**: PostgreSQL data remains intact until Phase 5 cleanup
3. **Dual-write option**: Consider implementing dual-write during Phase 4 for safer rollback

---

## References

- [ClickHouse MergeTree Engine](https://clickhouse.com/docs/en/engines/table-engines/mergetree-family/mergetree)
- [ClickHouse.Client .NET Library](https://github.com/DarkWanderer/ClickHouse.Client)
- [ClickHouse Best Practices](https://clickhouse.com/docs/en/guides/best-practices)
- [ClickHouse Bulk Insert](https://github.com/DarkWanderer/ClickHouse.Client#bulk-insert)
