-- W14: bounded, explicitly preparer-attested regular cash-dividend authority.
-- Ordinary maker/checker approvals and their historic rows remain unchanged.
create table if not exists __SCHEMA__.cash_dividend_attestation_declarations (
    tenant_id text not null,
    company_id text not null,
    declaration_id uuid not null,
    revision bigint not null check (revision > 0),
    ledger_book_id uuid not null,
    enabled boolean not null,
    principal_id text not null,
    payload jsonb not null,
    payload_hash char(64) not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    primary key (tenant_id, company_id),
    check (length(tenant_id) between 1 and 256 and length(company_id) between 1 and 256),
    check (length(principal_id) between 1 and 256)
);
-- Tenant/company identity is compared case-insensitively by native book ownership. A spelling
-- variant must not create a second selected book or a fresh economic capacity namespace.
create unique index if not exists ux_cash_dividend_attestation_declaration_owner
    on __SCHEMA__.cash_dividend_attestation_declarations (lower(btrim(tenant_id)), lower(btrim(company_id)));

create table if not exists __SCHEMA__.cash_dividend_attestation_declaration_history (
    tenant_id text not null,
    company_id text not null,
    declaration_id uuid not null,
    revision bigint not null check (revision > 0),
    ledger_book_id uuid not null,
    recorded_at timestamptz not null,
    payload jsonb not null,
    payload_hash char(64) not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    primary key (tenant_id, company_id, revision)
);

-- Reservations are immutable and survive posting failures/unknown outcomes. Reaffirming the
-- exact same economic posting reuses its reservation; changed operands cannot reuse its key.
create table if not exists __SCHEMA__.cash_dividend_attestation_reservations (
    reservation_id uuid primary key,
    tenant_id text not null,
    company_id text not null,
    ledger_book_id uuid not null,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    posting_idempotency_key text not null,
    related_event_key text not null,
    budget_month date not null check (extract(day from budget_month) = 1),
    gross_usd numeric(38, 18) not null check (gross_usd > 0 and gross_usd <= 10000),
    whole_event_gross_usd numeric not null check (whole_event_gross_usd >= gross_usd and whole_event_gross_usd <= 10000),
    economic_fingerprint char(64) not null check (economic_fingerprint ~ '^[0-9a-f]{64}$'),
    reserved_at timestamptz not null,
    unique (tenant_id, company_id, ledger_book_id, posting_idempotency_key)
);
create index if not exists ix_cash_dividend_attestation_reservation_month
    on __SCHEMA__.cash_dividend_attestation_reservations (tenant_id, company_id, ledger_book_id, budget_month);
create index if not exists ix_cash_dividend_attestation_reservation_event
    on __SCHEMA__.cash_dividend_attestation_reservations (tenant_id, company_id, ledger_book_id, related_event_key);

-- A provider event alias can never move an already observed dividend into another date/key.
create table if not exists __SCHEMA__.cash_dividend_attestation_event_aliases (
    tenant_id text not null,
    company_id text not null,
    provider_id text not null,
    source_event_id text not null,
    source_event_version text not null,
    source_evidence_hash char(64) not null check (source_evidence_hash ~ '^[0-9a-f]{64}$'),
    related_event_key text not null,
    recorded_at timestamptz not null,
    primary key (tenant_id, company_id, provider_id, source_event_id)
);

create table if not exists __SCHEMA__.cash_dividend_attestations (
    attestation_id uuid primary key,
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    projection_id uuid not null references __SCHEMA__.corporate_action_case_accounting_projections(projection_id),
    reservation_id uuid not null references __SCHEMA__.cash_dividend_attestation_reservations(reservation_id),
    tenant_id text not null,
    company_id text not null,
    ledger_book_id uuid not null,
    declaration_id uuid not null,
    declaration_revision bigint not null check (declaration_revision > 0),
    bound_case_version bigint not null check (bound_case_version > 0),
    attested_by text not null,
    attested_at timestamptz not null,
    payload jsonb not null,
    payload_hash char(64) not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    projection_payload jsonb not null,
    projection_hash char(64) not null check (projection_hash ~ '^[0-9a-f]{64}$'),
    voided_at timestamptz null,
    voided_by text null,
    check ((voided_at is null) = (voided_by is null))
);
create unique index if not exists ux_cash_dividend_attestation_active_case
    on __SCHEMA__.cash_dividend_attestations (case_id) where voided_at is null;

create table if not exists __SCHEMA__.cash_dividend_attestation_postings (
    posting_id uuid primary key,
    attestation_id uuid not null unique references __SCHEMA__.cash_dividend_attestations(attestation_id),
    case_id uuid not null references __SCHEMA__.corporate_action_processing_cases(case_id),
    projection_id uuid not null references __SCHEMA__.corporate_action_case_accounting_projections(projection_id),
    tenant_id text not null,
    company_id text not null,
    ledger_book_id uuid not null,
    period_id uuid not null,
    journal_entry_id uuid not null unique,
    posted_at timestamptz not null,
    payload jsonb not null,
    payload_hash char(64) not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    unique (case_id, projection_id)
);
create index if not exists ix_cash_dividend_attestation_posting_scope
    on __SCHEMA__.cash_dividend_attestation_postings (tenant_id, company_id, journal_entry_id);

create or replace function __SCHEMA__.guard_cash_dividend_attestation_history()
returns trigger language plpgsql as $$
begin
    raise exception 'Cash-dividend attestation history is append-only.' using errcode = '55000';
end;
$$;

do $$
declare t text;
begin
    foreach t in array array['cash_dividend_attestation_declaration_history',
        'cash_dividend_attestation_reservations', 'cash_dividend_attestation_event_aliases',
        'cash_dividend_attestation_postings']
    loop
        execute format('create trigger guard_cash_dividend_immutable before update or delete on __SCHEMA__.%I '
            'for each row execute function __SCHEMA__.guard_cash_dividend_attestation_history()', t);
        execute format('create trigger guard_cash_dividend_truncate before truncate on __SCHEMA__.%I '
            'for each statement execute function __SCHEMA__.guard_cash_dividend_attestation_history()', t);
    end loop;
end;
$$;

create or replace function __SCHEMA__.guard_cash_dividend_attestation_receipt()
returns trigger language plpgsql as $$
begin
    if tg_op = 'DELETE' or old.voided_at is not null
        or new.voided_at is null or new.voided_by is null
        or (to_jsonb(new) - 'voided_at' - 'voided_by') <> (to_jsonb(old) - 'voided_at' - 'voided_by') then
        raise exception 'Cash-dividend attestation receipts permit only append-only voiding metadata.' using errcode = '55000';
    end if;
    return new;
end;
$$;
create trigger guard_cash_dividend_receipt before update or delete on __SCHEMA__.cash_dividend_attestations
    for each row execute function __SCHEMA__.guard_cash_dividend_attestation_receipt();
create trigger guard_cash_dividend_receipt_truncate before truncate on __SCHEMA__.cash_dividend_attestations
    for each statement execute function __SCHEMA__.guard_cash_dividend_attestation_history();
