-- Replace V020/V021 repeatable attribution after V036 introduced full-row period auditing.
-- Historical scripts remain unchanged for first-install history and are no longer repeated.
-- Existing stamps are never reassigned. Audit-protected unattributed periods must retain their
-- original fact and appear in reviewed cutover quarantine until a governed repair is available.

update __SCHEMA__.ledger_books b
set tenant_id = t.tenant_id
from __SCHEMA__.fund_profile_tenancy t
where t.fund_profile_id = lower(trim(b.fund_profile_id))
  and b.tenant_id is null;

update __SCHEMA__.accounting_periods p
set tenant_id = b.tenant_id
from __SCHEMA__.ledger_books b
where b.ledger_book_id = p.ledger_book_id
  and p.ledger_book_id is not null
  and b.tenant_id is not null
  and p.tenant_id is null
  and not exists (
      select 1 from __SCHEMA__.ledger_event_audit_events e
      where e.subject_kind = 'period' and e.subject_id = p.period_id);

-- Workflows inherit after book attribution in this same startup. Compare the stored D-format
-- book identity as text so malformed legacy references remain visible to readiness/quarantine
-- instead of aborting migration with a UUID cast failure. Bookless workflows remain unchanged.
update __SCHEMA__.operations_continuity_workflows w
set tenant_id = b.tenant_id
from __SCHEMA__.ledger_books b
where lower(trim(w.workflow_json ->> 'ledgerBookId')) = b.ledger_book_id::text
  and b.tenant_id is not null
  and w.tenant_id is null;
