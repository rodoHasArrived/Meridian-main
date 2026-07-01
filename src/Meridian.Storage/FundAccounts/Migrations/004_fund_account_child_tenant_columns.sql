-- Fund-account child-table tenant partition columns (security backlog SEC-005, slice 4c —
-- defense-in-depth completion of the fund-account aggregate).
--
-- Migration 003 partitioned the aggregate ROOT (account_definition.tenant_id, stamped
-- trust-on-first-use from the writing operator). The child tables below are reached by account_id
-- (or a run id) through the alternate-identifier read/write routes on FundAccountEndpoints, so a
-- caller who knows another tenant's account UUID could otherwise read that account's balances /
-- statements / reconciliation / sync / margin rows, or inject child rows keyed on it, without ever
-- naming the owning fundProfileId. This migration extends the same nullable, fail-open tenant_id
-- partition to every child table so those alternate-identifier paths are uniformly scoped.
--
-- Stamped from the OWNING account's tenant (inherited by PostgresFundAccountStore's child-write guard,
-- which resolves account_definition.tenant_id and refuses a cross-tenant write), falling back to the
-- writing caller's tenant when the parent account is still unstamped. Nullable and fail-open: legacy
-- rows (written before this column existed) stay null and remain visible to every caller, matching the
-- single-company-per-deployment posture; the read predicate filters by tenant_id only when the caller
-- has a resolved tenant. There is no backfill (no authoritative source in this separate database — the
-- parent account's tenant is itself only stamped forward); the partial indexes cover stamped rows only.

-- ── account_balance_snapshot ────────────────────────────────────────────────────

alter table __SCHEMA__.account_balance_snapshot
    add column if not exists tenant_id text null;

create index if not exists ix_balance_snapshot_tenant
    on __SCHEMA__.account_balance_snapshot (tenant_id)
    where tenant_id is not null;

create index if not exists ix_balance_snapshot_tenant_lower
    on __SCHEMA__.account_balance_snapshot (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── custodian_statement_batch ───────────────────────────────────────────────────

alter table __SCHEMA__.custodian_statement_batch
    add column if not exists tenant_id text null;

create index if not exists ix_custodian_batch_tenant
    on __SCHEMA__.custodian_statement_batch (tenant_id)
    where tenant_id is not null;

create index if not exists ix_custodian_batch_tenant_lower
    on __SCHEMA__.custodian_statement_batch (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── custodian_position_line ─────────────────────────────────────────────────────

alter table __SCHEMA__.custodian_position_line
    add column if not exists tenant_id text null;

create index if not exists ix_custodian_position_tenant
    on __SCHEMA__.custodian_position_line (tenant_id)
    where tenant_id is not null;

create index if not exists ix_custodian_position_tenant_lower
    on __SCHEMA__.custodian_position_line (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── bank_statement_batch ────────────────────────────────────────────────────────

alter table __SCHEMA__.bank_statement_batch
    add column if not exists tenant_id text null;

create index if not exists ix_bank_batch_tenant
    on __SCHEMA__.bank_statement_batch (tenant_id)
    where tenant_id is not null;

create index if not exists ix_bank_batch_tenant_lower
    on __SCHEMA__.bank_statement_batch (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── bank_statement_line ─────────────────────────────────────────────────────────

alter table __SCHEMA__.bank_statement_line
    add column if not exists tenant_id text null;

create index if not exists ix_bank_line_tenant
    on __SCHEMA__.bank_statement_line (tenant_id)
    where tenant_id is not null;

create index if not exists ix_bank_line_tenant_lower
    on __SCHEMA__.bank_statement_line (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── account_reconciliation_run ──────────────────────────────────────────────────

alter table __SCHEMA__.account_reconciliation_run
    add column if not exists tenant_id text null;

create index if not exists ix_recon_run_tenant
    on __SCHEMA__.account_reconciliation_run (tenant_id)
    where tenant_id is not null;

create index if not exists ix_recon_run_tenant_lower
    on __SCHEMA__.account_reconciliation_run (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── account_reconciliation_breaks ───────────────────────────────────────────────

alter table __SCHEMA__.account_reconciliation_breaks
    add column if not exists tenant_id text null;

create index if not exists ix_recon_breaks_tenant
    on __SCHEMA__.account_reconciliation_breaks (tenant_id)
    where tenant_id is not null;

create index if not exists ix_recon_breaks_tenant_lower
    on __SCHEMA__.account_reconciliation_breaks (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── account_sync_history ────────────────────────────────────────────────────────

alter table __SCHEMA__.account_sync_history
    add column if not exists tenant_id text null;

create index if not exists ix_sync_history_tenant
    on __SCHEMA__.account_sync_history (tenant_id)
    where tenant_id is not null;

create index if not exists ix_sync_history_tenant_lower
    on __SCHEMA__.account_sync_history (lower(trim(tenant_id)))
    where tenant_id is not null;

-- ── account_margin_snapshot ─────────────────────────────────────────────────────

alter table __SCHEMA__.account_margin_snapshot
    add column if not exists tenant_id text null;

create index if not exists ix_margin_snapshot_tenant
    on __SCHEMA__.account_margin_snapshot (tenant_id)
    where tenant_id is not null;

create index if not exists ix_margin_snapshot_tenant_lower
    on __SCHEMA__.account_margin_snapshot (lower(trim(tenant_id)))
    where tenant_id is not null;
