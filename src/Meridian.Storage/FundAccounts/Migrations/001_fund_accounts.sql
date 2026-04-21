-- Fund Accounts schema: custodian accounts, bank accounts, balance snapshots,
-- statement ingestion, and account-level reconciliation.
-- Schema variable replaced at runtime: __SCHEMA__ -> fund_accounts (default)

create schema if not exists __SCHEMA__;

-- ── Core account definition ────────────────────────────────────────────────────

create table if not exists __SCHEMA__.account_definition (
    account_id          uuid        primary key,
    account_type        text        not null,       -- AccountTypeDto value
    entity_id           uuid,
    fund_id             uuid,
    sleeve_id           uuid,
    vehicle_id          uuid,
    account_code        text        not null,
    display_name        text        not null,
    base_currency       text        not null,
    institution         text,
    is_active           boolean     not null default true,
    effective_from      timestamptz not null,
    effective_to        timestamptz,
    portfolio_id        text,
    ledger_reference    text,
    strategy_id         text,
    run_id              text,
    -- Extended settlement details (nullable; present for Custody and Bank types)
    custodian_details   jsonb,
    bank_details        jsonb,
    created_at          timestamptz not null default now(),
    updated_at          timestamptz not null default now()
);

create unique index if not exists ux_account_code_active
    on __SCHEMA__.account_definition (account_code)
    where is_active = true;

create index if not exists ix_account_fund
    on __SCHEMA__.account_definition (fund_id, account_type)
    where fund_id is not null;

create index if not exists ix_account_entity
    on __SCHEMA__.account_definition (entity_id)
    where entity_id is not null;

-- ── External balance snapshots ─────────────────────────────────────────────────

create table if not exists __SCHEMA__.account_balance_snapshot (
    snapshot_id             uuid        primary key,
    account_id              uuid        not null references __SCHEMA__.account_definition (account_id),
    fund_id                 uuid,
    as_of_date              date        not null,
    currency                text        not null,
    cash_balance            numeric(24,6) not null,
    securities_market_value numeric(24,6),
    accrued_interest        numeric(24,6),
    pending_settlement      numeric(24,6),
    source                  text        not null,
    recorded_at             timestamptz not null default now(),
    external_reference      text
);

create index if not exists ix_balance_snapshot_account_date
    on __SCHEMA__.account_balance_snapshot (account_id, as_of_date desc);

-- ── Custodian statement batches and position lines ────────────────────────────

create table if not exists __SCHEMA__.custodian_statement_batch (
    batch_id        uuid        primary key,
    account_id      uuid        not null references __SCHEMA__.account_definition (account_id),
    as_of_date      date        not null,
    custodian_name  text        not null,
    source_format   text        not null,
    file_name       text,
    line_count      integer     not null,
    ingested_at     timestamptz not null default now(),
    loaded_by       text        not null
);

create table if not exists __SCHEMA__.custodian_position_line (
    position_line_id      uuid          primary key,
    batch_id              uuid          not null references __SCHEMA__.custodian_statement_batch (batch_id) on delete cascade,
    account_id            uuid          not null references __SCHEMA__.account_definition (account_id),
    as_of_date            date          not null,
    security_identifier   text          not null,
    identifier_type       text          not null,
    quantity              numeric(24,8) not null,
    market_value          numeric(24,6) not null,
    market_value_currency text          not null,
    cost_basis            numeric(24,6),
    accrued_income        numeric(24,6),
    settlement_pending    boolean       not null default false,
    raw_payload           jsonb
);

create index if not exists ix_custodian_position_account_date
    on __SCHEMA__.custodian_position_line (account_id, as_of_date desc);

-- ── Bank statement batches and transaction lines ──────────────────────────────

create table if not exists __SCHEMA__.bank_statement_batch (
    batch_id        uuid        primary key,
    account_id      uuid        not null references __SCHEMA__.account_definition (account_id),
    statement_date  date        not null,
    bank_name       text        not null,
    file_name       text,
    line_count      integer     not null,
    ingested_at     timestamptz not null default now(),
    loaded_by       text        not null
);

create table if not exists __SCHEMA__.bank_statement_line (
    statement_line_id   uuid          primary key,
    batch_id            uuid          not null references __SCHEMA__.bank_statement_batch (batch_id) on delete cascade,
    account_id          uuid          not null references __SCHEMA__.account_definition (account_id),
    statement_date      date          not null,
    value_date          date          not null,
    amount              numeric(24,6) not null,
    currency            text          not null,
    transaction_type    text          not null,
    description         text          not null,
    external_reference  text,
    running_balance     numeric(24,6)
);

create index if not exists ix_bank_statement_account_date
    on __SCHEMA__.bank_statement_line (account_id, statement_date desc);

-- ── Account reconciliation runs and results ───────────────────────────────────

create table if not exists __SCHEMA__.account_reconciliation_run (
    reconciliation_run_id   uuid          primary key,
    account_id              uuid          not null references __SCHEMA__.account_definition (account_id),
    as_of_date              date          not null,
    status                  text          not null,
    total_checks            integer       not null default 0,
    total_matched           integer       not null default 0,
    total_breaks            integer       not null default 0,
    break_amount_total      numeric(24,6) not null default 0,
    requested_at            timestamptz   not null default now(),
    completed_at            timestamptz,
    requested_by            text          not null
);

create index if not exists ix_recon_run_account
    on __SCHEMA__.account_reconciliation_run (account_id, as_of_date desc);

create table if not exists __SCHEMA__.account_reconciliation_result (
    result_id               uuid          primary key,
    reconciliation_run_id   uuid          not null references __SCHEMA__.account_reconciliation_run (reconciliation_run_id) on delete cascade,
    check_label             text          not null,
    is_match                boolean       not null,
    category                text          not null,
    status                  text          not null,
    expected_amount         numeric(24,6),
    actual_amount           numeric(24,6),
    variance                numeric(24,6),
    reason                  text          not null
);

create index if not exists ix_recon_result_run
    on __SCHEMA__.account_reconciliation_result (reconciliation_run_id);
