create table if not exists __SCHEMA__.accounting_configuration_rule_test_cases (
    fund_profile_id text not null references __SCHEMA__.accounting_configuration_workspaces(fund_profile_id) on delete cascade,
    test_case_id text not null,
    display_name text not null,
    test_case_payload jsonb not null,
    primary key (fund_profile_id, test_case_id)
);

create index if not exists ix_accounting_configuration_rule_test_cases_payload
    on __SCHEMA__.accounting_configuration_rule_test_cases using gin (test_case_payload);
