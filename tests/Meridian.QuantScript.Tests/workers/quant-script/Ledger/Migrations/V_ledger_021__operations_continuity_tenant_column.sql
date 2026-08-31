-- Operations-continuity tenant partition column (security backlog SEC-005, slice 4c — store extension).
--
-- Adds a tenant_id column to operations_continuity_workflows so the close-cockpit and workflow-summary
-- reads (which reach this store by fund_account_id / ledgerBookId, not fund_profile_id) can filter by the
-- owning tenant directly, closing the slice-3b alternate-identifier residual for this store. The
-- fund_profile_tenancy registry is fund-keyed and this table carries no fund_profile_id, so ownership is
-- inherited transitively through the workflow's ledger book (workflow_json ->> 'ledgerBookId') -> the
-- ledger_books.tenant_id stamped by V_ledger_020, which is explicitly repeatable and runs first.
--
-- The column is nullable and fail-open: a null tenant_id means "unknown/unbound" (a workflow with no
-- ledger book, or whose book is itself unbound) and stays visible to every caller, matching the
-- deployment-boundary posture. The 4c-ii read predicate filters by it only when the caller has a resolved
-- tenant, so single-company-per-deployment behavior is unchanged. The backfill is idempotent (guarded by
-- `tenant_id is null`) and re-runs safely because this migration is explicitly configured as repeatable
-- on startup.

alter table __SCHEMA__.operations_continuity_workflows
    add column if not exists tenant_id text null;

-- Inherit the owning tenant from the workflow's ledger book via the authoritative registry-backfilled
-- ledger_books.tenant_id. workflow_json ->> 'ledgerBookId' is the app's "D"-format GUID string (or null
-- when the workflow has no book); null casts to a null uuid and simply matches no book, so unbound
-- workflows stay null/fail-open. Only fills rows still null so a re-run is a no-op.
update __SCHEMA__.operations_continuity_workflows w
set tenant_id = b.tenant_id
from __SCHEMA__.ledger_books b
where (w.workflow_json ->> 'ledgerBookId') is not null
  and b.ledger_book_id = (w.workflow_json ->> 'ledgerBookId')::uuid
  and b.tenant_id is not null
  and w.tenant_id is null;

create index if not exists ix_operations_continuity_workflows_tenant
    on __SCHEMA__.operations_continuity_workflows (tenant_id)
    where tenant_id is not null;
