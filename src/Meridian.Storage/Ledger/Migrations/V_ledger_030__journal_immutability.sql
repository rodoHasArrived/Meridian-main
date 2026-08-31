-- Makes the retained journal immutable at the database, the way V_ledger_027 already protects the
-- tax-lot audit tables. The journal is the system of record the books balance on, and until now
-- nothing at the database stopped an UPDATE or DELETE against journal_entries/journal_legs, the
-- legs' foreign key cascaded entry deletion into silent leg deletion, and per-entry
-- debits = credits was enforced only in application code. Corrections stay append-only: a posted
-- entry is reversed and rebooked, never edited (see LedgerJournalReversal).
--
-- One governed mutation survives, because one legitimate runtime writer exists. The currency
-- backfill (V_ledger_029 at migration time, PostgresLedgerCurrencyBackfill at runtime) completes
-- the currency detail V_ledger_026 left null on historical legs. That repair only ever stamps the
-- identity translation onto a currency-blind leg and never touches an amount the books balance on,
-- so the leg trigger admits exactly that shape -- currency columns filled on a leg whose currency
-- was null, every other column byte-identical -- and only inside a transaction that has declared
-- itself via the transaction-scoped meridian.ledger_currency_repair setting. Everything else, on
-- both tables, is rejected outright.

-- 1. The delete-cascade footgun. Deleting an entry can no longer silently take its legs with it;
-- deletes are rejected by the trigger below anyway, but the plain foreign key removes the cascade
-- so even a session that disables triggers cannot vaporize legs as a side effect of an entry
-- delete. The original constraint was unnamed in V_ledger_001, so it is located by shape rather
-- than by name.
do $migration$
declare
    cascade_fk text;
begin
    for cascade_fk in
        select conname
        from pg_constraint
        where conrelid = '__SCHEMA__.journal_legs'::regclass
          and confrelid = '__SCHEMA__.journal_entries'::regclass
          and contype = 'f'
          and confdeltype = 'c'
    loop
        execute format('alter table __SCHEMA__.journal_legs drop constraint %I', cascade_fk);
    end loop;

    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.journal_legs'::regclass
          and conname = 'fk_journal_legs_journal_entry') then
        alter table __SCHEMA__.journal_legs
            add constraint fk_journal_legs_journal_entry
                foreign key (journal_entry_id)
                references __SCHEMA__.journal_entries(journal_entry_id);
    end if;
end
$migration$;

-- 2. Entry immutability. No governed path mutates journal_entries at all, so the rejection is
-- unconditional -- same mechanism and message as V_ledger_027's tax-lot audit guard.
create or replace function __SCHEMA__.reject_journal_entry_mutation()
returns trigger
language plpgsql
as $function$
begin
    raise exception '% is immutable and append-only', tg_table_name
        using errcode = '55000';
end
$function$;

drop trigger if exists trg_journal_entries_immutable
    on __SCHEMA__.journal_entries;
create trigger trg_journal_entries_immutable
    before update or delete on __SCHEMA__.journal_entries
    for each row execute function __SCHEMA__.reject_journal_entry_mutation();

-- TRUNCATE bypasses row triggers, so immutability that ignored it would be hollow.
drop trigger if exists trg_journal_entries_truncate_guard
    on __SCHEMA__.journal_entries;
create trigger trg_journal_entries_truncate_guard
    before truncate on __SCHEMA__.journal_entries
    for each statement execute function __SCHEMA__.reject_journal_entry_mutation();

-- 3. Leg immutability, with the one governed exception described above. The jsonb comparison
-- strips only the five currency-detail columns, so any drift in any other column -- including
-- columns added by future migrations -- fails the equality and is rejected. The repair itself is
-- recognized by its full shape: the leg was currency-blind, the fill is the identity translation
-- (transaction currency equal to functional, transaction amounts equal to the functional amounts,
-- FX rate 1), and the writer declared itself for this transaction via
-- set_config('meridian.ledger_currency_repair', 'on', true).
create or replace function __SCHEMA__.reject_journal_leg_mutation()
returns trigger
language plpgsql
as $function$
declare
    currency_detail_columns constant text[] := array[
        'transaction_currency',
        'functional_currency',
        'transaction_debit',
        'transaction_credit',
        'fx_rate_to_functional'];
begin
    if tg_op = 'UPDATE' then
        if current_setting('meridian.ledger_currency_repair', true) = 'on'
            and old.transaction_currency is null
            and new.transaction_currency is not null
            and new.functional_currency = new.transaction_currency
            and new.transaction_debit = new.debit
            and new.transaction_credit = new.credit
            and new.fx_rate_to_functional = 1
            and (to_jsonb(new) - currency_detail_columns)
                = (to_jsonb(old) - currency_detail_columns) then
            return new;
        end if;
    end if;

    raise exception '% is immutable and append-only', tg_table_name
        using errcode = '55000';
end
$function$;

drop trigger if exists trg_journal_legs_immutable
    on __SCHEMA__.journal_legs;
create trigger trg_journal_legs_immutable
    before update or delete on __SCHEMA__.journal_legs
    for each row execute function __SCHEMA__.reject_journal_leg_mutation();

drop trigger if exists trg_journal_legs_truncate_guard
    on __SCHEMA__.journal_legs;
create trigger trg_journal_legs_truncate_guard
    before truncate on __SCHEMA__.journal_legs
    for each statement execute function __SCHEMA__.reject_journal_leg_mutation();

-- 4. Entry-level debits = credits, enforced at commit. The guarded posting path inserts an entry
-- and all of its legs in one transaction, so a deferred constraint trigger sees the complete
-- entry; an entry whose legs do not sum to balance can no longer reach durable state no matter
-- which client wrote it. Existing rows are not re-validated: every retained entry already passed
-- the application-side balance check on its way in.
create or replace function __SCHEMA__.enforce_journal_entry_balance()
returns trigger
language plpgsql
as $function$
declare
    total_debit numeric;
    total_credit numeric;
begin
    select coalesce(sum(debit), 0), coalesce(sum(credit), 0)
    into total_debit, total_credit
    from __SCHEMA__.journal_legs
    where journal_entry_id = new.journal_entry_id;

    if total_debit <> total_credit then
        raise exception
            'journal entry % does not balance: debits % <> credits %',
            new.journal_entry_id, total_debit, total_credit
            using errcode = '23514';
    end if;

    return null;
end
$function$;

drop trigger if exists ctrg_journal_legs_entry_balanced
    on __SCHEMA__.journal_legs;
create constraint trigger ctrg_journal_legs_entry_balanced
    after insert on __SCHEMA__.journal_legs
    deferrable initially deferred
    for each row execute function __SCHEMA__.enforce_journal_entry_balance();
