using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task LockSuccessorCarryBoundaryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        bool forSuccessorPosting, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // SHARE excludes deferral inserts through successor commit. ROW EXCLUSIVE lets ordinary
        // deferral writers coexist, while forcing them to recheck a completed successor first.
        var mode = forSuccessorPosting ? "share" : "row exclusive";
        command.CommandText = $"lock table {Qualified("wash_sale_deferrals")} in {mode} mode;";
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task RejectUnsupportedSuccessorCarryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Guid bookId, Guid lotId, DateOnly inheritedHoldingDate, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Sale date is not a retention boundary: later sales can already have retained a carry
        // onto this lot. Neither those rows nor ancestor rows may disappear at a new lot identity.
        command.CommandText = $"""
            select exists(select 1 from {Qualified("wash_sale_deferrals")}
                where ledger_book_id = @book and replacement_tax_lot_record_id = @lot
                  and holding_period_carry_date < @inherited_date);
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("lot", lotId);
        command.Parameters.AddWithValue("inherited_date", inheritedHoldingDate);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!)
            throw new LedgerValidationException("Successor posting cannot preserve a separately retained wash-sale holding-period carry; reviewed inherited carry lineage is required.");
    }

    private async Task RejectCarryAfterSuccessorPostingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        WashSaleDeferralRecord deferral, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select exists(
                select 1 from {Qualified("tax_lot_mutations")} mutation
                join {Qualified("atomic_tax_lot_posting_batches")} batch
                  on batch.mutation_batch_id = mutation.mutation_batch_id
                join {Qualified("tax_lots")} lot on lot.tax_lot_record_id = mutation.tax_lot_record_id
                where batch.ledger_book_id = @book and lot.ledger_book_id = @book
                  and mutation.tax_lot_record_id = @lot and mutation.mutation_kind = 'CorporateAction'
                  and mutation.quantity_delta < 0
                  and @carry < coalesce((lot.acquisition_terms ->> 'HoldingPeriodStartDate')::date,
                      (lot.acquisition_terms ->> 'holdingPeriodStartDate')::date, lot.acquired_date));
            """;
        command.Parameters.AddWithValue("book", deferral.LedgerBookId);
        command.Parameters.AddWithValue("lot", deferral.ReplacementTaxLotRecordId);
        command.Parameters.AddWithValue("carry", deferral.HoldingPeriodCarryDate);
        if ((bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!
            && !await IsExactRetainedSuccessorCarryAsync(connection, transaction, deferral, ct).ConfigureAwait(false))
            throw new LedgerValidationException("A wash-sale holding-period carry cannot be retained after its replacement lot became a successor predecessor; reviewed inherited carry lineage is required.");
    }

    private async Task<bool> IsExactRetainedSuccessorCarryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        WashSaleDeferralRecord deferral, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // Old receipts may predate the carry guard. Replaying all retained fields introduces no
        // new carry; sharing only the disposal/recipient key cannot authorize a changed payload.
        command.CommandText = $"""
            select exists(select 1 from {Qualified("wash_sale_deferrals")}
                where deferral_id = @id and ledger_book_id = @book and disposal_mutation_batch_id = @batch
                  and security_id = @security and sale_date = @sale_date
                  and disposal_account_name = @account_name and disposal_account_type = @account_type
                  and disposal_symbol is not distinct from @symbol
                  and disposal_financial_account_id is not distinct from @financial_account_id
                  and replacement_tax_lot_record_id = @lot and replacement_lot_id = @lot_name
                  and disallowed_amount = @amount and matched_quantity = @quantity
                  and holding_period_carry_date = @carry and policy_id = @policy
                  and window_days = @window and scope = @scope and recorded_at = @recorded_at);
            """;
        command.Parameters.AddWithValue("id", deferral.DeferralId);
        command.Parameters.AddWithValue("book", deferral.LedgerBookId);
        command.Parameters.AddWithValue("batch", deferral.DisposalMutationBatchId);
        command.Parameters.AddWithValue("security", deferral.SecurityId);
        command.Parameters.AddWithValue("sale_date", deferral.SaleDate);
        AddAccountParameters(command, deferral.DisposalAccount);
        command.Parameters.AddWithValue("lot", deferral.ReplacementTaxLotRecordId);
        command.Parameters.AddWithValue("lot_name", deferral.ReplacementLotId.Trim());
        command.Parameters.AddWithValue("amount", deferral.DisallowedAmount);
        command.Parameters.AddWithValue("quantity", deferral.MatchedReplacementQuantity);
        command.Parameters.AddWithValue("carry", deferral.HoldingPeriodCarryDate);
        command.Parameters.AddWithValue("policy", deferral.PolicyId.Trim());
        command.Parameters.AddWithValue("window", deferral.WindowDays);
        command.Parameters.AddWithValue("scope", deferral.Scope.ToString());
        command.Parameters.AddWithValue("recorded_at", deferral.RecordedAt.UtcDateTime);
        return (bool)(await command.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
    }
}
