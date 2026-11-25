-- Migration: ledger / manual_journal_entry_drafts
-- Append-only: never rename or renumber applied migrations.
-- PostgreSQL counterpart of the file-backed manual journal draft store, plus the command receipts that
-- serialize every manual journal mutation across hosts. Drafts are mutable working state: each row holds the
-- complete draft DTO so server-held fields (intercompany proposal identity, digests, FX and accrual evidence)
-- round-trip exactly. Optimistic version checks stay in the workbench service. Completed command receipts are
-- retained and cannot be rewritten or removed.
create table if not exists __SCHEMA__.manual_journal_entry_drafts (
    fund_key text not null check(length(fund_key) > 0),
    tenant_key text not null check(length(tenant_key) > 0),
    company_key text not null check(length(company_key) > 0),
    journal_entry_id uuid not null,
    fund_profile_id text not null,
    tenant_id text null,
    company_id text null,
    ledger_book_id uuid null,
    version integer not null,
    updated_at_utc timestamptz not null,
    payload text not null,
    recorded_at timestamptz not null default now(),
    primary key(fund_key, tenant_key, company_key, journal_entry_id)
);
create index if not exists ix_manual_journal_entry_drafts_book
    on __SCHEMA__.manual_journal_entry_drafts(fund_key, ledger_book_id);
create index if not exists ix_manual_journal_entry_drafts_updated
    on __SCHEMA__.manual_journal_entry_drafts(fund_key, updated_at_utc desc, journal_entry_id);

create table if not exists __SCHEMA__.manual_journal_mutation_receipts (
    command_key text primary key check(command_key ~ '^[0-9a-fA-F]{64}$'),
    completed boolean not null,
    completed_at_utc timestamptz null,
    digest text not null,
    payload text not null,
    recorded_at timestamptz not null default now()
);
create index if not exists ix_manual_journal_mutation_receipts_pending
    on __SCHEMA__.manual_journal_mutation_receipts(command_key) where not completed;
create or replace function __SCHEMA__.refuse_completed_manual_journal_receipt_rewrite()
returns trigger language plpgsql as $$
begin
    if old.completed then
        raise exception 'Completed manual journal command receipts are retained and cannot be rewritten'
            using errcode = '55000';
    end if;
    if tg_op = 'DELETE' then
        return old;
    end if;
    return new;
end;
$$;
drop trigger if exists tr_manual_journal_receipt_completed_immutable on __SCHEMA__.manual_journal_mutation_receipts;
create trigger tr_manual_journal_receipt_completed_immutable
before update or delete on __SCHEMA__.manual_journal_mutation_receipts
for each row execute function __SCHEMA__.refuse_completed_manual_journal_receipt_rewrite();
