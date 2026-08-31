create table if not exists __SCHEMA__.fxspot_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    display_name text not null,
    base_currency text not null,
    quote_currency text not null,
    pair_code text not null,
    primary_identifier_value text not null,
    version bigint not null
);

create unique index if not exists ux_fxspot_projection_pair_code
    on __SCHEMA__.fxspot_projection (pair_code);

create index if not exists ix_fxspot_projection_base_currency
    on __SCHEMA__.fxspot_projection (base_currency);

create index if not exists ix_fxspot_projection_quote_currency
    on __SCHEMA__.fxspot_projection (quote_currency);
