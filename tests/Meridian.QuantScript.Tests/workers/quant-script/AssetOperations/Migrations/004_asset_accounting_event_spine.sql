create table if not exists __SCHEMA__.asset_accounting_event_projections (
    event_id uuid not null,
    event_version bigint not null,
    spine_version bigint not null,
    prior_spine_version bigint not null,
    event_kind text not null,
    security_id uuid not null,
    expected_security_version bigint not null,
    book_position_id uuid not null,
    expected_book_position_version bigint not null,
    ledger_book_id uuid not null,
    period_id uuid not null,
    effective_date date not null,
    event_amount numeric(38, 18) not null,
    currency text not null,
    source_content_hash text not null,
    projection_run_id uuid not null,
    projection_model_key text not null,
    projection_model_version text not null,
    posted_journal_entry_id uuid null,
    tax_lot_mutation_batch_id uuid null,
    canonical_fingerprint char(64) not null,
    payload jsonb not null,
    created_at timestamptz not null default now(),
    primary key (event_id, event_version, spine_version),
    constraint fk_asset_accounting_event_book_position
        foreign key (book_position_id)
        references __SCHEMA__.book_position_projections (position_id),
    constraint ck_asset_accounting_event_versions
        check (event_version > 0 and spine_version > 0 and prior_spine_version >= 0
               and spine_version = prior_spine_version + 1),
    constraint ck_asset_accounting_event_authority_versions
        check (expected_security_version > 0 and expected_book_position_version > 0),
    constraint ck_asset_accounting_event_currency
        check (btrim(currency) <> ''),
    constraint ck_asset_accounting_event_source_hash
        check (source_content_hash ~ '^[0-9A-Fa-f]{64}$'),
    constraint ck_asset_accounting_event_fingerprint
        check (canonical_fingerprint ~ '^[0-9a-f]{64}$')
);

create index if not exists ix_asset_accounting_event_position
    on __SCHEMA__.asset_accounting_event_projections
        (book_position_id, expected_book_position_version, effective_date, spine_version desc);

create index if not exists ix_asset_accounting_event_book_period
    on __SCHEMA__.asset_accounting_event_projections
        (ledger_book_id, period_id, effective_date, event_id, event_version, spine_version desc);

create index if not exists ix_asset_accounting_event_posted_journal
    on __SCHEMA__.asset_accounting_event_projections (posted_journal_entry_id)
    where posted_journal_entry_id is not null;

create index if not exists ix_asset_accounting_event_tax_lot_batch
    on __SCHEMA__.asset_accounting_event_projections (tax_lot_mutation_batch_id)
    where tax_lot_mutation_batch_id is not null;

create or replace function __SCHEMA__.reject_asset_accounting_event_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception
        'Asset accounting event projections are append-only; % is not permitted.',
        tg_op
        using errcode = '55000';
end;
$$;

drop trigger if exists trg_asset_accounting_event_no_update
    on __SCHEMA__.asset_accounting_event_projections;
create trigger trg_asset_accounting_event_no_update
before update on __SCHEMA__.asset_accounting_event_projections
for each row execute function __SCHEMA__.reject_asset_accounting_event_mutation();

drop trigger if exists trg_asset_accounting_event_no_delete
    on __SCHEMA__.asset_accounting_event_projections;
create trigger trg_asset_accounting_event_no_delete
before delete on __SCHEMA__.asset_accounting_event_projections
for each row execute function __SCHEMA__.reject_asset_accounting_event_mutation();
