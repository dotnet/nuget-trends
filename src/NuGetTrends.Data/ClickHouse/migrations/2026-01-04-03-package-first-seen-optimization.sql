-- ClickHouse Migration: 2026-01-04-03-package-first-seen-optimization
--
-- Purpose: Fix OOM error in trending packages snapshot by creating a
-- pre-computed package_first_seen table instead of computing min(week)
-- for every package on each query.
--
-- Problem: The original query runs:
--   SELECT package_id, min(week) AS first_seen FROM weekly_downloads GROUP BY package_id
-- This scans 400K+ packages × 600+ weeks and exceeds the 7GB memory limit.
--
-- Solution: Store first_seen in a dedicated table, populated incrementally.
--
-- This script is REENTRANT - safe to run multiple times.
-- Each INSERT skips packages already in the table.
--
-- Usage: Run each section one at a time in ClickHouse CLI client.
--        Copy-paste each statement and verify it completes before moving on.
--
-- Time estimate: ~10-30 minutes depending on server load

-- ============================================================================
-- SECTION 1: Create package_first_seen table
-- ============================================================================

CREATE TABLE IF NOT EXISTS nugettrends.package_first_seen
(
    package_id String,
    first_seen Date
)
ENGINE = ReplacingMergeTree()
ORDER BY package_id;


-- ============================================================================
-- SECTION 2: Verify table exists and check current state
-- ============================================================================

SELECT 'package_first_seen row count:' AS info, count() AS cnt FROM nugettrends.package_first_seen;


-- ============================================================================
-- SECTION 3: Populate package_first_seen (week by week, oldest first)
--
-- Each query is IDEMPOTENT - packages already in the table are skipped.
-- Run in order (oldest to newest) so first_seen reflects actual first appearance.
--
-- If any query fails with OOM, wait a minute and retry - the NOT IN clause
-- ensures no duplicates even on retry.
-- ============================================================================

-- Week 01: 2024-12-30 (440784 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2024-12-30') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2024-12-30'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 02: 2025-01-06 (431778 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-01-06') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-01-06'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 03: 2025-01-13 (432881 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-01-13') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-01-13'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 04: 2025-01-20 (433684 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-01-20') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-01-20'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 05: 2025-01-27 (434632 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-01-27') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-01-27'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 06: 2025-02-03 (435597 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-02-03') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-02-03'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 07: 2025-02-10 (436878 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-02-10') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-02-10'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 08: 2025-02-17 (437965 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-02-17') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-02-17'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 09: 2025-02-24 (703118 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-02-24') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-02-24'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 10: 2025-03-03 (439986 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-03-03') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-03-03'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 11: 2025-03-10 (441318 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-03-10') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-03-10'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 12: 2025-03-17 (442628 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-03-17') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-03-17'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 13: 2025-03-24 (443968 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-03-24') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-03-24'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 14: 2025-03-31 (622187 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-03-31') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-03-31'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 15: 2025-04-07 (445932 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-04-07') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-04-07'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 16: 2025-04-14 (446928 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-04-14') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-04-14'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 17: 2025-04-21 (447927 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-04-21') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-04-21'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 18: 2025-04-28 (689006 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-04-28') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-04-28'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 19: 2025-05-05 (450130 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-05-05') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-05-05'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 20: 2025-05-12 (451066 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-05-12') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-05-12'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 21: 2025-05-19 (452321 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-05-19') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-05-19'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 22: 2025-05-26 (663683 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-05-26') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-05-26'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 23: 2025-06-02 (454359 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-06-02') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-06-02'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 24: 2025-06-09 (455447 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-06-09') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-06-09'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 25: 2025-06-16 (456459 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-06-16') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-06-16'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 26: 2025-06-23 (457338 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-06-23') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-06-23'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 27: 2025-06-30 (497612 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-06-30') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-06-30'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 28: 2025-07-07 (459161 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-07-07') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-07-07'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 29: 2025-07-14 (460396 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-07-14') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-07-14'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 30: 2025-07-21 (461366 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-07-21') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-07-21'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 31: 2025-07-28 (883925 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-07-28') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-07-28'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 32: 2025-08-04 (463352 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-08-04') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-08-04'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 33: 2025-08-11 (463820 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-08-11') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-08-11'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 34: 2025-08-18 (301589 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-08-18') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-08-18'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 35: 2025-10-06 (464478 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-10-06') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-10-06'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 36: 2025-10-13 (464413 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-10-13') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-10-13'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 37: 2025-11-03 (464000 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-11-03') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-11-03'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 38: 2025-11-17 (463636 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-11-17') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-11-17'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 39: 2025-11-24 (463606 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-11-24') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-11-24'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 40: 2025-12-01 (463522 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-12-01') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-12-01'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 41: 2025-12-08 (463426 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-12-08') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-12-08'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 42: 2025-12-15 (463253 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-12-15') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-12-15'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 43: 2025-12-22 (463139 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-12-22') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-12-22'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);

-- Week 44: 2025-12-29 (929513 packages)
INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
SELECT DISTINCT package_id, toDate('2025-12-29') AS first_seen
FROM nugettrends.weekly_downloads
WHERE week = '2025-12-29'
  AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);


-- ============================================================================
-- SECTION 4: Verify population completed
-- ============================================================================

SELECT 'package_first_seen total rows:' AS info, count() AS cnt FROM nugettrends.package_first_seen;

-- Check distribution of first_seen dates (should see entries for each week)
SELECT 
    first_seen,
    count() AS packages
FROM nugettrends.package_first_seen
GROUP BY first_seen
ORDER BY first_seen;


-- ============================================================================
-- SECTION 5: Test the optimized SELECT query (without INSERT)
-- This should complete in seconds, not cause OOM
-- ============================================================================

WITH
    toMonday(today() - INTERVAL 1 WEEK) AS data_week,
    toMonday(today() - INTERVAL 2 WEEK) AS comparison_week,
    toDate(today() - INTERVAL 12 MONTH) AS age_cutoff
SELECT
    data_week AS week,
    cur.package_id AS package_id,
    toInt64(avgMerge(cur.download_avg) * 7) AS week_downloads,
    toInt64(avgMerge(prev.download_avg) * 7) AS comparison_downloads,
    (week_downloads - comparison_downloads) / comparison_downloads AS growth_rate
FROM nugettrends.weekly_downloads cur
INNER JOIN nugettrends.weekly_downloads prev
    ON cur.package_id = prev.package_id
    AND prev.week = comparison_week
INNER JOIN nugettrends.package_first_seen first
    ON cur.package_id = first.package_id
WHERE cur.week = data_week
  AND first.first_seen >= age_cutoff
GROUP BY cur.package_id
HAVING week_downloads >= 1000
   AND comparison_downloads > 0
ORDER BY growth_rate DESC
LIMIT 10;


-- ============================================================================
-- SECTION 6: Populate the trending_packages_snapshot (full INSERT)
-- Run this after Section 5 succeeds
-- ============================================================================

INSERT INTO nugettrends.trending_packages_snapshot
    (week, package_id, week_downloads, comparison_week_downloads, growth_rate)
WITH
    toMonday(today() - INTERVAL 1 WEEK) AS data_week,
    toMonday(today() - INTERVAL 2 WEEK) AS comparison_week,
    toDate(today() - INTERVAL 12 MONTH) AS age_cutoff
SELECT
    data_week AS week,
    cur.package_id AS package_id,
    toInt64(avgMerge(cur.download_avg) * 7) AS week_downloads,
    toInt64(avgMerge(prev.download_avg) * 7) AS comparison_downloads,
    (week_downloads - comparison_downloads) / comparison_downloads AS growth_rate
FROM nugettrends.weekly_downloads cur
INNER JOIN nugettrends.weekly_downloads prev
    ON cur.package_id = prev.package_id
    AND prev.week = comparison_week
INNER JOIN nugettrends.package_first_seen first
    ON cur.package_id = first.package_id
WHERE cur.week = data_week
  AND first.first_seen >= age_cutoff
GROUP BY cur.package_id
HAVING week_downloads >= 1000
   AND comparison_downloads > 0
ORDER BY growth_rate DESC
LIMIT 1000;


-- ============================================================================
-- SECTION 7: Verify trending_packages_snapshot was populated
-- ============================================================================

SELECT count() AS total_trending FROM nugettrends.trending_packages_snapshot;

SELECT * FROM nugettrends.trending_packages_snapshot ORDER BY growth_rate DESC LIMIT 10;


-- ============================================================================
-- GOING FORWARD: Adding new weeks
--
-- Each Monday when the Hangfire job runs, it needs to:
-- 1. Add new packages to package_first_seen
-- 2. Refresh the trending snapshot
--
-- The code changes will add this query before the snapshot refresh:
-- ============================================================================

-- This query will be run automatically by the updated Hangfire job:
-- INSERT INTO nugettrends.package_first_seen (package_id, first_seen)
-- SELECT DISTINCT package_id, toMonday(today() - INTERVAL 1 WEEK) AS first_seen
-- FROM nugettrends.weekly_downloads
-- WHERE week = toMonday(today() - INTERVAL 1 WEEK)
--   AND package_id NOT IN (SELECT package_id FROM nugettrends.package_first_seen);
