-- Migration 017: Security Master Pricing Hierarchy & Raw Prices
-- Implements the Clearwater pricing hierarchy model: per-security/account priority chain
-- of pricing sources, max-days-stale configuration, and raw price records for golden copy selection.

create table if not exists __SCHEMA__.security_pricing_hierarchy (
    security_id  uuid    not null references __SCHEMA__.securities(security_id),
    account_id   text    not null default '',
    entries      jsonb   not null default '[]'::jsonb,
    as_of        timestamptz not null default now(),
    updated_by   text    not null,
    primary key (security_id, account_id)
);

create index if not exists ix_security_pricing_hierarchy_security_id
    on __SCHEMA__.security_pricing_hierarchy (security_id);

create table if not exists __SCHEMA__.security_raw_prices (
    security_id  uuid    not null references __SCHEMA__.securities(security_id),
    source_id    text    not null,
    price        numeric(28, 10) not null,
    price_as_of  timestamptz not null,
    recorded_by  text    not null,
    recorded_at  timestamptz not null default now(),
    primary key (security_id, source_id)
);

create index if not exists ix_security_raw_prices_security_id
    on __SCHEMA__.security_raw_prices (security_id);
