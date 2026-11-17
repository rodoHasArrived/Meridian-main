-- Migration: ledger / intercompany_balance_confirmations
-- Append-only: never rename or renumber applied migrations.
-- Reciprocal intercompany balance confirmations: one confirmation per tenant, company, accounting profile and
-- as-of date, retained as an append-only sequence of versions. Advisory only; nothing here gates close.
create table if not exists __SCHEMA__.intercompany_balance_confirmations (
    confirmation_id uuid not null,
    version bigint not null check(version > 0),
    tenant_id text not null check(length(btrim(tenant_id))>0 and lower(btrim(tenant_id))<>'all'),
    company_id text null,
    accounting_arrangement_id uuid not null,
    as_of date not null,
    payload jsonb not null,
    recorded_at timestamptz not null default now(),
    primary key(confirmation_id, version)
);
-- One confirmation identity per scope, profile and date; each of its versions is written once.
create unique index if not exists ux_intercompany_balance_confirmation_scope_version
    on __SCHEMA__.intercompany_balance_confirmations(tenant_id, coalesce(company_id,''), accounting_arrangement_id, as_of, version);
create unique index if not exists ux_intercompany_balance_confirmation_scope_identity
    on __SCHEMA__.intercompany_balance_confirmations(tenant_id, coalesce(company_id,''), accounting_arrangement_id, as_of)
    where version = 1;
create or replace function __SCHEMA__.refuse_intercompany_balance_confirmation_rewrite()
returns trigger language plpgsql as $$
begin raise exception 'Intercompany balance confirmations are append-only; retain the next version'; end;
$$;
drop trigger if exists tr_intercompany_balance_confirmation_immutable on __SCHEMA__.intercompany_balance_confirmations;
create trigger tr_intercompany_balance_confirmation_immutable
before update or delete on __SCHEMA__.intercompany_balance_confirmations
for each row execute function __SCHEMA__.refuse_intercompany_balance_confirmation_rewrite();
drop trigger if exists tr_intercompany_balance_confirmation_no_truncate on __SCHEMA__.intercompany_balance_confirmations;
create trigger tr_intercompany_balance_confirmation_no_truncate
before truncate on __SCHEMA__.intercompany_balance_confirmations
for each statement execute function __SCHEMA__.refuse_intercompany_balance_confirmation_rewrite();
