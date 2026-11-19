-- Migration: ledger / intercompany_confirmation_close_policies
-- Append-only: never rename or renumber applied migrations.
-- Opt-in, reviewed confirmation close policies (ADR-032), retained as append-only versions. While a book's policy is
-- active, the confirmation findings it names block close readiness; without one, confirmation stays advisory.
create table if not exists __SCHEMA__.intercompany_confirmation_close_policies (
    policy_id uuid not null,
    version bigint not null check(version > 0),
    tenant_id text not null check(length(btrim(tenant_id))>0 and lower(btrim(tenant_id))<>'all'),
    company_id text null,
    ledger_book_id uuid not null,
    status text not null,
    payload jsonb not null,
    recorded_at timestamptz not null default now(),
    primary key(policy_id, version)
);
create unique index if not exists ux_intercompany_confirmation_close_policy_identity
    on __SCHEMA__.intercompany_confirmation_close_policies(policy_id) where version = 1;
create index if not exists ix_intercompany_confirmation_close_policy_book
    on __SCHEMA__.intercompany_confirmation_close_policies(tenant_id, ledger_book_id, policy_id, version);
create or replace function __SCHEMA__.refuse_intercompany_confirmation_close_policy_rewrite()
returns trigger language plpgsql as $$
begin raise exception 'Intercompany confirmation close policies are append-only; retain the next version'; end;
$$;
drop trigger if exists tr_intercompany_confirmation_close_policy_immutable on __SCHEMA__.intercompany_confirmation_close_policies;
create trigger tr_intercompany_confirmation_close_policy_immutable
before update or delete on __SCHEMA__.intercompany_confirmation_close_policies
for each row execute function __SCHEMA__.refuse_intercompany_confirmation_close_policy_rewrite();
drop trigger if exists tr_intercompany_confirmation_close_policy_no_truncate on __SCHEMA__.intercompany_confirmation_close_policies;
create trigger tr_intercompany_confirmation_close_policy_no_truncate
before truncate on __SCHEMA__.intercompany_confirmation_close_policies
for each statement execute function __SCHEMA__.refuse_intercompany_confirmation_close_policy_rewrite();
