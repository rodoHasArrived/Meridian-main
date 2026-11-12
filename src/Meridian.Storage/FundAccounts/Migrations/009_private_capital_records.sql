-- Source observations and their replay receipts are append-only. Financial authority is
-- selected by explicit correction links, never by UUID or import arrival order.
create table if not exists __SCHEMA__.private_capital_record (
    tenant_id text not null,
    company_id text not null,
    record_kind text not null,
    record_id uuid not null,
    payload jsonb not null,
    retained_at timestamptz not null default now(),
    primary key (tenant_id, company_id, record_kind, record_id),
    check (length(trim(tenant_id)) > 0 and length(trim(company_id)) > 0)
);
create unique index if not exists ux_private_capital_single_correction
    on __SCHEMA__.private_capital_record
    (tenant_id, company_id, (payload->>'supersedesSnapshotId'))
    where record_kind = 'snapshot' and payload->>'supersedesSnapshotId' is not null;
