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
--
-- IMPORTANT: The backfill is batched by year/month to avoid OOM errors.
-- A single query over all data exceeds ClickHouse's memory limit.
-- ============================================================================

-- Clear any partial data first
-- TRUNCATE TABLE nugettrends.weekly_downloads;

-- ============================================================================
-- Small years (2012-2019) - single batch each
-- ============================================================================

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2012 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2013 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2014 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2015 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2016 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2017 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2018 GROUP BY package_id, week;

-- INSERT INTO nugettrends.weekly_downloads
-- SELECT package_id, toMonday(date) AS week, avgState(download_count)
-- FROM nugettrends.daily_downloads WHERE toYear(date) = 2019 GROUP BY package_id, week;

-- ============================================================================
-- Large years (2020-2025) - batch by month to avoid OOM
-- ============================================================================

-- 2020
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2020 AND toMonth(date) = 12 GROUP BY package_id, week;

-- 2021
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2021 AND toMonth(date) = 12 GROUP BY package_id, week;

-- 2022
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2022 AND toMonth(date) = 12 GROUP BY package_id, week;

-- 2023
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2023 AND toMonth(date) = 12 GROUP BY package_id, week;

-- 2024
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2024 AND toMonth(date) = 12 GROUP BY package_id, week;

-- 2025
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 1 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 2 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 3 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 4 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 5 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 6 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 7 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 8 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 9 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 10 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 11 GROUP BY package_id, week;
-- INSERT INTO nugettrends.weekly_downloads SELECT package_id, toMonday(date) AS week, avgState(download_count) FROM nugettrends.daily_downloads WHERE toYear(date) = 2025 AND toMonth(date) = 12 GROUP BY package_id, week;
