using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    private async Task CertifyReplacementClaimCapacityAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Guid ledgerBookId,
        IReadOnlyCollection<Guid> batchIds, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            with requested_recipients as (
                select distinct replacement_tax_lot_record_id
                from {Qualified("wash_sale_deferrals")}
                where ledger_book_id = @ledger_book_id
                  and disposal_mutation_batch_id = any(@batch_ids)
            ), claimant_batches as (
                select distinct claim.disposal_mutation_batch_id
                from {Qualified("wash_sale_deferrals")} claim
                join requested_recipients requested
                  on requested.replacement_tax_lot_record_id = claim.replacement_tax_lot_record_id
                where claim.ledger_book_id = @ledger_book_id
            )
            select claim.disposal_mutation_batch_id, claim.replacement_tax_lot_record_id,
                   claim.matched_quantity, claim.ledger_book_id, source.ledger_book_id,
                   recipient.ledger_book_id, recipient.original_quantity, recipient.security_id,
                   claim.security_id, source.security_id,
                   requested.replacement_tax_lot_record_id is not null
            from {Qualified("wash_sale_deferrals")} claim
            join claimant_batches claimant
              on claimant.disposal_mutation_batch_id = claim.disposal_mutation_batch_id
            left join {Qualified("atomic_tax_lot_posting_batches")} source
              on source.mutation_batch_id = claim.disposal_mutation_batch_id
            left join {Qualified("tax_lots")} recipient
              on recipient.tax_lot_record_id = claim.replacement_tax_lot_record_id
            left join requested_recipients requested
              on requested.replacement_tax_lot_record_id = claim.replacement_tax_lot_record_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("batch_ids", batchIds.ToArray());

        var claims = new Dictionary<Guid, ReplacementBatchClaim>();
        var recipients = new Dictionary<Guid, ReplacementRecipientClaims>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var batchId = reader.GetGuid(0);
            var recipientId = reader.GetGuid(1);
            var matched = reader.GetDecimal(2);
            if (matched <= 0m || reader.GetGuid(3) != ledgerBookId || reader.IsDBNull(4) ||
                reader.GetGuid(4) != ledgerBookId || reader.IsDBNull(5) || reader.GetGuid(5) != ledgerBookId ||
                reader.IsDBNull(7) || reader.GetGuid(7) != reader.GetGuid(8) ||
                reader.GetGuid(8) != reader.GetGuid(9) || reader.GetDecimal(6) <= 0m)
                throw new LedgerValidationException("Retained replacement claims lack consistent source, recipient, or quantity evidence.");

            if (!claims.TryGetValue(batchId, out var claim))
                claims[batchId] = claim = new(matched, []);
            else if (claim.MatchedQuantity != matched)
                throw new LedgerValidationException("Retained replacement claims disagree on the source disposal's aggregate matched quantity.");
            claim.Recipients.Add(recipientId);

            if (reader.GetBoolean(10))
            {
                if (!recipients.TryGetValue(recipientId, out var recipient))
                    recipients[recipientId] = recipient = new(reader.GetDecimal(6), []);
                recipient.SourceBatches.Add(batchId);
            }
        }

        foreach (var recipient in recipients.Values)
        {
            if (recipient.SourceBatches.Count < 2)
                continue; // The ordinary per-disposal check certifies this unshared aggregate.
            var remaining = recipient.OriginalQuantity;
            foreach (var sourceId in recipient.SourceBatches)
            {
                var claim = claims[sourceId];
                // A repeated disposal aggregate is not a per-recipient allocation. A shared lot
                // cannot be certified by inventing how a multi-recipient source divided it.
                if (claim.Recipients.Count != 1)
                    throw new LedgerValidationException("Shared replacement recipients lack retained per-disposal recipient quantity allocations.");
                if (claim.MatchedQuantity > remaining)
                    throw new LedgerValidationException("Cumulative retained replacement claims exceed the recipient's original quantity.");
                remaining -= claim.MatchedQuantity;
            }
        }
    }

    private sealed record ReplacementBatchClaim(decimal MatchedQuantity, HashSet<Guid> Recipients);
    private sealed record ReplacementRecipientClaims(decimal OriginalQuantity, HashSet<Guid> SourceBatches);
}
