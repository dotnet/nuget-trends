-- ClickHouse Schema Migration: 2025-12-26-01-init
-- Initial schema for NuGet Trends daily downloads

-- Create the nugettrends database if it doesn't exist
CREATE DATABASE IF NOT EXISTS nugettrends;

-- Create the daily_downloads table
-- Package IDs are stored in LOWERCASE for case-insensitive searching
-- Original case is available from PostgreSQL package_downloads table
CREATE TABLE IF NOT EXISTS nugettrends.daily_downloads
(
    -- Package ID stored in LOWERCASE for case-insensitive searching
    -- Not using LowCardinality since NuGet has 400K+ packages (exceeds recommended 10K threshold)
    package_id String,
    -- Date of the download count snapshot (daily granularity)
    date Date,
    -- Total download count for this package on this date
    download_count UInt64
)
ENGINE = ReplacingMergeTree()
-- Yearly partitions for optional data management (e.g., DROP PARTITION to remove old years)
-- Yearly is preferred over monthly to avoid INSERT issues with max_partitions_per_insert_block
PARTITION BY toYear(date)
-- Primary sort key: optimizes filter by package_id + range on date
ORDER BY (package_id, date)
-- Default index granularity
SETTINGS index_granularity = 8192;
