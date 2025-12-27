# Migration Scripts

## migrate-daily-downloads-to-clickhouse.cs

Migrates the `daily_downloads` table from PostgreSQL to ClickHouse using a single streaming query.

### Prerequisites

- .NET 10 SDK
- PostgreSQL with `daily_downloads` table
- ClickHouse with `nugettrends.daily_downloads` table

### Environment Variables

```bash
export PG_CONNECTION_STRING="Host=localhost;Database=nugettrends;Username=postgres;Password=xxx"
export CH_CONNECTION_STRING="Host=localhost;Port=8123;Database=nugettrends"
```

### Usage

```bash
# Recreate ClickHouse table (clean slate)
clickhouse-client --host=$CH_HOST --password=$CH_PASS --multiquery -q "
DROP TABLE IF EXISTS nugettrends.daily_downloads;
CREATE TABLE nugettrends.daily_downloads (
  package_id String, date Date, download_count UInt64
) ENGINE = ReplacingMergeTree()
PARTITION BY toYear(date)
ORDER BY (package_id, date);
"

# Run migration
./migrate-daily-downloads-to-clickhouse.cs
```

### Options

| Option | Default | Description |
|--------|---------|-------------|
| `--batch-size N` | 1000000 | Rows per batch insert |
| `--verify-only` | false | Only run verification |
| `--dry-run` | false | Show plan without executing |

### How It Works

1. Streams entire PostgreSQL table: `SELECT LOWER(package_id), date, download_count FROM daily_downloads`
2. Batches rows (1M default) and inserts to ClickHouse
3. `LOWER()` normalizes package IDs on-the-fly
4. No resume logic - restart from scratch if interrupted (ClickHouse deduplicates)

### Monitoring

```bash
# Check progress
clickhouse-client --query "SELECT count() as rows, count(DISTINCT package_id) as packages FROM nugettrends.daily_downloads"
```
