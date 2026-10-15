-- Migration 040: Append-only Security Master governance event log.
-- Economic changes are event-sourced in security_events. Operator overrides, alias changes and
-- projection rebuilds changed governed state without an append-only record of their own: the
-- override row carried its audit trail inside a mutable jsonb column, and a rebuild left no trace.
-- Each of those writes now appends here in the same transaction as the state change, so the log
-- and the state commit or roll back together. The log is a separate stream from security_events so
-- governance activity never advances the economic stream version that amendments are fenced on.
create table if not exists __SCHEMA__.security_governance_events (
    global_sequence bigserial primary key,
    security_id uuid null,
    stream text not null,
    event_type text not null,
    actor text not null,
    occurred_at timestamptz not null,
    payload jsonb not null,
    payload_version integer not null default 1,
    constraint security_governance_events_stream_check
        check (stream in ('OperatorOverrides', 'Aliases', 'ProjectionRebuild')),
    constraint security_governance_events_payload_version_check check (payload_version >= 1)
);

create index if not exists ix_security_governance_events_security_sequence
    on __SCHEMA__.security_governance_events (security_id, global_sequence);

create index if not exists ix_security_governance_events_stream_sequence
    on __SCHEMA__.security_governance_events (stream, global_sequence);

comment on table __SCHEMA__.security_governance_events is
    'Append-only log of Security Master governance writes (operator overrides, alias changes, projection rebuilds), written in the same transaction as the state change.';
comment on column __SCHEMA__.security_governance_events.security_id is
    'Affected security; null only for batch-level events such as a projection rebuild batch, whose payload lists the securities.';

create function __SCHEMA__.reject_security_governance_event_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master governance events are append-only' using errcode = '23514';
end;
$$;
create trigger security_governance_events_append_only
    before update or delete on __SCHEMA__.security_governance_events
    for each row execute function __SCHEMA__.reject_security_governance_event_mutation();
create trigger security_governance_events_no_truncate
    before truncate on __SCHEMA__.security_governance_events
    for each statement execute function __SCHEMA__.reject_security_governance_event_mutation();
