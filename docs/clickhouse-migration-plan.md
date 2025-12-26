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
- `package_downloads` - Latest download count (used for search)
- `package_dependency_group`, `package_dependency` - Dependency graph
- `cursors` - Catalog sync cursor

### What Moves to ClickHouse
- `daily_downloads` - Historical download time-series data

### Architecture Diagram
```
┌─────────────────────────────────────────────────────────────┐
│                       PostgreSQL                             │
│  - package_details_catalog_leafs                            │
│  - package_downloads (for search autocomplete)              │
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
│  Query: Aggregate on-the-fly (no materialized views)        │
└─────────────────────────────────────────────────────────────┘
```

---

## ClickHouse Schema

```sql
CREATE TABLE daily_downloads
(
    package_id LowCardinality(String),
    date Date,
    download_count UInt64
)
ENGINE = MergeTree()
PARTITION BY toYYYYMM(date)
ORDER BY (package_id, date)
SETTINGS index_granularity = 8192;
```

### Schema Design Decisions

| Decision | Rationale |
|----------|-----------|
| `LowCardinality(String)` for package_id | Dictionary encoding for repeated strings, reduces storage and speeds up filtering |
| `Date` instead of `DateTime` | Daily granularity is sufficient, saves storage |
| `UInt64` for download_count | Unsigned, matches expected values |
| `PARTITION BY toYYYYMM(date)` | Monthly partitions for efficient date range pruning |
| `ORDER BY (package_id, date)` | Optimizes the primary query pattern (filter by package_id, range on date) |
| No materialized views | Single-package queries are fast enough without pre-aggregation |

### Example Queries

**Weekly aggregation (replaces current PostgreSQL query):**
```sql
SELECT 
    toMonday(date) AS week,
    avg(download_count) AS download_count
FROM daily_downloads
WHERE package_id = 'Sentry'
  AND date >= today() - INTERVAL 12 MONTH
GROUP BY week
ORDER BY week;
```

**Daily granularity (for short time ranges):**
```sql
SELECT date, download_count
FROM daily_downloads
WHERE package_id = 'Sentry'
  AND date >= today() - INTERVAL 30 DAY
ORDER BY date;
```

**Monthly aggregation (for 10+ year queries):**
```sql
SELECT 
    toStartOfMonth(date) AS month,
    avg(download_count) AS download_count
FROM daily_downloads
WHERE package_id = 'Sentry'
  AND date >= today() - INTERVAL 10 YEAR
GROUP BY month
ORDER BY month;
```

---

## Code Changes

### 1. Add ClickHouse Client Package

**File**: `src/NuGetTrends.Data/NuGetTrends.Data.csproj`
```xml
<PackageReference Include="ClickHouse.Client" Version="7.*" />
```

### 2. New ClickHouse Service

**New file**: `src/NuGetTrends.Data/ClickHouseService.cs`

```csharp
public interface IClickHouseService
{
    Task InsertDailyDownloadAsync(string packageId, DateOnly date, long downloadCount, CancellationToken ct = default);
    Task<List<DailyDownloadResult>> GetWeeklyDownloadsAsync(string packageId, int months, CancellationToken ct = default);
    Task<HashSet<string>> GetPackagesWithDownloadsForDateAsync(DateOnly date, CancellationToken ct = default);
}

public class ClickHouseService : IClickHouseService
{
    private readonly ClickHouseConnection _connection;
    
    // Implementation details...
}
```

### 3. Modify DailyDownloadWorker

**File**: `src/NuGetTrends.Scheduler/DailyDownloadWorker.cs`

```csharp
// Before (lines 239-244):
context.DailyDownloads.Add(new DailyDownload
{
    PackageId = packageMetadata.Identity.Id,
    Date = DateTime.UtcNow.Date,
    DownloadCount = packageMetadata.DownloadCount
});

// After:
await _clickHouseService.InsertDailyDownloadAsync(
    packageMetadata.Identity.Id,
    DateOnly.FromDateTime(DateTime.UtcNow),
    packageMetadata.DownloadCount ?? 0,
    cancellationToken);
```

### 4. Modify PackageController

**File**: `src/NuGetTrends.Web/PackageController.cs`

```csharp
// Before (line 44):
var downloads = await context.GetDailyDownloads(id, months);

// After:
var downloads = await _clickHouseService.GetWeeklyDownloadsAsync(id, months, cancellationToken);
```

### 5. Modify DailyDownloadPackageIdPublisher

**File**: `src/NuGetTrends.Scheduler/DailyDownloadPackageIdPublisher.cs`

The current `pending_packages_daily_downloads` VIEW in PostgreSQL joins with `daily_downloads`. Options:

**Option A - Query ClickHouse for processed packages:**
```csharp
// Get all package IDs from PostgreSQL
var allPackages = await context.PackageDetailsCatalogLeafs
    .Select(p => p.PackageId)
    .Distinct()
    .ToListAsync(ct);

// Get packages already processed today from ClickHouse
var processedToday = await _clickHouseService.GetPackagesWithDownloadsForDateAsync(
    DateOnly.FromDateTime(DateTime.UtcNow), ct);

// Find pending
var pending = allPackages.Where(p => !processedToday.Contains(p));
```

**Option B - Keep the VIEW, replicate minimal data back:**
Not recommended due to added complexity.

### 6. Configuration

**File**: `src/NuGetTrends.Scheduler/appsettings.json` (and Web)

```json
{
  "ClickHouse": {
    "ConnectionString": "Host=localhost;Port=8123;Database=nugettrends"
  }
}
```

### 7. Remove PostgreSQL daily_downloads

After migration is complete:
- Remove `DailyDownload` entity from `NuGetTrendsContext`
- Remove `NuGetTrendsContextExtensions.GetDailyDownloads()`
- Create EF migration to drop `daily_downloads` table and `pending_packages_daily_downloads` view

---

## Infrastructure Changes

### Option A: ClickHouse on GCE VM (Similar to PostgreSQL)

Add to `nuget-trends-infra/main.tf`:
```hcl
resource "google_compute_instance" "clickhouse" {
  name         = "${local.name_prefix}-clickhouse"
  machine_type = "e2-standard-2"
  zone         = var.zone

  boot_disk {
    initialize_params {
      image = "debian-cloud/debian-11"
      size  = 100  # GB - adjust based on data volume
    }
  }

  network_interface {
    network    = google_compute_network.vpc.name
    subnetwork = google_compute_subnetwork.subnet.name
  }

  metadata_startup_script = <<-EOF
    #!/bin/bash
    apt-get update
    apt-get install -y apt-transport-https ca-certificates dirmngr
    apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv 8919F6BD2B48D754
    echo "deb https://packages.clickhouse.com/deb stable main" > /etc/apt/sources.list.d/clickhouse.list
    apt-get update
    apt-get install -y clickhouse-server clickhouse-client
    
    # Configure to listen on all interfaces
    sed -i 's/<!-- <listen_host>::<\/listen_host> -->/<listen_host>::<\/listen_host>/' /etc/clickhouse-server/config.xml
    
    systemctl start clickhouse-server
    systemctl enable clickhouse-server
    
    # Create database
    clickhouse-client --query "CREATE DATABASE IF NOT EXISTS nugettrends"
  EOF

  service_account {
    email  = google_service_account.gke_sa.email
    scopes = ["cloud-platform"]
  }

  tags = ["clickhouse"]
}

resource "google_compute_firewall" "clickhouse" {
  name    = "${local.name_prefix}-clickhouse"
  network = google_compute_network.vpc.name

  allow {
    protocol = "tcp"
    ports    = ["8123", "9000"]  # HTTP and native protocols
  }

  source_ranges = ["10.0.0.0/8", "10.1.0.0/16"]
  target_tags   = ["clickhouse"]
}
```

### Option B: ClickHouse Cloud (Managed)

- Sign up at https://clickhouse.cloud
- Create a service in the same region (us-central1)
- Use the provided connection string
- No Terraform changes needed for the service itself
- May need VPC peering or allow-listing GKE egress IPs

### Option C: ClickHouse in Kubernetes

Add `k8s/clickhouse-deployment.yaml`:
```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: clickhouse
spec:
  serviceName: clickhouse
  replicas: 1
  selector:
    matchLabels:
      app: clickhouse
  template:
    metadata:
      labels:
        app: clickhouse
    spec:
      containers:
      - name: clickhouse
        image: clickhouse/clickhouse-server:24.1
        ports:
        - containerPort: 8123
        - containerPort: 9000
        volumeMounts:
        - name: data
          mountPath: /var/lib/clickhouse
  volumeClaimTemplates:
  - metadata:
      name: data
    spec:
      accessModes: ["ReadWriteOnce"]
      resources:
        requests:
          storage: 100Gi
---
apiVersion: v1
kind: Service
metadata:
  name: clickhouse
spec:
  selector:
    app: clickhouse
  ports:
  - name: http
    port: 8123
  - name: native
    port: 9000
  type: ClusterIP
```

---

## Migration Steps

### Phase 1: Infrastructure Setup
- [ ] Choose deployment option (GCE VM / ClickHouse Cloud / K8s)
- [ ] Deploy ClickHouse instance
- [ ] Create `nugettrends` database
- [ ] Create `daily_downloads` table
- [ ] Configure firewall/network access
- [ ] Add connection string to secrets

### Phase 2: Code Implementation
- [ ] Add `ClickHouse.Client` NuGet package
- [ ] Implement `IClickHouseService`
- [ ] Register in DI containers (Scheduler and Web)
- [ ] Modify `DailyDownloadWorker` to write to ClickHouse
- [ ] Modify `PackageController` to read from ClickHouse
- [ ] Modify `DailyDownloadPackageIdPublisher` pending logic
- [ ] Add configuration options
- [ ] Write unit/integration tests

### Phase 3: Data Migration
- [ ] Take PostgreSQL backup/snapshot
- [ ] Export data from PostgreSQL:
  ```bash
  psql -h <host> -U nugettrends -d nugettrends \
    -c "COPY daily_downloads TO STDOUT WITH CSV" > daily_downloads.csv
  ```
- [ ] Import to ClickHouse:
  ```bash
  clickhouse-client --query \
    "INSERT INTO daily_downloads FORMAT CSV" < daily_downloads.csv
  ```
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

## Open Questions

- [ ] Estimated data volume? (affects storage sizing and migration time)
- [ ] Preferred deployment option for ClickHouse?
- [ ] Retention policy - keep all historical data indefinitely?
- [ ] Do we need adaptive query granularity (daily/weekly/monthly based on time range)?

---

## References

- [ClickHouse MergeTree Engine](https://clickhouse.com/docs/en/engines/table-engines/mergetree-family/mergetree)
- [ClickHouse.Client .NET Library](https://github.com/DarkWanderer/ClickHouse.Client)
- [ClickHouse Best Practices](https://clickhouse.com/docs/en/guides/best-practices)
