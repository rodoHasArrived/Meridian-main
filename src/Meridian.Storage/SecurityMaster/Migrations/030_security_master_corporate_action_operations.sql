-- Migration 030: Durable corporate-action source proposals and processing cases
-- Keeps provider observations separate from accepted Security Master events and from scoped
-- operator processing cases. Every governed mutation is versioned and has a durable idempotency
-- receipt; acceptance writes the canonical action, initial case, proposal decision, and transition
-- in one database transaction.

alter table __SCHEMA__.corporate_actions
    add column if not exists payload_schema_version integer not null default 1;

alter table __SCHEMA__.corporate_actions
    add column if not exists economic_fingerprint char(64) null;

do $$
begin
    alter table __SCHEMA__.corporate_actions
        add constraint ck_corporate_actions_economic_fingerprint
        check (economic_fingerprint is null or economic_fingerprint ~ '^[0-9a-f]{64}$');
exception
    when duplicate_object then null;
end $$;

do $$
begin
    alter table __SCHEMA__.corporate_actions
        add constraint fk_corporate_actions_superseded_action
        foreign key (supersedes_corp_act_id)
        references __SCHEMA__.corporate_actions(corp_act_id);
exception
    when duplicate_object then null;
end $$;

create unique index if not exists ux_corporate_actions_single_successor
    on __SCHEMA__.corporate_actions (supersedes_corp_act_id)
    where supersedes_corp_act_id is not null;

-- Provider-neutral canonical event identity. Null fingerprints are retained only for legacy
-- append paths; governed source-proposal acceptance always supplies one. NULL lifecycle means
-- Confirmed and a zero UUID is used solely as the expression-index sentinel for a root event.
create unique index if not exists ux_corporate_actions_canonical_economic_event
    on __SCHEMA__.corporate_actions (
        security_id,
        economic_fingerprint,
        coalesce(nullif(lifecycle_state, ''), 'Confirmed'),
        coalesce(supersedes_corp_act_id, '00000000-0000-0000-0000-000000000000'::uuid))
    where economic_fingerprint is not null;

create table if not exists __SCHEMA__.corporate_action_source_proposals (
    proposal_id uuid primary key,
    security_id uuid not null references __SCHEMA__.securities(security_id),
    provider_id text not null,
    source_event_id text not null,
    source_event_version text not null,
    observed_at timestamptz not null,
    evidence_hash text null,
    evidence_reference text null,
    provider_release_status text not null default 'ReviewOnly',
    payload_schema_version integer not null,
    economic_fingerprint char(64) not null,
    proposed_action jsonb not null,
    display_ticker text null,
    winning_source text null,
    agreeing_sources jsonb not null default '[]'::jsonb,
    dissenting_sources jsonb not null default '[]'::jsonb,
    dissent_fields jsonb not null default '[]'::jsonb,
    state text not null,
    version bigint not null,
    supersedes_proposal_id uuid null references __SCHEMA__.corporate_action_source_proposals(proposal_id),
    accepted_corp_act_id uuid null references __SCHEMA__.corporate_actions(corp_act_id),
    initial_case_id uuid null,
    recorded_by text not null,
    recorded_at timestamptz not null,
    updated_at timestamptz not null,
    decision_by text null,
    decision_at timestamptz null,
    decision_reason text null,
    correlation_id text null,
    constraint ck_corporate_action_source_proposal_version check (version > 0),
    constraint ck_corporate_action_source_proposal_schema_version check (payload_schema_version > 0),
    constraint ck_corporate_action_source_proposal_fingerprint check (economic_fingerprint ~ '^[0-9a-f]{64}$'),
    constraint ck_corporate_action_source_provider_release_status
        check (provider_release_status in ('ReviewOnly', 'AcceptanceEligible')),
    constraint ux_corporate_action_source_identity unique (provider_id, source_event_id, source_event_version)
);

alter table __SCHEMA__.corporate_action_source_proposals
    add column if not exists dissent_fields jsonb not null default '[]'::jsonb;

alter table __SCHEMA__.corporate_action_source_proposals
    add column if not exists provider_release_status text not null default 'ReviewOnly';

do $$
begin
    alter table __SCHEMA__.corporate_action_source_proposals
        add constraint ck_corporate_action_source_provider_release_status
        check (provider_release_status in ('ReviewOnly', 'AcceptanceEligible'));
exception
    when duplicate_object then null;
end $$;

create index if not exists ix_corporate_action_source_proposals_security_state
    on __SCHEMA__.corporate_action_source_proposals (security_id, state, observed_at desc);

create index if not exists ix_corporate_action_source_proposals_economic_fingerprint
    on __SCHEMA__.corporate_action_source_proposals (security_id, economic_fingerprint);

create index if not exists ix_corporate_action_source_proposals_accepted_action
    on __SCHEMA__.corporate_action_source_proposals (accepted_corp_act_id)
    where accepted_corp_act_id is not null;

create table if not exists __SCHEMA__.corporate_action_canonical_sources (
    corp_act_id uuid not null references __SCHEMA__.corporate_actions(corp_act_id),
    proposal_id uuid not null references __SCHEMA__.corporate_action_source_proposals(proposal_id),
    provider_id text not null,
    source_event_id text not null,
    source_event_version text not null,
    linked_at timestamptz not null,
    primary key (corp_act_id, proposal_id),
    constraint ux_corporate_action_canonical_source_proposal unique (proposal_id)
);

create index if not exists ix_corporate_action_canonical_sources_provider_event
    on __SCHEMA__.corporate_action_canonical_sources
        (provider_id, source_event_id, source_event_version);

create unique index if not exists ux_corporate_action_source_proposal_single_successor
    on __SCHEMA__.corporate_action_source_proposals (supersedes_proposal_id)
    where supersedes_proposal_id is not null;

-- Exactly one root and one successor make every provider event/version family a linear chain.
-- The store serializes on (provider_id, source_event_id) and validates same-family parentage.
create unique index if not exists ux_corporate_action_source_proposal_single_root
    on __SCHEMA__.corporate_action_source_proposals (provider_id, source_event_id)
    where supersedes_proposal_id is null;

create table if not exists __SCHEMA__.corporate_action_processing_cases (
    case_id uuid primary key,
    proposal_id uuid not null references __SCHEMA__.corporate_action_source_proposals(proposal_id),
    corp_act_id uuid not null references __SCHEMA__.corporate_actions(corp_act_id),
    security_id uuid not null references __SCHEMA__.securities(security_id),
    tenant_id text not null,
    company_id text not null,
    structure_node_id text null,
    fund_profile_id text null,
    financial_account_id text null,
    portfolio_id text null,
    custody_account_id text null,
    ledger_book_id text null,
    period_id text null,
    accounting_basis text null,
    functional_currency text null,
    jurisdiction text null,
    state text not null,
    version bigint not null,
    methodology_profile_id text null,
    assigned_to text null,
    blocked_reason text null,
    created_by text not null,
    created_at timestamptz not null,
    updated_by text not null,
    updated_at timestamptz not null,
    constraint ck_corporate_action_processing_case_version check (version > 0)
);

create index if not exists ix_corporate_action_processing_cases_scope_state
    on __SCHEMA__.corporate_action_processing_cases (tenant_id, company_id, state, updated_at desc);

create index if not exists ix_corporate_action_processing_cases_security
    on __SCHEMA__.corporate_action_processing_cases (security_id, updated_at desc);

create index if not exists ix_corporate_action_processing_cases_proposal
    on __SCHEMA__.corporate_action_processing_cases (proposal_id);

create index if not exists ix_corporate_action_processing_cases_canonical_action
    on __SCHEMA__.corporate_action_processing_cases (corp_act_id);

create unique index if not exists ux_corporate_action_processing_case_full_scope
    on __SCHEMA__.corporate_action_processing_cases (
        corp_act_id,
        tenant_id,
        company_id,
        coalesce(structure_node_id, ''),
        coalesce(fund_profile_id, ''),
        coalesce(financial_account_id, ''),
        coalesce(portfolio_id, ''),
        coalesce(custody_account_id, ''),
        coalesce(ledger_book_id, ''),
        coalesce(period_id, ''),
        coalesce(accounting_basis, ''),
        coalesce(functional_currency, ''),
        coalesce(jurisdiction, ''));

do $$
begin
    alter table __SCHEMA__.corporate_action_source_proposals
        add constraint fk_corporate_action_source_initial_case
        foreign key (initial_case_id)
        references __SCHEMA__.corporate_action_processing_cases(case_id);
exception
    when duplicate_object then null;
end $$;

create table if not exists __SCHEMA__.corporate_action_case_evidence (
    evidence_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    evidence_kind text not null,
    evidence_reference text not null,
    evidence_hash text null,
    description text null,
    metadata jsonb null,
    case_version bigint not null,
    recorded_by text not null,
    recorded_at timestamptz not null
);

create index if not exists ix_corporate_action_case_evidence_case
    on __SCHEMA__.corporate_action_case_evidence (case_id, recorded_at);

create table if not exists __SCHEMA__.corporate_action_case_conflicts (
    conflict_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    field_name text not null,
    description text not null,
    candidates jsonb not null,
    state text not null,
    resolution text null,
    case_version bigint not null,
    recorded_by text not null,
    recorded_at timestamptz not null,
    resolved_by text null,
    resolved_at timestamptz null,
    resolution_evidence_reference text null,
    resolution_evidence_hash text null
);

alter table __SCHEMA__.corporate_action_case_conflicts
    add column if not exists resolved_by text null,
    add column if not exists resolved_at timestamptz null,
    add column if not exists resolution_evidence_reference text null,
    add column if not exists resolution_evidence_hash text null;

create index if not exists ix_corporate_action_case_conflicts_case_state
    on __SCHEMA__.corporate_action_case_conflicts (case_id, state, recorded_at);

create table if not exists __SCHEMA__.corporate_action_processing_options (
    option_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    option_code text not null,
    label text not null,
    description text not null,
    state text not null,
    source_methodology text null,
    blockers jsonb not null default '[]'::jsonb,
    parameters jsonb null,
    case_version bigint not null,
    recorded_by text not null,
    recorded_at timestamptz not null
);

create index if not exists ix_corporate_action_processing_options_case_code_version
    on __SCHEMA__.corporate_action_processing_options (case_id, option_code, case_version desc);

create table if not exists __SCHEMA__.corporate_action_case_transitions (
    transition_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    operation_kind text not null,
    from_state text null,
    to_state text not null,
    expected_version bigint not null,
    resulting_version bigint not null,
    actor text not null,
    reason text not null,
    idempotency_key text not null,
    occurred_at timestamptz not null,
    correlation_id text null,
    policy_override_applied boolean not null default false,
    constraint ux_corporate_action_case_transition_idempotency
        unique (case_id, operation_kind, idempotency_key)
);

alter table __SCHEMA__.corporate_action_case_transitions
    add column if not exists operation_kind text not null default 'TransitionCase';

alter table __SCHEMA__.corporate_action_case_transitions
    drop constraint if exists ux_corporate_action_case_transition_idempotency;

alter table __SCHEMA__.corporate_action_case_transitions
    add constraint ux_corporate_action_case_transition_idempotency
    unique (case_id, operation_kind, idempotency_key);

create index if not exists ix_corporate_action_case_transitions_case_version
    on __SCHEMA__.corporate_action_case_transitions (case_id, resulting_version);

create table if not exists __SCHEMA__.corporate_action_restatement_obligations (
    obligation_id uuid primary key,
    case_id uuid not null unique references __SCHEMA__.corporate_action_processing_cases(case_id),
    corp_act_id uuid not null references __SCHEMA__.corporate_actions(corp_act_id),
    tenant_id text not null,
    company_id text not null,
    scope jsonb not null,
    restatement_required boolean not null,
    candidates jsonb not null,
    status text not null,
    recorded_at timestamptz not null
);

create index if not exists ix_corporate_action_restatement_obligations_pending
    on __SCHEMA__.corporate_action_restatement_obligations (tenant_id, company_id, status, recorded_at)
    where status = 'PendingPeriodValidation';

create index if not exists ix_corporate_action_restatement_obligations_action
    on __SCHEMA__.corporate_action_restatement_obligations (corp_act_id);

create table if not exists __SCHEMA__.corporate_action_command_receipts (
    receipt_id uuid primary key,
    operation_kind text not null,
    aggregate_id uuid not null,
    idempotency_key text not null,
    request_fingerprint char(64) not null,
    result_payload jsonb not null,
    recorded_at timestamptz not null,
    constraint ck_corporate_action_command_receipt_fingerprint check (request_fingerprint ~ '^[0-9a-f]{64}$'),
    constraint ux_corporate_action_command_receipt unique (operation_kind, aggregate_id, idempotency_key)
);
