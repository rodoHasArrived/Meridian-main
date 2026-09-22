-- Retain observations by effective timestamp. Existing observations remain explicitly untyped;
-- historical prices or quote units are never invented for legacy data.
alter table __SCHEMA__.security_raw_prices
    add column if not exists price_unit text not null default 'Unspecified';
alter table __SCHEMA__.security_raw_prices drop constraint if exists security_raw_prices_pkey;
alter table __SCHEMA__.security_raw_prices
    add primary key (security_id, source_id, price_as_of);
alter table __SCHEMA__.security_raw_prices
    add constraint security_raw_prices_unit_check check (price_unit in ('Unspecified', 'CurrencyPerUnit', 'PercentOfPar'));

alter table __SCHEMA__.security_pricing_hierarchy
    add column if not exists recorded_at timestamptz not null default now();

create table if not exists __SCHEMA__.security_pricing_hierarchy_history (
    security_id uuid not null references __SCHEMA__.securities(security_id),
    account_id text not null,
    entries jsonb not null,
    as_of timestamptz not null,
    updated_by text not null,
    recorded_at timestamptz not null default now(),
    primary key (security_id, account_id, as_of)
);
