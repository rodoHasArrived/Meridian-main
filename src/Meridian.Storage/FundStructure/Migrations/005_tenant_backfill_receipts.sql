-- Explicit maintenance backfill receipts. No attribution, startup apply, or enforcement change.
create table if not exists __SCHEMA__.fund_structure_tenant_backfill_receipt (
    run_id uuid primary key,
    plan_hash text not null check (plan_hash ~ '^[a-f0-9]{64}$'),
    operator_id text not null check (length(trim(operator_id)) > 0),
    review_reference text not null check (length(trim(review_reference)) > 0),
    applied_at_utc timestamptz not null default now(),
    stamped_rows integer not null check (stamped_rows >= 0),
    quarantined_rows integer not null check (quarantined_rows >= 0),
    plan jsonb not null
);

create or replace function __SCHEMA__.reject_tenant_backfill_receipt_rewrite()
returns trigger language plpgsql as $$
begin
    raise exception 'Fund structure tenant backfill receipts are immutable';
end;
$$;

drop trigger if exists tenant_backfill_receipt_immutable on __SCHEMA__.fund_structure_tenant_backfill_receipt;
create trigger tenant_backfill_receipt_immutable
before update or delete on __SCHEMA__.fund_structure_tenant_backfill_receipt
for each row execute function __SCHEMA__.reject_tenant_backfill_receipt_rewrite();

drop trigger if exists tenant_backfill_receipt_no_truncate on __SCHEMA__.fund_structure_tenant_backfill_receipt;
create trigger tenant_backfill_receipt_no_truncate
before truncate on __SCHEMA__.fund_structure_tenant_backfill_receipt
for each statement execute function __SCHEMA__.reject_tenant_backfill_receipt_rewrite();
