-- ClickHouse Schema Migration: 2026-01-04-01-trending-packages-snapshot
-- Pre-computed trending packages snapshot to avoid expensive real-time queries
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
-- - Historical snapshots are kept for analysis (partitioned by week)

-- Snapshot table for pre-computed trending packages
-- Uses the final schema (column renames from the original design were folded in here
-- so that all migrations are idempotent and safe to re-run on existing databases).
CREATE TABLE IF NOT EXISTS nugettrends.trending_packages_snapshot
(
    -- The week this data represents (Monday of the reported week)
    -- This is the PREVIOUS week relative to when the job runs
    week Date,
    -- Package ID (lowercase, same as weekly_downloads)
    package_id String,
    -- Total downloads for the reported week
    week_downloads Int64,
    -- Total downloads for the comparison week (week before the reported week)
    comparison_week_downloads Int64,
    -- Growth rate = (week - comparison) / comparison
    growth_rate Float64,
    -- Timestamp when this row was computed
    computed_at DateTime DEFAULT now()
)
ENGINE = ReplacingMergeTree(computed_at)
-- Keep historical snapshots, partitioned by year for easy cleanup
PARTITION BY toYear(week)
-- Primary key uses stable columns for proper deduplication.
-- ReplacingMergeTree uses ORDER BY as the dedup key, so we can't include
-- growth_rate (which may change on retries). Sorting by growth_rate is done
-- at query time instead (ORDER BY growth_rate DESC in the SELECT).
ORDER BY (week, package_id);
