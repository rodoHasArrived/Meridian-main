using System.Globalization;
using System.Text.Json;
using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    // Full immutable after-snapshots retain historical basis. HistoricalTaxLotQuantity is deliberately
    // not used here: that helper only restates quantity and carries today's basis.
    private async Task<LedgerReportingOpenPositions> CaptureReportingOpenPositionsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, Guid bookId, DateOnly asOf,
        DateTimeOffset? cutoffUtc, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            select l.tax_lot_record_id, l.version, l.last_mutation_batch_id,
                   m.expected_version, m.result_version, m.mutation_batch_id,
                   m.lot_snapshot_before::text, m.lot_snapshot_after::text,
                   j.metadata ->> 'effectiveDate', p.ledger_book_id, b.ledger_book_id, j.occurred_at,
                   l.security_id, l.book_position_id, m.security_id, m.book_position_id
            from {Qualified("tax_lots")} l
            left join {Qualified("tax_lot_mutations")} m on m.tax_lot_record_id = l.tax_lot_record_id
            left join {Qualified("atomic_tax_lot_posting_batches")} b on b.mutation_batch_id = m.mutation_batch_id
            left join {Qualified("journal_entries")} j on j.journal_entry_id = m.journal_entry_id
            left join {Qualified("accounting_periods")} p on p.period_id = j.period_id
            where l.ledger_book_id = @book and l.acquired_date <= @as_of
            order by l.tax_lot_record_id, m.result_version;
            """;
        command.Parameters.AddWithValue("book", bookId);
        command.Parameters.AddWithValue("as_of", asOf);
        var histories = new Dictionary<Guid, List<ReportingLotVersion>>();
        var blockers = new List<string>();
        var historyRows = 0;
        await using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                if (++historyRows > GeneralLedgerExportBuilder.MaximumRows)
                    throw new InvalidOperationException("Open-position reporting history exceeds the bounded export size.");
                var lotId = reader.GetGuid(0);
                if (!histories.TryGetValue(lotId, out var versions))
                    histories[lotId] = versions = [];
                if (reader.IsDBNull(3) || reader.IsDBNull(7) || reader.IsDBNull(8)
                    || reader.IsDBNull(9) || reader.GetGuid(9) != bookId || reader.IsDBNull(10) || reader.GetGuid(10) != bookId || reader.IsDBNull(11)
                    || reader.IsDBNull(12) || reader.IsDBNull(13) || reader.IsDBNull(14) || reader.IsDBNull(15)
                    || reader.GetGuid(12) != reader.GetGuid(14) || reader.GetGuid(13) != reader.GetGuid(15)
                    || !DateOnly.TryParseExact(reader.GetString(8), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var effectiveDate))
                {
                    blockers.Add($"Lot '{lotId}' lacks complete dated canonical mutation history.");
                    continue;
                }
                versions.Add(new(reader.GetInt64(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.GetInt64(3), reader.GetInt64(4), reader.GetGuid(5), effectiveDate,
                    reader.IsDBNull(6) ? null : DeserializeTaxLotSnapshot(reader.GetString(6)),
                    DeserializeTaxLotSnapshot(reader.GetString(7)), ReadUtcDateTimeOffset(reader, 11), reader.GetGuid(12), reader.GetGuid(13)));
            }
        }
        var lots = new List<LedgerTaxLotRecord>();
        var closed = new List<LedgerTaxLotRecord>();
        foreach (var (lotId, versions) in histories)
        {
            LedgerTaxLotRecord? previous = null, selected = null;
            DateOnly? priorDate = null;
            var valid = versions.Count > 0;
            var excludedVersion = false;
            foreach (var version in versions)
            {
                valid &= version.After.TaxLotRecordId == lotId && version.After.LedgerBookId == bookId
                    && version.After.SecurityId == version.SecurityId && version.After.BookPositionId == version.BookPositionId
                    && version.After.LastMutationBatchId == version.BatchId
                    && version.ResultVersion == version.ExpectedVersion + 1 && version.After.Version == version.ResultVersion
                    && (priorDate is null || version.EffectiveDate >= priorDate)
                    && (previous is null
                        ? version.ExpectedVersion == 0 && version.After.OriginatingMutationBatchId == version.BatchId && version.Before is null
                        : version.ExpectedVersion == previous.Version && version.Before is not null
                            && JsonSerializer.Serialize(previous, JsonOptions) == JsonSerializer.Serialize(version.Before, JsonOptions));
                previous = version.After;
                priorDate = version.EffectiveDate;
                var eligible = version.EffectiveDate <= asOf && (cutoffUtc is null || version.OccurredAt <= cutoffUtc);
                // A later admitted mutation cannot depend on an excluded earlier state.
                if (eligible && excludedVersion)
                    valid = false;
                if (eligible)
                    selected = version.After;
                else
                    excludedVersion = true;
            }
            valid &= previous is not null && previous.Version == versions.LastOrDefault()?.CurrentVersion
                && previous.LastMutationBatchId == versions.LastOrDefault()?.CurrentLastMutation;
            if (!valid)
                blockers.Add($"Lot '{lotId}' has incomplete, discontinuous or retrodated basis history; no current basis was substituted.");
            else if (selected is { OpenQuantity: > 0m })
            {
                try
                { Meridian.Contracts.Accounting.Lots.OpenLotValidation.Validate(selected.ToOpenLot()); lots.Add(selected); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or LedgerValidationException)
                { blockers.Add($"Lot '{lotId}' has unsupported canonical basis evidence."); }
            }
            else if (selected is { OpenQuantity: 0m })
                closed.Add(selected);
        }
        // Establish coverage against the entire as-of journal population, including opening activity
        // outside the selected report period. A detached empty lot table never proves zero positions.
        var population = await QueryAsync(connection, transaction,
            new LedgerJournalEntryQuery(LedgerBookId: bookId, EffectiveTo: asOf, OccurredTo: cutoffUtc), null, ct).ConfigureAwait(false);
        var lotAccounts = histories.Values.SelectMany(versions => versions.Select(version => version.After.Account)).ToHashSet();
        var coverageBlockers = new List<LedgerReportingInventoryCoverageBlocker>();
        var classified = population.SelectMany(record => record.Entry.Lines.Select(line =>
            (Line: line, Role: LedgerInventoryAccountRoleClassifier.Classify(line, record.Entry.Metadata, lotAccounts)))).ToArray();
        foreach (var unknown in classified.Where(row => row.Role == LedgerInventoryAccountRole.UnknownInstrumentAsset)
                     .GroupBy(row => row.Line.Account).Where(group => group.Sum(row => row.Line.Debit - row.Line.Credit) != 0m))
            coverageBlockers.Add(new(unknown.Key, "An instrument asset has no source-proven non-lot role or canonical acquisition carrying account; inventory coverage is unavailable."));
        var carrying = classified.Where(row => row.Role == LedgerInventoryAccountRole.LotCarrying)
            .GroupBy(row => row.Line.Account).ToDictionary(group => group.Key,
                group => group.Sum(row => row.Line.Debit - row.Line.Credit));
        foreach (var account in carrying.Keys.Union(lotAccounts))
        {
            var balance = carrying.GetValueOrDefault(account);
            var scopedLots = lots.Where(lot => lot.Account == account).ToArray();
            var basis = scopedLots.Sum(lot => lot.ToOpenLot().OpenFunctionalCostBasis);
            if (balance != basis)
                coverageBlockers.Add(new(account, $"Canonical open basis does not reconcile to journal carrying account '{account}'. Missing lots or unsupported valuation adjustments require review."));
        }
        return new(asOf, lots.AsReadOnly(), blockers.Distinct().Order(StringComparer.Ordinal).ToArray())
        { CutoffUtc = cutoffUtc, ClosedLots = closed.AsReadOnly(), CoverageBlockers = coverageBlockers.AsReadOnly(), HistoricalLotAccounts = lotAccounts };
    }

    private sealed record ReportingLotVersion(long CurrentVersion, Guid? CurrentLastMutation,
        long ExpectedVersion, long ResultVersion, Guid BatchId, DateOnly EffectiveDate,
        LedgerTaxLotRecord? Before, LedgerTaxLotRecord After, DateTimeOffset OccurredAt, Guid SecurityId, Guid BookPositionId);
}
