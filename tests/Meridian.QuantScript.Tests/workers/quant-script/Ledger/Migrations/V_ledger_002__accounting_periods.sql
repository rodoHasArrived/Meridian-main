create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.accounting_periods (
    period_id uuid primary key,
    fiscal_year integer not null,
    period_no integer not null,
    label text not null,
    start_date date not null,
    end_date date not null,
    status text not null,
    opened_at timestamptz not null,
    closed_at timestamptz null,
    optimistic_version bigint not null default 1,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_accounting_periods_date_range check (start_date <= end_date),
    constraint ck_accounting_periods_status check (status in ('Open', 'SoftClosed', 'HardClosed')),
    unique (fiscal_year, period_no)
);

create index if not exists ix_accounting_periods_date_range
    on __SCHEMA__.accounting_periods (start_date, end_date);

create index if not exists ix_accounting_periods_status
    on __SCHEMA__.accounting_periods (status);

create table if not exists __SCHEMA__.period_close_events (
    event_id uuid primary key,
    period_id uuid not null references __SCHEMA__.accounting_periods(period_id) on delete cascade,
    prior_status text not null,
    new_status text not null,
    closed_by text not null,
    notes text not null default '',
    recorded_at timestamptz not null,
    period_version bigint not null,
    constraint ck_period_close_events_prior_status check (prior_status in ('Open', 'SoftClosed', 'HardClosed')),
    constraint ck_period_close_events_new_status check (new_status in ('Open', 'SoftClosed', 'HardClosed'))
);

create index if not exists ix_period_close_events_period
    on __SCHEMA__.period_close_events (period_id, recorded_at);
