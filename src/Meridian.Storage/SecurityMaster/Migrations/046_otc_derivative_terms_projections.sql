-- Migration 046: relational terms projections for the OTC derivative classes.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- FxForward, NonDeliverableForward and CreditDefaultSwap join the Security Master catalog as
-- declarative classes: their terms are written by the schema-driven projection path
-- (SecurityTermsProjectionRegistry) from the day they are modeled, so there is no unprojected
-- interval to backfill later. Every table is an additive read model over
-- securities.asset_specific_terms, keyed by security_id like every other reference projection; the
-- blob stays the source of truth. NOT NULL columns are the terms the contract marks Required and the
-- writer gates on; a record missing one gets no row rather than a partial one.
--
-- ROLLOUT: the tables are created empty. Records populate them when next persisted; an existing
-- database that already holds records of these classes (none can, before this migration) would use
-- SecurityMasterRebuildOrchestrator.RebuildAssetClassAsync(<class>).

create table if not exists __SCHEMA__.fx_forward_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    base_currency            text            not null,
    quote_currency           text            not null,
    notional                 numeric(28,10),
    forward_rate             numeric(28,10),
    trade_date               date,
    value_date               date            not null,
    settlement_type          text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists fx_forward_projection_value_date_idx
    on __SCHEMA__.fx_forward_projection (value_date);

create index if not exists fx_forward_projection_pair_idx
    on __SCHEMA__.fx_forward_projection (base_currency, quote_currency);

comment on table __SCHEMA__.fx_forward_projection is
    'Relational projection of FxForward asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';

create table if not exists __SCHEMA__.ndf_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    base_currency            text            not null,
    quote_currency           text            not null,
    settlement_currency      text            not null,
    notional                 numeric(28,10),
    forward_rate             numeric(28,10),
    fixing_date              date            not null,
    value_date               date            not null,
    fixing_source            text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists ndf_projection_fixing_date_idx
    on __SCHEMA__.ndf_projection (fixing_date);

create index if not exists ndf_projection_value_date_idx
    on __SCHEMA__.ndf_projection (value_date);

comment on table __SCHEMA__.ndf_projection is
    'Relational projection of NonDeliverableForward asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';

create table if not exists __SCHEMA__.credit_default_swap_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    reference_entity         text            not null,
    reference_obligation     text,
    protection_side          text            not null,
    notional                 numeric(28,10)  not null,
    fixed_coupon_bps         numeric(18,8)   not null,
    effective_date           date            not null,
    maturity_date            date            not null,
    payment_frequency        text,
    day_count                text,
    seniority                text,
    restructuring_clause     text,
    upfront_points           numeric(18,8),
    recovery_rate_assumption numeric(18,10),
    index_name               text,
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists credit_default_swap_projection_reference_entity_idx
    on __SCHEMA__.credit_default_swap_projection (lower(reference_entity));

create index if not exists credit_default_swap_projection_maturity_idx
    on __SCHEMA__.credit_default_swap_projection (maturity_date);

comment on table __SCHEMA__.credit_default_swap_projection is
    'Relational projection of CreditDefaultSwap asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
