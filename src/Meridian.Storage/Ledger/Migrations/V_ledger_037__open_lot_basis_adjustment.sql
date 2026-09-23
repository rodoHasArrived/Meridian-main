-- Governed open-basis restatement (W10-LOT-002 average-cost relief). Additive: no lot receives
-- an adjustment from this migration, and acquisition facts stay immutable under V_ledger_034.
alter table __SCHEMA__.tax_lots
    add column if not exists basis_adjustment jsonb null;

do $migration$
begin
    if not exists (select 1 from pg_constraint
        where conrelid = '__SCHEMA__.tax_lots'::regclass and conname = 'ck_tax_lots_basis_adjustment') then
        alter table __SCHEMA__.tax_lots add constraint ck_tax_lots_basis_adjustment check (
            basis_adjustment is null or (
                jsonb_typeof(basis_adjustment) = 'object'
                and acquisition_terms is not null));
    end if;
end
$migration$;

-- A restatement is append-only lot state: it can be replaced only by a newer governed mutation
-- (a version increment stamped with a new batch) and can never be removed.
create or replace function __SCHEMA__.protect_open_lot_basis_adjustment()
returns trigger language plpgsql as $function$
begin
    if old.basis_adjustment is not null and new.basis_adjustment is null then
        raise exception 'Governed lot basis adjustments cannot be removed';
    end if;
    if old.basis_adjustment is distinct from new.basis_adjustment and (
        new.version <> old.version + 1
        or new.last_mutation_batch_id is null
        or new.last_mutation_batch_id is not distinct from old.last_mutation_batch_id) then
        raise exception 'Governed lot basis adjustments require a versioned mutation batch';
    end if;
    return new;
end
$function$;

do $migration$
begin
    if not exists (select 1 from pg_trigger
        where tgrelid = '__SCHEMA__.tax_lots'::regclass and tgname = 'protect_open_lot_basis_adjustment') then
        create trigger protect_open_lot_basis_adjustment before update on __SCHEMA__.tax_lots
            for each row execute function __SCHEMA__.protect_open_lot_basis_adjustment();
    end if;
end
$migration$;

-- Mutation rows gain the survivor restatement kind. Average-cost relief books the pooled basis,
-- which by definition differs from quantity times the lot's own acquisition unit cost.
alter table __SCHEMA__.tax_lot_mutations
    drop constraint if exists ck_tax_lot_mutations_kind;
alter table __SCHEMA__.tax_lot_mutations
    add constraint ck_tax_lot_mutations_kind
        check (mutation_kind in ('Acquisition', 'Disposal', 'BasisRedistribution'));

alter table __SCHEMA__.tax_lot_mutations
    drop constraint if exists ck_tax_lot_mutations_quantities;
alter table __SCHEMA__.tax_lot_mutations
    add constraint ck_tax_lot_mutations_quantities
        check (
            quantity_before >= 0 and
            quantity_after >= 0 and
            quantity_after = quantity_before + quantity_delta and
            ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0) or
             (mutation_kind = 'Disposal' and quantity_delta < 0) or
             (mutation_kind = 'BasisRedistribution' and quantity_delta = 0 and quantity_before > 0
                 and lower(coalesce(relief_method, '')) = 'averagecost')));

alter table __SCHEMA__.tax_lot_mutations
    drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations
    add constraint ck_tax_lot_mutations_cost
        check (
            unit_cost >= 0 and cost_basis >= 0 and
            (cost_basis = abs(quantity_delta) * unit_cost
             or lower(coalesce(relief_method, '')) = 'averagecost'));
