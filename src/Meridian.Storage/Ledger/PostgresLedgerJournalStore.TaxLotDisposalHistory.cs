using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Accounting.Lots;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

/// <summary>
/// A retained disposal as durable history, without the two facts that live on the journal rather
/// than in tax-lot storage: the sale's effective date and the gain or loss actually booked. The
/// caller supplies those from the journal entries it already holds, which keeps the rebuilt rows
/// tied to the same journals a report pack was certified over.
/// <see cref="PolicyRevision"/> is the exact revision recorded on the batch, never today's
/// account policy. <see cref="RecordedAt"/> is the atomic retention time, not proof that a later
/// replacement window was re-evaluated or finalized.
/// </summary>
public sealed record LedgerTaxLotDisposalHistoryRecord(
    Guid MutationBatchId,
    Guid JournalEntryId,
    LedgerAccount Account,
    LedgerTaxLotReliefMethod ReliefMethod,
    IReadOnlyList<LedgerTaxLotDisposalHistoryLot> Lots,
    IReadOnlyList<WashSaleBasisIncrease> WashSaleBasisIncreases,
    decimal MatchedReplacementQuantity,
    IReadOnlyList<OpenLotDto>? CanonicalLots = null,
    IReadOnlyList<OpenLotDto>? PoolLots = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ProceedsAllocationVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? SalePrice = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PolicyRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    DateTimeOffset? RecordedAt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<LedgerTaxLotDisposalRecipientEvidence>? DeferralRecipients = null);

/// <summary>
/// Durable replacement identity and basis attribution, in the same order as the retained wash-sale increases.
/// Missing evidence must not be inferred from an unrelated asset debit in the disposal journal.
/// </summary>
public sealed record LedgerTaxLotDisposalRecipientEvidence(
    Guid ReplacementTaxLotRecordId,
    string ReplacementLotId,
    LedgerAccount Account,
    Guid SecurityId,
    Guid BookPositionId,
    decimal DeferredLoss);

/// <summary>
/// Reads retained tax-lot disposal history so realized-gain reporting can be rebuilt from the
/// durable record instead of requiring the original in-memory projection to have been kept.
/// </summary>
public interface ILedgerTaxLotDisposalHistory
{
    /// <summary>
    /// Returns the disposals recorded against <paramref name="journalEntryIds"/>, one entry per
    /// atomic tax-lot batch, ordered by batch. Journal ids that produced no disposal are absent.
    /// </summary>
    Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> journalEntryIds,
        CancellationToken ct = default);
}

public sealed partial class PostgresLedgerJournalStore : ILedgerTaxLotDisposalHistory
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerTaxLotDisposalHistoryRecord>> GetTaxLotDisposalHistoryAsync(
        Guid ledgerBookId,
        IReadOnlyList<Guid> journalEntryIds,
        CancellationToken ct = default)
    {
        RequireWriteTenant();
        if (ledgerBookId == Guid.Empty)
        {
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        }

        ArgumentNullException.ThrowIfNull(journalEntryIds);
        var journalIds = journalEntryIds.Where(static id => id != Guid.Empty).Distinct().ToArray();
        if (journalIds.Length == 0)
        {
            return [];
        }

        await using var connection = await OpenConnectionAsync(ct).ConfigureAwait(false);
        await EnsureBookWriteAuthorityAsync(connection, null, ledgerBookId, ct).ConfigureAwait(false);
        var lotsByBatch = await LoadDisposalLotsAsync(connection, ledgerBookId, journalIds, ct).ConfigureAwait(false);
        if (lotsByBatch.Count == 0)
        {
            return [];
        }
        await CertifyCarriedHoldingPeriodsAsync(connection, ledgerBookId, lotsByBatch, ct).ConfigureAwait(false);

        var poolByBatch = await LoadAverageCostPoolsAsync(connection, ledgerBookId, lotsByBatch, ct)
            .ConfigureAwait(false);
        var deferralsByBatch = await LoadDeferralsByBatchAsync(
                connection,
                ledgerBookId,
                lotsByBatch,
                ct)
            .ConfigureAwait(false);
        await CertifyReplacementClaimCapacityAsync(connection, ledgerBookId, lotsByBatch.Keys.ToArray(), ct)
            .ConfigureAwait(false);

        return lotsByBatch
            .Select(batch =>
            {
                var hasDeferrals = deferralsByBatch.TryGetValue(batch.Key, out var retained);
                return new LedgerTaxLotDisposalHistoryRecord(
                    batch.Key,
                    batch.Value.JournalEntryId,
                    batch.Value.Account,
                    batch.Value.ReliefMethod,
                    batch.Value.Lots,
                    hasDeferrals ? retained.Increases : Array.Empty<WashSaleBasisIncrease>(),
                    hasDeferrals ? retained.MatchedQuantity : 0m,
                    batch.Value.CanonicalLots,
                    poolByBatch.GetValueOrDefault(batch.Key),
                    batch.Value.ProceedsAllocationVersion,
                    batch.Value.SalePrice,
                    batch.Value.PolicyRevision,
                    batch.Value.RecordedAt,
                    hasDeferrals ? retained.Recipients : null);
            })
            .OrderBy(static record => record.MutationBatchId)
            .ToArray();
    }

    private async Task<Dictionary<Guid, DisposalBatchAccumulator>> LoadDisposalLotsAsync(
        NpgsqlConnection connection,
        Guid ledgerBookId,
        Guid[] journalIds,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select batch.mutation_batch_id,
                   batch.journal_entry_id,
                   batch.relief_method,
                   mutation.lot_id,
                   mutation.quantity_delta,
                   mutation.unit_cost,
                   mutation.cost_basis,
                   lot.acquired_date,
                   lot.account_name,
                   lot.account_type,
                   lot.symbol,
                   lot.financial_account_id,
                   (select min(carried.holding_period_carry_date)
                    from {Qualified("wash_sale_deferrals")} carried
                    where carried.replacement_tax_lot_record_id = mutation.tax_lot_record_id
                      and carried.recorded_at <= batch.created_at) as holding_period_start,
                   mutation.lot_snapshot_before::text,
                   lot.acquisition_terms::text,
                   lot.security_id,
                   lot.book_position_id,
                   mutation.tax_lot_record_id,
                   lot.original_face,
                   lot.booked_factor,
                   lot.par_basis,
                   batch.proceeds_allocation_version,
                   batch.disposal_sale_price,
                   batch.policy_revision,
                   batch.created_at,
                   batch.security_id,
                   journal.metadata::text,
                   journal.occurred_at
            from {Qualified("tax_lot_mutations")} mutation
            join {Qualified("atomic_tax_lot_posting_batches")} batch
              on batch.mutation_batch_id = mutation.mutation_batch_id
            join {Qualified("tax_lots")} lot
              on lot.tax_lot_record_id = mutation.tax_lot_record_id
            join {Qualified("journal_entries")} journal
              on journal.journal_entry_id = batch.journal_entry_id
            where batch.ledger_book_id = @ledger_book_id
              and mutation.mutation_kind = 'Disposal'
              and batch.journal_entry_id = any(@journal_entry_ids)
            order by batch.mutation_batch_id, mutation.selection_ordinal;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("journal_entry_ids", journalIds);

        var batches = new Dictionary<Guid, DisposalBatchAccumulator>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var batchId = reader.GetGuid(0);
            var acquiredDate = DateOnly.FromDateTime(reader.GetDateTime(7));

            // A disposal mutation records a negative quantity delta; relief works in positive
            // relieved quantities.
            var quantity = Math.Abs(reader.GetDecimal(4));

            // A carried holding-period start earlier than acquisition means an earlier wash sale
            // capitalized into this lot; anything else leaves the period starting at acquisition.
            var carriedStart = reader.IsDBNull(12) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(12));
            var holdingPeriodStart = carriedStart is { } carried && carried < acquiredDate ? carried : acquiredDate;

            if (reader.IsDBNull(13))
                throw new LedgerValidationException("Disposal history lacks its immutable lot snapshot; canonical reporting is blocked.");
            var before = DeserializeTaxLotSnapshot(reader.GetString(13));
            if (before.TaxLotRecordId != reader.GetGuid(17) || before.LedgerBookId != ledgerBookId)
                throw new LedgerValidationException("Retained disposal snapshot does not bind the exact durable lot and ledger book.");
            // An approved backfill may supply only facts absent in the immutable legacy snapshot.
            // Quantity, basis, acquisition date, and version always come from that snapshot.
            before = before with
            {
                Acquisition = before.Acquisition ?? (reader.IsDBNull(14) ? null
                    : JsonSerializer.Deserialize<OpenLotAcquisitionDto>(reader.GetString(14), JsonOptions)),
                SecurityId = before.SecurityId != Guid.Empty ? before.SecurityId : reader.IsDBNull(15) ? Guid.Empty : reader.GetGuid(15),
                BookPositionId = before.BookPositionId != Guid.Empty ? before.BookPositionId : reader.IsDBNull(16) ? Guid.Empty : reader.GetGuid(16),
                OriginalFace = before.OriginalFace ?? (reader.IsDBNull(18) ? null : reader.GetDecimal(18)),
                BookedFactor = before.BookedFactor ?? (reader.IsDBNull(19) ? null : reader.GetDecimal(19)),
                ParBasis = before.ParBasis ?? (reader.IsDBNull(20) ? null : reader.GetDecimal(20))
            };
            var canonical = before.ToOpenLot();
            if (reader.GetGuid(25) != canonical.SecurityId)
                throw new LedgerValidationException("Retained disposal batch security differs from its canonical acquisition evidence.");
            var lot = new LedgerTaxLotDisposalHistoryLot(
                reader.GetString(3),
                acquiredDate,
                holdingPeriodStart,
                quantity,
                reader.GetDecimal(5),
                reader.GetDecimal(6));

            if (batches.TryGetValue(batchId, out var accumulator))
            {
                if (accumulator.Account != before.Account || accumulator.SecurityId != canonical.SecurityId)
                    throw new LedgerValidationException("Retained disposal snapshots span different authoritative asset accounts or securities.");
                accumulator.Lots.Add(lot);
                accumulator.CanonicalLots.Add(canonical);
                continue;
            }

            if (reader.IsDBNull(2) || !Enum.TryParse<LedgerTaxLotReliefMethod>(reader.GetString(2),
                    ignoreCase: true, out var reliefMethod) || !Enum.IsDefined(reliefMethod))
                throw new LedgerValidationException("Disposal history lacks an authoritative relief policy; canonical reporting is blocked.");

            batches[batchId] = new DisposalBatchAccumulator(
                reader.GetGuid(1),
                before.Account,
                reliefMethod,
                new List<LedgerTaxLotDisposalHistoryLot> { lot },
                new List<OpenLotDto> { canonical },
                reader.IsDBNull(21) ? null : reader.GetInt32(21),
                reader.IsDBNull(22) ? null : reader.GetDecimal(22),
                reader.IsDBNull(23) ? null : reader.GetString(23),
                ReadUtcDateTimeOffset(reader, 24),
                canonical.SecurityId,
                DeserializeMetadata(reader.GetString(26)).EffectiveDate
                    ?? DateOnly.FromDateTime(ReadUtcDateTimeOffset(reader, 27).UtcDateTime));
        }

        return batches;
    }

    private async Task<List<CarriedHoldingPeriodEvidence>> LoadCarriedHoldingPeriodEvidenceAsync(NpgsqlConnection connection,
        IReadOnlyDictionary<Guid, DisposalBatchAccumulator> batches, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select target.mutation_batch_id, target.tax_lot_record_id,
                   carried.disposal_mutation_batch_id, source.journal_entry_id,
                   carried.ledger_book_id, carried.security_id, carried.sale_date,
                   carried.disposal_account_name, carried.disposal_account_type,
                   carried.disposal_symbol, carried.disposal_financial_account_id,
                   carried.replacement_lot_id, carried.window_days, carried.scope, carried.policy_id,
                   carried.holding_period_carry_date
            from {Qualified("tax_lot_mutations")} target
            join {Qualified("atomic_tax_lot_posting_batches")} target_batch
              on target_batch.mutation_batch_id = target.mutation_batch_id
            join {Qualified("wash_sale_deferrals")} carried
              on carried.replacement_tax_lot_record_id = target.tax_lot_record_id
             and carried.recorded_at <= target_batch.created_at
            left join {Qualified("atomic_tax_lot_posting_batches")} source
              on source.mutation_batch_id = carried.disposal_mutation_batch_id
            where target.mutation_kind = 'Disposal'
              and target.mutation_batch_id = any(@batch_ids);
            """;
        command.Parameters.AddWithValue("batch_ids", batches.Keys.ToArray());
        var evidence = new List<CarriedHoldingPeriodEvidence>();
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                evidence.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                    reader.IsDBNull(3) ? Guid.Empty : reader.GetGuid(3), reader.GetGuid(4), reader.GetGuid(5),
                    DateOnly.FromDateTime(reader.GetDateTime(6)), ReadLedgerAccount(reader, 7),
                    reader.GetString(11), reader.GetInt32(12),
                    Enum.Parse<WashSaleReplacementScope>(reader.GetString(13), ignoreCase: true), reader.GetString(14),
                    DateOnly.FromDateTime(reader.GetDateTime(15))));
        }
        return evidence;
    }

    /// <summary>
    /// Rebuilds each average-cost batch's full pool as it stood before relief: the relieved lots'
    /// snapshots plus every survivor the batch restated, so reporting can re-run the pooled relief
    /// instead of trusting the retained slice bases.
    /// </summary>
    private async Task<Dictionary<Guid, IReadOnlyList<OpenLotDto>>> LoadAverageCostPoolsAsync(
        NpgsqlConnection connection,
        Guid ledgerBookId,
        Dictionary<Guid, DisposalBatchAccumulator> lotsByBatch,
        CancellationToken ct)
    {
        var averageCostBatches = lotsByBatch
            .Where(static batch => batch.Value.ReliefMethod == LedgerTaxLotReliefMethod.AverageCost)
            .ToDictionary(static batch => batch.Key, static batch => new List<OpenLotDto>(batch.Value.CanonicalLots));
        if (averageCostBatches.Count == 0)
        {
            return [];
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select mutation.mutation_batch_id, mutation.tax_lot_record_id, mutation.lot_snapshot_before::text
            from {Qualified("tax_lot_mutations")} mutation
            join {Qualified("atomic_tax_lot_posting_batches")} batch
              on batch.mutation_batch_id = mutation.mutation_batch_id
            where batch.ledger_book_id = @ledger_book_id
              and mutation.mutation_kind = 'BasisRedistribution'
              and mutation.mutation_batch_id = any(@batch_ids)
            order by mutation.mutation_batch_id, mutation.selection_ordinal;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("batch_ids", averageCostBatches.Keys.ToArray());

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (reader.IsDBNull(2))
                throw new LedgerValidationException("Average-cost restatement lacks its immutable pool snapshot; canonical reporting is blocked.");
            var before = DeserializeTaxLotSnapshot(reader.GetString(2));
            if (before.TaxLotRecordId != reader.GetGuid(1) || before.LedgerBookId != ledgerBookId)
                throw new LedgerValidationException("Retained average-cost pool snapshot does not bind the exact durable lot and ledger book.");
            averageCostBatches[reader.GetGuid(0)].Add(before.ToOpenLot());
        }

        return averageCostBatches.ToDictionary(
            static batch => batch.Key,
            static batch => (IReadOnlyList<OpenLotDto>)batch.Value);
    }

    private async Task<Dictionary<Guid, (IReadOnlyList<WashSaleBasisIncrease> Increases, decimal MatchedQuantity,
        IReadOnlyList<LedgerTaxLotDisposalRecipientEvidence> Recipients)>>
        LoadDeferralsByBatchAsync(
            NpgsqlConnection connection,
            Guid ledgerBookId,
            IReadOnlyDictionary<Guid, DisposalBatchAccumulator> batches,
            CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            select deferral.disposal_mutation_batch_id,
                   deferral.replacement_lot_id,
                   deferral.disallowed_amount,
                   deferral.matched_quantity,
                   deferral.holding_period_carry_date,
                   deferral.policy_id,
                   deferral.window_days,
                   deferral.scope,
                   deferral.security_id,
                   deferral.sale_date,
                   deferral.disposal_account_name,
                   deferral.disposal_account_type,
                   deferral.disposal_symbol,
                   deferral.disposal_financial_account_id,
                   recipient.tax_lot_record_id,
                   recipient.ledger_book_id,
                   recipient.lot_id,
                   recipient.security_id,
                   recipient.acquired_date,
                   recipient.account_name,
                   recipient.account_type,
                   recipient.symbol,
                   recipient.financial_account_id,
                   recipient.original_quantity,
                   recipient.book_position_id
            from {Qualified("wash_sale_deferrals")} deferral
            left join {Qualified("tax_lots")} recipient
              on recipient.tax_lot_record_id = deferral.replacement_tax_lot_record_id
            where deferral.ledger_book_id = @ledger_book_id
              and deferral.disposal_mutation_batch_id = any(@batch_ids)
            order by deferral.disposal_mutation_batch_id, deferral.replacement_lot_id;
            """;
        command.Parameters.AddWithValue("ledger_book_id", ledgerBookId);
        command.Parameters.AddWithValue("batch_ids", batches.Keys.ToArray());

        var byBatch = new Dictionary<Guid, (List<WashSaleBasisIncrease> Increases, decimal MatchedQuantity,
            List<LedgerTaxLotDisposalRecipientEvidence> Recipients)>();
        var recipientQuantities = new Dictionary<Guid, Dictionary<Guid, decimal>>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var batchId = reader.GetGuid(0);
            var batch = batches[batchId];
            // A same-book foreign deferral can otherwise alter proceeds, character, and finality.
            // Bind every retained row to the disposed security, exact journal date, and full account.
            if (reader.GetGuid(8) != batch.SecurityId ||
                DateOnly.FromDateTime(reader.GetDateTime(9)) != batch.SaleDate ||
                ReadLedgerAccount(reader, 10) != batch.Account)
                throw new LedgerValidationException("Retained wash-sale deferral security, sale date, or disposing account does not match its authoritative disposal batch.");
            var windowDays = reader.GetInt32(6);
            if (!Enum.TryParse<WashSaleReplacementScope>(reader.GetString(7), ignoreCase: true, out var scope) ||
                !Enum.IsDefined(scope) || windowDays < 0 || reader.IsDBNull(14) ||
                reader.GetGuid(15) != ledgerBookId || reader.IsDBNull(17) || reader.GetGuid(17) != batch.SecurityId ||
                reader.IsDBNull(24) || reader.GetGuid(24) == Guid.Empty ||
                !string.Equals(reader.GetString(1), reader.GetString(16), StringComparison.OrdinalIgnoreCase) ||
                Math.Abs(DateOnly.FromDateTime(reader.GetDateTime(18)).DayNumber - batch.SaleDate.DayNumber) > windowDays ||
                (scope == WashSaleReplacementScope.DisposingAccount && ReadLedgerAccount(reader, 19) != batch.Account) ||
                batch.CanonicalLots.Any(lot => lot.TaxLotRecordId == reader.GetGuid(14)))
                throw new LedgerValidationException("Retained wash-sale deferral recipient does not match its durable lot, security, ledger book, replacement window, or account scope.");

            if (!recipientQuantities.TryGetValue(batchId, out var quantities))
                recipientQuantities[batchId] = quantities = new Dictionary<Guid, decimal>();
            quantities[reader.GetGuid(14)] = reader.GetDecimal(23);
            var increase = new WashSaleBasisIncrease(
                reader.GetString(1),
                reader.GetDecimal(2),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                ReadLedgerAccount(reader, 19))
            {
                // The deferral proves that this revision governed this disposal. Its retained
                // window and scope are authoritative; the mutable current policy is not. The
                // activation date was not retained and must not be recovered from today's row.
                AppliedPolicy = new WashSalePolicy(true, windowDays, scope)
                {
                    PolicyId = reader.GetString(5)
                }
            };
            var recipient = new LedgerTaxLotDisposalRecipientEvidence(reader.GetGuid(14), reader.GetString(16),
                increase.ReplacementAccount!, reader.GetGuid(17), reader.GetGuid(24), increase.Amount);

            if (byBatch.TryGetValue(batchId, out var existing))
            {
                if (reader.GetDecimal(3) != existing.MatchedQuantity)
                    throw new LedgerValidationException("Retained wash-sale deferrals disagree on the disposal's aggregate matched replacement quantity.");
                existing.Increases.Add(increase);
                existing.Recipients.Add(recipient);
                continue;
            }

            // Matched quantity is a disposal aggregate repeated on every deferral row. Retain it
            // once and certify agreement across all rows before exposing it as saturation evidence.
            byBatch[batchId] = (new List<WashSaleBasisIncrease> { increase }, reader.GetDecimal(3),
                new List<LedgerTaxLotDisposalRecipientEvidence> { recipient });
        }

        // The aggregate is repeated per row, while replacement capacity is additive across
        // distinct durable recipients. Use original ledger units: later sales may reduce current
        // open quantity without invalidating an earlier retained replacement match.
        foreach (var batch in byBatch)
            if (batch.Value.MatchedQuantity > recipientQuantities[batch.Key].Values.Sum())
                throw new LedgerValidationException("Retained wash-sale aggregate matched replacement quantity exceeds its recipients' original quantity.");

        return byBatch.ToDictionary(
            static entry => entry.Key,
            static entry => ((IReadOnlyList<WashSaleBasisIncrease>)entry.Value.Increases, entry.Value.MatchedQuantity,
                (IReadOnlyList<LedgerTaxLotDisposalRecipientEvidence>)entry.Value.Recipients));
    }

    private sealed record DisposalBatchAccumulator(
        Guid JournalEntryId,
        LedgerAccount Account,
        LedgerTaxLotReliefMethod ReliefMethod,
        List<LedgerTaxLotDisposalHistoryLot> Lots,
        List<OpenLotDto> CanonicalLots,
        int? ProceedsAllocationVersion,
        decimal? SalePrice,
        string? PolicyRevision,
        DateTimeOffset RecordedAt,
        Guid SecurityId,
        DateOnly SaleDate);

    private sealed record CarriedHoldingPeriodEvidence(Guid TargetBatchId, Guid TargetLotId,
        Guid SourceBatchId, Guid SourceJournalId, Guid LedgerBookId, Guid SecurityId, DateOnly SaleDate,
        LedgerAccount DisposalAccount, string ReplacementLotId, int WindowDays, WashSaleReplacementScope Scope,
        string PolicyId, DateOnly HoldingPeriodCarryDate);
}
