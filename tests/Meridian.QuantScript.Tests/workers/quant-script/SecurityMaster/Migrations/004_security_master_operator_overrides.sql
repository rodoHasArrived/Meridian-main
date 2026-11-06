-- Migration 004: Operator Overrides
-- Stores operator-supplied per-security override values (free-form key/value strings) that
-- supplement the authoritative Security Master record. Keyed by security_id, one row per
-- security. Values are stored as jsonb (string -> string map).

create table if not exists __SCHEMA__.security_operator_overrides (
    security_id uuid primary key references __SCHEMA__.securities(security_id),
    values jsonb not null default '{}'::jsonb,
    updated_by text not null,
    updated_at timestamptz not null default now()
);
