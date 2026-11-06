alter table __SCHEMA__.loan_event
    add column if not exists event_schema_version integer not null default 1;

create table if not exists __SCHEMA__.loan_snapshot (
    loan_id uuid not null,
    aggregate_version bigint not null,
    contract_json jsonb not null,
    servicing_json jsonb not null,
    created_at timestamptz not null default now(),
    primary key (loan_id, aggregate_version)
);

create index if not exists ix_loan_snapshot_loan_version_desc
    on __SCHEMA__.loan_snapshot (loan_id, aggregate_version desc);
