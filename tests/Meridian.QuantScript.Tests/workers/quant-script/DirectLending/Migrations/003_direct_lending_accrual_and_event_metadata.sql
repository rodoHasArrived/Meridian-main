alter table __SCHEMA__.loan_event
    add column if not exists causation_id uuid,
    add column if not exists correlation_id uuid,
    add column if not exists command_id uuid,
    add column if not exists source_system text,
    add column if not exists replay_flag boolean not null default false;

create table if not exists __SCHEMA__.accrual_entry_projection (
    accrual_entry_id uuid primary key,
    loan_id uuid not null,
    accrual_date date not null,
    interest_amount numeric(24,8) not null,
    commitment_fee_amount numeric(24,8) not null,
    penalty_amount numeric(24,8) not null,
    annual_rate_applied numeric(18,8) not null,
    recorded_at timestamptz not null
);

create index if not exists ix_accrual_entry_projection_loan
    on __SCHEMA__.accrual_entry_projection (loan_id, accrual_date desc);
