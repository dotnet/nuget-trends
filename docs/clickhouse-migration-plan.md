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
┌─────────────────────────────────────────────────────────────┐
│                       PostgreSQL                             │
│  - package_details_catalog_leafs                            │
│  - package_downloads (for search autocomplete + orig case)  │
│  - package_dependency_group, package_dependency             │
│  - cursors                                                  │
└─────────────────────────────────────────────────────────────┘
                              │
        Publisher queries     │    Worker updates
        package list          │    package_downloads
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                       ClickHouse                             │
│                                                              │
│  daily_downloads                                            │
│  ├── ENGINE = MergeTree()                                   │
│  ├── PARTITION BY toYYYYMM(date)                           │
│  └── ORDER BY (package_id, date)                           │
│                                                              │
│  Key Design: package_id stored LOWERCASE for case-insensitive│
│  searching. Original case retrieved from PostgreSQL.         │
│                                                              │
│  Query: Aggregate on-the-fly (no materialized views)        │
└─────────────────────────────────────────────────────────────┘
```

---

## ClickHouse Schema

### Design Decisions

| Decision | Rationale |
|----------|-----------|
| `package_id` stored **lowercase only** | Enables case-insensitive queries without `LOWER()` function. Original case is available from PostgreSQL `package_downloads.PackageId` |
| `String` for package_id | NuGet has 400K+ packages, exceeding LowCardinality's recommended 10K threshold. Native string compression with ZSTD is efficient enough. |
| `Date` instead of `DateTime` | Daily granularity is sufficient, saves storage |
| `UInt64` for download_count | Unsigned, matches expected values |
| `PARTITION BY toYYYYMM(date)` | Monthly partitions for efficient date range pruning |
| `ORDER BY (package_id, date)` | Optimizes the primary query pattern (filter by package_id, range on date) |
| No materialized views | Single-package queries are fast enough without pre-aggregation |
| Keep all data forever | No retention policy, preserve full history |

### Schema Definition

```sql
CREATE TABLE IF NOT EXISTS daily_downloads
(
    -- Package ID stored in LOWERCASE for case-insensitive searching
    -- Original case is available from PostgreSQL package_downloads table
    package_id String,
    date Date,
    download_count UInt64
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(date)
ORDER BY (package_id, date)
SETTINGS index_granularity = 8192;
```

### Example Queries

**Weekly aggregation (replaces current PostgreSQL query):**
```sql
SELECT 
    toMonday(date) AS week,
    avg(download_count) AS download_count
FROM daily_downloads
WHERE package_id = lower('Sentry')  -- Always query with lowercase
  AND date >= today() - INTERVAL 12 MONTH
GROUP BY week
ORDER BY week;
```

**Daily granularity (for short time ranges):**
```sql
SELECT date, download_count
FROM daily_downloads
WHERE package_id = lower('Sentry')
  AND date >= today() - INTERVAL 30 DAY
ORDER BY date;
```

**Monthly aggregation (for 10+ year queries):**
```sql
SELECT 
    toStartOfMonth(date) AS month,
    avg(download_count) AS download_count
FROM daily_downloads
WHERE package_id = lower('Sentry')
  AND date >= today() - INTERVAL 10 YEAR
GROUP BY month
ORDER BY month;
```

**Check packages processed today:**
```sql
SELECT DISTINCT package_id
FROM daily_downloads
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

### 1. Add ClickHouse Client Package

**File**: `src/NuGetTrends.Data/NuGetTrends.Data.csproj`
```xml
<PackageReference Include="ClickHouse.Client" Version="7.*" />
```

### 2. ClickHouse Configuration

**File**: `src/NuGetTrends.Data/ClickHouseOptions.cs`
```csharp
public class ClickHouseOptions
{
    public const string SectionName = "ClickHouse";
    public string ConnectionString { get; set; } = "Host=localhost;Port=8123;Database=nugettrends";
}
```

### 3. New ClickHouse Service

**New file**: `src/NuGetTrends.Data/IClickHouseService.cs`

```csharp
public interface IClickHouseService
{
    /// <summary>
    /// Batch insert daily downloads. Package IDs are automatically lowercased.
    /// </summary>
    Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default);
    
    /// <summary>
    /// Get weekly download aggregations for a package.
    /// </summary>
    /// <param name="packageId">Package ID (will be lowercased for query)</param>
    /// <param name="months">Number of months to look back</param>
    Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId, 
        int months, 
        CancellationToken ct = default);
    
    /// <summary>
    /// Get package IDs that have downloads recorded for a specific date.
    /// Returns lowercase package IDs.
    /// </summary>
    Task<HashSet<string>> GetPackagesWithDownloadsForDateAsync(
        DateOnly date, 
        CancellationToken ct = default);
}
```

**New file**: `src/NuGetTrends.Data/ClickHouseService.cs`

```csharp
public class ClickHouseService : IClickHouseService
{
    private readonly ClickHouseConnection _connection;
    private readonly ILogger<ClickHouseService> _logger;

    public ClickHouseService(IOptions<ClickHouseOptions> options, ILogger<ClickHouseService> logger)
    {
        _connection = new ClickHouseConnection(options.Value.ConnectionString);
        _logger = logger;
    }

    public async Task InsertDailyDownloadsAsync(
        IEnumerable<(string PackageId, DateOnly Date, long DownloadCount)> downloads,
        CancellationToken ct = default)
    {
        var data = downloads.Select(d => new object[] 
        { 
            d.PackageId.ToLowerInvariant(),  // Always store lowercase
            d.Date, 
            d.DownloadCount 
        });

        await using var bulkCopy = new ClickHouseBulkCopy(_connection)
        {
            DestinationTableName = "daily_downloads",
            ColumnNames = new[] { "package_id", "date", "download_count" }
        };

        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(data, ct);
    }

    public async Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(
        string packageId, 
        int months, 
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT 
                toMonday(date) AS week,
                avg(download_count) AS download_count
            FROM daily_downloads
            WHERE package_id = {packageId:String}
              AND date >= today() - INTERVAL {months:Int32} MONTH
            GROUP BY week
            ORDER BY week";

        await _connection.OpenAsync(ct);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("packageId", packageId.ToLowerInvariant());
        cmd.AddParameter("months", months);

        var results = new List<DailyDownloadResult>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(new DailyDownloadResult
            {
                Week = reader.GetDateTime(0),
                DownloadCount = reader.IsDBNull(1) ? null : (long?)reader.GetDouble(1)
            });
        }
        return results;
    }

    public async Task<HashSet<string>> GetPackagesWithDownloadsForDateAsync(
        DateOnly date, 
        CancellationToken ct = default)
    {
        const string sql = @"
            SELECT DISTINCT package_id
            FROM daily_downloads
            WHERE date = {date:Date}";

        await _connection.OpenAsync(ct);
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddParameter("date", date);

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }
}
```

### 4. Modify DailyDownloadWorker for Batch Inserts

**File**: `src/NuGetTrends.Scheduler/DailyDownloadWorker.cs`

```csharp
// Before: Insert one at a time via EF Core
context.DailyDownloads.Add(new DailyDownload
{
    PackageId = packageMetadata.Identity.Id,
    Date = DateTime.UtcNow.Date,
    DownloadCount = packageMetadata.DownloadCount
});

// After: Collect all downloads, then batch insert to ClickHouse
private async Task UpdateDownloadCount(IList<string> packageIds, ISpan parentSpan)
{
    // ... fetch package metadata (existing code) ...

    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var downloads = new List<(string PackageId, DateOnly Date, long DownloadCount)>();

    for (var i = 0; i < tasks.Count; i++)
    {
        var packageMetadata = tasks[i].Result;
        if (packageMetadata?.DownloadCount != null)
        {
            downloads.Add((
                packageMetadata.Identity.Id,
                today,
                packageMetadata.DownloadCount.Value
            ));

            // Update PackageDownloads in PostgreSQL (existing code for search/autocomplete)
            // ...
        }
    }

    // Batch insert to ClickHouse
    if (downloads.Count > 0)
    {
        var chInsertSpan = parentSpan.StartChild("clickhouse.insert", "Batch insert daily downloads");
        chInsertSpan.SetTag("count", downloads.Count.ToString());
        await _clickHouseService.InsertDailyDownloadsAsync(downloads, _cancellationTokenSource.Token);
        chInsertSpan.Finish();
    }

    // Save PostgreSQL changes (PackageDownloads updates)
    await context.SaveChangesAsync(_cancellationTokenSource.Token);
}
```

### 5. Modify PackageController

**File**: `src/NuGetTrends.Web/PackageController.cs`

```csharp
public class PackageController(
    NuGetTrendsContext context,
    IClickHouseService clickHouseService) : ControllerBase
{
    [HttpGet("history/{id}")]
    public async Task<IActionResult> GetDownloadHistory(
        [FromRoute] string id,
        CancellationToken cancellationToken,
        [FromQuery] int months = 3)
    {
        // Validate package exists in PostgreSQL (also gets original case if needed)
        if (!await context.PackageDownloads
            .AnyAsync(p => p.PackageIdLowered == id.ToLower(CultureInfo.InvariantCulture), cancellationToken))
        {
            return NotFound();
        }

        // Query ClickHouse for download history
        var downloads = await clickHouseService.GetWeeklyDownloadsAsync(id, months, cancellationToken);
        
        return Ok(new { Id = id, Downloads = downloads });
    }
}
```

### 6. Modify DailyDownloadPackageIdPublisher

**File**: `src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs`

Replace PostgreSQL VIEW query with ClickHouse check:

```csharp
public class DailyDownloadPackageIdPublisher(
    IConnectionFactory connectionFactory,
    NuGetTrendsContext context,
    IClickHouseService clickHouseService,  // Add this
    IHub hub,
    ILogger<DailyDownloadPackageIdPublisher> logger)
{
    public async Task Import(IJobCancellationToken token)
    {
        // Get all package IDs from PostgreSQL
        var allPackages = await context.PackageDetailsCatalogLeafs
            .Select(p => p.PackageId)
            .Distinct()
            .ToListAsync();

        // Get packages already processed today from ClickHouse
        var processedToday = await clickHouseService.GetPackagesWithDownloadsForDateAsync(
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Find pending (case-insensitive comparison since CH stores lowercase)
        var pending = allPackages
            .Where(p => !processedToday.Contains(p.ToLowerInvariant()))
            .ToList();

        // Queue pending packages (existing batching logic)
        // ...
    }
}
```

### 7. Configuration

**File**: `src/NuGetTrends.Scheduler/appsettings.json`
```json
{
  "ClickHouse": {
    "ConnectionString": "Host=localhost;Port=8123;Database=nugettrends"
  }
}
```

**File**: `src/NuGetTrends.Web/appsettings.json`
```json
{
  "ClickHouse": {
    "ConnectionString": "Host=localhost;Port=8123;Database=nugettrends"
  }
}
```

### 8. DI Registration

**File**: `src/NuGetTrends.Scheduler/Startup.cs` and `src/NuGetTrends.Web/Program.cs`
```csharp
services.Configure<ClickHouseOptions>(configuration.GetSection(ClickHouseOptions.SectionName));
services.AddSingleton<IClickHouseService, ClickHouseService>();
```

### 9. Remove PostgreSQL daily_downloads

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

### Export from PostgreSQL (with lowercase transformation)

```bash
psql -h <host> -U nugettrends -d nugettrends \
  -c "COPY (SELECT LOWER(package_id), date::date, download_count FROM daily_downloads) TO STDOUT WITH CSV" \
  > daily_downloads.csv
```

### Import to ClickHouse

```bash
clickhouse-client --query \
  "INSERT INTO daily_downloads FORMAT CSV" < daily_downloads.csv
```

### Verify Migration

```sql
-- Compare row counts
-- PostgreSQL:
SELECT count(*) FROM daily_downloads;

-- ClickHouse:
SELECT count() FROM daily_downloads;

-- Spot check specific packages
SELECT package_id, min(date), max(date), count()
FROM daily_downloads
WHERE package_id = 'sentry'
GROUP BY package_id;
```

---

## Migration Steps

### Phase 1: Infrastructure Setup
- [x] Add ClickHouse to docker-compose.yml
- [x] Create migrations directory with init SQL
- [ ] Deploy ClickHouse to Kubernetes (production)
- [ ] Configure secrets/connection strings

### Phase 2: Code Implementation
- [x] Add `ClickHouse.Client` NuGet package
- [x] Implement `ClickHouseOptions` configuration class
- [x] Implement `IClickHouseService` and `ClickHouseService`
- [x] Register in DI containers (Scheduler and Web)
- [x] Add ClickHouse configuration to appsettings.json files
- [x] Modify `DailyDownloadWorker` for batch inserts to ClickHouse
- [x] Modify `PackageController` to read from ClickHouse
- [x] Modify `DailyDownloadPackageIdPublisher` pending logic
- [ ] Write unit/integration tests

### Phase 3: Data Migration
- [ ] Take PostgreSQL backup/snapshot
- [ ] Export data from PostgreSQL (with lowercase transformation)
- [ ] Import to ClickHouse
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
