-- Migration: security-master / security_master_revision_field_base
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- A governed field-edit revision records the canonical state its proposal was drafted against:
-- base_version is the security version the edit was staged at, and field_base_value is a
-- canonical snapshot of every stored key the approved merge would overwrite for that field.
-- field_base_recorded distinguishes a recorded snapshot from rows that predate these columns.
-- At publish, a field whose stored keys changed since the draft refuses the stale proposal
-- instead of silently overwriting the newer value; unrelated concurrent changes still merge.
-- Additive and nullable: existing rows keep their behavior (no base, no stale check).

alter table __SCHEMA__.security_master_revisions
    add column if not exists base_version bigint null;

alter table __SCHEMA__.security_master_revisions
    add column if not exists field_base_value text null;

alter table __SCHEMA__.security_master_revisions
    add column if not exists field_base_recorded boolean not null default false;
