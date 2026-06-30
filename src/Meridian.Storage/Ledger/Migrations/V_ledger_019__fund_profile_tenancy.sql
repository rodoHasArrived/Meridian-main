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
-- used. For each fund the earliest attributed audit event is treated as the first owner.
--
-- The owner tenant falls back to company_id: the runtime aliases TenantId == CompanyId until distinct
-- tenant ids are issued, and databases upgraded from the older schema (company_id from V_ledger_010,
-- tenant_id added later in V_ledger_017 and only filled when a workspace matched) have audit rows that
-- carry only company_id. Treating company-attributed history as ownership matches the removed audit
-- guard. Only the 'all'/null unscoped sentinels are skipped, so genuinely unscoped funds stay claimable.
-- fund_profile_id is lower-trimmed to match the application's normalization (Trim().ToLowerInvariant()),
-- so a legacy row like " fund-x " is keyed the same as a "fund-x" lookup.
insert into __SCHEMA__.fund_profile_tenancy (fund_profile_id, tenant_id, company_id)
select fund_profile_id, owner_tenant, company_id
from (
    select lower(trim(fund_profile_id)) as fund_profile_id,
           coalesce(nullif(tenant_id, 'all'), nullif(company_id, 'all')) as owner_tenant,
           coalesce(company_id, 'all') as company_id,
           row_number() over (
               partition by lower(trim(fund_profile_id))
               order by recorded_at_utc asc, audit_event_id asc) as rn
    from __SCHEMA__.accounting_action_audit_events
    where coalesce(nullif(tenant_id, 'all'), nullif(company_id, 'all')) is not null
      and fund_profile_id is not null
      and length(trim(fund_profile_id)) > 0
) ranked
where rn = 1
on conflict (fund_profile_id) do nothing;
