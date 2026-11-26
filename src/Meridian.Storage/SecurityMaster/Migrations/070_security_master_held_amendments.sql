-- Migration 070: held ingest amendments (Security Master review 2026-11-24, item A2).
--
-- An ingest amendment from a lower-ranked source that would overwrite a value a higher-ranked
-- source supplied is held instead of applied. The whole request is retained so an operator can
-- replay it through a governed draft; conflict_ids name the field conflicts opened for it.

create table if not exists __SCHEMA__.security_master_held_amendments (
    held_amendment_id    uuid primary key,
    security_id          uuid not null,
    source_system        text not null,
    request_json         jsonb not null,
    dominated_field_paths text[] not null,
    conflict_ids         uuid[] not null,
    held_at_version      bigint not null,
    held_at              timestamptz not null,
    status               text not null default 'Held',
    closed_by            text null,
    closed_at            timestamptz null,
    constraint ck_security_master_held_amendments_status
        check (status in ('Held', 'Replayed', 'Discarded')),
    constraint ck_security_master_held_amendments_closed
        check ((status = 'Held') = (closed_at is null))
);

create index if not exists ix_security_master_held_amendments_open
    on __SCHEMA__.security_master_held_amendments (security_id, held_at)
    where status = 'Held';
