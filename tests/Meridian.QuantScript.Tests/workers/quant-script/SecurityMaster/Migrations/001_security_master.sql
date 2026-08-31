create schema if not exists __SCHEMA__;

create table if not exists __SCHEMA__.security_events (
    global_sequence bigserial primary key,
    security_id uuid not null,
    stream_version bigint not null,
    event_type text not null,
    event_timestamp timestamptz not null,
    actor text not null,
    correlation_id uuid null,
    causation_id uuid null,
    payload jsonb not null,
    metadata jsonb not null default '{}'::jsonb,
    unique (security_id, stream_version)
);

create index if not exists ix_security_events_security_id_sequence
    on __SCHEMA__.security_events (security_id, stream_version);

create table if not exists __SCHEMA__.securities (
    security_id uuid primary key,
    asset_class text not null,
    status text not null,
    display_name text not null,
    currency text not null,
    country_of_risk text null,
    issuer_name text null,
    exchange_code text null,
    lot_size numeric null,
    tick_size numeric null,
    primary_identifier_kind text not null,
    primary_identifier_value text not null,
    common_terms jsonb not null,
    asset_specific_terms jsonb not null,
    provenance jsonb not null,
    version bigint not null,
    effective_from timestamptz not null,
    effective_to timestamptz null,
    search_vector tsvector null
);

create unique index if not exists ux_securities_primary_identifier
    on __SCHEMA__.securities (primary_identifier_kind, primary_identifier_value);

create table if not exists __SCHEMA__.security_identifiers (
    security_id uuid not null references __SCHEMA__.securities(security_id),
    identifier_kind text not null,
    identifier_value text not null,
    provider text null,
    is_primary boolean not null default false,
    valid_from timestamptz not null,
    valid_to timestamptz null,
    source text not null,
    confidence numeric(5,4) null,
    manual_override boolean not null default false,
    primary key (security_id, identifier_kind, identifier_value, valid_from)
);

create index if not exists ix_security_identifiers_lookup
    on __SCHEMA__.security_identifiers (identifier_kind, identifier_value, provider);

create table if not exists __SCHEMA__.security_aliases (
    alias_id uuid primary key,
    security_id uuid not null references __SCHEMA__.securities(security_id),
    alias_kind text not null,
    alias_value text not null,
    provider text null,
    scope text not null,
    reason text null,
    created_by text not null,
    created_at timestamptz not null,
    valid_from timestamptz not null,
    valid_to timestamptz null,
    is_enabled boolean not null default true
);

create index if not exists ix_security_aliases_lookup
    on __SCHEMA__.security_aliases (alias_kind, alias_value, provider, is_enabled);

create table if not exists __SCHEMA__.security_snapshots (
    security_id uuid primary key,
    version bigint not null,
    snapshot_timestamp timestamptz not null,
    payload jsonb not null
);

create table if not exists __SCHEMA__.projection_checkpoint (
    projection_name text primary key,
    last_global_sequence bigint not null,
    updated_at timestamptz not null
);
