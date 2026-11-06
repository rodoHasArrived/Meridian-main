create table if not exists __SCHEMA__.future_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    display_name text not null,
    currency text not null,
    root_symbol text not null,
    contract_month text not null,
    expiry_date date not null,
    multiplier numeric not null,
    settlement_type text null,
    is_roll_target boolean not null default false,
    roll_window_days int null,
    last_trading_dt date null,
    first_notice_dt date null,
    delivery_month_dt date null,
    delivery_location_code text null,
    lifecycle_stat text not null,
    primary_identifier_value text not null,
    version bigint not null
);

create index if not exists ix_future_projection_root_symbol_expiry
    on __SCHEMA__.future_projection (root_symbol, expiry_date);

create index if not exists ix_future_projection_roll_target
    on __SCHEMA__.future_projection (root_symbol)
    where is_roll_target = true;
