-- Migration 015: certificate of deposit reference projection table
create table if not exists __SCHEMA__.certificate_of_deposit_projection (
    security_id              uuid          not null primary key,
    display_name             text          not null,
    currency                 text          not null,
    issuer_name              text          not null,
    maturity_date            date          not null,
    coupon_rate              numeric(12,6),
    callable_date            date,
    day_count                text,
    primary_identifier_value text          not null,
    version                  bigint        not null
);

create index if not exists certificate_of_deposit_projection_issuer_idx
    on __SCHEMA__.certificate_of_deposit_projection (lower(issuer_name));

create index if not exists certificate_of_deposit_projection_maturity_idx
    on __SCHEMA__.certificate_of_deposit_projection (maturity_date);
