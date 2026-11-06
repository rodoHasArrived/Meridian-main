-- Migration 014: money market fund reference projection table
create table if not exists __SCHEMA__.money_market_fund_projection (
    security_id                      uuid          not null primary key,
    display_name                     text          not null,
    currency                         text          not null,
    fund_family                      text,
    sweep_eligible                   boolean       not null default false,
    weighted_average_maturity_days   integer,
    liquidity_fee_eligible           boolean       not null default false,
    primary_identifier_value         text          not null,
    version                          bigint        not null
);

create index if not exists money_market_fund_projection_family_idx
    on __SCHEMA__.money_market_fund_projection (lower(fund_family));

create index if not exists money_market_fund_projection_sweep_idx
    on __SCHEMA__.money_market_fund_projection (sweep_eligible);
