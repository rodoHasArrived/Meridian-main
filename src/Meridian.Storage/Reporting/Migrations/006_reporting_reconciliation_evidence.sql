create table if not exists __SCHEMA__.reporting_reconciliation_evidence (
    tenant_id text not null,
    receipt_key_sha256 text not null,
    organization_id text not null,
    company_id text not null,
    fund_id text not null,
    ledger_book_id text not null,
    accounting_period_id text not null,
    accounting_basis text not null,
    as_of_date date not null,
    source_checkpoint_id text not null,
    source_checkpoint_hash text not null,
    reconciliation_checkpoint_id text not null,
    reconciliation_checkpoint_hash text not null,
    -- Text is intentional: receipt_hash_sha256 protects the exact serialized bytes. jsonb would
    -- normalize whitespace/property order and make valid retained payload hashes non-repeatable.
    receipt_payload text not null,
    receipt_hash_sha256 text not null,
    retained_at_utc timestamptz not null default now(),
    primary key (tenant_id, receipt_key_sha256),
    constraint ck_reporting_reconciliation_key_hash
        check (receipt_key_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_reconciliation_source_hash
        check (source_checkpoint_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_reconciliation_checkpoint_hash
        check (reconciliation_checkpoint_hash ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_reconciliation_payload_hash
        check (receipt_hash_sha256 ~ '^[0-9a-f]{64}$'),
    constraint ck_reporting_reconciliation_payload_object
        check (jsonb_typeof(receipt_payload::jsonb) = 'object'),
    constraint ck_reporting_reconciliation_scope_trimmed
        check (
            tenant_id = btrim(tenant_id) and length(tenant_id) between 1 and 256
            and organization_id = btrim(organization_id) and length(organization_id) between 1 and 256
            and company_id = btrim(company_id) and length(company_id) between 1 and 256
            and fund_id = btrim(fund_id) and length(fund_id) between 1 and 256
            and ledger_book_id = btrim(ledger_book_id) and length(ledger_book_id) between 1 and 256
            and accounting_period_id = btrim(accounting_period_id) and length(accounting_period_id) between 1 and 256
            and accounting_basis = btrim(accounting_basis) and length(accounting_basis) between 1 and 128
            and source_checkpoint_id = btrim(source_checkpoint_id) and length(source_checkpoint_id) between 1 and 256
            and reconciliation_checkpoint_id = btrim(reconciliation_checkpoint_id)
                and length(reconciliation_checkpoint_id) between 1 and 256));

create unique index if not exists uq_reporting_reconciliation_checkpoint
    on __SCHEMA__.reporting_reconciliation_evidence (
        tenant_id,
        reconciliation_checkpoint_id,
        reconciliation_checkpoint_hash);

create index if not exists ix_reporting_reconciliation_exact_scope
    on __SCHEMA__.reporting_reconciliation_evidence (
        tenant_id,
        organization_id,
        company_id,
        fund_id,
        ledger_book_id,
        accounting_period_id,
        accounting_basis,
        as_of_date);

create or replace function __SCHEMA__.guard_reporting_reconciliation_evidence_immutable()
returns trigger
language plpgsql
as $$
begin
    raise exception 'reporting reconciliation evidence is append-only and cannot be updated or deleted'
        using errcode = '55000';
end;
$$;

drop trigger if exists reporting_reconciliation_evidence_immutable
    on __SCHEMA__.reporting_reconciliation_evidence;
create trigger reporting_reconciliation_evidence_immutable
before update or delete on __SCHEMA__.reporting_reconciliation_evidence
for each row execute function __SCHEMA__.guard_reporting_reconciliation_evidence_immutable();
