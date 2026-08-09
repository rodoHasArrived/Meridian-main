create table if not exists __SCHEMA__.accounting_configuration_workspaces (
    fund_profile_id text primary key,
    ledger_book_id uuid null,
    status text not null,
    configuration_version text not null,
    updated_at_utc timestamptz not null,
    validation_issues jsonb not null default '[]'::jsonb
);

create table if not exists __SCHEMA__.accounting_configuration_chart_nodes (
    fund_profile_id text not null references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id) on delete cascade,
    node_id text not null,
    path text not null,
    account_name text not null,
    account_type text not null,
    parent_path text null,
    symbol text null,
    financial_account_id text null,
    is_archived boolean not null default false,
    primary key (fund_profile_id, node_id)
);

create unique index if not exists ix_accounting_configuration_chart_nodes_path
    on __SCHEMA__.accounting_configuration_chart_nodes(fund_profile_id, lower(path));

create table if not exists __SCHEMA__.accounting_configuration_journal_templates (
    fund_profile_id text not null references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id) on delete cascade,
    template_id text not null,
    display_name text not null,
    description text not null,
    lines jsonb not null,
    is_archived boolean not null default false,
    version text not null,
    primary key (fund_profile_id, template_id)
);

create table if not exists __SCHEMA__.accounting_configuration_posting_rules (
    fund_profile_id text not null references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id) on delete cascade,
    rule_id text not null,
    display_name text not null,
    source_event_type text not null,
    template_id text not null,
    rule_version text not null,
    is_archived boolean not null default false,
    description text null,
    primary key (fund_profile_id, rule_id)
);

create index if not exists ix_accounting_configuration_posting_rules_template
    on __SCHEMA__.accounting_configuration_posting_rules(fund_profile_id, template_id);

create table if not exists __SCHEMA__.accounting_action_audit_events (
    audit_event_id uuid primary key,
    recorded_at_utc timestamptz not null,
    actor text not null,
    action text not null,
    fund_profile_id text null,
    ledger_book_id uuid null,
    correlation_id text null,
    before_hash text not null,
    after_hash text not null,
    validation_issues jsonb not null default '[]'::jsonb,
    evidence_links jsonb not null default '[]'::jsonb,
    company_id text null,
    report_group_principal_ids jsonb not null default '[]'::jsonb
);

create index if not exists ix_accounting_action_audit_events_scope
    on __SCHEMA__.accounting_action_audit_events(fund_profile_id, ledger_book_id, recorded_at_utc desc);

create index if not exists ix_accounting_action_audit_events_company
    on __SCHEMA__.accounting_action_audit_events(company_id, recorded_at_utc desc);
