-- Forward correction of V037's blanket no-removal guard. An approved atomic amortization
-- reversal can restore the exact retained absence of a prior adjustment; ordinary removals
-- remain forbidden. Earlier migrations and immutable mutation snapshots are unchanged.
create or replace function __SCHEMA__.protect_open_lot_basis_adjustment()
returns trigger language plpgsql as $function$
begin
    if old.basis_adjustment is not null and new.basis_adjustment is null and not exists (
        select 1
        from __SCHEMA__.atomic_tax_lot_posting_batches reversal
        join __SCHEMA__.tax_lot_mutations original
            on original.mutation_batch_id = reversal.corrects_mutation_batch_id
        join __SCHEMA__.atomic_tax_lot_posting_batches original_batch
            on original_batch.mutation_batch_id = original.mutation_batch_id
        join __SCHEMA__.journal_entries reversal_journal
            on reversal_journal.journal_entry_id = reversal.journal_entry_id
        join __SCHEMA__.journal_entries original_journal
            on original_journal.journal_entry_id = original_batch.journal_entry_id
        where reversal.mutation_batch_id = new.last_mutation_batch_id
            and reversal.mutation_kind = 'Amortization'
            and reversal.corrects_mutation_batch_id = old.last_mutation_batch_id
            and reversal.ledger_book_id = old.ledger_book_id
            and reversal.security_id = old.security_id
            and reversal.book_position_id = old.book_position_id
            and original.mutation_kind = 'Amortization'
            and original.tax_lot_record_id = old.tax_lot_record_id
            and original.result_version = old.version
            and original.quantity_delta = 0 and original.quantity_after = old.open_quantity
            and new.open_quantity = old.open_quantity
            and old.basis_adjustment ->> 'Reason' = 'Amortization'
            and old.basis_adjustment ->> 'MutationBatchId' = old.last_mutation_batch_id::text
            -- The column uses the historical default serializer (PascalCase, numeric enums),
            -- while immutable snapshots use web JSON (camelCase, string enums). Bind their
            -- exact financial/lineage facts explicitly; the atomic correction validator also
            -- compares the complete typed lot, instruction, security and evidence before insert.
            and jsonb_build_object(
                'mutationBatchId', old.basis_adjustment -> 'MutationBatchId',
                'reason', old.basis_adjustment -> 'Reason',
                'openQuantity', old.basis_adjustment -> 'OpenQuantity',
                'transactionCostBasis', old.basis_adjustment -> 'TransactionCostBasis',
                'functionalCostBasis', old.basis_adjustment -> 'FunctionalCostBasis')
                = (original.lot_snapshot_after -> 'basisAdjustment') - 'amortization' - 'corporateAction'
            and original.lot_snapshot_before is not null
            and (original.lot_snapshot_before -> 'basisAdjustment' is null
                or original.lot_snapshot_before -> 'basisAdjustment' = 'null'::jsonb)
            and reversal_journal.posting_kind = 'Adjustment'
            and reversal_journal.source_journal_entry_id = original_batch.journal_entry_id
            and reversal_journal.metadata ->> 'effectiveDate' = original_journal.metadata ->> 'effectiveDate'
            and reversal_journal.adjustment_approval_metadata ->> 'status' = 'Approved'
            and length(trim(reversal_journal.adjustment_approval_metadata ->> 'approvalId')) > 0
            and length(trim(reversal_journal.adjustment_approval_metadata ->> 'approvedBy')) > 0
            and length(trim(reversal_journal.adjustment_approval_metadata ->> 'reasonCode')) > 0
            and reversal_journal.adjustment_approval_metadata ->> 'approvedAt' is not null
    ) then
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
