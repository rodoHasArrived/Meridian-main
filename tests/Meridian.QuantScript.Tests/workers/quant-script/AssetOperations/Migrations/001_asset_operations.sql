create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.asset_operation_subjects (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_terms_versions (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_lifecycle_events (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_cash_flow_projection_runs (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_projected_cash_flows (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_actual_activity (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_reconciliation_runs (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_reconciliation_results (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_ledger_projections (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_operations_readiness (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.asset_workflow_audit (
    id uuid primary key,
    security_id uuid not null,
    source_domain text null,
    source_entity_id text null,
    payload jsonb not null,
    created_at timestamptz not null default now()
);

create index if not exists ix_asset_operation_subjects_security_id
    on __SCHEMA__.asset_operation_subjects (security_id);
create index if not exists ix_asset_terms_versions_security_id
    on __SCHEMA__.asset_terms_versions (security_id);
create index if not exists ix_asset_lifecycle_events_security_id
    on __SCHEMA__.asset_lifecycle_events (security_id);
create index if not exists ix_asset_cash_flow_projection_runs_security_id
    on __SCHEMA__.asset_cash_flow_projection_runs (security_id);
create index if not exists ix_asset_projected_cash_flows_security_id
    on __SCHEMA__.asset_projected_cash_flows (security_id);
create index if not exists ix_asset_actual_activity_security_id
    on __SCHEMA__.asset_actual_activity (security_id);
create index if not exists ix_asset_reconciliation_runs_security_id
    on __SCHEMA__.asset_reconciliation_runs (security_id);
create index if not exists ix_asset_reconciliation_results_security_id
    on __SCHEMA__.asset_reconciliation_results (security_id);
create index if not exists ix_asset_ledger_projections_security_id
    on __SCHEMA__.asset_ledger_projections (security_id);
create index if not exists ix_asset_operations_readiness_security_id
    on __SCHEMA__.asset_operations_readiness (security_id);
create index if not exists ix_asset_workflow_audit_security_id
    on __SCHEMA__.asset_workflow_audit (security_id);
