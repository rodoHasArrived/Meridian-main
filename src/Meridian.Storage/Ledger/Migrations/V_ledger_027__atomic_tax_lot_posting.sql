create table if not exists __SCHEMA__.atomic_tax_lot_posting_batches (
    mutation_batch_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    period_id uuid not null references __SCHEMA__.accounting_periods(period_id),
    journal_entry_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id),
    source_event_id uuid not null,
    idempotency_key text not null,
    canonical_fingerprint text not null,
    expected_period_version bigint not null,
    mutation_kind text not null,
    retained_evidence jsonb not null,
    corrects_mutation_batch_id uuid null references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id),
    relief_method text null,
    policy_revision text null,
    created_at timestamptz not null,
    security_id uuid not null,
    book_position_id uuid not null,
    constraint ck_atomic_tax_lot_batch_idempotency
        check (length(trim(idempotency_key)) > 0),
    constraint ck_atomic_tax_lot_batch_fingerprint
        check (canonical_fingerprint ~ '^sha256:[0-9a-f]{64}$'),
    constraint ck_atomic_tax_lot_batch_period_version
        check (expected_period_version >= 0),
    constraint ck_atomic_tax_lot_batch_kind
        check (mutation_kind in ('Acquisition', 'Disposal')),
    constraint ck_atomic_tax_lot_batch_asset_scope
        check (
            security_id <> '00000000-0000-0000-0000-000000000000'::uuid and
            book_position_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    constraint ck_atomic_tax_lot_batch_evidence
        check (jsonb_typeof(retained_evidence) = 'array' and jsonb_array_length(retained_evidence) > 0)
);

create unique index if not exists ux_atomic_tax_lot_batch_journal
    on __SCHEMA__.atomic_tax_lot_posting_batches (journal_entry_id);

create unique index if not exists ux_atomic_tax_lot_batch_book_source_event
    on __SCHEMA__.atomic_tax_lot_posting_batches (ledger_book_id, source_event_id);

create unique index if not exists ux_atomic_tax_lot_batch_book_idempotency
    on __SCHEMA__.atomic_tax_lot_posting_batches (ledger_book_id, lower(trim(idempotency_key)));

create index if not exists ix_atomic_tax_lot_batch_correction
    on __SCHEMA__.atomic_tax_lot_posting_batches (corrects_mutation_batch_id)
    where corrects_mutation_batch_id is not null;

alter table __SCHEMA__.tax_lots
    add column if not exists version bigint not null default 1,
    add column if not exists security_id uuid null,
    add column if not exists book_position_id uuid null,
    add column if not exists originating_mutation_batch_id uuid null
        references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id),
    add column if not exists last_mutation_batch_id uuid null
        references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id);

do $migration$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass
          and conname = 'ck_tax_lots_version') then
        alter table __SCHEMA__.tax_lots
            add constraint ck_tax_lots_version check (version > 0);
    end if;
end
$migration$;

do $migration$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass
          and conname = 'ck_tax_lots_asset_scope') then
        alter table __SCHEMA__.tax_lots
            add constraint ck_tax_lots_asset_scope check (
                (security_id is null and book_position_id is null) or
                (security_id is not null and book_position_id is not null));
    end if;
end
$migration$;

create index if not exists ix_tax_lots_originating_mutation_batch
    on __SCHEMA__.tax_lots (originating_mutation_batch_id)
    where originating_mutation_batch_id is not null;

create index if not exists ix_tax_lots_last_mutation_batch
    on __SCHEMA__.tax_lots (last_mutation_batch_id)
    where last_mutation_batch_id is not null;

create index if not exists ix_tax_lots_asset_scope_open
    on __SCHEMA__.tax_lots (ledger_book_id, security_id, book_position_id, acquired_date, lot_id)
    where open_quantity > 0 and security_id is not null and book_position_id is not null;

create table if not exists __SCHEMA__.tax_lot_mutations (
    mutation_record_id uuid primary key,
    mutation_batch_id uuid not null references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id),
    mutation_kind text not null,
    tax_lot_record_id uuid not null references __SCHEMA__.tax_lots(tax_lot_record_id),
    lot_id text not null,
    selection_ordinal integer not null,
    quantity_before numeric(38, 12) not null,
    quantity_delta numeric(38, 12) not null,
    quantity_after numeric(38, 12) not null,
    unit_cost numeric(38, 12) not null,
    cost_basis numeric(38, 12) not null,
    expected_version bigint not null,
    result_version bigint not null,
    selection_evidence_id text not null,
    retained_evidence jsonb not null,
    journal_entry_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id),
    source_event_id uuid not null,
    corrects_mutation_batch_id uuid null references __SCHEMA__.atomic_tax_lot_posting_batches(mutation_batch_id),
    relief_method text null,
    policy_revision text null,
    lot_snapshot_before jsonb null,
    lot_snapshot_after jsonb not null,
    recorded_at timestamptz not null,
    security_id uuid not null,
    book_position_id uuid not null,
    constraint ck_tax_lot_mutations_kind
        check (mutation_kind in ('Acquisition', 'Disposal')),
    constraint ck_tax_lot_mutations_asset_scope
        check (
            security_id <> '00000000-0000-0000-0000-000000000000'::uuid and
            book_position_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    constraint ck_tax_lot_mutations_ordinal
        check (selection_ordinal >= 0),
    constraint ck_tax_lot_mutations_quantities
        check (
            quantity_before >= 0 and
            quantity_after >= 0 and
            quantity_delta <> 0 and
            quantity_after = quantity_before + quantity_delta and
            ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0) or
             (mutation_kind = 'Disposal' and quantity_delta < 0))),
    constraint ck_tax_lot_mutations_cost
        check (unit_cost >= 0 and cost_basis = abs(quantity_delta) * unit_cost),
    constraint ck_tax_lot_mutations_versions
        check (expected_version >= 0 and result_version = expected_version + 1),
    constraint ck_tax_lot_mutations_evidence
        check (
            length(trim(selection_evidence_id)) > 0 and
            jsonb_typeof(retained_evidence) = 'array' and
            jsonb_array_length(retained_evidence) > 0),
    constraint ck_tax_lot_mutations_snapshots
        check (
            (lot_snapshot_before is null or jsonb_typeof(lot_snapshot_before) = 'object') and
            jsonb_typeof(lot_snapshot_after) = 'object')
);

create unique index if not exists ux_tax_lot_mutations_batch_ordinal
    on __SCHEMA__.tax_lot_mutations (mutation_batch_id, selection_ordinal);

create unique index if not exists ux_tax_lot_mutations_batch_lot
    on __SCHEMA__.tax_lot_mutations (mutation_batch_id, tax_lot_record_id);

create index if not exists ix_tax_lot_mutations_lot_version
    on __SCHEMA__.tax_lot_mutations (tax_lot_record_id, result_version);

create index if not exists ix_tax_lot_mutations_journal
    on __SCHEMA__.tax_lot_mutations (journal_entry_id);

create or replace function __SCHEMA__.reject_atomic_tax_lot_audit_mutation()
returns trigger
language plpgsql
as $function$
begin
    raise exception '% is immutable and append-only', tg_table_name
        using errcode = '55000';
end
$function$;

drop trigger if exists trg_atomic_tax_lot_batches_immutable
    on __SCHEMA__.atomic_tax_lot_posting_batches;
create trigger trg_atomic_tax_lot_batches_immutable
    before update or delete on __SCHEMA__.atomic_tax_lot_posting_batches
    for each row execute function __SCHEMA__.reject_atomic_tax_lot_audit_mutation();

drop trigger if exists trg_tax_lot_mutations_immutable
    on __SCHEMA__.tax_lot_mutations;
create trigger trg_tax_lot_mutations_immutable
    before update or delete on __SCHEMA__.tax_lot_mutations
    for each row execute function __SCHEMA__.reject_atomic_tax_lot_audit_mutation();
