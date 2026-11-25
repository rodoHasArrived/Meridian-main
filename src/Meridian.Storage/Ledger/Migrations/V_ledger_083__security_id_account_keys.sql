-- Migration: ledger / security_id_account_keys
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Security Master architecture review F2 (owner decision 2026-11-25): per-security ledger accounts
-- (Securities, Short Securities Payable, Dividend Receivable, Accrued Interest Receivable, Corporate
-- Action Distribution) are keyed by Security Master SecurityId, with the ticker kept in `symbol` as a
-- display attribute, so a ticker rename keeps one account history.
--
-- Every table that stores a ledger account identity (account_name, account_type, symbol,
-- financial_account_id) gains a nullable account_security_id. It is named apart from the existing
-- lot-level tax_lots.security_id: that column is the lot's instrument, which legacy ticker-keyed
-- accounts also carry, so it cannot tell a SecurityId-keyed account from a legacy one.
--
-- The account instrument key is coalesce(account_security_id::text, symbol, ''): a SecurityId-keyed
-- account matches on its SecurityId whatever its display ticker, and a legacy account (null
-- account_security_id) keeps matching on its ticker. Existing rows are not rewritten: existing
-- journals keep their keys. Moving legacy balances is a governed reclass, never a data migration.

alter table __SCHEMA__.journal_legs
    add column if not exists account_security_id uuid null;

alter table __SCHEMA__.tax_lot_policies
    add column if not exists account_security_id uuid null;

alter table __SCHEMA__.tax_lots
    add column if not exists account_security_id uuid null;

alter table __SCHEMA__.ledger_balance_checkpoint_rows
    add column if not exists account_security_id uuid null;

alter table __SCHEMA__.wash_sale_deferrals
    add column if not exists disposal_account_security_id uuid null;

do $$
begin
    if not exists (
        select 1 from pg_constraint
        where conname = 'ck_journal_legs_account_security_id'
          and conrelid = '__SCHEMA__.journal_legs'::regclass) then
        alter table __SCHEMA__.journal_legs
            add constraint ck_journal_legs_account_security_id
            check (account_security_id is null or account_security_id <> '00000000-0000-0000-0000-000000000000'::uuid);
    end if;
end
$$;

comment on column __SCHEMA__.journal_legs.account_security_id is
    'Security Master identity keying a per-security account. When present, symbol is display-only and does not participate in account identity.';
comment on column __SCHEMA__.tax_lots.account_security_id is
    'Account key of the lot''s owning per-security account (distinct from the lot instrument security_id).';
comment on column __SCHEMA__.tax_lot_policies.account_security_id is
    'Account key of the per-security account this relief policy governs.';
comment on column __SCHEMA__.ledger_balance_checkpoint_rows.account_security_id is
    'Account key of a per-security checkpoint row; symbol is the retained display ticker.';
comment on column __SCHEMA__.wash_sale_deferrals.disposal_account_security_id is
    'Account key of the disposing per-security account.';

-- Uniqueness boundary for lot labels within one logical account, now keyed by instrument key.
drop index if exists __SCHEMA__.ux_tax_lots_book_account_lot;
create unique index ux_tax_lots_book_account_instrument_lot
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(account_security_id::text, symbol, ''),
        lower(coalesce(financial_account_id, '')),
        lot_id);

drop index if exists __SCHEMA__.ix_tax_lots_book_account_open;
create index ix_tax_lots_book_account_instrument_open
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(account_security_id::text, symbol, ''),
        lower(coalesce(financial_account_id, '')),
        acquired_date,
        lot_id)
    where open_quantity > 0;

drop index if exists __SCHEMA__.ux_tax_lot_policies_book_account_effective;
create unique index ux_tax_lot_policies_book_account_instrument_effective
    on __SCHEMA__.tax_lot_policies (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(account_security_id::text, symbol, ''),
        lower(coalesce(financial_account_id, '')),
        effective_date);

-- Supports the mapping report's scan for SecurityId-keyed legs.
create index if not exists ix_journal_legs_account_security
    on __SCHEMA__.journal_legs (account_security_id)
    where account_security_id is not null;
