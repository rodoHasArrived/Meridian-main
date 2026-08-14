-- Migration 027: Durable field-level provenance.
-- Record-level provenance (one sourceSystem per security) cannot answer "which source asserted
-- this field's value, as of when" — the central lineage question for a golden record assembled
-- from multiple vendors. This table persists per-field attribution. Conflict resolution writes
-- the winning source here in the SAME transaction that closes the conflict, so the recorded
-- winner and the field attribution can never diverge; operator overlay edits record their own
-- attribution under a distinct origin so canonical and overlay lineage stay separable.

create table if not exists __SCHEMA__.security_field_provenance (
    security_id      uuid not null,
    field_path       text not null,
    -- Origin of the attribution: 'ConflictResolution' rows describe the canonical golden-record
    -- winner; 'OperatorFieldEdit' rows describe overlay annotations awaiting approval. Keeping
    -- origin in the key prevents an overlay edit from clobbering canonical lineage.
    origin           text not null,
    source_system    text not null,
    as_of            timestamptz null,
    updated_by       text null,
    confidence       numeric null,
    -- Back-reference to the originating artifact (conflict id or revision id).
    origin_reference text null,
    recorded_at      timestamptz not null,
    primary key (security_id, field_path, origin)
);

create index if not exists ix_security_field_provenance_security
    on __SCHEMA__.security_field_provenance (security_id);
