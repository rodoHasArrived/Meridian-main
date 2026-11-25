-- A reviewed cross-book settlement plan and its observed, immutable journal proofs.
create table if not exists __SCHEMA__.book_fx_settlement_sets (
    tenant_id text not null,
    company_id text not null,
    set_id uuid not null,
    gaap_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    cash_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    trade_event_id uuid not null,
    settlement_event_id uuid not null,
    plan_hash_sha256 text not null check (plan_hash_sha256 ~ '^[a-f0-9]{64}$'),
    payload_json text not null check (jsonb_typeof(payload_json::jsonb) = 'object'),
    primary key (tenant_id, company_id, set_id),
    unique (tenant_id, company_id, gaap_book_id, cash_book_id, trade_event_id, settlement_event_id),
    check (gaap_book_id <> cash_book_id and trade_event_id <> settlement_event_id)
);

create table if not exists __SCHEMA__.book_fx_settlement_receipts (
    tenant_id text not null,
    company_id text not null,
    set_id uuid not null,
    leg text not null check (leg in ('GaapTrade', 'GaapSettlement', 'CashSettlement')),
    command_id uuid not null,
    journal_entry_id uuid not null,
    content_hash_sha256 text not null check (content_hash_sha256 ~ '^[a-f0-9]{64}$'),
    payload_json text not null check (jsonb_typeof(payload_json::jsonb) = 'object'),
    primary key (tenant_id, company_id, set_id, leg),
    unique (tenant_id, company_id, set_id, command_id),
    unique (tenant_id, company_id, journal_entry_id),
    foreign key (tenant_id, company_id, set_id)
        references __SCHEMA__.book_fx_settlement_sets (tenant_id, company_id, set_id)
);

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.book_fx_settlement_sets'::regclass and tgname = 'book_fx_settlement_immutable') then
        create trigger book_fx_settlement_immutable before update or delete on __SCHEMA__.book_fx_settlement_sets
            for each row execute function __SCHEMA__.reject_close_snapshot_mutation();
    end if;
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.book_fx_settlement_receipts'::regclass and tgname = 'book_fx_settlement_immutable') then
        create trigger book_fx_settlement_immutable before update or delete on __SCHEMA__.book_fx_settlement_receipts
            for each row execute function __SCHEMA__.reject_close_snapshot_mutation();
    end if;
end;
$migration$;
