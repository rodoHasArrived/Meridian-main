-- Migration 018: Security Master Cash Flow Source Assignments
-- Tracks the authoritative structured cash flow source per security (MIAC, Moody's Analytics,
-- client-provided, or Clearwater-calculated). Client-provided assignments take precedence and
-- remain until explicitly changed per the Clearwater cash flow governance model.

create table if not exists __SCHEMA__.security_cashflow_source_assignments (
    security_id         uuid        not null references __SCHEMA__.securities(security_id),
    source_kind         text        not null,
    last_updated_utc    timestamptz,
    is_client_override  boolean     not null default false,
    client_confirmed_by text,
    client_confirmed_at timestamptz,
    primary key (security_id)
);

create index if not exists ix_security_cashflow_sources_source_kind
    on __SCHEMA__.security_cashflow_source_assignments (source_kind);
