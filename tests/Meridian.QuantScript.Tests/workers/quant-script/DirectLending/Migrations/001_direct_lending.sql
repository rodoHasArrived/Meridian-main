create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.loan_state (
    loan_id uuid primary key,
    aggregate_version bigint not null,
    contract_json jsonb not null,
    servicing_json jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.loan_event (
    event_id uuid primary key,
    loan_id uuid not null,
    aggregate_version bigint not null,
    event_type text not null,
    effective_date date,
    recorded_at timestamptz not null default now(),
    payload jsonb not null
);

create unique index if not exists ux_loan_event_version
    on __SCHEMA__.loan_event (loan_id, aggregate_version);

create index if not exists ix_loan_event_recorded_at
    on __SCHEMA__.loan_event (loan_id, recorded_at desc);
