alter table __SCHEMA__.accounting_configuration_workspaces
    add column if not exists configuration_scope_id text not null default 'fund';

update __SCHEMA__.accounting_configuration_workspaces
set configuration_scope_id = coalesce(ledger_book_id::text, 'fund')
where configuration_scope_id = 'fund'
  and ledger_book_id is not null;

alter table __SCHEMA__.accounting_configuration_chart_nodes
    add column if not exists configuration_scope_id text not null default 'fund';

alter table __SCHEMA__.accounting_configuration_journal_templates
    add column if not exists configuration_scope_id text not null default 'fund';

alter table __SCHEMA__.accounting_configuration_posting_rules
    add column if not exists configuration_scope_id text not null default 'fund';

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    add column if not exists configuration_scope_id text not null default 'fund';

update __SCHEMA__.accounting_configuration_chart_nodes node
set configuration_scope_id = workspace.configuration_scope_id
from __SCHEMA__.accounting_configuration_workspaces workspace
where node.fund_profile_id = workspace.fund_profile_id;

update __SCHEMA__.accounting_configuration_journal_templates template
set configuration_scope_id = workspace.configuration_scope_id
from __SCHEMA__.accounting_configuration_workspaces workspace
where template.fund_profile_id = workspace.fund_profile_id;

update __SCHEMA__.accounting_configuration_posting_rules rule
set configuration_scope_id = workspace.configuration_scope_id
from __SCHEMA__.accounting_configuration_workspaces workspace
where rule.fund_profile_id = workspace.fund_profile_id;

update __SCHEMA__.accounting_configuration_rule_test_cases test_case
set configuration_scope_id = workspace.configuration_scope_id
from __SCHEMA__.accounting_configuration_workspaces workspace
where test_case.fund_profile_id = workspace.fund_profile_id;

alter table __SCHEMA__.accounting_configuration_chart_nodes
    drop constraint if exists accounting_configuration_chart_nodes_fund_profile_id_fkey;

alter table __SCHEMA__.accounting_configuration_journal_templates
    drop constraint if exists accounting_configuration_journal_templates_fund_profile_id_fkey;

alter table __SCHEMA__.accounting_configuration_posting_rules
    drop constraint if exists accounting_configuration_posting_rules_fund_profile_id_fkey;

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    drop constraint if exists accounting_configuration_rule_test_cases_fund_profile_id_fkey;

alter table __SCHEMA__.accounting_configuration_chart_nodes
    drop constraint if exists accounting_configuration_chart_nodes_pkey;

alter table __SCHEMA__.accounting_configuration_journal_templates
    drop constraint if exists accounting_configuration_journal_templates_pkey;

alter table __SCHEMA__.accounting_configuration_posting_rules
    drop constraint if exists accounting_configuration_posting_rules_pkey;

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    drop constraint if exists accounting_configuration_rule_test_cases_pkey;

alter table __SCHEMA__.accounting_configuration_chart_nodes
    drop constraint if exists accounting_configuration_chart_nodes_workspace_fkey;

alter table __SCHEMA__.accounting_configuration_journal_templates
    drop constraint if exists accounting_configuration_journal_templates_workspace_fkey;

alter table __SCHEMA__.accounting_configuration_posting_rules
    drop constraint if exists accounting_configuration_posting_rules_workspace_fkey;

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    drop constraint if exists accounting_configuration_rule_test_cases_workspace_fkey;

alter table __SCHEMA__.accounting_configuration_workspaces
    drop constraint if exists accounting_configuration_workspaces_pkey;

alter table __SCHEMA__.accounting_configuration_workspaces
    add constraint accounting_configuration_workspaces_pkey
    primary key (fund_profile_id, configuration_scope_id);

alter table __SCHEMA__.accounting_configuration_chart_nodes
    add constraint accounting_configuration_chart_nodes_pkey
    primary key (fund_profile_id, configuration_scope_id, node_id);

alter table __SCHEMA__.accounting_configuration_journal_templates
    add constraint accounting_configuration_journal_templates_pkey
    primary key (fund_profile_id, configuration_scope_id, template_id);

alter table __SCHEMA__.accounting_configuration_posting_rules
    add constraint accounting_configuration_posting_rules_pkey
    primary key (fund_profile_id, configuration_scope_id, rule_id);

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    add constraint accounting_configuration_rule_test_cases_pkey
    primary key (fund_profile_id, configuration_scope_id, test_case_id);

alter table __SCHEMA__.accounting_configuration_chart_nodes
    add constraint accounting_configuration_chart_nodes_workspace_fkey
    foreign key (fund_profile_id, configuration_scope_id)
    references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id, configuration_scope_id)
    on delete cascade;

alter table __SCHEMA__.accounting_configuration_journal_templates
    add constraint accounting_configuration_journal_templates_workspace_fkey
    foreign key (fund_profile_id, configuration_scope_id)
    references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id, configuration_scope_id)
    on delete cascade;

alter table __SCHEMA__.accounting_configuration_posting_rules
    add constraint accounting_configuration_posting_rules_workspace_fkey
    foreign key (fund_profile_id, configuration_scope_id)
    references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id, configuration_scope_id)
    on delete cascade;

alter table __SCHEMA__.accounting_configuration_rule_test_cases
    add constraint accounting_configuration_rule_test_cases_workspace_fkey
    foreign key (fund_profile_id, configuration_scope_id)
    references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id, configuration_scope_id)
    on delete cascade;

drop index if exists __SCHEMA__.ix_accounting_configuration_chart_nodes_path;
drop index if exists __SCHEMA__.ix_accounting_configuration_posting_rules_template;

create unique index if not exists ix_accounting_configuration_chart_nodes_path
    on __SCHEMA__.accounting_configuration_chart_nodes(fund_profile_id, configuration_scope_id, lower(path));

create index if not exists ix_accounting_configuration_posting_rules_template
    on __SCHEMA__.accounting_configuration_posting_rules(fund_profile_id, configuration_scope_id, template_id);
