-- Migration 012: crypto pair reference projection table
create table if not exists __SCHEMA__.crypto_projection (
    security_id              uuid         not null primary key,
    display_name             text         not null,
    base_currency            text         not null,
    quote_currency           text         not null,
    network                  text,
    primary_identifier_value text         not null,
    version                  bigint       not null
);

create index if not exists crypto_projection_base_currency_idx
    on __SCHEMA__.crypto_projection (base_currency);

create index if not exists crypto_projection_network_idx
    on __SCHEMA__.crypto_projection (lower(network));
