-- 031: Corporate-action accounting approval and posting lane (W9-CORPACT-011).
--
-- Persists the exact-version accounting projection authority, maker-checker approvals, and
-- immutable posting records for scoped processing cases. The projection binding retains the Asset
-- Accounting Event Spine identity of the drafted candidate together with the case, scope, policy,
-- and period versions it was prepared against, so ReadyForApproval and posting can be gated on
-- durable exact-version evidence instead of an unconditional refusal.

create table if not exists __SCHEMA__.corporate_action_case_accounting_projections (
    projection_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    bound_case_version bigint not null,
    accounting_event_id uuid not null,
    accounting_event_version bigint not null,
    spine_version bigint not null,
    projection_input_hash char(64) not null,
    posting_intent_hash char(64) not null,
    posting_idempotency_key text not null,
    drafted_candidate_fingerprint char(64) not null,
    policy_decision_id uuid not null,
    policy_decision_version bigint not null,
    rule_pack_id text not null,
    rule_pack_version text not null,
    selected_rule_id text not null,
    selected_rule_version text not null,
    ledger_book_id uuid not null,
    period_id uuid not null,
    expected_period_version bigint not null,
    accounting_basis text not null,
    fund_profile_id text not null,
    currency text not null,
    effective_date date not null,
    total_debits numeric(28, 10) not null,
    total_credits numeric(28, 10) not null,
    lot_snapshot_id uuid not null,
    lot_snapshot_version bigint not null,
    has_authoritative_lot_resolution boolean not null,
    prepared_by text not null,
    prepared_at timestamptz not null,
    is_current boolean not null,
    superseded_at timestamptz null,
    constraint ck_corporate_action_case_accounting_projection_versions check (
        bound_case_version > 0
        and accounting_event_version > 0
        and spine_version > 0
        and policy_decision_version > 0
        and expected_period_version > 0
        and lot_snapshot_version > 0),
    constraint ck_corporate_action_case_accounting_projection_hashes check (
        projection_input_hash ~ '^[0-9a-f]{64}$'
        and posting_intent_hash ~ '^[0-9a-f]{64}$'
        and drafted_candidate_fingerprint ~ '^[0-9a-f]{64}$'),
    constraint ck_corporate_action_case_accounting_projection_identity_lengths check (
        octet_length(posting_idempotency_key) between 1 and 256
        and octet_length(rule_pack_id) between 1 and 256
        and octet_length(rule_pack_version) between 1 and 256
        and octet_length(selected_rule_id) between 1 and 256
        and octet_length(selected_rule_version) between 1 and 256
        and octet_length(accounting_basis) between 1 and 256
        and octet_length(fund_profile_id) between 1 and 256
        and octet_length(currency) between 1 and 16
        and octet_length(prepared_by) between 1 and 256),
    constraint ck_corporate_action_case_accounting_projection_superseded check (
        (is_current and superseded_at is null) or (not is_current and superseded_at is not null))
);

create unique index if not exists ux_corporate_action_case_accounting_projection_current
    on __SCHEMA__.corporate_action_case_accounting_projections (case_id)
    where is_current;

create index if not exists ix_corporate_action_case_accounting_projections_case
    on __SCHEMA__.corporate_action_case_accounting_projections (case_id, prepared_at);

create table if not exists __SCHEMA__.corporate_action_case_accounting_approvals (
    approval_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    projection_id uuid not null references __SCHEMA__.corporate_action_case_accounting_projections(projection_id),
    bound_case_version bigint not null,
    approved_by text not null,
    approved_at timestamptz not null,
    reason text not null,
    evidence_reference text not null,
    evidence_hash char(64) not null,
    voided_at timestamptz null,
    voided_by text null,
    constraint ck_corporate_action_case_accounting_approval_version check (bound_case_version > 0),
    constraint ck_corporate_action_case_accounting_approval_hash check (evidence_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_corporate_action_case_accounting_approval_identity_lengths check (
        octet_length(approved_by) between 1 and 256
        and (voided_by is null or octet_length(voided_by) between 1 and 256)),
    constraint ck_corporate_action_case_accounting_approval_voiding check (
        (voided_at is null and voided_by is null) or (voided_at is not null and voided_by is not null))
);

create unique index if not exists ux_corporate_action_case_accounting_approval_active
    on __SCHEMA__.corporate_action_case_accounting_approvals (case_id)
    where voided_at is null;

create index if not exists ix_corporate_action_case_accounting_approvals_projection
    on __SCHEMA__.corporate_action_case_accounting_approvals (projection_id);

create table if not exists __SCHEMA__.corporate_action_case_accounting_postings (
    posting_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    projection_id uuid not null references __SCHEMA__.corporate_action_case_accounting_projections(projection_id),
    approval_id uuid not null references __SCHEMA__.corporate_action_case_accounting_approvals(approval_id),
    journal_entry_id uuid not null,
    ledger_book_id uuid not null,
    period_id uuid not null,
    accounting_basis text not null,
    currency text not null,
    total_debits numeric(28, 10) not null,
    total_credits numeric(28, 10) not null,
    posting_status text not null,
    tax_lot_mutation_batch_id uuid null,
    posted_by text not null,
    posted_at timestamptz not null,
    constraint ck_corporate_action_case_accounting_posting_status check (posting_status = 'Posted'),
    constraint ck_corporate_action_case_accounting_posting_balance check (
        total_debits = total_credits and total_debits > 0),
    constraint ck_corporate_action_case_accounting_posting_identity_lengths check (
        octet_length(accounting_basis) between 1 and 256
        and octet_length(currency) between 1 and 16
        and octet_length(posted_by) between 1 and 256),
    -- One durable posting per projection binding: a correction never reuses a superseded binding,
    -- it flows through the governed restatement lane onto a fresh exact-version projection.
    constraint ux_corporate_action_case_accounting_posting_projection unique (projection_id)
);

create index if not exists ix_corporate_action_case_accounting_postings_case
    on __SCHEMA__.corporate_action_case_accounting_postings (case_id, posted_at);
