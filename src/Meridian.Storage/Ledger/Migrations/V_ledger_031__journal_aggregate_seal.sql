-- Closes the two aggregate-integrity gaps left by V_ledger_030. That migration made retained
-- entry and leg rows immutable after they existed and checked balance after a leg insert, but it
-- did not distinguish the initial child-row append from a later INSERT against an already-posted
-- entry. It also had no event on journal_entries from which to reject an entry that committed
-- without any legs.
--
-- A same-transaction open marker admits the initial leg set, then a deferred constraint trigger
-- validates the complete aggregate, creates its durable seal, and removes that marker. Existing
-- valid entries are sealed during migration and never receive an open marker. Any later leg INSERT
-- therefore fails closed before it can extend the accounting aggregate, even from an old MVCC
-- snapshot. Seal rows are append-only. Corrections remain reversal/rebook entries; no client can
-- change the child set of a posted JournalEntry.

create table if not exists __SCHEMA__.journal_entry_integrity_seals (
    journal_entry_id uuid primary key
        references __SCHEMA__.journal_entries(journal_entry_id),
    leg_count integer not null,
    sealed_at timestamptz not null default now(),
    constraint ck_journal_entry_integrity_seals_leg_count
        check (leg_count >= 2)
);

-- A short-lived marker proves that a child insert belongs to the same transaction that created
-- its parent. The marker is written by the parent AFTER trigger and removed by the deferred seal
-- trigger. Requiring positive same-transaction proof (rather than treating a missing seal as open)
-- keeps the guard fail-closed under REPEATABLE READ/SERIALIZABLE snapshots that cannot see a seal
-- committed after their snapshot began.
create table if not exists __SCHEMA__.journal_entry_open_postings (
    journal_entry_id uuid primary key
        references __SCHEMA__.journal_entries(journal_entry_id),
    opening_xid xid8 not null
);

-- The migration runner's advisory lock serializes only other migration runners. Hold both journal
-- tables against INSERT/UPDATE/DELETE from this point through validation, backfill, and trigger
-- installation so an application append cannot commit between the backfill snapshot and the new
-- parent trigger becoming active. Readers remain available during the upgrade.
lock table __SCHEMA__.journal_entries, __SCHEMA__.journal_legs
    in share row exclusive mode;

-- Fail the upgrade rather than certifying pre-existing corruption. V_ledger_030 already rejects
-- unbalanced future leg sets, but a zero-leg entry never fired its leg trigger and could therefore
-- have reached a database upgraded from that version.
do $migration$
begin
    if exists (
        select 1
        from __SCHEMA__.journal_entries je
        left join __SCHEMA__.journal_legs jl
            on jl.journal_entry_id = je.journal_entry_id
        group by je.journal_entry_id
        having count(jl.entry_id) < 2
            or coalesce(sum(jl.debit), 0) <> coalesce(sum(jl.credit), 0)
    ) then
        raise exception
            'cannot seal retained journal: an entry is empty, incomplete, or unbalanced'
            using errcode = '23514';
    end if;
end
$migration$;

insert into __SCHEMA__.journal_entry_integrity_seals (journal_entry_id, leg_count)
select je.journal_entry_id, count(jl.entry_id)::integer
from __SCHEMA__.journal_entries je
join __SCHEMA__.journal_legs jl
    on jl.journal_entry_id = je.journal_entry_id
group by je.journal_entry_id
on conflict (journal_entry_id) do nothing;

-- Parent creation and every child insert serialize on one transaction-scoped key. The key exists
-- independently of row visibility, which closes the foreign-key wait race: a child INSERT started
-- while the parent transaction is uncommitted blocks here; after the parent commits its deferred
-- seal and releases the key, the child trigger takes a fresh snapshot and observes the seal before
-- the row can reach the foreign-key check. The schema is part of the key so isolated ledger schemas
-- do not contend with one another.
create or replace function __SCHEMA__.acquire_journal_entry_integrity_lock(
    schema_name text,
    journal_entry_id uuid)
returns void
language plpgsql
volatile
as $function$
begin
    perform pg_advisory_xact_lock(
        hashtextextended(
            'meridian.journal-entry-integrity:' || schema_name || ':' || journal_entry_id::text,
            0));
end
$function$;

create or replace function __SCHEMA__.lock_new_journal_entry_integrity()
returns trigger
language plpgsql
as $function$
begin
    perform __SCHEMA__.acquire_journal_entry_integrity_lock(
        tg_table_schema,
        new.journal_entry_id);
    return new;
end
$function$;

drop trigger if exists trg_journal_entries_integrity_lock
    on __SCHEMA__.journal_entries;
create trigger trg_journal_entries_integrity_lock
    before insert on __SCHEMA__.journal_entries
    for each row execute function __SCHEMA__.lock_new_journal_entry_integrity();

create or replace function __SCHEMA__.mark_new_journal_entry_open()
returns trigger
language plpgsql
as $function$
begin
    insert into __SCHEMA__.journal_entry_open_postings (journal_entry_id, opening_xid)
    values (new.journal_entry_id, pg_current_xact_id());
    return new;
end
$function$;

drop trigger if exists trg_journal_entries_mark_open
    on __SCHEMA__.journal_entries;
create trigger trg_journal_entries_mark_open
    after insert on __SCHEMA__.journal_entries
    for each row execute function __SCHEMA__.mark_new_journal_entry_open();

-- New entries are sealed only at the transaction's deferred-constraint boundary, after the normal
-- parent-then-legs append has completed. Checking both cardinality and balance here makes a zero-leg
-- entry fail at commit even though no journal_legs trigger ever ran.
create or replace function __SCHEMA__.seal_complete_journal_entry()
returns trigger
language plpgsql
as $function$
declare
    retained_leg_count integer;
    sealed_leg_count integer;
    total_debit numeric;
    total_credit numeric;
begin
    -- Re-entrant for the normal parent INSERT transaction, and defensive if this function is ever
    -- invoked after trigger recreation. The transaction retains the same entry lock through seal
    -- creation and commit.
    perform __SCHEMA__.acquire_journal_entry_integrity_lock(
        tg_table_schema,
        new.journal_entry_id);

    select count(*)::integer,
           coalesce(sum(debit), 0),
           coalesce(sum(credit), 0)
    into retained_leg_count, total_debit, total_credit
    from __SCHEMA__.journal_legs
    where journal_entry_id = new.journal_entry_id;

    if retained_leg_count < 2 then
        raise exception
            'journal entry % must contain at least two legs; retained %',
            new.journal_entry_id, retained_leg_count
            using errcode = '23514';
    end if;

    if total_debit <> total_credit then
        raise exception
            'journal entry % does not balance: debits % <> credits %',
            new.journal_entry_id, total_debit, total_credit
            using errcode = '23514';
    end if;

    insert into __SCHEMA__.journal_entry_integrity_seals (journal_entry_id, leg_count)
    values (new.journal_entry_id, retained_leg_count)
    on conflict (journal_entry_id) do nothing;

    select leg_count
    into sealed_leg_count
    from __SCHEMA__.journal_entry_integrity_seals
    where journal_entry_id = new.journal_entry_id;

    if sealed_leg_count <> retained_leg_count then
        raise exception
            'journal entry % seal count % does not match retained leg count %',
            new.journal_entry_id, sealed_leg_count, retained_leg_count
            using errcode = '23514';
    end if;

    delete from __SCHEMA__.journal_entry_open_postings
    where journal_entry_id = new.journal_entry_id;

    return null;
end
$function$;

drop trigger if exists ctrg_journal_entry_complete
    on __SCHEMA__.journal_entries;
create constraint trigger ctrg_journal_entry_complete
    after insert on __SCHEMA__.journal_entries
    deferrable initially deferred
    for each row execute function __SCHEMA__.seal_complete_journal_entry();

-- A seal is the durable proof that the initial aggregate append completed. It is intentionally
-- checked by a non-deferred BEFORE trigger: any subsequent child insert fails immediately, even
-- when the proposed extra legs would remain balanced as a set.
create or replace function __SCHEMA__.reject_sealed_journal_leg_insert()
returns trigger
language plpgsql
as $function$
begin
    perform __SCHEMA__.acquire_journal_entry_integrity_lock(
        tg_table_schema,
        new.journal_entry_id);

    if not exists (
        select 1
        from __SCHEMA__.journal_entry_open_postings
        where journal_entry_id = new.journal_entry_id
          and opening_xid = pg_current_xact_id()
    ) then
        raise exception
            'journal entry % is not open in this posting transaction; journal_legs is immutable and append-only',
            new.journal_entry_id
            using errcode = '55000';
    end if;

    return new;
end
$function$;

drop trigger if exists trg_journal_legs_reject_sealed_insert
    on __SCHEMA__.journal_legs;
create trigger trg_journal_legs_reject_sealed_insert
    before insert on __SCHEMA__.journal_legs
    for each row execute function __SCHEMA__.reject_sealed_journal_leg_insert();

create or replace function __SCHEMA__.reject_journal_integrity_seal_mutation()
returns trigger
language plpgsql
as $function$
begin
    raise exception '% is immutable and append-only', tg_table_name
        using errcode = '55000';
end
$function$;

drop trigger if exists trg_journal_integrity_seals_immutable
    on __SCHEMA__.journal_entry_integrity_seals;
create trigger trg_journal_integrity_seals_immutable
    before update or delete on __SCHEMA__.journal_entry_integrity_seals
    for each row execute function __SCHEMA__.reject_journal_integrity_seal_mutation();

drop trigger if exists trg_journal_integrity_seals_truncate_guard
    on __SCHEMA__.journal_entry_integrity_seals;
create trigger trg_journal_integrity_seals_truncate_guard
    before truncate on __SCHEMA__.journal_entry_integrity_seals
    for each statement execute function __SCHEMA__.reject_journal_integrity_seal_mutation();

-- Only the nested parent/seal triggers may create or remove an open marker. A direct client write
-- reaches this guard at depth one and is rejected; updates and truncation are never legitimate.
create or replace function __SCHEMA__.guard_journal_open_posting_mutation()
returns trigger
language plpgsql
as $function$
begin
    if tg_op = 'INSERT'
        and pg_trigger_depth() > 1
        and new.opening_xid = pg_current_xact_id() then
        return new;
    end if;

    if tg_op = 'DELETE'
        and pg_trigger_depth() > 1
        and old.opening_xid = pg_current_xact_id() then
        return old;
    end if;

    raise exception '% is internal posting state and cannot be mutated directly', tg_table_name
        using errcode = '55000';
end
$function$;

drop trigger if exists trg_journal_open_postings_guard
    on __SCHEMA__.journal_entry_open_postings;
create trigger trg_journal_open_postings_guard
    before insert or update or delete on __SCHEMA__.journal_entry_open_postings
    for each row execute function __SCHEMA__.guard_journal_open_posting_mutation();

drop trigger if exists trg_journal_open_postings_truncate_guard
    on __SCHEMA__.journal_entry_open_postings;
create trigger trg_journal_open_postings_truncate_guard
    before truncate on __SCHEMA__.journal_entry_open_postings
    for each statement execute function __SCHEMA__.guard_journal_open_posting_mutation();
