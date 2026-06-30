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

-- Backfill ownership from existing tenant-attributed accounting audit history so a shared deployment
-- that already has data does not let the first caller after upgrade claim a fund another tenant already
-- used. For each fund the earliest real-tenant audit event is treated as the first owner; the 'all'
-- (unscoped) sentinel and null tenant are excluded so genuinely unscoped funds stay claimable on first
-- use. fund_profile_id is lowered to match the application's case-insensitive normalization.
insert into __SCHEMA__.fund_profile_tenancy (fund_profile_id, tenant_id, company_id)
select fund_profile_id, tenant_id, company_id
from (
    select lower(fund_profile_id) as fund_profile_id,
           tenant_id,
           coalesce(company_id, 'all') as company_id,
           row_number() over (
               partition by lower(fund_profile_id)
               order by recorded_at_utc asc, audit_event_id asc) as rn
    from __SCHEMA__.accounting_action_audit_events
    where tenant_id is not null
      and tenant_id <> 'all'
      and fund_profile_id is not null
      and length(trim(fund_profile_id)) > 0
) ranked
where rn = 1
on conflict (fund_profile_id) do nothing;
