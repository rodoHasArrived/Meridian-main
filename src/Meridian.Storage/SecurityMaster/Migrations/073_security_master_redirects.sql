-- Migration 073: survivorship redirects (Security Master review 2026-11-24, C1).
--
-- Merging a duplicate security into a survivor retires the duplicate's stream (deactivation with a
-- MergedInto reason) and records a redirect here. Identifier resolution that lands only on the
-- retired security follows the redirect to the survivor. Lots, positions and journals keyed to the
-- retired SecurityId keep their id and history (owner decision 2026-11-25: redirect, not transfer).
-- A retired security has exactly one survivor; chains are followed to a bounded depth by readers.

create table if not exists __SCHEMA__.security_master_redirects (
    retired_security_id   uuid primary key,
    survivor_security_id  uuid not null,
    merged_at             timestamptz not null default now(),
    merged_by             text not null,
    reason                text not null,
    constraint ck_security_master_redirects_distinct
        check (retired_security_id <> survivor_security_id)
);

create index if not exists ix_security_master_redirects_survivor
    on __SCHEMA__.security_master_redirects (survivor_security_id);
