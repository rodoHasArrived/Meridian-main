-- Migration 030: Durable corporate-action source proposals and processing cases
-- Keeps provider observations separate from accepted Security Master events and from scoped
-- operator processing cases. Every governed mutation is versioned and has a durable idempotency
-- receipt; acceptance writes the canonical action, initial case, proposal decision, and transition
-- in one database transaction.

alter table __SCHEMA__.corporate_actions
    add column if not exists payload_schema_version integer not null default 1;

-- Keep the new column nullable for rolling-upgrade compatibility: binaries from before migration
-- 030 do not supply a fingerprint. Current application writers always supply one, acceptance
-- reconciles matching historical nulls, and a later migration may require/validate the column only
-- after legacy rows are backfilled and old writers are retired.
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

-- Migration 021 introduced supersede links without enforcing structural or semantic lineage.
-- Hold legacy writers while checking those invariants so an upgrade fails with repairable evidence,
-- rather than an opaque constraint/index error or a newly blessed invalid chain. The migration
-- runner wraps this script in one transaction, so a failed preflight leaves schema and data unchanged.
lock table __SCHEMA__.corporate_actions in share row exclusive mode;

do $$
declare
    orphan_count bigint;
    orphan_sample text;
begin
    select count(*)
    into orphan_count
    from __SCHEMA__.corporate_actions child
    left join __SCHEMA__.corporate_actions parent
        on parent.corp_act_id = child.supersedes_corp_act_id
    where child.supersedes_corp_act_id is not null
      and parent.corp_act_id is null;

    if orphan_count > 0 then
        select string_agg(
            format('%s -> %s', sample.corp_act_id, sample.supersedes_corp_act_id),
            ', ' order by sample.corp_act_id)
        into orphan_sample
        from (
            select child.corp_act_id, child.supersedes_corp_act_id
            from __SCHEMA__.corporate_actions child
            left join __SCHEMA__.corporate_actions parent
                on parent.corp_act_id = child.supersedes_corp_act_id
            where child.supersedes_corp_act_id is not null
              and parent.corp_act_id is null
            order by child.corp_act_id
            limit 5
        ) sample;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce corporate-action parentage: %s orphaned supersede link(s) exist.',
                orphan_count),
            detail = format('Sample child -> missing parent links: %s.', orphan_sample),
            hint = 'Restore or clear each missing parent link through governed repair, then retry. Migration 030 does not infer or delete canonical lineage.';
    end if;
end $$;

do $$
declare
    cycle_member_count bigint;
    cycle_sample text;
begin
    with recursive lineage_walk as (
        select
            action.corp_act_id as start_id,
            action.corp_act_id as current_id,
            action.supersedes_corp_act_id as next_id,
            array[action.corp_act_id]::uuid[] as visited,
            false as is_cycle
        from __SCHEMA__.corporate_actions action
        where action.supersedes_corp_act_id is not null

        union all

        select
            walk.start_id,
            parent.corp_act_id as current_id,
            parent.supersedes_corp_act_id as next_id,
            walk.visited || parent.corp_act_id,
            parent.corp_act_id = any(walk.visited) as is_cycle
        from lineage_walk walk
        join __SCHEMA__.corporate_actions parent
            on parent.corp_act_id = walk.next_id
        where walk.next_id is not null
          and not walk.is_cycle
    )
    select count(distinct current_id)
    into cycle_member_count
    from lineage_walk
    where is_cycle;

    if cycle_member_count > 0 then
        with recursive lineage_walk as (
            select
                action.corp_act_id as start_id,
                action.corp_act_id as current_id,
                action.supersedes_corp_act_id as next_id,
                array[action.corp_act_id]::uuid[] as visited,
                false as is_cycle
            from __SCHEMA__.corporate_actions action
            where action.supersedes_corp_act_id is not null

            union all

            select
                walk.start_id,
                parent.corp_act_id as current_id,
                parent.supersedes_corp_act_id as next_id,
                walk.visited || parent.corp_act_id,
                parent.corp_act_id = any(walk.visited) as is_cycle
            from lineage_walk walk
            join __SCHEMA__.corporate_actions parent
                on parent.corp_act_id = walk.next_id
            where walk.next_id is not null
              and not walk.is_cycle
        ), cycle_members as (
            select distinct on (current_id)
                current_id,
                visited
            from lineage_walk
            where is_cycle
            order by current_id, cardinality(visited), visited::text
        )
        select string_agg(
            format('%s via %s', sample.current_id, array_to_string(sample.visited, ' -> ')),
            ', ' order by sample.current_id)
        into cycle_sample
        from (
            select current_id, visited
            from cycle_members
            order by current_id
            limit 5
        ) sample;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce a linear corporate-action lineage: %s cycle member(s) exist.',
                cycle_member_count),
            detail = format('Sample repeated member via traversal path: %s.', cycle_sample),
            hint = 'Break every self-reference and multi-node cycle through governed repair, then retry. Migration 030 does not choose which historical link to discard.';
    end if;
end $$;

do $$
declare
    branching_parent_count bigint;
    branching_sample text;
begin
    select count(*)
    into branching_parent_count
    from (
        select supersedes_corp_act_id
        from __SCHEMA__.corporate_actions
        where supersedes_corp_act_id is not null
        group by supersedes_corp_act_id
        having count(*) > 1
    ) branches;

    if branching_parent_count > 0 then
        select string_agg(
            format('%s (%s successors)', sample.parent_id, sample.successor_count),
            ', ' order by sample.parent_id)
        into branching_sample
        from (
            select supersedes_corp_act_id as parent_id, count(*) as successor_count
            from __SCHEMA__.corporate_actions
            where supersedes_corp_act_id is not null
            group by supersedes_corp_act_id
            having count(*) > 1
            order by supersedes_corp_act_id
            limit 5
        ) sample;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce a single corporate-action successor: %s branching predecessor(s) exist.',
                branching_parent_count),
            detail = format('Sample predecessor (successor count) values: %s.', branching_sample),
            hint = 'Resolve each branching lineage through governed repair, then retry. Migration 030 does not select or delete a canonical successor.';
    end if;
end $$;

do $$
declare
    cross_security_count bigint;
    cross_security_sample text;
begin
    select count(*)
    into cross_security_count
    from __SCHEMA__.corporate_actions child
    join __SCHEMA__.corporate_actions parent
        on parent.corp_act_id = child.supersedes_corp_act_id
    where child.security_id <> parent.security_id;

    if cross_security_count > 0 then
        select string_agg(
            format(
                '%s [%s] -> %s [%s]',
                sample.child_id,
                sample.child_security_id,
                sample.parent_id,
                sample.parent_security_id),
            ', ' order by sample.child_id)
        into cross_security_sample
        from (
            select
                child.corp_act_id as child_id,
                child.security_id as child_security_id,
                parent.corp_act_id as parent_id,
                parent.security_id as parent_security_id
            from __SCHEMA__.corporate_actions child
            join __SCHEMA__.corporate_actions parent
                on parent.corp_act_id = child.supersedes_corp_act_id
            where child.security_id <> parent.security_id
            order by child.corp_act_id
            limit 5
        ) sample;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce corporate-action lineage: %s cross-security supersede link(s) exist.',
                cross_security_count),
            detail = format('Sample child [security] -> parent [security] links: %s.', cross_security_sample),
            hint = 'Repair each link so child and parent reference the same security, then retry. Migration 030 does not infer cross-security lineage.';
    end if;
end $$;

do $$
declare
    changed_event_type_count bigint;
    changed_event_type_sample text;
begin
    select count(*)
    into changed_event_type_count
    from __SCHEMA__.corporate_actions child
    join __SCHEMA__.corporate_actions parent
        on parent.corp_act_id = child.supersedes_corp_act_id
    where child.event_type <> parent.event_type;

    if changed_event_type_count > 0 then
        select string_agg(
            format(
                '%s [%s] -> %s [%s]',
                sample.child_id,
                sample.child_event_type,
                sample.parent_id,
                sample.parent_event_type),
            ', ' order by sample.child_id)
        into changed_event_type_sample
        from (
            select
                child.corp_act_id as child_id,
                child.event_type as child_event_type,
                parent.corp_act_id as parent_id,
                parent.event_type as parent_event_type
            from __SCHEMA__.corporate_actions child
            join __SCHEMA__.corporate_actions parent
                on parent.corp_act_id = child.supersedes_corp_act_id
            where child.event_type <> parent.event_type
            order by child.corp_act_id
            limit 5
        ) sample;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce corporate-action lineage: %s changed-event-type supersede link(s) exist.',
                changed_event_type_count),
            detail = format('Sample child [type] -> parent [type] links: %s.', changed_event_type_sample),
            hint = 'Normalize stored event types or repair each link so child and parent retain one canonical event type, then retry.';
    end if;
end $$;

do $$
declare
    invalid_lifecycle_count bigint;
    invalid_lifecycle_sample text;
begin
    with lineage as (
        select
            child.corp_act_id as child_id,
            parent.corp_act_id as parent_id,
            coalesce(nullif(child.lifecycle_state, ''), 'Confirmed') as child_state,
            coalesce(nullif(parent.lifecycle_state, ''), 'Confirmed') as parent_state
        from __SCHEMA__.corporate_actions child
        join __SCHEMA__.corporate_actions parent
            on parent.corp_act_id = child.supersedes_corp_act_id
    )
    select count(*)
    into invalid_lifecycle_count
    from lineage
    where child_state not in ('Announced', 'Confirmed', 'Ex', 'Paid', 'Cancelled')
       or parent_state not in ('Announced', 'Confirmed', 'Ex', 'Paid', 'Cancelled')
       or parent_state = 'Cancelled'
       or case child_state
              when 'Announced' then 0
              when 'Confirmed' then 1
              when 'Ex' then 2
              when 'Paid' then 3
              when 'Cancelled' then 2147483647
              else -1
          end
          < case parent_state
                when 'Announced' then 0
                when 'Confirmed' then 1
                when 'Ex' then 2
                when 'Paid' then 3
                when 'Cancelled' then 2147483647
                else -1
            end;

    if invalid_lifecycle_count > 0 then
        with lineage as (
            select
                child.corp_act_id as child_id,
                parent.corp_act_id as parent_id,
                coalesce(nullif(child.lifecycle_state, ''), 'Confirmed') as child_state,
                coalesce(nullif(parent.lifecycle_state, ''), 'Confirmed') as parent_state
            from __SCHEMA__.corporate_actions child
            join __SCHEMA__.corporate_actions parent
                on parent.corp_act_id = child.supersedes_corp_act_id
        ), invalid as (
            select *
            from lineage
            where child_state not in ('Announced', 'Confirmed', 'Ex', 'Paid', 'Cancelled')
               or parent_state not in ('Announced', 'Confirmed', 'Ex', 'Paid', 'Cancelled')
               or parent_state = 'Cancelled'
               or case child_state
                      when 'Announced' then 0
                      when 'Confirmed' then 1
                      when 'Ex' then 2
                      when 'Paid' then 3
                      when 'Cancelled' then 2147483647
                      else -1
                  end
                  < case parent_state
                        when 'Announced' then 0
                        when 'Confirmed' then 1
                        when 'Ex' then 2
                        when 'Paid' then 3
                        when 'Cancelled' then 2147483647
                        else -1
                    end
            order by child_id
            limit 5
        )
        select string_agg(
            format(
                '%s [%s] -> %s [%s]',
                invalid.child_id,
                invalid.child_state,
                invalid.parent_id,
                invalid.parent_state),
            ', ' order by invalid.child_id)
        into invalid_lifecycle_sample
        from invalid;

        raise exception using
            errcode = '23514',
            message = format(
                'Migration 030 cannot enforce corporate-action lineage: %s invalid or backward-lifecycle supersede link(s) exist.',
                invalid_lifecycle_count),
            detail = format('Sample child [state] -> parent [state] links: %s.', invalid_lifecycle_sample),
            hint = 'Repair unknown lifecycle values and links that move lifecycle backwards, then retry. Cancelled is an absorbing terminal state.';
    end if;
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

-- Provider-neutral canonical event identity. Null fingerprints remain outside this index only for
-- rolling-upgrade and historical compatibility; current application writes supply one. NULL
-- lifecycle means Confirmed and a zero UUID is solely the expression-index sentinel for a root.
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
    constraint ck_corporate_action_source_provider_id_length
        check (octet_length(provider_id) between 1 and 256),
    constraint ck_corporate_action_source_event_id_length
        check (octet_length(source_event_id) between 1 and 256),
    constraint ck_corporate_action_source_event_version_length
        check (octet_length(source_event_version) between 1 and 256),
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
    constraint ck_corporate_action_canonical_source_provider_id_length
        check (octet_length(provider_id) between 1 and 256),
    constraint ck_corporate_action_canonical_source_event_id_length
        check (octet_length(source_event_id) between 1 and 256),
    constraint ck_corporate_action_canonical_source_event_version_length
        check (octet_length(source_event_version) between 1 and 256),
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
    constraint ck_corporate_action_processing_case_version check (version > 0),
    constraint ck_corporate_action_processing_case_scope_identity_lengths check (
        octet_length(tenant_id) between 1 and 256
        and octet_length(company_id) between 1 and 256
        and (structure_node_id is null or octet_length(structure_node_id) between 1 and 256)
        and (fund_profile_id is null or octet_length(fund_profile_id) between 1 and 256)
        and (financial_account_id is null or octet_length(financial_account_id) between 1 and 256)
        and (portfolio_id is null or octet_length(portfolio_id) between 1 and 256)
        and (custody_account_id is null or octet_length(custody_account_id) between 1 and 256)
        and (ledger_book_id is null or octet_length(ledger_book_id) between 1 and 256)
        and (period_id is null or octet_length(period_id) between 1 and 256)
        and (accounting_basis is null or octet_length(accounting_basis) between 1 and 256)
        and (functional_currency is null or octet_length(functional_currency) between 1 and 256)
        and (jurisdiction is null or octet_length(jurisdiction) between 1 and 256)),
    constraint ck_corporate_action_processing_case_scope_identity_total check (
        octet_length(tenant_id)
        + octet_length(company_id)
        + coalesce(octet_length(structure_node_id), 0)
        + coalesce(octet_length(fund_profile_id), 0)
        + coalesce(octet_length(financial_account_id), 0)
        + coalesce(octet_length(portfolio_id), 0)
        + coalesce(octet_length(custody_account_id), 0)
        + coalesce(octet_length(ledger_book_id), 0)
        + coalesce(octet_length(period_id), 0)
        + coalesce(octet_length(accounting_basis), 0)
        + coalesce(octet_length(functional_currency), 0)
        + coalesce(octet_length(jurisdiction), 0) <= 2048)
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
    recorded_at timestamptz not null,
    constraint ck_corporate_action_processing_option_code_length
        check (octet_length(option_code) between 1 and 256)
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
    constraint ck_corporate_action_case_transition_operation_kind_length
        check (octet_length(operation_kind) between 1 and 64),
    constraint ck_corporate_action_case_transition_idempotency_key_length
        check (octet_length(idempotency_key) between 1 and 256),
    constraint ux_corporate_action_case_transition_idempotency
        unique (case_id, operation_kind, idempotency_key)
);

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
    recorded_at timestamptz not null,
    constraint ck_corporate_action_restatement_tenant_id_length
        check (octet_length(tenant_id) between 1 and 256),
    constraint ck_corporate_action_restatement_company_id_length
        check (octet_length(company_id) between 1 and 256)
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
    constraint ck_corporate_action_command_receipt_operation_kind_length
        check (octet_length(operation_kind) between 1 and 64),
    constraint ck_corporate_action_command_receipt_idempotency_key_length
        check (octet_length(idempotency_key) between 1 and 256),
    constraint ux_corporate_action_command_receipt unique (operation_kind, aggregate_id, idempotency_key)
);
