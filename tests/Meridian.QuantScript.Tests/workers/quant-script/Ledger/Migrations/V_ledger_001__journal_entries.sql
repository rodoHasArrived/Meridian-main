create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.journal_entries (
    global_sequence bigserial primary key,
    journal_entry_id uuid not null,
    aggregate_id uuid not null,
    period_id uuid not null,
    command_id uuid null,
    correlation_id uuid null,
    occurred_at timestamptz not null,
    description text not null,
    metadata jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    unique (journal_entry_id)
);

create index if not exists ix_journal_entries_period
    on __SCHEMA__.journal_entries (period_id, occurred_at, global_sequence);

create index if not exists ix_journal_entries_aggregate
    on __SCHEMA__.journal_entries (aggregate_id, occurred_at, global_sequence);

create index if not exists ix_journal_entries_correlation
    on __SCHEMA__.journal_entries (correlation_id)
    where correlation_id is not null;

create table if not exists __SCHEMA__.journal_legs (
    entry_id uuid primary key,
    journal_entry_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id) on delete cascade,
    line_no integer not null,
    aggregate_id uuid not null,
    period_id uuid not null,
    command_id uuid null,
    correlation_id uuid null,
    occurred_at timestamptz not null,
    account_name text not null,
    account_type text not null,
    symbol text null,
    financial_account_id text null,
    debit numeric(38, 10) not null,
    credit numeric(38, 10) not null,
    description text not null,
    constraint ck_journal_legs_non_negative_amounts check (debit >= 0 and credit >= 0),
    constraint ck_journal_legs_single_sided_amount check (
        (debit > 0 and credit = 0)
        or (debit = 0 and credit > 0)
    ),
    unique (journal_entry_id, line_no)
);

create index if not exists ix_journal_legs_period
    on __SCHEMA__.journal_legs (period_id, occurred_at);

create index if not exists ix_journal_legs_aggregate
    on __SCHEMA__.journal_legs (aggregate_id, occurred_at);
