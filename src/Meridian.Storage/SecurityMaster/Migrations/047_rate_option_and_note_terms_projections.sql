-- Migration 047: relational terms projections for the rate options and linked notes.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Swaption, CapFloor and StructuredNote join the Security Master catalog as declarative classes:
-- their terms are written by the schema-driven projection path (SecurityTermsProjectionRegistry)
-- from the day they are modeled. Every table is an additive read model over
-- securities.asset_specific_terms, keyed by security_id like every other reference projection; the
-- blob stays the source of truth. NOT NULL columns are the terms the contract marks Required and the
-- writer gates on; a record missing one gets no row rather than a partial one. Strike and rate
-- columns are signed: negative strikes are valid in negative-rate currencies.
--
-- ROLLOUT: the tables are created empty and populate as records of these classes are persisted.

create table if not exists __SCHEMA__.swaption_projection (
    security_id                 uuid            not null primary key,
    display_name                text            not null,
    currency                    text            not null,
    payer_receiver              text            not null,
    exercise_style              text,
    strike_rate                 numeric(18,10)  not null,
    expiry_date                 date            not null,
    underlying_effective_date   date,
    underlying_maturity_date    date            not null,
    notional                    numeric(28,10)  not null,
    floating_index              text,
    fixed_leg_payment_frequency text,
    fixed_leg_day_count         text,
    settlement_type             text,
    primary_identifier_value    text            not null,
    version                     bigint          not null
);

create index if not exists swaption_projection_expiry_idx
    on __SCHEMA__.swaption_projection (expiry_date);

comment on table __SCHEMA__.swaption_projection is
    'Relational projection of Swaption asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';

create table if not exists __SCHEMA__.cap_floor_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    cap_floor_type           text            not null,
    reference_index          text            not null,
    cap_strike               numeric(18,10),
    floor_strike             numeric(18,10),
    notional                 numeric(28,10)  not null,
    effective_date           date            not null,
    maturity_date            date            not null,
    reset_frequency          text,
    day_count                text,
    premium_amount           numeric(28,10),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists cap_floor_projection_maturity_idx
    on __SCHEMA__.cap_floor_projection (maturity_date);

create index if not exists cap_floor_projection_reference_index_idx
    on __SCHEMA__.cap_floor_projection (lower(reference_index));

comment on table __SCHEMA__.cap_floor_projection is
    'Relational projection of CapFloor asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';

create table if not exists __SCHEMA__.structured_note_projection (
    security_id              uuid            not null primary key,
    display_name             text            not null,
    currency                 text            not null,
    issuer_name              text            not null,
    underlying_description   text            not null,
    maturity_date            date            not null,
    issue_date               date,
    denomination             numeric(28,10),
    principal_protection_pct numeric(18,10),
    participation_rate       numeric(18,10),
    return_cap               numeric(18,10),
    coupon_rate              numeric(18,10),
    payment_frequency        text,
    barrier_level            numeric(18,10),
    primary_identifier_value text            not null,
    version                  bigint          not null
);

create index if not exists structured_note_projection_maturity_idx
    on __SCHEMA__.structured_note_projection (maturity_date);

create index if not exists structured_note_projection_issuer_idx
    on __SCHEMA__.structured_note_projection (lower(issuer_name));

comment on table __SCHEMA__.structured_note_projection is
    'Relational projection of StructuredNote asset-specific terms, keyed by security_id. Additive read model over securities.asset_specific_terms.';
