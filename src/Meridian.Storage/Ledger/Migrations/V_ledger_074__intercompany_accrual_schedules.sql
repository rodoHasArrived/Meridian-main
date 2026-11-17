-- Migration: ledger / intercompany_accrual_schedules
-- Append-only: never rename or renumber applied migrations.
-- Opt-in, reviewed intercompany accrual schedules, retained as append-only versions. A schedule drafts governed
-- accruals for completed periods; it never posts and never changes a draft a reviewer has touched.
create table if not exists __SCHEMA__.intercompany_accrual_schedules (
    schedule_id uuid not null,
    version bigint not null check(version > 0),
    tenant_id text not null check(length(btrim(tenant_id))>0 and lower(btrim(tenant_id))<>'all'),
    company_id text null,
    ledger_book_id uuid not null,
    status text not null,
    payload jsonb not null,
    recorded_at timestamptz not null default now(),
    primary key(schedule_id, version)
);
create unique index if not exists ux_intercompany_accrual_schedule_identity
    on __SCHEMA__.intercompany_accrual_schedules(schedule_id) where version = 1;
create index if not exists ix_intercompany_accrual_schedule_book
    on __SCHEMA__.intercompany_accrual_schedules(tenant_id, ledger_book_id, schedule_id, version);
create or replace function __SCHEMA__.refuse_intercompany_accrual_schedule_rewrite()
returns trigger language plpgsql as $$
begin raise exception 'Intercompany accrual schedules are append-only; retain the next version'; end;
$$;
drop trigger if exists tr_intercompany_accrual_schedule_immutable on __SCHEMA__.intercompany_accrual_schedules;
create trigger tr_intercompany_accrual_schedule_immutable
before update or delete on __SCHEMA__.intercompany_accrual_schedules
for each row execute function __SCHEMA__.refuse_intercompany_accrual_schedule_rewrite();
drop trigger if exists tr_intercompany_accrual_schedule_no_truncate on __SCHEMA__.intercompany_accrual_schedules;
create trigger tr_intercompany_accrual_schedule_no_truncate
before truncate on __SCHEMA__.intercompany_accrual_schedules
for each statement execute function __SCHEMA__.refuse_intercompany_accrual_schedule_rewrite();
