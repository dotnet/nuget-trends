-- ClickHouse Seed Data: Development sample data
-- Inserts the "Sentry" package with 2 weeks of daily download snapshots.
-- This file runs automatically on first container start (empty volume).
-- The weekly_downloads materialized view auto-aggregates from these inserts.

INSERT INTO nugettrends.daily_downloads (package_id, date, download_count) VALUES
    ('sentry', '2026-01-25', 48000000),
    ('sentry', '2026-01-26', 48100000),
    ('sentry', '2026-01-27', 48200000),
    ('sentry', '2026-01-28', 48350000),
    ('sentry', '2026-01-29', 48500000),
    ('sentry', '2026-01-30', 48620000),
    ('sentry', '2026-01-31', 48750000),
    ('sentry', '2026-02-01', 48830000),
    ('sentry', '2026-02-02', 48900000),
    ('sentry', '2026-02-03', 49050000),
    ('sentry', '2026-02-04', 49200000),
    ('sentry', '2026-02-05', 49350000),
    ('sentry', '2026-02-06', 49480000),
    ('sentry', '2026-02-07', 49600000);
