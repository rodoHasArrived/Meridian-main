-- Migration 043: relational terms projections for the twelve asset classes that had none.
--
-- Until now 13 of the 26 catalog classes had a relational projection; the other 13 kept their terms
-- only inside securities.asset_specific_terms, reachable one security at a time by parsing the blob.
-- Twelve of them are projected here through the schema-driven writer (SecurityTermsProjectionRegistry):
-- the money-market and short-paper classes (CommercialPaper, TreasuryBill, Repo, CashSweep), the
-- catch-alls (OtherSecurity), the derivatives without a dedicated writer (Cfd, Warrant), funds
-- (InvestmentFund), and the four Asset Operations classes that sat on the shrink-only projection
-- backlog (PrivateFundInterest, PrivateCompanyEquity, RealEstateHolding, CommitmentGuarantee).
-- CustomAsset is the one deliberate gap: its fields are governed per profile and have no fixed
-- column set.
--
-- Every table is an additive read model over the same blob, keyed by security_id like every other
-- reference projection; the blob stays the source of truth and nothing here changes what the
-- cash-flow, obligation or ledger paths read. NOT NULL columns are the terms the contract marks
-- Required and the writer gates on; a record missing one gets no row rather than a partial one.
--
-- ROLLOUT: like every projection migration before it, this creates the tables empty. Records
-- populate them when next persisted, so an existing database needs a one-time
-- SecurityMasterRebuildOrchestrator.RebuildAssetClassAsync(<class>) per class, which folds the
-- class's event streams and upserts the projections without advancing the shared replay checkpoint.

create table if not exists __SCHEMA__.commercial_paper_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    issuer_name              text            not null,
    maturity_date            date            not null,
    discount_rate            numeric(18,8),
    day_count                text,
    is_asset_backed          boolean         not null default false,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists commercial_paper_projection_maturity_idx
    on __SCHEMA__.commercial_paper_projection (maturity_date);

create index if not exists commercial_paper_projection_issuer_idx
    on __SCHEMA__.commercial_paper_projection (lower(issuer_name));

create table if not exists __SCHEMA__.treasury_bill_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    maturity_date            date            not null,
    auction_date             date,
    cusip                    text,
    discount_rate            numeric(18,8),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists treasury_bill_projection_maturity_idx
    on __SCHEMA__.treasury_bill_projection (maturity_date);

create table if not exists __SCHEMA__.repo_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    counterparty             text            not null,
    start_date               date            not null,
    end_date                 date            not null,
    repo_rate                numeric(18,8),
    collateral_type          text,
    haircut                  numeric(18,8),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists repo_projection_end_date_idx
    on __SCHEMA__.repo_projection (end_date);

create index if not exists repo_projection_counterparty_idx
    on __SCHEMA__.repo_projection (lower(counterparty));

create table if not exists __SCHEMA__.cash_sweep_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    program_name             text            not null,
    sweep_vehicle_type       text            not null,
    sweep_frequency          text,
    target_account_type      text,
    yield_rate               numeric(18,8),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create table if not exists __SCHEMA__.other_security_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    category                 text            not null,
    sub_type                 text,
    maturity_date            date,
    issuer_name              text,
    settlement_type          text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists other_security_projection_category_idx
    on __SCHEMA__.other_security_projection (lower(category));

create table if not exists __SCHEMA__.cfd_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    underlying_asset_class   text            not null,
    underlying_description   text,
    leverage                 numeric(18,8),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

-- underlying_id is a Security Master identity (the first Guid-typed projected term); it is not a
-- foreign key because the underlying may be registered after the warrant and projections are
-- rebuilt class by class.
create table if not exists __SCHEMA__.warrant_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    underlying_id            uuid            not null,
    warrant_type             text            not null,
    strike                   numeric(28,10),
    expiry_date              date,
    multiplier               numeric(18,8),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists warrant_projection_underlying_idx
    on __SCHEMA__.warrant_projection (underlying_id);

create index if not exists warrant_projection_expiry_idx
    on __SCHEMA__.warrant_projection (expiry_date);

-- Every InvestmentFund term is optional, so no column beyond the identity spine is NOT NULL and a
-- row exists for every record of the class.
create table if not exists __SCHEMA__.investment_fund_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    fund_type                text,
    fund_family              text,
    nav_currency             text,
    distribution_policy      text,
    is_stable_nav            boolean,
    pricing_source           text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists investment_fund_projection_fund_family_idx
    on __SCHEMA__.investment_fund_projection (lower(fund_family));

create table if not exists __SCHEMA__.private_fund_interest_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    gp_sponsor               text            not null,
    strategy                 text            not null,
    vintage                  integer         not null,
    commitment               numeric(28,10)  not null,
    funded_amount            numeric(28,10),
    unfunded_amount          numeric(28,10),
    nav_date                 date            not null,
    lockup                   text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists private_fund_interest_projection_gp_sponsor_idx
    on __SCHEMA__.private_fund_interest_projection (lower(gp_sponsor));

create index if not exists private_fund_interest_projection_vintage_idx
    on __SCHEMA__.private_fund_interest_projection (vintage);

create table if not exists __SCHEMA__.private_company_equity_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    issuer                   text            not null,
    share_class              text            not null,
    round                    text            not null,
    ownership_percent        numeric(18,8),
    cost_basis               numeric(28,10)  not null,
    latest_valuation         numeric(28,10),
    transfer_restrictions    text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists private_company_equity_projection_issuer_idx
    on __SCHEMA__.private_company_equity_projection (lower(issuer));

create table if not exists __SCHEMA__.real_estate_holding_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    property_type            text            not null,
    address_or_market        text            not null,
    ownership_percent        numeric(18,8)   not null,
    appraisal_value          numeric(28,10)  not null,
    valuation_date           date            not null,
    debt_stack               text,
    sponsor                  text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists real_estate_holding_projection_valuation_date_idx
    on __SCHEMA__.real_estate_holding_projection (valuation_date);

create table if not exists __SCHEMA__.commitment_guarantee_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    counterparty             text            not null,
    beneficiary              text,
    committed_amount         numeric(28,10)  not null,
    unfunded_amount          numeric(28,10),
    effective_date           date            not null,
    expiry_date              date,
    fee_rate                 numeric(18,8),
    collateral               text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists commitment_guarantee_projection_counterparty_idx
    on __SCHEMA__.commitment_guarantee_projection (lower(counterparty));

create index if not exists commitment_guarantee_projection_expiry_idx
    on __SCHEMA__.commitment_guarantee_projection (expiry_date);

-- Threshold is text, like the direct-loan covenant: the canonical covenant carries it as written
-- ("$5m", "4.5x"), and coercing it to a number would drop every ratio covenant.
create table if not exists __SCHEMA__.commitment_guarantee_covenant_projection (
    security_id   uuid    not null references __SCHEMA__.commitment_guarantee_projection(security_id) on delete cascade,
    ordinal       integer not null,
    covenant_type text    not null,
    threshold     text    not null,
    notes         text,
    primary key (security_id, ordinal)
);

comment on table __SCHEMA__.commercial_paper_projection is
    'Relational projection of CommercialPaper asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms, which remains the source of truth.';
comment on table __SCHEMA__.treasury_bill_projection is
    'Relational projection of TreasuryBill asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.repo_projection is
    'Relational projection of Repo asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.cash_sweep_projection is
    'Relational projection of CashSweep asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.other_security_projection is
    'Relational projection of OtherSecurity asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.cfd_projection is
    'Relational projection of Cfd asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.warrant_projection is
    'Relational projection of Warrant asset-specific terms, keyed by security_id. underlying_id is the underlying security identity, deliberately not a foreign key.';
comment on table __SCHEMA__.investment_fund_projection is
    'Relational projection of InvestmentFund asset-specific terms, keyed by security_id. Every term is optional, so a row exists for every record of the class.';
comment on table __SCHEMA__.private_fund_interest_projection is
    'Relational projection of PrivateFundInterest terms (commitment, funded and unfunded amounts, NAV date), keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.private_company_equity_projection is
    'Relational projection of PrivateCompanyEquity terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.real_estate_holding_projection is
    'Relational projection of RealEstateHolding terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.commitment_guarantee_projection is
    'Relational projection of CommitmentGuarantee terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
comment on table __SCHEMA__.commitment_guarantee_covenant_projection is
    'Covenants declared by a projected commitment guarantee, in terms-document order (ordinal). Threshold is text because the canonical covenant term is written prose.';
