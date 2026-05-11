-- Migration 010: swap reference projection table
create table if not exists {schema}.swap_projection (
    security_id              uuid         not null primary key,
    display_name             text         not null,
    currency                 text         not null,
    swap_type                text,
    effective_date           date         not null,
    maturity_date            date         not null,
    lifecycle_stat           text         not null default 'Active',
    primary_identifier_value text         not null,
    version                  bigint       not null
);

create index if not exists swap_projection_swap_type_idx
    on {schema}.swap_projection (lower(swap_type));

create index if not exists swap_projection_maturity_date_idx
    on {schema}.swap_projection (maturity_date);
