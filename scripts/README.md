# Migration Scripts

## migrate-daily-downloads-to-clickhouse.cs

A .NET 10 file-based script to migrate the `daily_downloads` table from PostgreSQL to ClickHouse.

### Features

- **Package-by-package migration** - Processes one package at a time, ordered by download count (largest first)
- **Uses existing indexes** - Leverages the primary key `(package_id, date)`, no new indexes needed
- **Memory efficient** - Streams data with batch inserts (100K rows per batch)
- **Resumable** - Progress tracked per package, can resume after interruption
- **Retry logic** - 3 failures on a single package stops the script
- **Three-level verification** - Total rows, package count, and sample package spot checks

### Prerequisites

- .NET 10 SDK
- PostgreSQL with the `daily_downloads` and `package_downloads` tables
- ClickHouse with the `nugettrends.daily_downloads` table created

### Environment Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `PG_CONNECTION_STRING` | PostgreSQL connection string | `Host=localhost;Database=nugettrends;Username=postgres;Password=xxx` |
| `CH_CONNECTION_STRING` | ClickHouse connection string | `Host=localhost;Port=8123;Database=nugettrends` |

### Usage

```bash
# Set environment variables
export PG_CONNECTION_STRING="Host=prod-db;Database=nugettrends;Username=user;Password=pass"
export CH_CONNECTION_STRING="Host=clickhouse;Port=8123;Database=nugettrends"

# Run migration
./migrate-daily-downloads-to-clickhouse.cs

# Or using dotnet directly
dotnet migrate-daily-downloads-to-clickhouse.cs
```

### Command Line Options

| Option | Default | Description |
|--------|---------|-------------|
| `--batch-size N` | 100000 | Rows per batch insert |
| `--save-every N` | 100 | Save progress every N packages |
| `--package PKG` | | Migrate only a specific package (for testing) |
| `--limit N` | | Migrate only first N packages (for testing) |
| `--verify-only` | false | Only run verification, skip migration |
| `--dry-run` | false | Show plan without executing |
| `--reset` | false | Clear progress file and start fresh |
| `--help`, `-h` | | Show help message |

### Examples

```bash
# Full migration (processes all packages, largest first)
dotnet migrate-daily-downloads-to-clickhouse.cs

# Test with a single package
dotnet migrate-daily-downloads-to-clickhouse.cs --package Newtonsoft.Json

# Migrate first 100 packages (for testing)
dotnet migrate-daily-downloads-to-clickhouse.cs --limit 100

# Verify only (no migration)
dotnet migrate-daily-downloads-to-clickhouse.cs --verify-only

# Dry run (show plan without executing)
dotnet migrate-daily-downloads-to-clickhouse.cs --dry-run

# Reset progress and start fresh
dotnet migrate-daily-downloads-to-clickhouse.cs --reset

# Use smaller batches (for memory-constrained environments)
dotnet migrate-daily-downloads-to-clickhouse.cs --batch-size 50000
```

### Progress Tracking

The script saves progress to `.migration-progress.json` in the script's directory. This allows:

- **Resume** - If the script is interrupted, it will resume from where it left off
- **Tracking** - See which packages have been migrated
- **Retry tracking** - Failed packages are tracked to enforce the 3-attempt limit

Progress file format:
```json
{
  "completedPackages": ["newtonsoft.json", "sentry", ...],
  "totalRowsMigrated": 123456789,
  "totalPackagesMigrated": 50000,
  "startedAt": "2024-12-27T10:00:00Z",
  "lastUpdatedAt": "2024-12-27T12:00:00Z",
  "currentPackage": null,
  "failedPackages": {}
}
```

To start completely fresh, use the `--reset` flag.

### How It Works

1. **Load package list** - Queries `package_downloads` table ordered by `latest_download_count DESC` (largest packages first)
2. **Filter completed** - Skips packages already in the progress file
3. **Migrate each package**:
   - Query: `SELECT LOWER(package_id), date, download_count FROM daily_downloads WHERE package_id = @pkg ORDER BY date`
   - Uses existing primary key index on `(package_id, date)` - no new indexes needed
   - Batch insert to ClickHouse (100K rows per batch)
4. **Save progress** - Every 100 packages (configurable with `--save-every`)
5. **Verify** - After migration, run three-level verification

### Why Package-by-Package?

The previous month-based approach required an index on the `date` column for efficient queries. With billions of rows, creating that index would take hours. 

The package-based approach uses the existing primary key `(package_id, date)`, which is already indexed.

### Error Handling

| Scenario | Behavior |
|----------|----------|
| Package fails | Retry up to 3 times, then stop |
| Connection lost | Retry batch, then fail package |
| Script interrupted | Resume from last saved progress |
| Duplicate rows | ClickHouse ReplacingMergeTree deduplicates automatically |

### Monitoring Progress

While the migration is running, you can monitor progress using these commands:

**Check rows and packages migrated (ClickHouse):**
```bash
clickhouse-client --query "SELECT 
    count() as rows_migrated, 
    count(DISTINCT package_id) as packages_migrated 
FROM nugettrends.daily_downloads"
```

**Compare to PostgreSQL total:**
```bash
psql -c "SELECT count(*) as total_rows FROM daily_downloads"
psql -c "SELECT count(*) as total_packages FROM package_downloads"
```

**Check if the migration process is running:**
```bash
ps aux | grep migrate-daily-downloads | grep -v grep
```

**Check process resource usage:**
```bash
ps aux | grep migrate-daily-downloads | grep -v grep | awk '{print "CPU time:", $10, "| Memory:", $4"%"}'
```

**Check progress file:**
```bash
# Summary
cat .migration-progress.json | jq '{
  packages: .totalPackagesMigrated,
  rows: .totalRowsMigrated,
  started: .startedAt,
  lastUpdate: .lastUpdatedAt
}'

# Check for failed packages
cat .migration-progress.json | jq '.failedPackages'

# Count completed packages
cat .migration-progress.json | jq '.completedPackages | length'
```

**Verify a specific package was migrated:**
```bash
clickhouse-client --query "SELECT 
    package_id,
    count() as rows, 
    min(date) as first_date, 
    max(date) as last_date,
    sum(download_count) as total_downloads
FROM nugettrends.daily_downloads 
WHERE package_id = 'newtonsoft.json'
GROUP BY package_id"
```

**Check migration rate (rows per package):**
```bash
clickhouse-client --query "SELECT 
    count() / count(DISTINCT package_id) as avg_rows_per_package,
    count() as total_rows,
    count(DISTINCT package_id) as total_packages
FROM nugettrends.daily_downloads"
```

### Output Example

```
NuGet Trends: PostgreSQL -> ClickHouse Migration
=================================================

Configuration:
  PostgreSQL: Host=prod-db;Database=nugettrends;Username=user;Password=***
  ClickHouse: Host=clickhouse;Port=8123;Database=nugettrends
  Batch Size: 100,000 rows
  Save Every: 100 packages

Loading Package List
====================
  Total packages: 412,345
  Already migrated: 50,000
  Remaining: 362,345

Migration Progress
==================
  [#########----------------] 50,000/412,345 (12.1%) | Newtonsoft.Json      | 1.2M rows | ETA: 4h 30m

Verification
============
[OK] Total rows match: 5.23B
[OK] Package count matches: 412,345
[OK] Sample packages: 10/10 verified

Migration Complete
==================
  Duration: 6h 42m 15s
  Rows migrated: 5,234,567,890
  Packages migrated: 412,345
  Avg throughput: 216,543 rows/sec
```


