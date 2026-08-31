-- Direct Lending servicer statement intake evidence and apply audit.

create table if not exists __SCHEMA__.servicer_statement_import_batch (
    servicer_statement_batch_id uuid primary key,
    servicer_report_batch_id    uuid references __SCHEMA__.servicer_report_batch(servicer_report_batch_id),
    statement_kind              text not null,
    servicer_name               text not null,
    statement_date              date not null,
    source_format               text not null,
    source_file_name            text,
    raw_payload_hash            text not null,
    imported_by                 text,
    imported_at                 timestamptz not null default now(),
    fund_account_id             text,
    cash_account_id             text,
    statement_run_id            text,
    status                      text not null,
    evidence_route              text,
    row_count                   integer not null default 0,
    mapped_row_count            integer not null default 0,
    unmapped_row_count          integer not null default 0,
    duplicate_row_count         integer not null default 0,
    ready_to_apply_row_count    integer not null default 0,
    applied_row_count           integer not null default 0,
    notes                       text
);

create unique index if not exists ux_servicer_statement_import_batch_payload
    on __SCHEMA__.servicer_statement_import_batch(raw_payload_hash, servicer_name, statement_date);

create table if not exists __SCHEMA__.servicer_statement_import_row (
    servicer_statement_row_id   uuid primary key,
    servicer_statement_batch_id uuid not null references __SCHEMA__.servicer_statement_import_batch(servicer_statement_batch_id) on delete cascade,
    row_id                      text not null,
    row_number                  integer not null,
    statement_kind              text not null,
    row_status                  text not null,
    loan_id                     uuid,
    security_id                 uuid,
    security_symbol             text,
    external_loan_reference     text,
    servicer_loan_number        text,
    borrower_reference          text,
    fund_account_id             text,
    cash_account_id             text,
    statement_date              date,
    effective_date              date,
    settlement_date             date,
    principal_outstanding       numeric(24, 2),
    interest_accrued_unpaid     numeric(24, 2),
    fees_accrued_unpaid         numeric(24, 2),
    penalty_accrued_unpaid      numeric(24, 2),
    commitment_available        numeric(24, 2),
    gross_amount                numeric(24, 2),
    principal_amount            numeric(24, 2),
    interest_amount             numeric(24, 2),
    fee_amount                  numeric(24, 2),
    penalty_amount              numeric(24, 2),
    currency                    text,
    external_ref                text,
    row_hash                    text not null,
    raw_payload                 jsonb not null,
    suggested_apply_mode        text,
    evidence_route              text,
    created_at                  timestamptz not null default now(),
    unique (servicer_statement_batch_id, row_id)
);

create unique index if not exists ux_servicer_statement_import_row_hash
    on __SCHEMA__.servicer_statement_import_row(row_hash);

create index if not exists ix_servicer_statement_import_row_status
    on __SCHEMA__.servicer_statement_import_row(servicer_statement_batch_id, row_status);

create table if not exists __SCHEMA__.servicer_statement_validation_issue (
    servicer_statement_issue_id uuid primary key,
    servicer_statement_batch_id uuid not null references __SCHEMA__.servicer_statement_import_batch(servicer_statement_batch_id) on delete cascade,
    row_id                      text,
    issue_code                  text not null,
    severity                    text not null,
    message                     text not null,
    field_name                  text,
    route                       text,
    created_at                  timestamptz not null default now()
);

create index if not exists ix_servicer_statement_validation_issue_batch
    on __SCHEMA__.servicer_statement_validation_issue(servicer_statement_batch_id, severity, issue_code);

create table if not exists __SCHEMA__.servicer_statement_apply_audit (
    servicer_statement_apply_id uuid primary key,
    servicer_statement_batch_id uuid not null references __SCHEMA__.servicer_statement_import_batch(servicer_statement_batch_id) on delete cascade,
    row_id                      text not null,
    loan_id                     uuid,
    apply_mode                  text not null,
    applied_by                  text,
    applied_at                  timestamptz not null default now(),
    command_id                  uuid,
    correlation_id              uuid,
    source_system               text,
    status                      text not null,
    result_message              text,
    rationale                   text
);

create index if not exists ix_servicer_statement_apply_audit_batch
    on __SCHEMA__.servicer_statement_apply_audit(servicer_statement_batch_id, row_id);
