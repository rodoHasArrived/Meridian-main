create table if not exists __SCHEMA__.ledger_books (
    ledger_book_id uuid primary key,
    fund_profile_id text not null,
    fund_structure_node_id uuid not null,
    fund_structure_node_kind text not null,
    display_name text not null,
    base_currency text not null,
    description text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    unique (fund_profile_id, fund_structure_node_id)
);

alter table __SCHEMA__.accounting_periods
    add column if not exists ledger_book_id uuid null references __SCHEMA__.ledger_books(ledger_book_id);

alter table __SCHEMA__.accounting_periods
    drop constraint if exists accounting_periods_fiscal_year_period_no_key;

create unique index if not exists ux_accounting_periods_book_fiscal_period
    on __SCHEMA__.accounting_periods (ledger_book_id, fiscal_year, period_no)
    where ledger_book_id is not null;

create index if not exists ix_ledger_books_fund_structure_node
    on __SCHEMA__.ledger_books (fund_profile_id, fund_structure_node_id);

create index if not exists ix_accounting_periods_ledger_book_status
    on __SCHEMA__.accounting_periods (ledger_book_id, status, start_date);
