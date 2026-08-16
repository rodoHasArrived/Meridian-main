alter table __SCHEMA__.accounting_configuration_posting_rules
    add column if not exists rule_payload jsonb null;

create index if not exists ix_accounting_configuration_posting_rules_payload
    on __SCHEMA__.accounting_configuration_posting_rules using gin (rule_payload);
