-- ClickHouse Schema Migration: 2026-01-03-01-weekly-downloads-mv
-- Pre-aggregated weekly downloads to avoid OOM on large time range queries
-- See: https://github.com/dotnet/nuget-trends/issues/318
--
-- IMPORTANT: Duplicate handling limitation
-- =========================================
-- The daily_downloads table uses ReplacingMergeTree which deduplicates rows with
-- the same (package_id, date) during background merges. However, this MV fires on
-- every INSERT before deduplication occurs. If the same (package_id, date) is
-- inserted multiple times (e.g., job reruns), the MV will emit multiple aggregate
-- states that cannot be retracted, causing avgMerge() to overcount.
--
-- To prevent this:
-- 1. The DailyDownloadPackageIdPublisher job has retries disabled (Attempts = 0)
-- 2. The publisher queries for packages not yet checked today before queueing
-- 3. If manual rerun is needed, wait for the RabbitMQ queue to drain first
--
-- If data corruption occurs, rebuild weekly_downloads from daily_downloads:
--   TRUNCATE TABLE nugettrends.weekly_downloads;
--   INSERT INTO nugettrends.weekly_downloads
--   SELECT package_id, toMonday(date) AS week, avgState(download_count)
--   FROM nugettrends.daily_downloads
--   GROUP BY package_id, week;

-- Target table for weekly aggregates
-- Uses AggregatingMergeTree to store pre-computed avg() state
CREATE TABLE IF NOT EXISTS nugettrends.weekly_downloads
(
    -- Package ID stored in LOWERCASE (same as daily_downloads)
    package_id String,
    -- Monday of the week
    week Date,
    -- Pre-aggregated average state (stores sum + count internally)
    download_avg AggregateFunction(avg, UInt64)
)
ENGINE = AggregatingMergeTree()
PARTITION BY toYear(week)
ORDER BY (package_id, week);

-- Materialized view: transforms daily inserts into weekly aggregates
-- Automatically triggers on INSERT to daily_downloads
CREATE MATERIALIZED VIEW IF NOT EXISTS nugettrends.weekly_downloads_mv
TO nugettrends.weekly_downloads
AS SELECT
    package_id,
    toMonday(date) AS week,
    avgState(download_count) AS download_avg
FROM nugettrends.daily_downloads
GROUP BY package_id, week;

-- ============================================================================
-- ONE-TIME BACKFILL (run manually after creating the MV)
-- This populates weekly_downloads from existing daily_downloads data.
-- The MV only captures NEW inserts, so this is needed for historical data.
-- ============================================================================
-- INSERT INTO nugettrends.weekly_downloads
-- SELECT
--     package_id,
--     toMonday(date) AS week,
--     avgState(download_count) AS download_avg
-- FROM nugettrends.daily_downloads
-- GROUP BY package_id, week;
