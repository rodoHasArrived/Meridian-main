-- Durable commitment authority and immutable call/posting receipts. No historical authority is inferred.
create table if not exists __SCHEMA__.private_capital_commitments (
    tenant_id text not null,
    company_id text not null,
    fund_profile_id text not null,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    commitment_id text not null,
    revision integer not null check (revision > 0),
    document jsonb not null check (jsonb_typeof(document) = 'object'),
    primary key (tenant_id, company_id, fund_profile_id, ledger_book_id, commitment_id, revision)
);

create table if not exists __SCHEMA__.private_capital_calls (
    tenant_id text not null,
    company_id text not null,
    fund_profile_id text not null,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    call_id text not null,
    document jsonb not null check (jsonb_typeof(document) = 'object'),
    primary key (tenant_id, company_id, fund_profile_id, ledger_book_id, call_id)
);

create table if not exists __SCHEMA__.private_capital_postings (
    tenant_id text not null,
    company_id text not null,
    entity_id text not null,
    fund_profile_id text not null,
    ledger_book_id uuid not null references __SCHEMA__.ledger_books(ledger_book_id),
    currency text not null,
    posting_id text not null,
    commitment_id text not null,
    commitment_revision integer not null,
    call_id text not null,
    installment_id text not null,
    kind text not null check (kind in ('Issuance', 'Funding')),
    amount numeric not null check (amount > 0),
    funding_receipt_id text null,
    bank_line_id text null,
    journal_entry_id uuid not null unique references __SCHEMA__.journal_entries(journal_entry_id)
        deferrable initially deferred,
    effective_date date not null,
    primary key (tenant_id, company_id, fund_profile_id, ledger_book_id, posting_id),
    foreign key (tenant_id, company_id, fund_profile_id, ledger_book_id, call_id)
        references __SCHEMA__.private_capital_calls(tenant_id, company_id, fund_profile_id, ledger_book_id, call_id),
    foreign key (tenant_id, company_id, fund_profile_id, ledger_book_id, commitment_id, commitment_revision)
        references __SCHEMA__.private_capital_commitments(tenant_id, company_id, fund_profile_id, ledger_book_id, commitment_id, revision),
    check ((kind = 'Issuance' and funding_receipt_id is null and bank_line_id is null)
        or (kind = 'Funding' and funding_receipt_id is not null and bank_line_id is not null
            and length(btrim(funding_receipt_id)) > 0 and length(btrim(bank_line_id)) > 0))
);

create unique index if not exists ux_private_capital_issuance
    on __SCHEMA__.private_capital_postings(tenant_id, company_id, fund_profile_id, ledger_book_id, call_id, commitment_id)
    where kind = 'Issuance';
create unique index if not exists ux_private_capital_receipt
    on __SCHEMA__.private_capital_postings(tenant_id, company_id, funding_receipt_id)
    where kind = 'Funding';
create unique index if not exists ux_private_capital_bank_line
    on __SCHEMA__.private_capital_postings(tenant_id, company_id, bank_line_id)
    where kind = 'Funding';

create or replace function __SCHEMA__.protect_private_capital_authority()
returns trigger language plpgsql as $function$
begin
    if tg_op = 'UPDATE' and tg_table_name = 'private_capital_commitments' then
        -- Separate the table dispatch: posting rows do not have a document column, and
        -- PL/pgSQL resolves record fields before SQL boolean short-circuit evaluation.
        if old.document ->> 'approvalStatus' = 'Draft'
            and new.document ->> 'approvalStatus' = 'Approved'
            and (to_jsonb(old) - 'document') = (to_jsonb(new) - 'document')
            and old.document - 'approvalStatus' - 'approvedBy' - 'approvedAtUtc'
                = new.document - 'approvalStatus' - 'approvedBy' - 'approvedAtUtc'
            and length(btrim(new.document ->> 'approvedBy')) > 0
            and new.document ->> 'approvedAtUtc' is not null then
            return new;
        end if;
    end if;
    raise exception 'Private-capital authority is immutable; retain a new revision or governed correction';
end
$function$;

drop trigger if exists protect_private_capital_commitment on __SCHEMA__.private_capital_commitments;
create trigger protect_private_capital_commitment before update or delete on __SCHEMA__.private_capital_commitments
    for each row execute function __SCHEMA__.protect_private_capital_authority();
drop trigger if exists protect_private_capital_call on __SCHEMA__.private_capital_calls;
create trigger protect_private_capital_call before update or delete on __SCHEMA__.private_capital_calls
    for each row execute function __SCHEMA__.protect_private_capital_authority();
drop trigger if exists protect_private_capital_posting on __SCHEMA__.private_capital_postings;
create trigger protect_private_capital_posting before update or delete on __SCHEMA__.private_capital_postings
    for each row execute function __SCHEMA__.protect_private_capital_authority();
