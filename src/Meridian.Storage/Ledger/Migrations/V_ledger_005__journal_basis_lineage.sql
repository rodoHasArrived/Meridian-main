alter table __SCHEMA__.journal_entries
    add column if not exists accounting_basis text not null default 'Primary',
    add column if not exists accounting_policy_id text not null default 'legacy-v1',
    add column if not exists accounting_policy_version text not null default 'legacy-v1',
    add column if not exists rule_id text null,
    add column if not exists rule_version text null,
    add column if not exists source_event_id uuid null,
    add column if not exists source_journal_entry_id uuid null;

alter table __SCHEMA__.journal_legs
    add column if not exists accounting_basis text not null default 'Primary',
    add column if not exists accounting_policy_id text not null default 'legacy-v1',
    add column if not exists accounting_policy_version text not null default 'legacy-v1',
    add column if not exists rule_id text null,
    add column if not exists rule_version text null,
    add column if not exists source_event_id uuid null,
    add column if not exists source_journal_entry_id uuid null;

create index if not exists ix_journal_entries_basis_period
    on __SCHEMA__.journal_entries (accounting_basis, period_id, occurred_at, global_sequence);

create index if not exists ix_journal_entries_source_event
    on __SCHEMA__.journal_entries (source_event_id)
    where source_event_id is not null;

create index if not exists ix_journal_entries_source_journal
    on __SCHEMA__.journal_entries (source_journal_entry_id)
    where source_journal_entry_id is not null;

create index if not exists ix_journal_legs_basis_account
    on __SCHEMA__.journal_legs (accounting_basis, account_name, account_type);
