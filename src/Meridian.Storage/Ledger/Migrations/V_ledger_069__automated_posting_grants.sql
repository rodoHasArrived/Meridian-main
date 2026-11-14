-- Inactive by default: only an authenticated operator creates a narrowly scoped delegation.
create table if not exists __SCHEMA__.automated_posting_grants (
    grant_id uuid primary key,
    version bigint not null check (version > 0),
    tenant_id text not null,
    company_id text null,
    financial_account_id text not null,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    period_id uuid not null references __SCHEMA__.accounting_periods(period_id),
    policy_id text not null,
    policy_version text not null,
    mapping_revision text not null,
    mapping_fingerprint text not null,
    source_mode text not null check (source_mode = 'Synthetic'),
    is_active boolean not null,
    activated_by text not null,
    activated_at_utc timestamptz not null,
    revoked_by text null,
    revoked_at_utc timestamptz null,
    check (is_active = (revoked_at_utc is null))
);

alter table __SCHEMA__.ledger_event_audit_events
    drop constraint if exists ledger_event_audit_events_subject_kind_check;
alter table __SCHEMA__.ledger_event_audit_events
    add constraint ledger_event_audit_events_subject_kind_check
    check (subject_kind in ('journal', 'period', 'automation-grant'));
