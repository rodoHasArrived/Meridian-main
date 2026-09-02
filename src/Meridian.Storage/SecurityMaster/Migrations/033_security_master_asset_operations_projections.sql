-- Migration 033: DirectLoan and StructuredCredit relational terms projections.
--
-- DirectLoan and StructuredCredit are Asset Operations classes: the catalog gives both a capability
-- set declaring ProjectedCashFlows, Reconciliation and LedgerProjection, so their economic terms
-- drive money movement rather than only labelling a record. Until now every one of those terms lived
-- only inside the securities.asset_specific_terms JSONB blob, reachable one security at a time by
-- parsing the document -- there was no way to ask which loans amortize in a window, what a pool
-- factor was on a date, or which covenants a borrower carries, without reading every record.
--
-- These tables are additive read models over the same blob, written by the schema-driven projection
-- writer and keyed by security_id like every other reference projection. The blob stays the source
-- of truth: nothing here changes what the cash-flow, obligation or ledger paths read.
--
-- The two schedules get child tables rather than a jsonb column because their rows are the queryable
-- unit -- a principal instalment due in a window, a pool factor effective on a date. Ordinal keeps
-- the persisted order identical to the terms document, so a projected schedule reads back in the
-- order the contract declares it rather than in whatever order a scan returns.
--
-- Not to be confused with the direct-lending servicing tables (loan_contract, loan_terms_version),
-- which DirectLendingOptions co-locates in this same schema by default: those key on loan_id and
-- belong to Meridian's own loan origination aggregate, written by a different migration runner.
-- These key on security_id and project a Security Master reference record. The two families being
-- neighbours in one schema is exactly why the distinction is written down here.

create table if not exists __SCHEMA__.direct_loan_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    borrower                 text            not null,
    maturity_date            date,
    reference_index          text,
    spread_bps               numeric(18,8),
    current_coupon_rate      numeric(12,6),
    reset_frequency          text,
    pricing_source           text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists direct_loan_projection_borrower_idx
    on __SCHEMA__.direct_loan_projection (lower(borrower));

create index if not exists direct_loan_projection_maturity_idx
    on __SCHEMA__.direct_loan_projection (maturity_date);

create index if not exists direct_loan_projection_reference_index_idx
    on __SCHEMA__.direct_loan_projection (lower(reference_index));

-- Covenant threshold is text, not numeric: the canonical covenant carries thresholds as written
-- ("4.5x", "2.00x fixed charge"), and coercing them to a number would drop every ratio covenant.
create table if not exists __SCHEMA__.direct_loan_covenant_projection (
    security_id   uuid    not null references __SCHEMA__.direct_loan_projection(security_id) on delete cascade,
    ordinal       integer not null,
    covenant_type text    not null,
    threshold     text    not null,
    notes         text,
    primary key (security_id, ordinal)
);

create table if not exists __SCHEMA__.direct_loan_principal_schedule_projection (
    security_id  uuid            not null references __SCHEMA__.direct_loan_projection(security_id) on delete cascade,
    ordinal      integer         not null,
    payment_date date            not null,
    amount       numeric(28,10)  not null,
    primary key (security_id, ordinal)
);

-- Serves the instalments-due-in-a-window query the JSONB blob cannot answer at all.
create index if not exists direct_loan_principal_schedule_projection_payment_date_idx
    on __SCHEMA__.direct_loan_principal_schedule_projection (payment_date);

create table if not exists __SCHEMA__.structured_credit_projection (
    security_id               uuid            not null primary key,
    display_name              text            not null,
    currency                  text            not null,
    tranche                   text            not null,
    pool_id                   text,
    collateral_type           text            not null,
    original_face             numeric(28,10)  not null,
    current_factor            numeric(18,10),
    coupon_or_index           text            not null,
    -- The free-text trustee-report pointer (structured credit's factorSchedule term), kept distinct
    -- from the typed dated schedule below so prose is never mistaken for factor data.
    factor_schedule_reference text,
    maturity_date             date,
    primary_identifier_value  text            not null,
    version                   bigint          not null
);

create index if not exists structured_credit_projection_pool_idx
    on __SCHEMA__.structured_credit_projection (lower(pool_id));

create index if not exists structured_credit_projection_collateral_type_idx
    on __SCHEMA__.structured_credit_projection (lower(collateral_type));

create table if not exists __SCHEMA__.structured_credit_factor_schedule_projection (
    security_id uuid            not null references __SCHEMA__.structured_credit_projection(security_id) on delete cascade,
    ordinal     integer         not null,
    as_of_date  date            not null,
    factor      numeric(18,10)  not null,
    primary key (security_id, ordinal)
);

-- Serves the factor-as-of lookup (latest entry on or before a date) in one indexed read instead of
-- a scan over the whole schedule.
create index if not exists structured_credit_factor_schedule_projection_as_of_idx
    on __SCHEMA__.structured_credit_factor_schedule_projection (security_id, as_of_date desc);

comment on table __SCHEMA__.direct_loan_projection is
    'Relational projection of DirectLoan asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms, which remains the source of truth. Distinct from the loan_contract family in this same schema, which keys on loan_id and belongs to the direct-lending servicing aggregate.';

comment on table __SCHEMA__.direct_loan_covenant_projection is
    'Covenants declared by a projected direct loan, in terms-document order (ordinal). Threshold is text because the canonical covenant term is written prose ("4.5x"), not a number.';

comment on table __SCHEMA__.direct_loan_principal_schedule_projection is
    'Contractual principal instalments of a projected direct loan, in terms-document order (ordinal). Makes instalments-due-in-a-window answerable without parsing every security document.';

comment on table __SCHEMA__.structured_credit_projection is
    'Relational projection of StructuredCredit tranche terms, keyed by security_id. Additive read model over securities.asset_specific_terms, which remains the source of truth. factor_schedule_reference is the free-text trustee-report pointer, never factor data.';

comment on table __SCHEMA__.structured_credit_factor_schedule_projection is
    'Dated pool-factor points of a projected structured-credit tranche, in terms-document order (ordinal). Serves the factor-as-of lookup used by amortization.';
