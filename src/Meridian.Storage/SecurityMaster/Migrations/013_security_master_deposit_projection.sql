-- Migration 013: deposit reference projection table
create table if not exists __SCHEMA__.deposit_projection (
    security_id              uuid          not null primary key,
    display_name             text          not null,
    currency                 text          not null,
    deposit_type             text          not null,
    institution_name         text          not null,
    maturity_date            date,
    interest_rate            numeric(12,6),
    day_count                text,
    is_callable              boolean       not null default false,
    primary_identifier_value text          not null,
    version                  bigint        not null
);

create index if not exists deposit_projection_institution_idx
    on __SCHEMA__.deposit_projection (lower(institution_name));

create index if not exists deposit_projection_maturity_idx
    on __SCHEMA__.deposit_projection (maturity_date);
