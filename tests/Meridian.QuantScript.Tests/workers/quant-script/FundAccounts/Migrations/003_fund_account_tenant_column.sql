-- Fund-account tenant partition column (security backlog SEC-005, slice 4c — defense-in-depth).
--
-- The fund-accounts store is a SEPARATE database from the ledger schema that holds the
-- fund_profile_tenancy registry, and account_definition carries only a fund_id (uuid) with no
-- fund_profile_id (string), so there is no in-DB path to resolve the owner via the registry (unlike the
-- ledger and operations-continuity stores). This column is therefore stamped trust-on-first-use from the
-- WRITING operator's resolved tenant (the same caller-tenant fallback the operations-continuity store
-- uses for book-less workflows), not from the registry.
--
-- Nullable and fail-open: a null tenant_id (legacy rows written before this column existed, or any row
-- created without a tenant in scope — a background/non-request writer) stays visible to every caller,
-- matching the deployment-boundary posture. The slice-4c read predicate filters by it only when the
-- caller has a resolved tenant, so single-company-per-deployment behavior is unchanged. There is no
-- backfill (no authoritative source in this database); the partial index covers stamped rows only.

alter table __SCHEMA__.account_definition
    add column if not exists tenant_id text null;

create index if not exists ix_account_definition_tenant
    on __SCHEMA__.account_definition (tenant_id)
    where tenant_id is not null;

create index if not exists ix_account_definition_tenant_lower
    on __SCHEMA__.account_definition (lower(trim(tenant_id)))
    where tenant_id is not null;
