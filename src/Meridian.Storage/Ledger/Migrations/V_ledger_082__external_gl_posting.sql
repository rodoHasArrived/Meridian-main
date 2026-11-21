create table __SCHEMA__.external_gl_book_policy (
    ledger_book_id uuid primary key references __SCHEMA__.ledger_books(ledger_book_id),
    tenant_id text not null,
    company_id text not null,
    provider_id text not null,
    external_ledger_id text not null,
    environment text not null,
    confirmation_required boolean not null default false,
    policy text not null
);

create table __SCHEMA__.external_gl_mapping_profiles (
    revision bigint generated always as identity primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    tenant_id text not null,
    company_id text not null,
    provider_id text not null,
    profile_id text not null,
    content text not null
);

create index ix_external_gl_mapping_profiles_book
    on __SCHEMA__.external_gl_mapping_profiles (ledger_book_id, provider_id, profile_id, revision desc);

create table __SCHEMA__.external_gl_posting_packages (
    package_id text primary key,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    period_id uuid not null references __SCHEMA__.accounting_periods(period_id),
    tenant_id text not null,
    company_id text not null,
    payload_hash text not null,
    payload text not null,
    prepared_by text not null,
    prepared_at timestamptz not null
);

create index ix_external_gl_posting_packages_period
    on __SCHEMA__.external_gl_posting_packages (ledger_book_id, period_id, prepared_at desc);

create table __SCHEMA__.external_gl_posting_decisions (
    package_id text not null references __SCHEMA__.external_gl_posting_packages(package_id),
    decision_kind text not null check (decision_kind in ('Certification', 'Approval')),
    decision text not null,
    primary key (package_id, decision_kind)
);

create table __SCHEMA__.external_gl_posting_outbox (
    idempotency_key text primary key,
    package_id text not null references __SCHEMA__.external_gl_posting_packages(package_id),
    journal_entry_id uuid not null references __SCHEMA__.journal_entries(journal_entry_id),
    provider_id text not null,
    external_ledger_id text not null,
    environment text not null,
    status text not null default 'Pending'
        check (status in ('Pending', 'FullyAccepted', 'PartiallyAccepted', 'Rejected', 'OutcomeUnknown')),
    superseded boolean not null default false,
    post_attempts integer not null default 0 check (post_attempts >= 0),
    last_post_at timestamptz,
    provider_acknowledged boolean not null default false,
    next_check_at timestamptz not null default now(),
    lease_token uuid,
    lease_until timestamptz,
    unique (package_id, journal_entry_id)
);

create unique index ux_external_gl_posting_live_journal
    on __SCHEMA__.external_gl_posting_outbox (provider_id, environment, external_ledger_id, journal_entry_id)
    where not superseded;

create index ix_external_gl_posting_outbox_due
    on __SCHEMA__.external_gl_posting_outbox (next_check_at, idempotency_key)
    where status in ('Pending', 'OutcomeUnknown');

create table __SCHEMA__.external_gl_posting_receipts (
    observation_id bigint generated always as identity primary key,
    idempotency_key text not null references __SCHEMA__.external_gl_posting_outbox(idempotency_key),
    observation text not null
);

create index ix_external_gl_posting_receipts_document
    on __SCHEMA__.external_gl_posting_receipts (idempotency_key, observation_id);

create function __SCHEMA__.refuse_external_gl_evidence_mutation() returns trigger
language plpgsql
as $$
begin
    raise exception 'External GL posting evidence is immutable' using errcode = '55000';
end;
$$;

create trigger external_gl_immutable_package
    before update or delete on __SCHEMA__.external_gl_posting_packages
    for each row execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

create trigger external_gl_immutable_decision
    before update or delete on __SCHEMA__.external_gl_posting_decisions
    for each row execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

create trigger external_gl_immutable_receipt
    before update or delete on __SCHEMA__.external_gl_posting_receipts
    for each row execute function __SCHEMA__.refuse_external_gl_evidence_mutation();

create trigger external_gl_immutable_mapping
    before update or delete on __SCHEMA__.external_gl_mapping_profiles
    for each row execute function __SCHEMA__.refuse_external_gl_evidence_mutation();
