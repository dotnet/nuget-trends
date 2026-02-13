-- TFM Adoption Snapshot table
-- Stores pre-computed cumulative package counts per Target Framework Moniker (TFM) per month.
-- Used by the /frameworks page to show how TFM adoption changes over time.
-- Populated by TfmAdoptionSnapshotRefresher (weekly scheduler job).

CREATE TABLE IF NOT EXISTS nugettrends.tfm_adoption_snapshot
(
    month Date,
    tfm String,
    family LowCardinality(String),
    new_package_count UInt32,
    cumulative_package_count UInt32,
    computed_at DateTime DEFAULT now()
)
ENGINE = ReplacingMergeTree(computed_at)
PARTITION BY toYear(month)
ORDER BY (month, tfm);
