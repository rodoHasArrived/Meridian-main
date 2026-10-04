-- Partial W10-LOT-002: basis-only amortization uses the existing atomic journal/lot boundary.
-- No acquisition facts or historical lots are backfilled by this migration.
alter table __SCHEMA__.atomic_tax_lot_posting_batches
    drop constraint if exists ck_atomic_tax_lot_batch_kind;
alter table __SCHEMA__.atomic_tax_lot_posting_batches
    add constraint ck_atomic_tax_lot_batch_kind
        check (mutation_kind in ('Acquisition', 'Disposal', 'Amortization'));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_kind;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'BasisRedistribution', 'Amortization'));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_quantities;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_quantities check (
    quantity_before >= 0 and quantity_after >= 0
    and quantity_after = quantity_before + quantity_delta
    and ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0)
        or (mutation_kind = 'Disposal' and quantity_delta < 0)
        or (mutation_kind = 'BasisRedistribution' and quantity_delta = 0 and quantity_before > 0
            and lower(coalesce(relief_method, '')) = 'averagecost')
        or (mutation_kind = 'Amortization' and quantity_delta = 0 and quantity_before > 0
            and relief_method is null and policy_revision is null)));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (
    unit_cost >= 0 and cost_basis >= 0
    and (cost_basis = abs(quantity_delta) * unit_cost
        or lower(coalesce(relief_method, '')) = 'averagecost'
        or (mutation_kind = 'Amortization' and cost_basis > 0)));
