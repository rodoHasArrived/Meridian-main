drop index if exists __SCHEMA__.ux_journal_entries_aggregate_command;

create unique index if not exists ux_journal_entries_command
    on __SCHEMA__.journal_entries (command_id)
    where command_id is not null;
