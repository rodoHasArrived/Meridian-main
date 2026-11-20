-- Fiscal close retains a view of continuous-book opening balances, not an additional journal.
-- Each correction creates a new immutable receipt linked to the immediately preceding revision.
create table if not exists __SCHEMA__.fiscal_year_close_receipts (
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    fiscal_year integer not null check (fiscal_year between 1900 and 9999),
    receipt_id uuid not null,
    supersedes_receipt_id uuid null,
    revision integer not null check (revision > 0),
    tenant_id text not null,
    company_id text not null,
    plan_hash_sha256 text not null check (plan_hash_sha256 ~ '^[a-f0-9]{64}$'),
    payload_json text not null check (jsonb_typeof(payload_json::jsonb) = 'object'),
    content_hash_sha256 text not null check (content_hash_sha256 ~ '^[a-f0-9]{64}$'),
    primary key (ledger_book_id, fiscal_year, receipt_id),
    unique (ledger_book_id, fiscal_year, revision),
    unique (ledger_book_id, fiscal_year, plan_hash_sha256),
    unique (ledger_book_id, fiscal_year, supersedes_receipt_id),
    foreign key (ledger_book_id, fiscal_year, supersedes_receipt_id)
        references __SCHEMA__.fiscal_year_close_receipts (ledger_book_id, fiscal_year, receipt_id),
    check ((revision = 1) = (supersedes_receipt_id is null))
);

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.fiscal_year_close_receipts'::regclass
          and tgname = 'fiscal_year_close_receipt_immutable') then
        create trigger fiscal_year_close_receipt_immutable
            before update or delete on __SCHEMA__.fiscal_year_close_receipts
            for each row execute function __SCHEMA__.reject_close_snapshot_mutation();
    end if;
end;
$migration$;
