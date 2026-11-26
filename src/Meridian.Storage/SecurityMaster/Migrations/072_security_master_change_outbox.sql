-- Migration 072: Security Master change outbox (review 2026-11-24, P4 / A4; PR #3662 Phase 3).
--
-- One change record per appended economic event, written in the SAME transaction as the event by
-- every write origin (direct amend, ingest, workbench publication, rebuild-independent). Consumers
-- claim rows with FOR UPDATE SKIP LOCKED instead of following a global_sequence cursor: a bigserial
-- cursor can skip an event whose lower sequence commits after a higher one, which the outbox cannot,
-- because an uncommitted row is simply not yet visible and is claimed once it commits.
--
-- restatement_decision persists the period-aware restatement routing a workbench publish computed
-- (it was previously only returned to the caller and logged).

create table if not exists __SCHEMA__.security_master_change_outbox (
    change_id             uuid primary key,
    security_id           uuid not null,
    stream_version        bigint not null,
    event_type            text not null,
    actor                 text not null,
    recorded_at           timestamptz not null default now(),
    restatement_decision  jsonb null,
    claimed_by            text null,
    claimed_at            timestamptz null,
    processed_at          timestamptz null,
    constraint uq_security_master_change_outbox_event unique (security_id, stream_version)
);

create index if not exists ix_security_master_change_outbox_pending
    on __SCHEMA__.security_master_change_outbox (recorded_at, change_id)
    where processed_at is null;
