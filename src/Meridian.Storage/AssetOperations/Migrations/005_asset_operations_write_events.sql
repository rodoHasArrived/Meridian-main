-- Migration 005: Append-only record of every Asset Operations projection write.
-- Publishers such as Direct Lending replace a security's Asset Operations projection wholesale.
-- Each row carried its approval metadata, but the replacement itself left no durable trace: the
-- prior rows were deleted and nothing recorded who replaced them, when, or with what. Every write
-- now appends one event here in the same transaction as the replacement.
create table if not exists __SCHEMA__.asset_operations_write_events (
    global_sequence bigserial primary key,
    security_id uuid not null,
    approval_actor text not null,
    approval_reference text not null,
    approval_rationale text not null,
    approved_at timestamptz not null,
    recorded_at timestamptz not null default now(),
    source_domain text not null,
    summary jsonb not null
);

create index if not exists ix_asset_operations_write_events_security
    on __SCHEMA__.asset_operations_write_events (security_id, global_sequence);

comment on table __SCHEMA__.asset_operations_write_events is
    'Append-only log of Asset Operations projection writes (one row per wholesale replacement), recorded in the replacing transaction.';

create or replace function __SCHEMA__.reject_asset_operations_write_event_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception
        'Asset Operations write events are append-only; % is not permitted.',
        tg_op
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_asset_operations_write_event_no_update
    on __SCHEMA__.asset_operations_write_events;
create trigger trg_asset_operations_write_event_no_update
before update on __SCHEMA__.asset_operations_write_events
for each row execute function __SCHEMA__.reject_asset_operations_write_event_mutation();

drop trigger if exists trg_asset_operations_write_event_no_delete
    on __SCHEMA__.asset_operations_write_events;
create trigger trg_asset_operations_write_event_no_delete
before delete on __SCHEMA__.asset_operations_write_events
for each row execute function __SCHEMA__.reject_asset_operations_write_event_mutation();
