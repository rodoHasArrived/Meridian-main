-- Migration: ledger / atomic_lot_factor_paydown
-- Append-only: never rename or renumber applied migrations.
-- The migration runner owns the transaction.
--
-- The governed FactorPaydown lot mutation: every open face lot of one (security, book position)
-- scope restated to a lower paydown factor in one atomic journal/lot batch, its open basis relieved
-- pro rata to the principal repaid, the difference recognised as realized gain or loss.
--
-- V_ledger_052 added tax_lots.current_factor and deferred two decisions to this mutation: how a
-- pool that pays down to nothing is represented, and which batch kind may move the factor. Both are
-- decided here. A new factor of zero is a FULL paydown: the lot holds current_factor = 0 and is
-- closed (open_quantity = 0), so it drops out of open-lot and as-of reads after its paydown date
-- while its acquisition facts, basis adjustment and mutation receipt stay retained. A partial
-- paydown preserves quantity (quantity is booked face per 100 and is factor-invariant by design).
--
-- Statements, every one re-runnable under DriftPolicy.Reapply:
--   * atomic_tax_lot_posting_batches.factor_paydown_instruction (nullable jsonb) with the V050
--     exclusivity shape: present exactly on FactorPaydown batches.
--   * The four V050 checks are replaced by strict supersets admitting 'FactorPaydown'. The mutation
--     row keeps quantity (delta 0) or closes the lot (after 0), carries the before snapshot, and its
--     cost_basis is the functional basis relieved (>= 0: a lot already at zero basis relieves 0, so
--     the CapitalReturn '> 0' clause is not reused). The economic tie Cr asset = sum(cost_basis) is
--     enforced by the typed store.
--   * ck_tax_lots_current_factor is replaced with the '>= 0' form so a fully repaid lot can hold 0.
--   * protect_open_lot_current_factor is tightened by create or replace (the V052 trigger object is
--     kept): a factor change must come from a FactorPaydown batch of the same book/security/position
--     that restates the lot's basis in the same batch, a partial paydown preserves quantity, a full
--     paydown closes the lot, and a lot may open with a factor only as a corporate-action successor
--     inheriting its predecessor's paydown.
--
-- Every previously valid row stays valid; nothing is backfilled, dropped or rewritten. The batch
-- row is inserted before the lot compare-and-swap in the same transaction, so the trigger's join on
-- atomic_tax_lot_posting_batches resolves in-transaction (the V043 ordering).

alter table __SCHEMA__.atomic_tax_lot_posting_batches
    add column if not exists factor_paydown_instruction jsonb null;
alter table __SCHEMA__.atomic_tax_lot_posting_batches drop constraint if exists ck_atomic_tax_lot_factor_paydown_instruction;
alter table __SCHEMA__.atomic_tax_lot_posting_batches add constraint ck_atomic_tax_lot_factor_paydown_instruction
    check ((mutation_kind = 'FactorPaydown' and factor_paydown_instruction is not null
        and jsonb_typeof(factor_paydown_instruction) = 'object')
        or (mutation_kind <> 'FactorPaydown' and factor_paydown_instruction is null));

alter table __SCHEMA__.atomic_tax_lot_posting_batches drop constraint if exists ck_atomic_tax_lot_batch_kind;
alter table __SCHEMA__.atomic_tax_lot_posting_batches add constraint ck_atomic_tax_lot_batch_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'Amortization', 'CorporateAction', 'CapitalReturn', 'FactorPaydown'));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_kind;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'BasisRedistribution', 'Amortization', 'CorporateAction', 'CapitalReturn', 'FactorPaydown'));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_quantities;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_quantities check (
    quantity_before >= 0 and quantity_after >= 0 and quantity_after = quantity_before + quantity_delta
    and ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0)
        or (mutation_kind = 'Disposal' and quantity_delta < 0)
        or (mutation_kind = 'BasisRedistribution' and quantity_delta = 0 and quantity_before > 0
            and lower(coalesce(relief_method, '')) = 'averagecost')
        or (mutation_kind in ('Amortization', 'CapitalReturn') and quantity_delta = 0 and quantity_before > 0
            and relief_method is null and policy_revision is null)
        or (mutation_kind = 'FactorPaydown' and quantity_before > 0 and relief_method is null and policy_revision is null
            and lot_snapshot_before is not null and (quantity_delta = 0 or quantity_after = 0))
        or (mutation_kind = 'CorporateAction' and relief_method is null and policy_revision is null
            and ((quantity_before > 0 and quantity_after = 0 and lot_snapshot_before is not null)
                or (quantity_before = 0 and quantity_after > 0 and lot_snapshot_before is null)))));
alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (
    unit_cost >= 0 and cost_basis >= 0 and
    (cost_basis = abs(quantity_delta) * unit_cost
        or lower(coalesce(relief_method, '')) = 'averagecost'
        or (mutation_kind in ('Amortization', 'CorporateAction', 'CapitalReturn') and cost_basis > 0)
        or mutation_kind = 'FactorPaydown'
        or (mutation_kind = 'Disposal'
            and lower(coalesce(relief_method, '')) in ('fifo', 'lifo', 'hifo', 'specificid')
            and coalesce(jsonb_typeof(lot_snapshot_before -> 'acquisition') = 'object', false)
            and coalesce(jsonb_typeof(lot_snapshot_before -> 'basisAdjustment') = 'object', false))));

alter table __SCHEMA__.tax_lots drop constraint if exists ck_tax_lots_current_factor;
alter table __SCHEMA__.tax_lots add constraint ck_tax_lots_current_factor check (
    current_factor is null or (
        acquisition_terms is not null and
        original_face is not null and
        booked_factor is not null and
        par_basis is not null and
        current_factor >= 0 and
        current_factor < booked_factor));

create or replace function __SCHEMA__.protect_open_lot_current_factor()
returns trigger language plpgsql as $function$
begin
    if tg_op = 'INSERT' then
        if new.current_factor is not null and not exists (
            select 1
            from __SCHEMA__.atomic_tax_lot_posting_batches birth
            where birth.mutation_batch_id = new.originating_mutation_batch_id
              and birth.mutation_kind = 'CorporateAction'
              and birth.ledger_book_id = new.ledger_book_id) then
            raise exception 'A lot of record opens at its booked factor unless a corporate-action successor inherits its predecessor''s paydown factor';
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
        or new.last_mutation_batch_id is not distinct from old.last_mutation_batch_id then
        raise exception 'A lot''s current factor changes only through a versioned mutation batch';
    end if;
    if (new.current_factor > 0 and new.open_quantity <> old.open_quantity)
        or (new.current_factor = 0 and new.open_quantity <> 0) then
        raise exception 'A partial paydown preserves quantity; a full paydown (factor 0) closes the lot';
    end if;
    if not exists (
        select 1
        from __SCHEMA__.atomic_tax_lot_posting_batches paydown
        where paydown.mutation_batch_id = new.last_mutation_batch_id
          and paydown.mutation_kind = 'FactorPaydown'
          and paydown.ledger_book_id = old.ledger_book_id
          and paydown.security_id = old.security_id
          and paydown.book_position_id = old.book_position_id
          and paydown.factor_paydown_instruction is not null) then
        raise exception 'A lot''s current factor changes only through a FactorPaydown mutation batch';
    end if;
    -- basis_adjustment uses the historical default serializer (PascalCase keys).
    if new.basis_adjustment ->> 'Reason' is distinct from 'FactorPaydown'
        or new.basis_adjustment ->> 'MutationBatchId' is distinct from new.last_mutation_batch_id::text then
        raise exception 'A paydown restates the lot''s open basis in the same batch';
    end if;
    return new;
end
$function$;
