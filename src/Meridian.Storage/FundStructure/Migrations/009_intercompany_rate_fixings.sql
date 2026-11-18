-- Migration: fund-structure / intercompany_rate_fixings
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
-- Reviewed index fixings for floating-rate intercompany accrual (rule intercompany-accrual-v2). Every proposal,
-- approval and rejection is a new immutable version; nothing is rewritten.
create table if not exists __SCHEMA__.intercompany_rate_fixings (
    tenant_id text not null check (tenant_id = lower(trim(tenant_id)) and tenant_id <> '' and tenant_id <> 'all'),
    fixing_id uuid not null,
    version bigint not null check (version > 0),
    index_key text not null check (index_key = upper(trim(index_key)) and index_key <> ''),
    fixing_date date not null,
    payload jsonb not null,
    recorded_at timestamptz not null default now(),
    primary key (tenant_id, fixing_id, version)
);

create unique index if not exists intercompany_rate_fixings_first_version
    on __SCHEMA__.intercompany_rate_fixings (tenant_id, index_key, fixing_date) where version = 1;
create index if not exists intercompany_rate_fixings_by_index_date
    on __SCHEMA__.intercompany_rate_fixings (tenant_id, index_key, fixing_date, version);

create or replace function __SCHEMA__.refuse_intercompany_rate_fixing_rewrite() returns trigger language plpgsql as $$
begin
    raise exception 'Retained intercompany rate fixings are immutable';
end;
$$;

drop trigger if exists intercompany_rate_fixings_immutable on __SCHEMA__.intercompany_rate_fixings;
create trigger intercompany_rate_fixings_immutable before update or delete
on __SCHEMA__.intercompany_rate_fixings for each row
execute function __SCHEMA__.refuse_intercompany_rate_fixing_rewrite();

drop trigger if exists intercompany_rate_fixings_no_truncate on __SCHEMA__.intercompany_rate_fixings;
create trigger intercompany_rate_fixings_no_truncate before truncate
on __SCHEMA__.intercompany_rate_fixings for each statement
execute function __SCHEMA__.refuse_intercompany_rate_fixing_rewrite();
