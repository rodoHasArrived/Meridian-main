create table if not exists __SCHEMA__.instrument_role_projections (
    role_id uuid primary key,
    security_id uuid not null,
    owner_scope_id text not null,
    owner_scope_kind text not null,
    role_kind text not null,
    effective_from date not null,
    effective_to date null,
    version bigint not null,
    source_event_id uuid null,
    approval_actor text not null,
    approval_reference text not null,
    approved_at timestamptz not null,
    evidence_links jsonb not null default '[]'::jsonb,
    payload jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint ck_instrument_role_projection_dates
        check (effective_to is null or effective_to >= effective_from),
    constraint ck_instrument_role_projection_version check (version > 0)
);

create table if not exists __SCHEMA__.book_position_projections (
    position_id uuid primary key,
    security_id uuid not null,
    role_id uuid not null,
    ledger_book_id uuid not null,
    owner_scope_id text not null,
    owner_scope_kind text not null,
    effective_from date not null,
    effective_to date null,
    version bigint not null,
    source_event_id uuid null,
    approval_actor text not null,
    approval_reference text not null,
    approved_at timestamptz not null,
    evidence_links jsonb not null default '[]'::jsonb,
    payload jsonb not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint fk_book_position_projection_role
        foreign key (role_id) references __SCHEMA__.instrument_role_projections (role_id),
    constraint ck_book_position_projection_dates
        check (effective_to is null or effective_to >= effective_from),
    constraint ck_book_position_projection_version check (version > 0)
);

create table if not exists __SCHEMA__.position_economic_state_projections (
    economic_state_id uuid primary key,
    position_id uuid not null,
    security_id uuid not null,
    as_of_date date not null,
    version bigint not null,
    source_event_id uuid null,
    approval_actor text not null,
    approval_reference text not null,
    approved_at timestamptz not null,
    evidence_links jsonb not null default '[]'::jsonb,
    payload jsonb not null,
    created_at timestamptz not null default now(),
    constraint fk_position_economic_state_position
        foreign key (position_id) references __SCHEMA__.book_position_projections (position_id),
    constraint ck_position_economic_state_version check (version > 0)
);

create index if not exists ix_instrument_role_projections_security_id
    on __SCHEMA__.instrument_role_projections (security_id, effective_from, effective_to);
create index if not exists ix_book_position_projections_security_book
    on __SCHEMA__.book_position_projections (security_id, ledger_book_id, effective_from, effective_to);
create index if not exists ix_position_economic_state_position_as_of
    on __SCHEMA__.position_economic_state_projections (position_id, as_of_date desc, version desc);
