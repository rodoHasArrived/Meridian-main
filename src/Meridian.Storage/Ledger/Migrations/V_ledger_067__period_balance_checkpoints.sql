-- Immutable native balance checkpoints are committed with the authoritative period-close event.
-- Historical close events retain null checkpoint references; no balances or authority are inferred
-- or backfilled from a later journal view.
create table if not exists __SCHEMA__.ledger_balance_checkpoints (
    checkpoint_id uuid primary key,
    close_event_id uuid not null unique
        references __SCHEMA__.period_close_events(event_id),
    ledger_book_id uuid not null
        references __SCHEMA__.ledger_books(ledger_book_id),
    period_id uuid not null
        references __SCHEMA__.accounting_periods(period_id),
    period_version bigint not null,
    tenant_id text null,
    schema_version integer not null default 1,
    as_of_utc timestamptz not null,
    source_sequence bigint not null,
    future_audit_sequence bigint null,
    journal_count bigint not null,
    line_count bigint not null,
    content_hash text not null,
    created_at_utc timestamptz not null default now(),
    constraint ck_ledger_balance_checkpoints_period_version
        check (period_version > 0),
    constraint ck_ledger_balance_checkpoints_schema_version
        check (schema_version = 1),
    constraint ck_ledger_balance_checkpoints_source_sequence
        check (source_sequence >= 0),
    constraint ck_ledger_balance_checkpoints_future_audit_sequence
        check (future_audit_sequence is null or future_audit_sequence > 0),
    constraint ck_ledger_balance_checkpoints_journal_count
        check (journal_count >= 0),
    constraint ck_ledger_balance_checkpoints_line_count
        check (line_count >= 0),
    constraint ck_ledger_balance_checkpoints_content_hash
        check (content_hash ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_ledger_balance_checkpoints_book_as_of
    on __SCHEMA__.ledger_balance_checkpoints
        (ledger_book_id, as_of_utc desc, source_sequence desc);

create index if not exists ix_ledger_balance_checkpoints_period_version
    on __SCHEMA__.ledger_balance_checkpoints (period_id, period_version);

comment on table __SCHEMA__.ledger_balance_checkpoints is
    'Immutable cumulative balances captured atomically with an authoritative hard close and bound to its retained audit evidence.';

create table if not exists __SCHEMA__.ledger_balance_checkpoint_rows (
    checkpoint_id uuid not null
        references __SCHEMA__.ledger_balance_checkpoints(checkpoint_id),
    row_no integer not null,
    account_name text not null,
    account_type text not null,
    symbol text null,
    financial_account_id text null,
    dimensions jsonb null,
    -- Aggregated amounts must not be narrowed to an individual journal leg's storage scale.
    debit numeric not null,
    credit numeric not null,
    entry_count bigint not null,
    primary key (checkpoint_id, row_no),
    constraint ck_ledger_balance_checkpoint_rows_row_no
        check (row_no >= 0),
    constraint ck_ledger_balance_checkpoint_rows_account_type
        check (account_type in ('Asset', 'Liability', 'Equity', 'Revenue', 'Expense')),
    constraint ck_ledger_balance_checkpoint_rows_dimensions
        check (dimensions is null or jsonb_typeof(dimensions) = 'object'),
    constraint ck_ledger_balance_checkpoint_rows_debit
        check (debit >= 0),
    constraint ck_ledger_balance_checkpoint_rows_credit
        check (credit >= 0),
    constraint ck_ledger_balance_checkpoint_rows_entry_count
        check (entry_count > 0)
);

comment on table __SCHEMA__.ledger_balance_checkpoint_rows is
    'Exact account and line-dimension debit/credit totals in one immutable hard-close balance checkpoint, including known zero balances.';

-- The header's required foreign key binds its native close event. These optional references have
-- no reverse foreign key: a native writer inserts the close event before its checkpoint header in
-- the same transaction, without temporarily breaking a circular immediate constraint.
alter table __SCHEMA__.period_close_events
    add column if not exists balance_checkpoint_id uuid null,
    add column if not exists balance_checkpoint_hash text null;

do $constraints$
begin
    if not exists (
        select 1 from pg_constraint
        where conrelid = '__SCHEMA__.period_close_events'::regclass
          and conname = 'ck_period_close_events_balance_checkpoint_pair'
    ) then
        alter table __SCHEMA__.period_close_events
            add constraint ck_period_close_events_balance_checkpoint_pair
                check ((balance_checkpoint_id is null) = (balance_checkpoint_hash is null));
    end if;
    if not exists (
        select 1 from pg_constraint
        where conrelid = '__SCHEMA__.period_close_events'::regclass
          and conname = 'ck_period_close_events_balance_checkpoint_hash'
    ) then
        alter table __SCHEMA__.period_close_events
            add constraint ck_period_close_events_balance_checkpoint_hash
                check (balance_checkpoint_hash is null or balance_checkpoint_hash ~ '^[0-9a-f]{64}$');
    end if;
end
$constraints$;

-- Checkpoint admission starts from retained audited close authority. The native header is not
-- an authority index: deleting it must not turn an audited checkpoint into a legacy fallback.
create index if not exists ix_ledger_event_audit_balance_checkpoint_lookup
    on __SCHEMA__.ledger_event_audit_events
        ((fact_snapshot::jsonb ->> 'ledger_book_id'),
         (fact_snapshot::jsonb ->> 'end_date') desc, chain_sequence desc)
    where subject_kind = 'period' and close_event_id is not null
      and close_event_snapshot::jsonb ->> 'balance_checkpoint_id' is not null;

create or replace function __SCHEMA__.reject_ledger_balance_checkpoint_mutation()
returns trigger
language plpgsql
as $function$
begin
    raise exception '% is immutable and append-only', tg_table_name
        using errcode = '55000';
end
$function$;

create or replace trigger trg_ledger_balance_checkpoints_immutable
    before update or delete on __SCHEMA__.ledger_balance_checkpoints
    for each row execute function __SCHEMA__.reject_ledger_balance_checkpoint_mutation();

create or replace trigger trg_ledger_balance_checkpoints_truncate_guard
    before truncate on __SCHEMA__.ledger_balance_checkpoints
    for each statement execute function __SCHEMA__.reject_ledger_balance_checkpoint_mutation();

create or replace trigger trg_ledger_balance_checkpoint_rows_immutable
    before update or delete on __SCHEMA__.ledger_balance_checkpoint_rows
    for each row execute function __SCHEMA__.reject_ledger_balance_checkpoint_mutation();

create or replace trigger trg_ledger_balance_checkpoint_rows_truncate_guard
    before truncate on __SCHEMA__.ledger_balance_checkpoint_rows
    for each statement execute function __SCHEMA__.reject_ledger_balance_checkpoint_mutation();
