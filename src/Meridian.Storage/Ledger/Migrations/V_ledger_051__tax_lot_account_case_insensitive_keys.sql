-- Migration: ledger / tax_lot_account_case_insensitive_keys
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- LedgerAccount compares FinancialAccountId with OrdinalIgnoreCase, so two lots whose account ids
-- differ only by casing are one logical account in C#. The V_ledger_009 unique indexes compared
-- coalesce(financial_account_id, '') byte-for-byte, so the same lot label could be retained twice
-- under one logical account and lookups by the "same" account could miss rows. Storage now agrees
-- with the domain: uniqueness and the supporting lookup indexes key on
-- lower(coalesce(financial_account_id, '')). Stored values keep their original casing; no row is
-- rewritten and no table is emptied.
--
-- Fail loudly first. Rows that already collide under the case-insensitive key must be resolved by
-- an operator before this migration proceeds; a silent index rebuild would simply fail with a less
-- actionable duplicate-key error.
do $$
declare
    lot_groups bigint;
    lot_rows bigint;
    policy_groups bigint;
    policy_rows bigint;
begin
    select count(*), coalesce(sum(group_rows), 0)
      into lot_groups, lot_rows
      from (
          select count(*) as group_rows
            from __SCHEMA__.tax_lots
           group by ledger_book_id,
                    account_name,
                    account_type,
                    coalesce(symbol, ''),
                    lower(coalesce(financial_account_id, '')),
                    lot_id
          having count(*) > 1) as lot_conflicts;
    if lot_groups > 0 then
        raise exception
            'V_ledger_051: % tax_lots lot-label group(s) spanning % row(s) collide once financial_account_id is compared case-insensitively (ledger_book_id, account_name, account_type, symbol, lower(financial_account_id), lot_id). Resolve the duplicate lot labels, then rerun the migration.',
            lot_groups, lot_rows
            using errcode = 'unique_violation';
    end if;

    select count(*), coalesce(sum(group_rows), 0)
      into policy_groups, policy_rows
      from (
          select count(*) as group_rows
            from __SCHEMA__.tax_lot_policies
           group by ledger_book_id,
                    account_name,
                    account_type,
                    coalesce(symbol, ''),
                    lower(coalesce(financial_account_id, '')),
                    effective_date
          having count(*) > 1) as policy_conflicts;
    if policy_groups > 0 then
        raise exception
            'V_ledger_051: % tax_lot_policies effective-date group(s) spanning % row(s) collide once financial_account_id is compared case-insensitively (ledger_book_id, account_name, account_type, symbol, lower(financial_account_id), effective_date). Resolve the duplicate policies, then rerun the migration.',
            policy_groups, policy_rows
            using errcode = 'unique_violation';
    end if;
end
$$;

-- Uniqueness boundary for lot labels within one logical account.
drop index if exists __SCHEMA__.ux_tax_lots_book_account_lot;
create unique index ux_tax_lots_book_account_lot
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        lower(coalesce(financial_account_id, '')),
        lot_id);

-- Open-lot lookup support for the same logical account key.
drop index if exists __SCHEMA__.ix_tax_lots_book_account_open;
create index ix_tax_lots_book_account_open
    on __SCHEMA__.tax_lots (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        lower(coalesce(financial_account_id, '')),
        acquired_date,
        lot_id)
    where open_quantity > 0;

-- Uniqueness boundary for one effective-dated relief policy per logical account.
drop index if exists __SCHEMA__.ux_tax_lot_policies_book_account_effective;
create unique index ux_tax_lot_policies_book_account_effective
    on __SCHEMA__.tax_lot_policies (
        ledger_book_id,
        account_name,
        account_type,
        coalesce(symbol, ''),
        lower(coalesce(financial_account_id, '')),
        effective_date);
