create schema if not exists __SCHEMA__;

alter table if exists __SCHEMA__.loan_event
    add column if not exists event_position bigserial;

create unique index if not exists ix_loan_event_event_position
    on __SCHEMA__.loan_event(event_position);

create table if not exists __SCHEMA__.cash_transaction (
    cash_transaction_id      uuid primary key,
    loan_id                  uuid not null references __SCHEMA__.loan_contract(loan_id),
    transaction_type         text not null,
    effective_date           date not null,
    transaction_date         date not null,
    settlement_date          date not null,
    amount                   numeric(24,2) not null,
    currency                 text not null,
    counterparty             text,
    external_ref             text,
    source_event_id          uuid not null references __SCHEMA__.loan_event(event_id),
    recorded_at              timestamptz not null default now(),
    voided_at                timestamptz
);

create index if not exists ix_cash_transaction_loan
    on __SCHEMA__.cash_transaction(loan_id, settlement_date desc);

create unique index if not exists ux_cash_transaction_external_ref
    on __SCHEMA__.cash_transaction(loan_id, external_ref, transaction_type)
    where external_ref is not null;

create table if not exists __SCHEMA__.payment_allocation (
    allocation_id            uuid primary key,
    loan_id                  uuid not null references __SCHEMA__.loan_contract(loan_id),
    cash_transaction_id      uuid not null references __SCHEMA__.cash_transaction(cash_transaction_id),
    allocation_seq_no        integer not null,
    target_type              text not null,
    target_id                uuid not null,
    allocated_amount         numeric(24,2) not null,
    allocation_rule          text not null,
    source_event_id          uuid not null references __SCHEMA__.loan_event(event_id),
    created_at               timestamptz not null default now(),
    unique (cash_transaction_id, allocation_seq_no)
);

create table if not exists __SCHEMA__.fee_balance (
    fee_balance_id           uuid primary key,
    loan_id                  uuid not null references __SCHEMA__.loan_contract(loan_id),
    fee_type                 text not null,
    effective_date           date not null,
    original_amount          numeric(24,2) not null,
    unpaid_amount            numeric(24,2) not null,
    source_event_id          uuid not null references __SCHEMA__.loan_event(event_id),
    note                     text,
    created_at               timestamptz not null default now()
);

create table if not exists __SCHEMA__.projection_run (
    projection_run_id            uuid primary key,
    loan_id                      uuid not null references __SCHEMA__.loan_contract(loan_id),
    loan_terms_version           integer not null,
    servicing_revision           bigint not null,
    projection_as_of             date not null,
    market_data_as_of            date,
    trigger_event_id             uuid not null references __SCHEMA__.loan_event(event_id),
    trigger_type                 text not null,
    terms_hash                   text not null,
    engine_version               text not null,
    status                       text not null,
    supersedes_projection_run_id uuid references __SCHEMA__.projection_run(projection_run_id),
    generated_at                 timestamptz not null default now()
);

-- A functional index is required because market_data_as_of is nullable; the
-- sentinel date '1900-01-01' substitutes for NULL so that two projection runs
-- with no market data as-of date are treated as duplicates of each other.
create unique index if not exists ux_projection_run_lineage
    on __SCHEMA__.projection_run (
        loan_id,
        loan_terms_version,
        servicing_revision,
        engine_version,
        projection_as_of,
        coalesce(market_data_as_of, date '1900-01-01'));

create index if not exists ix_projection_run_loan
    on __SCHEMA__.projection_run(loan_id, generated_at desc);

create table if not exists __SCHEMA__.projected_cash_flow (
    projected_flow_id         uuid primary key,
    projection_run_id         uuid not null references __SCHEMA__.projection_run(projection_run_id) on delete cascade,
    loan_id                   uuid not null references __SCHEMA__.loan_contract(loan_id),
    flow_seq_no               integer not null,
    flow_type                 text not null,
    due_date                  date not null,
    accrual_start_date        date,
    accrual_end_date          date,
    amount                    numeric(24,2) not null,
    currency                  text not null,
    principal_basis           numeric(24,8),
    annual_rate               numeric(12,8),
    formula_trace_json        jsonb,
    created_at                timestamptz not null default now(),
    unique (projection_run_id, flow_seq_no)
);

create table if not exists __SCHEMA__.journal_entry (
    journal_entry_id          uuid primary key,
    loan_id                   uuid references __SCHEMA__.loan_contract(loan_id),
    accounting_date           date not null,
    effective_date            date not null,
    source_event_id           uuid not null references __SCHEMA__.loan_event(event_id),
    entry_type                text not null,
    ledger_basis              text not null,
    description               text not null,
    recorded_at               timestamptz not null default now(),
    posted_at                 timestamptz,
    status                    text not null
);

create index if not exists ix_journal_entry_loan
    on __SCHEMA__.journal_entry(loan_id, accounting_date desc);

create table if not exists __SCHEMA__.journal_line (
    journal_line_id           uuid primary key,
    journal_entry_id          uuid not null references __SCHEMA__.journal_entry(journal_entry_id) on delete cascade,
    line_no                   integer not null,
    account_code              text not null,
    debit_amount              numeric(24,2) not null default 0,
    credit_amount             numeric(24,2) not null default 0,
    currency                  text not null,
    dimensions_json           jsonb,
    created_at                timestamptz not null default now(),
    unique (journal_entry_id, line_no)
);

create table if not exists __SCHEMA__.accounting_period_lock (
    ledger_basis              text not null,
    period_start_date         date not null,
    period_end_date           date not null,
    status                    text not null,
    locked_by                 text,
    locked_at                 timestamptz,
    reopened_by               text,
    reopened_at               timestamptz,
    reason                    text,
    created_at                timestamptz not null default now(),
    primary key (ledger_basis, period_start_date, period_end_date)
);

create table if not exists __SCHEMA__.reconciliation_run (
    reconciliation_run_id     uuid primary key,
    loan_id                   uuid not null references __SCHEMA__.loan_contract(loan_id),
    projection_run_id         uuid not null references __SCHEMA__.projection_run(projection_run_id),
    requested_at              timestamptz not null default now(),
    completed_at              timestamptz,
    status                    text not null
);

create table if not exists __SCHEMA__.reconciliation_result (
    reconciliation_result_id  uuid primary key,
    reconciliation_run_id     uuid not null references __SCHEMA__.reconciliation_run(reconciliation_run_id) on delete cascade,
    loan_id                   uuid not null references __SCHEMA__.loan_contract(loan_id),
    projected_flow_id         uuid references __SCHEMA__.projected_cash_flow(projected_flow_id),
    cash_transaction_id       uuid references __SCHEMA__.cash_transaction(cash_transaction_id),
    match_status              text not null,
    expected_amount           numeric(24,2),
    actual_amount             numeric(24,2),
    variance_amount           numeric(24,2),
    expected_date             date,
    actual_date               date,
    match_rule                text,
    tolerance_json            jsonb,
    notes                     text[] not null default '{}',
    created_at                timestamptz not null default now()
);

create index if not exists ix_reconciliation_result_run
    on __SCHEMA__.reconciliation_result(reconciliation_run_id);

create table if not exists __SCHEMA__.reconciliation_exception (
    exception_id              uuid primary key,
    reconciliation_result_id  uuid not null references __SCHEMA__.reconciliation_result(reconciliation_result_id) on delete cascade,
    exception_type            text not null,
    severity                  text not null,
    status                    text not null,
    assigned_to               text,
    resolution_note           text,
    created_at                timestamptz not null default now(),
    resolved_at               timestamptz
);

create index if not exists ix_reconciliation_exception_status
    on __SCHEMA__.reconciliation_exception(status, severity);

create table if not exists __SCHEMA__.servicer_report_batch (
    servicer_report_batch_id  uuid primary key,
    servicer_name             text not null,
    report_type               text not null,
    source_format             text not null,
    report_as_of_date         date not null,
    received_at               timestamptz not null default now(),
    file_name                 text,
    file_hash                 text,
    row_count                 integer,
    status                    text not null,
    loaded_by                 text,
    notes                     text
);

create table if not exists __SCHEMA__.servicer_position_report_line (
    servicer_report_line_id   uuid primary key,
    servicer_report_batch_id  uuid not null references __SCHEMA__.servicer_report_batch(servicer_report_batch_id) on delete cascade,
    loan_id                   uuid not null references __SCHEMA__.loan_contract(loan_id),
    report_as_of_date         date not null,
    principal_outstanding     numeric(24,2),
    interest_accrued_unpaid   numeric(24,2),
    fees_accrued_unpaid       numeric(24,2),
    penalty_accrued_unpaid    numeric(24,2),
    commitment_available      numeric(24,2),
    next_due_date             date,
    next_due_amount           numeric(24,2),
    delinquency_status        text,
    raw_payload               jsonb not null,
    created_at                timestamptz not null default now()
);

create table if not exists __SCHEMA__.servicer_transaction_report_line (
    servicer_report_line_id   uuid primary key,
    servicer_report_batch_id  uuid not null references __SCHEMA__.servicer_report_batch(servicer_report_batch_id) on delete cascade,
    loan_id                   uuid not null references __SCHEMA__.loan_contract(loan_id),
    servicer_transaction_id   text,
    transaction_type          text not null,
    effective_date            date not null,
    transaction_date          date,
    settlement_date           date,
    gross_amount              numeric(24,2) not null,
    principal_amount          numeric(24,2),
    interest_amount           numeric(24,2),
    fee_amount                numeric(24,2),
    penalty_amount            numeric(24,2),
    currency                  text,
    external_ref              text,
    raw_payload               jsonb not null,
    created_at                timestamptz not null default now()
);

create table if not exists __SCHEMA__.outbox_message (
    outbox_message_id         uuid primary key,
    topic                     text not null,
    message_key               text not null,
    payload                   jsonb not null,
    headers                   jsonb,
    occurred_at               timestamptz not null,
    visible_after             timestamptz not null default now(),
    processed_at              timestamptz,
    error_count               integer not null default 0,
    last_error                text
);

create index if not exists ix_outbox_message_pending
    on __SCHEMA__.outbox_message(processed_at, visible_after)
    where processed_at is null;

create table if not exists __SCHEMA__.read_model_checkpoint (
    projection_name           text primary key,
    last_processed_position   bigint not null,
    last_event_id             uuid,
    last_rebuilt_at           timestamptz,
    status                    text not null,
    details                   text
);
