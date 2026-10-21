-- Migration 049: the underlying graph.
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- One edge per row: security_id depends on underlying_security_id (a Security Master record) or
-- on reference_text (a name the terms carry that resolves to no record yet) in the stated role.
-- Derived rows are rewritten from the instrument's terms on every projection write and carry
-- source = 'Derived'; declared rows are written through the graph API and carry source =
-- 'Declared' with their own validity window. underlying_security_id is deliberately not a foreign
-- key: an option is often modelled before its underlying, and a dangling target is reported as a
-- gap rather than refused.
--
-- ROLLOUT: the table is created empty. Derived rows appear as records are next persisted; an
-- existing database uses SecurityMasterRebuildOrchestrator to backfill them.

create table if not exists __SCHEMA__.security_underlying_links (
    link_id                uuid        not null primary key,
    security_id            uuid        not null references __SCHEMA__.securities (security_id) on delete cascade,
    role                   text        not null,
    underlying_security_id uuid        null,
    reference_text         text        null,
    weight                 numeric(28,10) null,
    valid_from             timestamptz not null,
    valid_to               timestamptz null,
    source                 text        not null,
    notes                  text        null,
    created_by             text        not null,
    created_at             timestamptz not null,
    constraint security_underlying_links_target_check
        check (underlying_security_id is not null or reference_text is not null),
    constraint security_underlying_links_source_check
        check (source in ('Declared', 'Derived')),
    constraint security_underlying_links_window_check
        check (valid_to is null or valid_to > valid_from)
);

create index if not exists security_underlying_links_security_idx
    on __SCHEMA__.security_underlying_links (security_id, source);

create index if not exists security_underlying_links_underlying_idx
    on __SCHEMA__.security_underlying_links (underlying_security_id)
    where underlying_security_id is not null;

comment on table __SCHEMA__.security_underlying_links is
    'The underlying graph: one edge per row from an instrument to the security or named reference it depends on, in a stated role, with its validity window. Derived rows follow the terms; declared rows are governed through the graph API.';
