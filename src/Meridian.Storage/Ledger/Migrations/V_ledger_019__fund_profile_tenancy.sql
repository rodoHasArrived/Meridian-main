-- Fund-profile -> tenant/company ownership registry (security backlog SEC-005).
--
-- Authoritative source of truth for fund-scoped tenant isolation: once a fund profile is bound to a
-- tenant, data keyed by that fund (strategy runs, ledger books, report packs) must only be served to
-- callers in the owning tenant. Ownership is first-owner-wins (insert ... on conflict do nothing). The
-- fund_profile_id is stored normalized (trimmed, lower-invariant) by the application so the raw-column
-- primary key keys funds case-insensitively without a fragile expression index. company_id uses the
-- 'all' scope sentinel for an unscoped company, matching the accounting_configuration tables.
create table if not exists __SCHEMA__.fund_profile_tenancy (
    fund_profile_id text primary key,
    tenant_id text not null,
    company_id text not null default 'all',
    registered_at_utc timestamptz not null default now()
);

create index if not exists ix_fund_profile_tenancy_tenant
    on __SCHEMA__.fund_profile_tenancy(tenant_id, registered_at_utc desc);
