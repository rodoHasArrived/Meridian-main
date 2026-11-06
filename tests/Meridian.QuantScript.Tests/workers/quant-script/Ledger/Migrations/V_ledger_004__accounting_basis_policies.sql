create table if not exists __SCHEMA__.accounting_policies (
    accounting_policy_key text primary key,
    accounting_basis text not null,
    policy_id text not null,
    policy_version text not null,
    display_name text not null,
    fund_profile_id text null,
    fund_structure_node_id uuid null,
    instrument_id text null,
    source_event_id uuid null,
    effective_from date not null,
    effective_to date null,
    is_default boolean not null default false,
    rules_json jsonb not null default '{}'::jsonb,
    created_at timestamptz not null default now(),
    unique (accounting_basis, policy_id, policy_version)
);

insert into __SCHEMA__.accounting_policies (
    accounting_policy_key,
    accounting_basis,
    policy_id,
    policy_version,
    display_name,
    effective_from,
    is_default,
    rules_json)
values
    ('Primary:legacy-v1:legacy-v1', 'Primary', 'legacy-v1', 'legacy-v1', 'Legacy primary ledger policy', date '1900-01-01', true, '{}'::jsonb),
    ('Gaap:gaap-default-v1:v1', 'Gaap', 'gaap-default-v1', 'v1', 'Default GAAP basis policy', date '1900-01-01', true, '{}'::jsonb),
    ('Cash:cash-default-v1:v1', 'Cash', 'cash-default-v1', 'v1', 'Default cash basis policy', date '1900-01-01', true, '{}'::jsonb),
    ('Tax:tax-default-v1:v1', 'Tax', 'tax-default-v1', 'v1', 'Default tax basis policy', date '1900-01-01', true, '{}'::jsonb),
    ('Statutory:stat-default-v1:v1', 'Statutory', 'stat-default-v1', 'v1', 'Default statutory basis policy', date '1900-01-01', true, '{}'::jsonb)
on conflict (accounting_policy_key) do nothing;

alter table __SCHEMA__.ledger_books
    add column if not exists accounting_basis text not null default 'Primary',
    add column if not exists accounting_policy_id text not null default 'legacy-v1',
    add column if not exists accounting_policy_version text not null default 'legacy-v1';

alter table __SCHEMA__.ledger_books
    drop constraint if exists ledger_books_fund_profile_id_fund_structure_node_id_key;

create unique index if not exists ux_ledger_books_fund_node_basis
    on __SCHEMA__.ledger_books (fund_profile_id, fund_structure_node_id, accounting_basis);

create index if not exists ix_ledger_books_accounting_basis
    on __SCHEMA__.ledger_books (accounting_basis, accounting_policy_id, accounting_policy_version);

create index if not exists ix_accounting_policies_scope
    on __SCHEMA__.accounting_policies (
        accounting_basis,
        fund_profile_id,
        fund_structure_node_id,
        instrument_id,
        source_event_id,
        effective_from);
