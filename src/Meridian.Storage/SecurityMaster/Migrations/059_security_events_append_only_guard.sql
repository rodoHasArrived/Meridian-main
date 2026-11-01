-- Migration 059: Append-only guard for the Security Master economic event log.
-- security_events is the event-sourced record of economic changes, but unlike
-- security_governance_events (migration 040/041) and security_alias_revisions (migration 038) it
-- had no database-level protection: any session with table privileges could rewrite or remove
-- history. Row-level BEFORE UPDATE/DELETE triggers refuse changes to existing events, and a
-- statement-level BEFORE TRUNCATE trigger refuses truncation, which row-level triggers do not see.
-- Nothing is changed here; the triggers only refuse future mutation. Migration 039's one-time
-- payload_version backfill ran earlier in migration order, before these triggers exist.

create or replace function __SCHEMA__.reject_security_event_mutation() returns trigger
language plpgsql as $$
begin
    raise exception 'Security Master events are append-only' using errcode = '23514';
end;
$$;

create trigger security_events_append_only
    before update or delete on __SCHEMA__.security_events
    for each row execute function __SCHEMA__.reject_security_event_mutation();

create trigger security_events_no_truncate
    before truncate on __SCHEMA__.security_events
    for each statement execute function __SCHEMA__.reject_security_event_mutation();
