-- Migration 011: commodity reference projection table
create table if not exists {schema}.commodity_projection (
    security_id              uuid          not null primary key,
    display_name             text          not null,
    currency                 text          not null,
    commodity_type           text          not null,
    denomination             text,
    contract_size            numeric(24,8),
    exchange_code            text,
    delivery_country         char(2),
    primary_identifier_value text          not null,
    version                  bigint        not null
);

create index if not exists commodity_projection_type_idx
    on {schema}.commodity_projection (lower(commodity_type));

create index if not exists commodity_projection_exchange_idx
    on {schema}.commodity_projection (exchange_code);
