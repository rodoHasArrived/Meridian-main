create table if not exists __SCHEMA__.loan_contract (
    loan_id uuid primary key,
    aggregate_version bigint not null,
    facility_name text not null,
    borrower_id uuid not null,
    borrower_name text not null,
    legal_entity_id uuid,
    status text not null,
    effective_date date not null,
    activation_date date,
    close_date date,
    current_terms_version integer not null,
    base_currency text not null,
    updated_at timestamptz not null default now()
);

create index if not exists ix_loan_contract_borrower
    on __SCHEMA__.loan_contract (borrower_id);

create index if not exists ix_loan_contract_status
    on __SCHEMA__.loan_contract (status);

create table if not exists __SCHEMA__.loan_terms_version (
    loan_id uuid not null,
    terms_version integer not null,
    terms_hash text not null,
    source_action text not null,
    amendment_reason text,
    recorded_at timestamptz not null,
    origination_date date not null,
    maturity_date date not null,
    commitment_amount numeric(24,2) not null,
    base_currency text not null,
    rate_type_kind text not null,
    fixed_annual_rate numeric(18,8),
    interest_index_name text,
    spread_bps numeric(18,8),
    floor_rate numeric(18,8),
    cap_rate numeric(18,8),
    day_count_basis text not null,
    payment_frequency text not null,
    amortization_type text not null,
    commitment_fee_rate numeric(18,8),
    default_rate_spread_bps numeric(18,8),
    prepayment_allowed boolean not null,
    covenants_json jsonb,
    primary key (loan_id, terms_version)
);

create index if not exists ix_loan_terms_version_maturity
    on __SCHEMA__.loan_terms_version (maturity_date);

create table if not exists __SCHEMA__.loan_servicing_projection (
    loan_id uuid primary key,
    aggregate_version bigint not null,
    status text not null,
    current_commitment numeric(24,2) not null,
    total_drawn numeric(24,2) not null,
    available_to_draw numeric(24,2) not null,
    principal_outstanding numeric(24,2) not null,
    interest_accrued_unpaid numeric(24,2) not null,
    commitment_fee_accrued_unpaid numeric(24,2) not null,
    fees_accrued_unpaid numeric(24,2) not null,
    penalty_accrued_unpaid numeric(24,2) not null,
    current_rate_reset_json jsonb,
    last_accrual_date date,
    last_payment_date date,
    servicing_revision bigint not null,
    updated_at timestamptz not null default now()
);

create table if not exists __SCHEMA__.drawdown_lot_projection (
    lot_id uuid primary key,
    loan_id uuid not null,
    drawdown_date date not null,
    settle_date date not null,
    original_principal numeric(24,2) not null,
    remaining_principal numeric(24,2) not null,
    external_ref text
);

create index if not exists ix_drawdown_lot_projection_loan
    on __SCHEMA__.drawdown_lot_projection (loan_id, drawdown_date);

create table if not exists __SCHEMA__.servicing_revision_projection (
    loan_id uuid not null,
    revision_number bigint not null,
    revision_source_type text not null,
    effective_as_of_date date not null,
    created_at timestamptz not null,
    notes text not null,
    primary key (loan_id, revision_number)
);

create index if not exists ix_servicing_revision_projection_loan
    on __SCHEMA__.servicing_revision_projection (loan_id, revision_number desc);
