-- Migration 050: the OTC contract and collateral model.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- A bilateral trade is not an instrument. The Security Master record says what was traded (a
-- payer swap on SOFR, protection on a reference entity); the contract says who it was traded with,
-- how much, which way, under which netting set and where it is in its life. Netting sets group the
-- contracts whose exposures net on close-out under one master agreement (ISDA, GMRA, GMSLA), and a
-- collateral agreement (CSA) under that master states the thresholds, minimum transfer amount,
-- independent amount and eligible collateral that drive margin calls. Many contracts share one
-- instrument; a contract names its instrument with a foreign key that forbids deleting the
-- instrument from under it.
--
-- ROLLOUT: the tables are created empty; the model is populated through the contract API.

create table if not exists __SCHEMA__.otc_master_agreements (
    agreement_id       uuid        not null primary key,
    counterparty       text        not null,
    counterparty_lei   text        null,
    our_entity         text        not null,
    our_entity_id      uuid        null,
    agreement_type     text        not null,
    agreement_date     date        not null,
    governing_law      text        null,
    notes              text        null,
    version            bigint      not null,
    updated_by         text        not null,
    updated_at         timestamptz not null,
    constraint otc_master_agreements_type_check
        check (agreement_type in ('Isda1992', 'Isda2002', 'Gmra', 'Gmsla', 'Other'))
);

create index if not exists otc_master_agreements_counterparty_idx
    on __SCHEMA__.otc_master_agreements (counterparty);

comment on table __SCHEMA__.otc_master_agreements is
    'Master agreements (ISDA, GMRA, GMSLA) between a fund legal entity and a counterparty. Every bilateral contract hangs off one through its netting set.';

create table if not exists __SCHEMA__.otc_collateral_agreements (
    collateral_agreement_id uuid           not null primary key,
    master_agreement_id     uuid           not null references __SCHEMA__.otc_master_agreements (agreement_id),
    currency                text           not null,
    threshold_ours          numeric(28,10) not null,
    threshold_theirs        numeric(28,10) not null,
    minimum_transfer_amount numeric(28,10) not null,
    independent_amount      numeric(28,10) null,
    rounding_amount         numeric(28,10) null,
    valuation_frequency     text           not null,
    eligible_collateral     jsonb          not null,
    effective_date          date           not null,
    termination_date        date           null,
    notes                   text           null,
    version                 bigint         not null,
    updated_by              text           not null,
    updated_at              timestamptz    not null,
    constraint otc_collateral_agreements_amounts_check
        check (threshold_ours >= 0 and threshold_theirs >= 0 and minimum_transfer_amount >= 0),
    constraint otc_collateral_agreements_window_check
        check (termination_date is null or termination_date >= effective_date)
);

create index if not exists otc_collateral_agreements_master_idx
    on __SCHEMA__.otc_collateral_agreements (master_agreement_id);

comment on table __SCHEMA__.otc_collateral_agreements is
    'Credit support annexes under a master agreement: thresholds, minimum transfer amount, independent amount, rounding, valuation cadence and eligible collateral with haircuts.';

create table if not exists __SCHEMA__.otc_netting_sets (
    netting_set_id          uuid        not null primary key,
    master_agreement_id     uuid        not null references __SCHEMA__.otc_master_agreements (agreement_id),
    collateral_agreement_id uuid        null references __SCHEMA__.otc_collateral_agreements (collateral_agreement_id),
    name                    text        not null,
    notes                   text        null,
    version                 bigint      not null,
    updated_by              text        not null,
    updated_at              timestamptz not null
);

create index if not exists otc_netting_sets_master_idx
    on __SCHEMA__.otc_netting_sets (master_agreement_id);

comment on table __SCHEMA__.otc_netting_sets is
    'Sets of contracts under one master agreement whose exposures net on close-out, optionally margined under one collateral agreement.';

create table if not exists __SCHEMA__.otc_contracts (
    contract_id            uuid           not null primary key,
    security_id            uuid           not null references __SCHEMA__.securities (security_id),
    netting_set_id         uuid           not null references __SCHEMA__.otc_netting_sets (netting_set_id),
    external_trade_id      text           null,
    direction              text           not null,
    notional               numeric(28,10) not null,
    notional_currency      text           not null,
    trade_date             date           not null,
    effective_date         date           not null,
    termination_date       date           null,
    status                 text           not null,
    confirmation_reference text           null,
    notes                  text           null,
    version                bigint         not null,
    updated_by             text           not null,
    updated_at             timestamptz    not null,
    constraint otc_contracts_direction_check
        check (direction in ('Buy', 'Sell')),
    constraint otc_contracts_status_check
        check (status in ('Pending', 'Confirmed', 'Terminated', 'Novated', 'Matured')),
    constraint otc_contracts_notional_check
        check (notional > 0),
    constraint otc_contracts_dates_check
        check (effective_date >= trade_date and (termination_date is null or termination_date >= effective_date))
);

create index if not exists otc_contracts_security_idx
    on __SCHEMA__.otc_contracts (security_id);

create index if not exists otc_contracts_netting_set_idx
    on __SCHEMA__.otc_contracts (netting_set_id);

create unique index if not exists otc_contracts_external_trade_idx
    on __SCHEMA__.otc_contracts (external_trade_id)
    where external_trade_id is not null;

comment on table __SCHEMA__.otc_contracts is
    'Bilateral contracts: one trade against a counterparty on a Security Master instrument, in a netting set, with its notional, direction, dates and lifecycle status. Many contracts share one instrument.';
