create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.operations_continuity_workflows (
    workflow_id uuid primary key,
    fund_account_id uuid not null,
    period_id text not null,
    security_master_snapshot_id uuid null,
    broker_source text not null,
    derived_status text not null,
    version bigint not null,
    created_at_utc timestamptz not null,
    updated_at_utc timestamptz not null,
    workflow_json jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists ix_operations_continuity_workflows_account_period
    on __SCHEMA__.operations_continuity_workflows (fund_account_id, period_id, updated_at_utc desc);

create index if not exists ix_operations_continuity_workflows_status
    on __SCHEMA__.operations_continuity_workflows (derived_status, updated_at_utc desc);

create unique index if not exists ux_operations_continuity_open_workflow
    on __SCHEMA__.operations_continuity_workflows (fund_account_id, period_id)
    where derived_status <> 'Closed';

create table if not exists __SCHEMA__.operations_continuity_audit (
    audit_id uuid primary key,
    occurred_at_utc timestamptz not null,
    workflow_id uuid not null references __SCHEMA__.operations_continuity_workflows(workflow_id) on delete cascade,
    fund_account_id uuid not null,
    period_id text not null,
    event_type text not null,
    from_state text not null,
    to_state text not null,
    gate text null,
    from_gate_status text null,
    to_gate_status text null,
    actor text not null,
    rationale text null,
    correlation_id text null,
    references_json jsonb not null default '[]'::jsonb,
    previous_hash text null,
    current_hash text not null,
    audit_json jsonb not null,
    created_at timestamptz not null default now()
);

create index if not exists ix_operations_continuity_audit_timeline
    on __SCHEMA__.operations_continuity_audit (workflow_id, occurred_at_utc, audit_id);

create unique index if not exists ux_operations_continuity_audit_hash
    on __SCHEMA__.operations_continuity_audit (workflow_id, current_hash);

create unique index if not exists ux_operations_continuity_audit_previous_hash
    on __SCHEMA__.operations_continuity_audit (workflow_id, previous_hash)
    where previous_hash is not null;

create unique index if not exists ux_operations_continuity_audit_genesis
    on __SCHEMA__.operations_continuity_audit (workflow_id)
    where previous_hash is null;
