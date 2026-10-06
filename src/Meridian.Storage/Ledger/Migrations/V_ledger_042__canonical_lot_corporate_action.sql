-- W10-LOT-002: full predecessor closures and carried-basis successors commit with the
-- approved corporate-action journal. Existing acquisition facts and history are unchanged.
alter table __SCHEMA__.atomic_tax_lot_posting_batches drop constraint if exists ck_atomic_tax_lot_batch_kind;
alter table __SCHEMA__.atomic_tax_lot_posting_batches add constraint ck_atomic_tax_lot_batch_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'Amortization', 'CorporateAction'));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_kind;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_kind
    check (mutation_kind in ('Acquisition', 'Disposal', 'BasisRedistribution', 'Amortization',
        'CorporateActionClose', 'CorporateActionSuccessor'));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_quantities;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_quantities check (
    quantity_before >= 0 and quantity_after >= 0 and quantity_after = quantity_before + quantity_delta
    and ((mutation_kind = 'Acquisition' and quantity_before = 0 and quantity_delta > 0)
        or (mutation_kind = 'Disposal' and quantity_delta < 0)
        or (mutation_kind = 'BasisRedistribution' and quantity_delta = 0 and quantity_before > 0
            and lower(coalesce(relief_method, '')) = 'averagecost')
        or (mutation_kind = 'Amortization' and quantity_delta = 0 and quantity_before > 0
            and relief_method is null and policy_revision is null)
        or (mutation_kind = 'CorporateActionClose' and quantity_before > 0 and quantity_after = 0
            and relief_method is null and policy_revision is null and corrects_mutation_batch_id is null
            and expected_version > 0 and lot_snapshot_before is not null)
        or (mutation_kind = 'CorporateActionSuccessor' and quantity_before = 0 and quantity_delta > 0
            and relief_method is null and policy_revision is null and corrects_mutation_batch_id is null
            and expected_version = 0 and lot_snapshot_before is null)));

alter table __SCHEMA__.tax_lot_mutations drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations add constraint ck_tax_lot_mutations_cost check (
    unit_cost >= 0 and cost_basis >= 0 and
    (cost_basis = abs(quantity_delta) * unit_cost
     or lower(coalesce(relief_method, '')) = 'averagecost'
     or (mutation_kind = 'Amortization' and cost_basis > 0)
     or (mutation_kind = 'Disposal'
         and lower(coalesce(relief_method, '')) in ('fifo', 'lifo', 'hifo', 'specificid')
         and coalesce(jsonb_typeof(lot_snapshot_before -> 'acquisition') = 'object', false)
         and coalesce(jsonb_typeof(lot_snapshot_before -> 'basisAdjustment') = 'object', false))
     or (mutation_kind in ('CorporateActionClose', 'CorporateActionSuccessor') and cost_basis > 0
         and coalesce(jsonb_typeof(lot_snapshot_after -> 'acquisition') = 'object', false))));
