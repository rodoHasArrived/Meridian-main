-- Migration 024: Promote the asset-specific schema version to a queryable column.
-- The asset-specific-terms schema version was previously readable only by extracting the
-- `schemaVersion` property from the asset_specific_terms jsonb payload. Promote it to a first-class
-- column so compatibility and audit queries can filter on it directly (e.g. WHERE schema_version = 1)
-- without a per-row JSON extraction.

alter table __SCHEMA__.securities
    add column if not exists schema_version integer not null default 1;

-- Backfill from the existing payload where a numeric schemaVersion is present; rows that predate
-- explicit versioning keep the legacy default of 1, which is the same version the upcaster resolves
-- an unstamped payload to on read.
update __SCHEMA__.securities
set schema_version = (asset_specific_terms->>'schemaVersion')::integer
where jsonb_typeof(asset_specific_terms) = 'object'
  and jsonb_typeof(asset_specific_terms->'schemaVersion') = 'number';

create index if not exists ix_securities_schema_version
    on __SCHEMA__.securities (schema_version);
