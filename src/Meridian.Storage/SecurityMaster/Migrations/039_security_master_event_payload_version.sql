-- Migration 039: Record each security event's payload family and version in first-class columns.
-- Replay previously decided an event's payload family by inspecting the jsonb payload's shape, and
-- its version lived only in the metadata blob (when it was written at all). Promote both to
-- columns so replay refuses a payload version newer than the node understands, and so audit and
-- compatibility queries can filter on them without per-row JSON extraction.

alter table __SCHEMA__.security_events
    add column if not exists payload_type text null;

alter table __SCHEMA__.security_events
    add column if not exists payload_version integer null;

-- Backfill from the metadata every economic write has carried (payloadType + schemaVersion).
update __SCHEMA__.security_events
set payload_type = metadata->>'payloadType',
    payload_version = case
        when jsonb_typeof(metadata->'schemaVersion') = 'number' then (metadata->>'schemaVersion')::integer
        else 2
    end
where payload_type is null
  and jsonb_typeof(metadata->'payloadType') = 'string';

-- Events written without payload metadata are classified by shape, exactly as replay did: an
-- economic definition carries classification and economicTerms; anything else is the legacy
-- flat projection record.
update __SCHEMA__.security_events
set payload_type = case
        when payload ? 'classification' and payload ? 'economicTerms' then 'SecurityEconomicDefinition'
        else 'SecurityProjection'
    end,
    payload_version = case
        when payload ? 'classification' and payload ? 'economicTerms' then 2
        else 1
    end
where payload_type is null;

alter table __SCHEMA__.security_events
    alter column payload_type set not null;

alter table __SCHEMA__.security_events
    alter column payload_version set not null;

alter table __SCHEMA__.security_events
    add constraint security_events_payload_version_check check (payload_version >= 1);

create index if not exists ix_security_events_payload_type_version
    on __SCHEMA__.security_events (payload_type, payload_version);

comment on column __SCHEMA__.security_events.payload_type is
    'Payload family of the event (SecurityEconomicDefinition or the legacy SecurityProjection). Replay refuses unknown families.';
comment on column __SCHEMA__.security_events.payload_version is
    'Schema version of the payload family. Replay refuses versions newer than the node supports.';
