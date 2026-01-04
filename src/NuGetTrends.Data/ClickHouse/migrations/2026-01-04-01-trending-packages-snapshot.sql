-- ClickHouse Schema Migration: 2026-01-04-01-trending-packages-snapshot
-- Pre-computed trending packages snapshot to avoid expensive real-time queries
-- See: https://github.com/dotnet/nuget-trends/issues/XXX
--
-- The trending packages query involves:
-- 1. Self-join on weekly_downloads (current vs previous week)
-- 2. Subquery to compute first_seen date for each package (scans entire table)
-- 3. Aggregation and sorting by growth rate
--
-- This is expensive (~seconds) because it scans 400K+ packages × 600+ weeks.
-- Since trending data only changes weekly, we pre-compute and store the results.
--
-- Refresh strategy:
-- - A Hangfire job runs weekly (e.g., Monday morning) to refresh the snapshot
-- - The job can also be triggered manually via Hangfire dashboard
-- - Historical snapshots are kept for analysis (partitioned by computed_week)

-- Snapshot table for pre-computed trending packages
CREATE TABLE IF NOT EXISTS nugettrends.trending_packages_snapshot
(
    -- The week this snapshot was computed for (Monday of the week)
    computed_week Date,
    -- Package ID (lowercase, same as weekly_downloads)
    package_id String,
    -- Downloads for the computed week
    current_week_downloads Int64,
    -- Downloads for the previous week
    previous_week_downloads Int64,
    -- Growth rate = (current - previous) / previous
    growth_rate Float64,
    -- Timestamp when this row was computed
    computed_at DateTime DEFAULT now()
)
ENGINE = ReplacingMergeTree(computed_at)
-- Keep historical snapshots, partitioned by year for easy cleanup
PARTITION BY toYear(computed_week)
-- Primary key: lookup by week, then by growth rate (descending via negative)
-- This allows efficient "top N trending for week X" queries
ORDER BY (computed_week, -growth_rate, package_id);

-- Note: The snapshot is populated by the application via INSERT...SELECT
-- See ClickHouseService.RefreshTrendingPackagesSnapshotAsync()
