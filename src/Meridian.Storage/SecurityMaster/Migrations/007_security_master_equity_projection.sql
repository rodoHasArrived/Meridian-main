create table if not exists __SCHEMA__.equity_projection (
    security_id uuid primary key references __SCHEMA__.securities(security_id) on delete cascade,
    display_name text not null,
    currency text not null,
    share_class text null,
    voting_rights_cat text null,
    classification text null,
    exchange_code text null,
    country_of_risk text null,
    issuer_name text null,
    primary_identifier_value text not null,
    version bigint not null
);

create index if not exists ix_equity_projection_exchange_code
    on __SCHEMA__.equity_projection (exchange_code)
    where exchange_code is not null;

create index if not exists ix_equity_projection_issuer_name
    on __SCHEMA__.equity_projection (lower(issuer_name))
    where issuer_name is not null;
