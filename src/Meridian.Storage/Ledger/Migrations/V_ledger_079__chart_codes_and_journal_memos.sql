-- Legacy chart nodes keep their existing identities and receive a code only when explicitly mapped.
alter table __SCHEMA__.accounting_configuration_chart_nodes
    add column if not exists account_code text null;

create unique index if not exists ux_accounting_configuration_chart_node_code
    on __SCHEMA__.accounting_configuration_chart_nodes
        (tenant_id, company_id, fund_profile_id, configuration_scope_id, lower(btrim(account_code)))
    where account_code is not null;

-- A memo and the original chart reference are line evidence rather than balance dimensions.
alter table __SCHEMA__.journal_legs
    add column if not exists memo text null,
    add column if not exists chart_account jsonb null;
