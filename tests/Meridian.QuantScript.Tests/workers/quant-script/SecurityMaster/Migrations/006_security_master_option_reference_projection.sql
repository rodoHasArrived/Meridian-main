create table if not exists __SCHEMA__.option_contract_projection (
    contract_symbol text primary key,
    security_id uuid null references __SCHEMA__.securities(security_id) on delete set null,
    option_chain_id text not null,
    underlying_symbol text not null,
    underlying_security_id uuid null references __SCHEMA__.securities(security_id) on delete set null,
    put_call text not null,
    strike numeric not null,
    expiry_date date not null,
    multiplier numeric not null,
    is_adjusted boolean not null default false,
    last_trading_dt date null,
    lifecycle_stat text not null,
    updated_at timestamptz not null,
    version bigint not null
);

create unique index if not exists ux_option_contract_projection_security_id
    on __SCHEMA__.option_contract_projection (security_id)
    where security_id is not null;

create index if not exists ix_option_contract_projection_chain_expiry
    on __SCHEMA__.option_contract_projection (option_chain_id, expiry_date);

create index if not exists ix_option_contract_projection_underlying_expiry
    on __SCHEMA__.option_contract_projection (underlying_symbol, expiry_date);

create table if not exists __SCHEMA__.option_series_projection (
    option_chain_id text not null,
    contract_symbol text not null references __SCHEMA__.option_contract_projection(contract_symbol) on delete cascade,
    underlying_symbol text not null,
    expiry_date date not null,
    strike numeric not null,
    put_call text not null,
    primary key (option_chain_id, contract_symbol)
);

create table if not exists __SCHEMA__.option_lifecycle_projection (
    contract_symbol text primary key references __SCHEMA__.option_contract_projection(contract_symbol) on delete cascade,
    lifecycle_stat text not null,
    listed_at timestamptz not null,
    last_trading_dt date null,
    expiry_date date not null,
    version bigint not null
);

create table if not exists __SCHEMA__.option_alias_projection (
    contract_symbol text not null references __SCHEMA__.option_contract_projection(contract_symbol) on delete cascade,
    alias_kind text not null,
    alias_value text not null,
    provider text not null default '',
    normalized_alias text not null,
    primary key (contract_symbol, alias_kind, alias_value, provider)
);

create index if not exists ix_option_alias_projection_normalized_alias
    on __SCHEMA__.option_alias_projection (normalized_alias);
