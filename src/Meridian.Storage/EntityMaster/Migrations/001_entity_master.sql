-- Entity master (ADR-033): legal entities shared by Security Master issuers, counterparties and fund
-- structure. Rows are append-only in spirit: links close by setting valid_to when superseded, and
-- entities are never deleted.

create table if not exists __SCHEMA__.legal_entities (
    entity_id     uuid primary key,
    legal_name    text not null,
    lei           text null,
    cik           text null,
    jurisdiction  text null,
    recorded_at   timestamptz not null default now(),
    recorded_by   text not null,
    constraint ck_legal_entities_lei check (lei is null or lei ~ '^[A-Z0-9]{20}$'),
    constraint ck_legal_entities_cik check (cik is null or cik ~ '^[0-9]{10}$')
);

comment on table __SCHEMA__.legal_entities is
    'ADR-033 entity master: legal entities keyed by entity_id; LEI and CIK are optional unique external identifiers.';

create unique index if not exists ux_legal_entities_lei on __SCHEMA__.legal_entities (lei) where lei is not null;
create unique index if not exists ux_legal_entities_cik on __SCHEMA__.legal_entities (cik) where cik is not null;

create table if not exists __SCHEMA__.entity_parent_links (
    entity_id         uuid not null references __SCHEMA__.legal_entities (entity_id),
    parent_entity_id  uuid not null references __SCHEMA__.legal_entities (entity_id),
    valid_from        date not null,
    valid_to          date null,
    recorded_at       timestamptz not null default now(),
    recorded_by       text not null,
    primary key (entity_id, valid_from),
    constraint ck_entity_parent_links_distinct check (entity_id <> parent_entity_id),
    constraint ck_entity_parent_links_window check (valid_to is null or valid_to > valid_from)
);

comment on table __SCHEMA__.entity_parent_links is
    'ADR-033 direct-parent relationships with valid-time windows [valid_from, valid_to).';

create index if not exists ix_entity_parent_links_parent on __SCHEMA__.entity_parent_links (parent_entity_id);

create table if not exists __SCHEMA__.security_issuer_links (
    security_id       uuid not null,
    issuer_entity_id  uuid not null references __SCHEMA__.legal_entities (entity_id),
    valid_from        date not null,
    valid_to          date null,
    recorded_at       timestamptz not null default now(),
    recorded_by       text not null,
    primary key (security_id, valid_from),
    constraint ck_security_issuer_links_window check (valid_to is null or valid_to > valid_from)
);

comment on table __SCHEMA__.security_issuer_links is
    'ADR-033 Security Master issuer links (security_id from the Security Master) with valid-time windows.';

create index if not exists ix_security_issuer_links_issuer on __SCHEMA__.security_issuer_links (issuer_entity_id);
