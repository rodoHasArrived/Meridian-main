-- Migration 028: Revision staged values + version-ordered canonical provenance.
--
-- security_master_revisions.field_value / field_value_recorded: a field-edit revision now
-- durably records the exact overlay VALUE it governs (field_value_recorded distinguishes an
-- explicitly recorded clear — field_value null — from legacy rows that predate the column).
-- Without the recorded value, discarding the revision that owns an overlay key leaves every
-- older same-path sibling's value unrecoverable: an older APPROVED sibling could neither
-- publish its reviewed value nor be discarded, deadlocking the field. With it, the discard
-- restores the exact value the latest remaining sibling governs.
--
-- security_field_provenance.source_version: CanonicalWrite attribution rows are ordered by the
-- Security Master projection version they attribute, not by asynchronous callback wall-clock
-- time. Two amendments' attribution writes can complete out of order (v2 pausing before its
-- provenance write while v3 persists and attributes first); a recorded_at-only guard would let
-- the resumed v2 overwrite v3's row and misname the field's incumbent provider for conflict
-- detection. When both rows carry a version, the higher version wins regardless of arrival time.

alter table __SCHEMA__.security_master_revisions
    add column if not exists field_value text null;

alter table __SCHEMA__.security_master_revisions
    add column if not exists field_value_recorded boolean not null default false;

alter table __SCHEMA__.security_field_provenance
    add column if not exists source_version bigint null;
