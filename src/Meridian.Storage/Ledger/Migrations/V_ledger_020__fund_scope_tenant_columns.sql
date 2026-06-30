-- Fund-scoped tenant partition columns (security backlog SEC-005, slice 4c-i).
--
-- Adds a tenant_id column to the fund-scoped ledger stores so a read can filter by the owning tenant
-- directly, closing the slice-3 / slice-3b "alternate fund identifier" residual: ledger books and
-- accounting periods are reachable by ledger_book_id / period_id without an explicit fund_profile_id, and
-- the fund_profile_tenancy registry is fund-keyed (there is no book/period -> tenant lookup), so a caller
-- who knows another tenant's book/period GUID can read it. With tenant_id stamped on the row, slice 4c-ii
-- adds the read predicates and 4c-iii requires a non-null tenant on writes (fail closed).
--
-- The column is nullable and INERT in this slice: nothing reads or writes it yet (that is 4c-ii), so there
-- is no behavior change. A null tenant_id means "unknown/unbound" and stays fail-open (legacy rows, and any
-- new row until 4c-ii stamps writes), matching the registry guard's deployment-boundary posture. The
-- backfill is idempotent -- guarded by `tenant_id is null` -- and re-runs safely since the migration runner
-- replays every script on each startup (there is no version table).
--
-- tenant_id is backfilled from the authoritative fund_profile_tenancy registry (itself backfilled from
-- tenant-attributed audit history in V_ledger_019), joined on the application's normalized fund key
-- (lower(trim(fund_profile_id)), matching the registry's stored Trim().ToLowerInvariant() key). Accounting
-- periods inherit the tenant of their ledger book; legacy periods with no ledger_book_id stay null (unbound,
-- fail-open) until a later slice attributes them.

alter table __SCHEMA__.ledger_books
    add column if not exists tenant_id text null;

update __SCHEMA__.ledger_books b
set tenant_id = t.tenant_id
from __SCHEMA__.fund_profile_tenancy t
where t.fund_profile_id = lower(trim(b.fund_profile_id))
  and b.tenant_id is null;

create index if not exists ix_ledger_books_tenant
    on __SCHEMA__.ledger_books (tenant_id)
    where tenant_id is not null;

alter table __SCHEMA__.accounting_periods
    add column if not exists tenant_id text null;

update __SCHEMA__.accounting_periods p
set tenant_id = b.tenant_id
from __SCHEMA__.ledger_books b
where b.ledger_book_id = p.ledger_book_id
  and p.ledger_book_id is not null
  and b.tenant_id is not null
  and p.tenant_id is null;

create index if not exists ix_accounting_periods_tenant
    on __SCHEMA__.accounting_periods (tenant_id)
    where tenant_id is not null;
