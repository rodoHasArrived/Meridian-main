create table if not exists __SCHEMA__.tax_lot_policies (
    policy_record_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade,
    account_name text not null,
    account_type text not null,
    symbol text null,
    financial_account_id text null,
    relief_method text not null,
    policy_id text not null,
    effective_date date not null,
    rationale text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    constraint ck_tax_lot_policies_relief_method
        check (relief_method in ('Fifo', 'Lifo', 'Hifo', 'SpecificId'))
);

create unique index if not exists ux_tax_lot_policies_book_account_effective
    on __SCHEMA__.tax_lot_policies (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        coalesce(financial_account_id, ''),
        effective_date);

create index if not exists ix_tax_lot_policies_book_policy
    on __SCHEMA__.tax_lot_policies (ledger_book_id, policy_id, effective_date desc);

create table if not exists __SCHEMA__.tax_lots (
    tax_lot_record_id uuid primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id) on delete cascade,
    account_name text not null,
    account_type text not null,
    symbol text null,
    financial_account_id text null,
    lot_id text not null,
    acquired_date date not null,
    original_quantity numeric(38, 12) not null,
    open_quantity numeric(38, 12) not null,
    unit_cost numeric(38, 12) not null,
    currency text not null,
    source_journal_entry_id uuid null references __SCHEMA__.journal_entries(journal_entry_id),
    evidence_ref text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    constraint ck_tax_lots_quantity
        check (original_quantity > 0 and open_quantity >= 0 and open_quantity <= original_quantity),
    constraint ck_tax_lots_unit_cost
        check (unit_cost >= 0),
    constraint ck_tax_lots_currency
        check (length(currency) between 3 and 12)
);

create unique index if not exists ux_tax_lots_book_account_lot
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        coalesce(financial_account_id, ''),
        lot_id);

create index if not exists ix_tax_lots_book_account_open
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        coalesce(financial_account_id, ''),
        acquired_date,
        lot_id)
    where open_quantity > 0;

create index if not exists ix_tax_lots_source_journal_entry
    on __SCHEMA__.tax_lots (source_journal_entry_id)
    where source_journal_entry_id is not null;
