alter table __SCHEMA__.accounting_action_audit_events
    add column if not exists tenant_id text null;

update __SCHEMA__.accounting_action_audit_events audit
set tenant_id = workspace.tenant_id
from __SCHEMA__.accounting_configuration_workspaces workspace
where audit.tenant_id is null
  and audit.fund_profile_id = workspace.fund_profile_id
  and coalesce(audit.company_id, 'all') = workspace.company_id
  and coalesce(audit.ledger_book_id::text, 'fund') = workspace.configuration_scope_id;

create index if not exists ix_accounting_action_audit_events_tenant_company
    on __SCHEMA__.accounting_action_audit_events(tenant_id, company_id, recorded_at_utc desc);
