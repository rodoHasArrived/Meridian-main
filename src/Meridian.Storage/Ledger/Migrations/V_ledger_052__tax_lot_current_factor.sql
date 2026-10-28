-- Migration: ledger / tax_lot_current_factor
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- Records the CURRENT paydown factor on the lot of record.
--
-- V_ledger_033 recorded the par conventions a face-value lot was booked at (original_face,
-- booked_factor, par_basis). Those are acquisition facts and never move. What did move -- the pool
-- factor as principal is paid down -- lived only in the position's economic state, so a persisted
-- lot was rehydrated at its booked factor and could not say what face it currently holds
-- (`FaceValueLot.CurrentFactor` fell back to `BookedFactor`). current_factor is where the lot now
-- states it.
--
-- Semantics. NULL means "still at the booked factor", which is every lot today. The column is
-- non-null only after a governed paydown mutation, and then it is below booked_factor (a lot booked
-- at 0.95 that pays down to 0.90 holds 0.90). It is NOT stamped at acquisition even when it would
-- equal booked_factor: the acquisition lot is hashed whole into the atomic fingerprint, so a value
-- written at acquisition would change the digest of every face lot already retained and turn an
-- idempotent replay into an identity collision.
--
-- Rules the database enforces, independently of the typed store:
--   * ck_tax_lots_current_factor: a current factor requires the acquisition and face terms and lies
--     in (0, booked_factor), the same range `FaceValueLot` accepts. How a pool that pays down to
--     nothing is represented is the paydown mutation's decision, not this column's. The
--     all-three-or-none face-terms rule is deliberately not widened to four: every existing face
--     lot stays valid.
--   * protect_open_lot_current_factor: a lot of record opens at its booked factor (no value on
--     insert); the factor can only decline, never be removed, and only through a versioned,
--     quantity-preserving mutation batch (version + 1, a new last_mutation_batch_id). The paydown
--     mutation that writes it replaces this function and tightens it to its own batch kind.
--
-- Legacy rows are not backfilled, by the same rule as V_ledger_033: derived facts are never written
-- as synthetic defaults.
--
-- The ledger set runs under DriftPolicy.Reapply, so every statement is re-runnable: `add column if
-- not exists`, a pg_constraint lookup before the constraint, `create or replace` for the function
-- and a pg_trigger lookup before the trigger.

alter table __SCHEMA__.tax_lots
    add column if not exists current_factor numeric(38, 12) null;

do $migration$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass
          and conname = 'ck_tax_lots_current_factor') then
        alter table __SCHEMA__.tax_lots
            add constraint ck_tax_lots_current_factor check (
                current_factor is null or (
                    acquisition_terms is not null and
                    original_face is not null and
                    booked_factor is not null and
                    par_basis is not null and
                    current_factor > 0 and
                    current_factor < booked_factor));
    end if;
end
$migration$;

create or replace function __SCHEMA__.protect_open_lot_current_factor()
returns trigger language plpgsql as $function$
begin
    if tg_op = 'INSERT' then
        if new.current_factor is not null then
            raise exception 'A lot of record opens at its booked factor; a paydown factor is applied only by a governed lot mutation';
        end if;
        return new;
    end if;
    if old.current_factor is not distinct from new.current_factor then
        return new;
    end if;
    if old.current_factor is not null and new.current_factor is null then
        raise exception 'A lot''s applied paydown factor cannot be removed';
    end if;
    if new.current_factor > coalesce(old.current_factor, old.booked_factor) then
        raise exception 'A lot''s current factor can only decline; an upward restatement requires governed correction lineage';
    end if;
    if new.version <> old.version + 1
        or new.last_mutation_batch_id is null
        or new.last_mutation_batch_id is not distinct from old.last_mutation_batch_id
        or new.open_quantity <> old.open_quantity then
        raise exception 'A lot''s current factor changes only through a versioned, quantity-preserving mutation batch';
    end if;
    return new;
end
$function$;

do $migration$
begin
    if not exists (
        select 1
        from pg_trigger
        where tgrelid = '__SCHEMA__.tax_lots'::regclass
          and tgname = 'protect_open_lot_current_factor') then
        create trigger protect_open_lot_current_factor
            before insert or update on __SCHEMA__.tax_lots
            for each row execute function __SCHEMA__.protect_open_lot_current_factor();
    end if;
end
$migration$;
