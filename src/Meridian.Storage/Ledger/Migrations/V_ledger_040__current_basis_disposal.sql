-- W10-LOT-002 partial delivery. Ordinal 039 is reserved for PR #3048 amortization;
-- preserve its cost convention when this migration is applied after that slice.
-- Acquisition unit cost stays immutable. A canonical discrete disposal can relieve a
-- different governed current basis, retained in the exact pre-relief lot snapshot.
alter table __SCHEMA__.tax_lot_mutations
    drop constraint if exists ck_tax_lot_mutations_cost;
alter table __SCHEMA__.tax_lot_mutations
    add constraint ck_tax_lot_mutations_cost check (
        unit_cost >= 0 and cost_basis >= 0 and
        (cost_basis = abs(quantity_delta) * unit_cost
         or lower(coalesce(relief_method, '')) = 'averagecost'
         or (mutation_kind = 'Amortization' and cost_basis > 0)
         or (mutation_kind = 'Disposal'
             and lower(coalesce(relief_method, '')) in ('fifo', 'lifo', 'hifo', 'specificid')
             and coalesce(jsonb_typeof(lot_snapshot_before -> 'acquisition') = 'object', false)
             and coalesce(jsonb_typeof(lot_snapshot_before -> 'basisAdjustment') = 'object', false))));
