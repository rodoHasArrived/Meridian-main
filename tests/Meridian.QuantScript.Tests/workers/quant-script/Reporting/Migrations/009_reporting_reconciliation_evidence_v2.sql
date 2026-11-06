-- v1 reconciliation evidence rows are immutable historical facts. Do not backfill break evidence
-- into them: the original payload and inner receipt hash intentionally omit that field. A v2
-- recovery receipt is retained beside its verified legacy source row instead.
create table if not exists __SCHEMA__.reporting_reconciliation_evidence_v2 (
    like __SCHEMA__.reporting_reconciliation_evidence
        including defaults
        including constraints
        including storage,
    receipt_format_version smallint not null,
    supersedes_legacy_receipt_key_sha256 text null,
    primary key (tenant_id, receipt_key_sha256),
    constraint ck_reporting_reconciliation_v2_format
        check (receipt_format_version = 2),
    constraint ck_reporting_reconciliation_v2_supersedes_hash
        check (supersedes_legacy_receipt_key_sha256 is null
            or supersedes_legacy_receipt_key_sha256 ~ '^[0-9a-f]{64}$')
);

create unique index if not exists uq_reporting_reconciliation_v2_checkpoint
    on __SCHEMA__.reporting_reconciliation_evidence_v2 (
        tenant_id,
        reconciliation_checkpoint_id,
        reconciliation_checkpoint_hash);

create index if not exists ix_reporting_reconciliation_v2_exact_scope
    on __SCHEMA__.reporting_reconciliation_evidence_v2 (
        tenant_id,
        organization_id,
        company_id,
        fund_id,
        ledger_book_id,
        accounting_period_id,
        accounting_basis,
        as_of_date);

create or replace function __SCHEMA__.guard_reporting_reconciliation_evidence_v2_append()
returns trigger
language plpgsql
as $$
begin
    if tg_op <> 'INSERT' then
        raise exception 'reporting reconciliation evidence v2 is append-only and cannot be updated or deleted'
            using errcode = '55000';
    end if;

    if new.receipt_format_version <> 2 then
        raise exception 'new reporting reconciliation evidence must use canonical format v2'
            using errcode = '55000';
    end if;

    if exists (
        select 1
        from __SCHEMA__.reporting_reconciliation_evidence legacy
        where legacy.tenant_id = new.tenant_id
          and legacy.receipt_key_sha256 = new.receipt_key_sha256) then
        if new.supersedes_legacy_receipt_key_sha256 is distinct from new.receipt_key_sha256 then
            raise exception 'a v2 reconciliation recovery receipt must explicitly supersede its immutable legacy source key'
                using errcode = '55000';
        end if;
    elsif new.supersedes_legacy_receipt_key_sha256 is not null then
        raise exception 'a reconciliation recovery receipt cannot supersede a missing legacy source key'
            using errcode = '55000';
    end if;

    return new;
end;
$$;

create trigger reporting_reconciliation_evidence_v2_append
before insert or update or delete on __SCHEMA__.reporting_reconciliation_evidence_v2
for each row execute function __SCHEMA__.guard_reporting_reconciliation_evidence_v2_append();
