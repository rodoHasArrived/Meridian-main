create unique index if not exists ux_journal_entries_aggregate_command
    on __SCHEMA__.journal_entries (aggregate_id, command_id)
    where command_id is not null;

create unique index if not exists ux_journal_entries_aggregate_source_event
    on __SCHEMA__.journal_entries (aggregate_id, source_event_id)
    where source_event_id is not null;

create unique index if not exists ux_journal_entries_aggregate_idempotency_key
    on __SCHEMA__.journal_entries (aggregate_id, lower(metadata ->> 'idempotencyKey'))
    where nullif(btrim(metadata ->> 'idempotencyKey'), '') is not null;
