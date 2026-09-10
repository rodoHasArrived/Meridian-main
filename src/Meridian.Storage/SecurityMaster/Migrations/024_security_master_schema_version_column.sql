-- Migration 024: Promote the asset-specific schema version to a queryable column.
-- The asset-specific-terms schema version was previously readable only by extracting the
-- `schemaVersion` property from the asset_specific_terms jsonb payload. Promote it to a first-class
-- column so compatibility and audit queries can filter on it directly (e.g. WHERE schema_version = 1)
-- without a per-row JSON extraction.

alter table __SCHEMA__.securities
    add column if not exists schema_version integer not null default 1;

-- Definition of the column (shared with the projection store's upsert, which writes it the same
-- way): the schemaVersion stamped on the STORED asset_specific_terms blob, or 1 when the blob is
-- unstamped. It is NOT the post-upcast version — a cross-family economic-terms document sitting in
-- this slot backfills and upserts as 2 so `where schema_version = 2` finds every row that needs the
-- migrate-on-read bridge, and `where schema_version = 1` selects only rows the flat readers accept
-- as-is. Readability is decided by the mapping guard from the blob, never from this column.
--
-- Backfill from the existing payload where a numeric schemaVersion is present; rows that predate
-- explicit versioning keep the legacy default of 1, which is the same version the upcaster resolves
-- an unstamped payload to on read.
update __SCHEMA__.securities
set schema_version = (asset_specific_terms->>'schemaVersion')::integer
where jsonb_typeof(asset_specific_terms) = 'object'
  and jsonb_typeof(asset_specific_terms->'schemaVersion') = 'number';

create index if not exists ix_securities_schema_version
    on __SCHEMA__.securities (schema_version);
