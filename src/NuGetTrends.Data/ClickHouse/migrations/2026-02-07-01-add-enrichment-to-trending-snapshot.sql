-- ClickHouse Schema Migration: 2026-02-07-01-add-enrichment-to-trending-snapshot
-- Add enrichment columns to trending_packages_snapshot so the web app
-- can serve trending data without querying PostgreSQL.
--
-- Previously the web app joined against PostgreSQL (package_details_catalog_leafs,
-- package_downloads) on every cache miss to get icon URLs, GitHub URLs, and
-- original-cased package IDs. If PostgreSQL was locked (e.g. during a migration),
-- the web app would time out.
--
-- Now the scheduler enriches data at snapshot-refresh time and stores it directly
-- in ClickHouse. The web app reads everything from a single ClickHouse query.

ALTER TABLE nugettrends.trending_packages_snapshot
    ADD COLUMN IF NOT EXISTS package_id_original String DEFAULT '';

ALTER TABLE nugettrends.trending_packages_snapshot
    ADD COLUMN IF NOT EXISTS icon_url String DEFAULT '';

ALTER TABLE nugettrends.trending_packages_snapshot
    ADD COLUMN IF NOT EXISTS github_url String DEFAULT '';
