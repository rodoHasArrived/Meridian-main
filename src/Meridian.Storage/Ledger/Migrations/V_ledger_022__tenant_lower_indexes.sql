-- Tenant read-predicate expression indexes (security backlog SEC-005, slice 4c — read performance).
--
-- The SEC-005 4c-ii/4c read predicates compare `lower(trim(tenant_id))` on both sides to match the
-- write-side stamp and FundProfileOwnership.IsHeldBy (trimmed, case-insensitive). A plain B-tree index
-- on tenant_id (added by V_ledger_020 / V_ledger_021) cannot serve that functional comparison, so as the
-- fund-scoped tables grow the predicate would fall back to a sequential scan. These expression indexes
-- match the predicate exactly so Postgres can use them.
--
-- All additive and idempotent (create index if not exists), partial over populated rows only (mirroring
-- the plain tenant indexes), and behavior-neutral — they only affect query planning. Safe to replay on
-- each startup.

create index if not exists ix_ledger_books_tenant_lower
    on __SCHEMA__.ledger_books (lower(trim(tenant_id)))
    where tenant_id is not null;

create index if not exists ix_accounting_periods_tenant_lower
    on __SCHEMA__.accounting_periods (lower(trim(tenant_id)))
    where tenant_id is not null;

create index if not exists ix_operations_continuity_workflows_tenant_lower
    on __SCHEMA__.operations_continuity_workflows (lower(trim(tenant_id)))
    where tenant_id is not null;
