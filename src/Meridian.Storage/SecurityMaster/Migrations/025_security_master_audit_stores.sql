-- Migration 025: Durable Security Master conflict and revision-lifecycle stores.
-- The golden-record conflict store and the governed passport revision-lifecycle store were previously
-- process-local (in-memory). Persist both so the audit guarantee — a publish only ever runs against a
-- revision that was durably approved through the governed gate, and a conflict resolution/winner is
-- retained across restarts — survives process recycles and horizontal scale-out.

create table if not exists __SCHEMA__.security_master_conflicts (
    conflict_id            uuid primary key,
    security_id            uuid not null,
    conflict_kind          text not null,
    field_path             text not null,
    provider_a             text not null,
    value_a                text not null,
    provider_b             text not null,
    value_b                text not null,
    detected_at            timestamptz not null,
    status                 text not null,
    resolved_winner_source text null,
    resolved_by            text null,
    resolved_reason        text null,
    resolved_at            timestamptz null
);

create index if not exists ix_security_master_conflicts_status
    on __SCHEMA__.security_master_conflicts (status, detected_at);

create table if not exists __SCHEMA__.security_master_revisions (
    revision_id          uuid primary key,
    security_id          uuid not null,
    state                text not null,
    actor                text not null,
    created_at           timestamptz not null,
    updated_at           timestamptz not null,
    workflow_id          uuid null,
    field_path           text null,
    field_effective_from timestamptz null,
    field_justification  text null,
    fund_profile_id      text null
);

create index if not exists ix_security_master_revisions_security_id
    on __SCHEMA__.security_master_revisions (security_id, created_at);
