-- Records the acquisition-time par conventions on the lot of record.
--
-- `Meridian.Contracts.SecurityMaster.FaceValueLot` is the canonical open lot for par-denominated
-- instruments: it makes explicit the quote basis the acquisition price was struck in (par_basis --
-- 100 for the bond price-per-100 convention, 1 for prices quoted per unit of face), the pool factor
-- the recorded face was booked at (booked_factor), and the face amount itself (original_face). Until
-- now none of those facts survived into `tax_lots`: the lot of record carried only unit-denominated
-- original_quantity/open_quantity/unit_cost, so "face" was recoverable only by assuming quantity x
-- 100 and a booked factor of 1. That assumption is precisely what the aggregate exists to kill.
-- A per-unit-priced lot mis-amortizes through math that assumes 100; and a pool bought when the
-- factor was already 0.80 has 800 of face booked against 1,000 at issuance, so presuming a booked
-- factor of 1 uses 800 where 1,000 is owed and understates every principal paydown by 20%.
--
-- All three columns are NULLABLE and constrained all-three-or-none. Legacy rows are NOT backfilled:
-- V_ledger_028 already records the convention that derived tax facts are not denormalized onto
-- tax_lots, and the wider rule is that legacy rows are backfilled only from retained evidence and
-- never receive synthetic defaults. A `not null default 100` par_basis would assert the very
-- price-per-100 convention that was never actually recorded for those lots. "The lot's face terms
-- were recorded" therefore stays a checkable fact, and consumers fail closed when it is absent
-- rather than silently inheriting a default.
--
-- The runner records applied scripts by checksum in `ledger_journal_schema_migrations` and skips
-- those that match, but this set runs under DriftPolicy.Reapply and a script whose checksum is
-- absent or changed is replayed against a schema that may already carry its objects. Every statement
-- here is therefore written to be re-runnable: `add column if not exists` for the columns, and
-- pg_constraint lookups guarding each `add constraint`.

alter table __SCHEMA__.tax_lots
    add column if not exists original_face numeric(38, 12) null,
    add column if not exists booked_factor numeric(38, 12) null,
    add column if not exists par_basis numeric(38, 12) null;

-- Mirrors the FaceValueLot constructor invariants so a lot that violates them cannot reach the
-- table even if it bypasses the typed validation in PostgresLedgerJournalStore.
do $migration$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass
          and conname = 'ck_tax_lots_face_terms') then
        alter table __SCHEMA__.tax_lots
            add constraint ck_tax_lots_face_terms check (
                (original_face is null or original_face > 0) and
                (booked_factor is null or (booked_factor > 0 and booked_factor <= 1)) and
                (par_basis is null or par_basis > 0));
    end if;
end
$migration$;

-- All three or none: a face amount without the basis it was priced against, or without the factor
-- it was booked at, is exactly the half-stated convention this migration exists to remove.
do $migration$
begin
    if not exists (
        select 1
        from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass
          and conname = 'ck_tax_lots_face_terms_complete') then
        alter table __SCHEMA__.tax_lots
            add constraint ck_tax_lots_face_terms_complete check (
                (original_face is null and booked_factor is null and par_basis is null) or
                (original_face is not null and booked_factor is not null and par_basis is not null));
    end if;
end
$migration$;
