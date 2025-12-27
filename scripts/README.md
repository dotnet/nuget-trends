# Migration Scripts

## migrate-daily-downloads-to-clickhouse.cs

A .NET 10 file-based script to migrate the `daily_downloads` table from PostgreSQL to ClickHouse.

### Features

- Month-by-month migration with streaming (memory efficient for billions of rows)
- Progress tracking with resume support
- Retry logic: 3 failures on the same month stops the script
- Batch inserts to ClickHouse (default 100K rows per batch)
- Three-level verification:
  1. Total row count comparison
  2. Per-month row count comparison
  3. Sample package verification (5 most popular + 5 random)

### Prerequisites

- .NET 10 SDK
- PostgreSQL with the `daily_downloads` table
- ClickHouse with the `nugettrends.daily_downloads` table created

### Pre-migration: Create Index (Recommended)

For billions of rows, create an index on the `date` column to speed up chunked queries:

```sql
CREATE INDEX CONCURRENTLY idx_daily_downloads_date ON daily_downloads (date);
```

This runs without locking the table but takes time proportional to table size.

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
| `--start-month YYYY-MM` | Auto-detect | First month to migrate |
| `--end-month YYYY-MM` | Current month | Last month to migrate |
| `--batch-size N` | 100000 | Rows per batch insert |
| `--verify-only` | false | Only run verification, skip migration |
| `--dry-run` | false | Show plan without executing |
| `--reset` | false | Clear progress file and start fresh |
| `--help`, `-h` | | Show help message |

### Examples

```bash
# Migrate specific date range
dotnet migrate-daily-downloads-to-clickhouse.cs -- \
  --start-month 2010-01 \
  --end-month 2024-12

# Use smaller batches (for memory-constrained environments)
dotnet migrate-daily-downloads-to-clickhouse.cs -- --batch-size 50000

# Verify only (no migration)
dotnet migrate-daily-downloads-to-clickhouse.cs -- --verify-only

# Dry run (show plan without executing)
dotnet migrate-daily-downloads-to-clickhouse.cs -- --dry-run

# Reset progress and start fresh
dotnet migrate-daily-downloads-to-clickhouse.cs -- --reset
```

### Progress Tracking

The script saves progress to `.migration-progress.json` in the script's cache directory. This allows:

- **Resume**: If the script is interrupted, it will resume from the last completed month
- **Retry tracking**: Failed months are tracked to enforce the 3-attempt limit

To start completely fresh, use the `--reset` flag.

### Error Handling

| Scenario | Behavior |
|----------|----------|
| Month fails | Retry up to 3 times |
| Month fails 3 times | Stop script, preserve progress |
| Row count mismatch after insert | Delete ClickHouse data for month, retry |
| Connection lost | Retry batch, then fail month |

### Output Example

```
NuGet Trends: PostgreSQL → ClickHouse Migration
════════════════════════════════════════════════

Configuration:
  PostgreSQL: Host=prod-db;Database=nugettrends;Username=user;Password=***
  ClickHouse: Host=clickhouse;Port=8123;Database=nugettrends
  Batch Size: 100,000 rows

Detecting Data Range
════════════════════
  PostgreSQL: 5.23B total rows
  Date range: 2010-03-15 to 2024-12-25
  Will migrate: 2010-03 to 2024-12

Migration Progress
══════════════════
✓ [2010-03] Complete | 12.3K rows | 0s
✓ [2010-04] Complete | 45.6K rows | 1s
  [2024-12] ██████████████░░░░░░░░░░░░░░░░ 47% | 5.8M/12.3M | 52K/sec | ETA: 2m 15s

Verification
════════════
✓ Total rows match: 5.23B
✓ All 180 months match
✓ Sample packages: 10/10 verified

Migration Complete
══════════════════
  Duration: 6h 42m 15s
  Rows migrated: 5,234,567,890
  Avg throughput: 216,543 rows/sec
```
