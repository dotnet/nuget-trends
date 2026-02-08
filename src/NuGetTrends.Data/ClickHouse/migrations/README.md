# ClickHouse Migrations

This directory contains SQL migration scripts for ClickHouse schema changes.

## How It Works

The ClickHouse migration system works similarly to Entity Framework migrations:

1. **Migration Files**: SQL files in this directory are executed in alphabetical order by filename
2. **Tracking**: Applied migrations are tracked in the `clickhouse_migrations` table
3. **Automatic Execution**: Migrations run automatically when the Scheduler starts (see `Startup.cs`)
4. **Idempotent**: Running migrations multiple times is safe - only new migrations are applied

## File Naming Convention

Migrations should be named with the following pattern:
```
YYYY-MM-DD-NN-description.sql
```

Examples:
- `2025-12-26-01-init.sql`
- `2026-01-03-01-weekly-downloads-mv.sql`
- `2026-02-07-01-add-enrichment-to-trending-snapshot.sql`

The date and sequence number (NN) ensure proper ordering.

## Creating a New Migration

1. Create a new `.sql` file in this directory following the naming convention
2. Write your SQL statements (CREATE TABLE, ALTER TABLE, etc.)
3. Use `IF NOT EXISTS` or `IF EXISTS` clauses to make migrations idempotent
4. Test locally by restarting the Scheduler - it will automatically apply the new migration

## Migration Tracking

The `clickhouse_migrations` table stores:
- `migration_name`: Name of the SQL file that was executed
- `applied_at`: Timestamp when the migration was applied

## Implementation Details

- **Runner**: `ClickHouseMigrationRunner.cs` handles migration execution
- **Service**: Exposed via `IClickHouseService.RunMigrationsAsync()`
- **Startup**: Called in `Scheduler/Startup.cs` after EF Core migrations
- **Tests**: See `ClickHouseMigrationRunnerTests.cs` for test coverage
