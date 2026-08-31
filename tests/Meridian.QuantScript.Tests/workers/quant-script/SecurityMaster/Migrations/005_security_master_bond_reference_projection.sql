create table if not exists __SCHEMA__.bond_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    issuer_name text null,
    seniority text null,
    subclass text null,
    issue_date date null,
    maturity_date date not null,
    coupon_kind text null,
    fixed_coupon_rate numeric null,
    floating_rate_index text null,
    floating_spread_bps numeric null,
    version bigint not null
);

create index if not exists ix_bond_projection_maturity_date
    on __SCHEMA__.bond_projection (maturity_date);

create table if not exists __SCHEMA__.bond_lifecycle_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    lifecycle_stat text not null,
    issue_date date null,
    call_date date null,
    maturity_date date not null,
    is_callable boolean not null,
    version bigint not null
);

create table if not exists __SCHEMA__.bond_accrual_convention_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    day_count_convention text null,
    settlement_cycle_days integer null,
    holiday_calendar_id text null,
    coupon_kind text null,
    fixed_coupon_rate numeric null,
    floating_rate_index text null,
    floating_spread_bps numeric null,
    version bigint not null
);

create table if not exists __SCHEMA__.bond_issuer_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    issuer_name text not null,
    display_name text not null,
    primary_identifier_value text not null,
    currency text not null,
    maturity_date date null,
    seniority text null,
    version bigint not null
);

create index if not exists ix_bond_issuer_projection_issuer_name_maturity
    on __SCHEMA__.bond_issuer_projection (issuer_name, maturity_date);
