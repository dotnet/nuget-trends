-- ClickHouse Schema Migration: 2026-01-04-02-fix-trending-snapshot-timing
-- Fix trending packages snapshot timing and rename columns for clarity
--
-- Problem:
-- The previous implementation compared "current week" (partial, just started) vs "previous week".
-- On Monday morning, "current week" only has a few hours of data, making the comparison skewed.
--
-- Solution:
-- Compare "last week" (complete) vs "week before" (complete).
-- On Monday at 2 AM UTC, we compute trending for the week that just ended.
--
-- Column renames for clarity:
-- - computed_week: The week this snapshot was computed FOR (the week being reported on)
-- - week_downloads: Downloads for the reported week (was: current_week_downloads)
-- - comparison_week_downloads: Downloads for the week before (was: previous_week_downloads)
--
-- Note: We drop and recreate the table since it only contains cached data that will be refreshed.

DROP TABLE IF EXISTS nugettrends.trending_packages_snapshot;

CREATE TABLE nugettrends.trending_packages_snapshot
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
