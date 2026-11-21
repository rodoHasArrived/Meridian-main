using System.Globalization;
using Meridian.Contracts.Integrity;
using Meridian.Ledger;
using Meridian.Storage.Export;

namespace Meridian.Storage.Ledger;

public sealed record LedgerJournalExportCapture(IReadOnlyList<LedgerJournalEntryRecord> Journals, string SnapshotSha256);

/// <summary>Selects complete journals from one authorized, immutable period capture.</summary>
public static class LedgerJournalExportSelection
{
    public static LedgerJournalExportCapture Select(LedgerReportingSnapshot snapshot, Guid bookId,
        Guid periodId, DateOnly from, DateOnly to, IReadOnlyList<Guid>? journalEntryIds = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var period = snapshot.Period;
        if (bookId == Guid.Empty || periodId == Guid.Empty || period is null ||
            period.LedgerBookId != bookId || period.PeriodId != periodId ||
            snapshot.Journals.Any(record => record.PeriodId != periodId))
            throw new InvalidOperationException("The captured journal population must belong to the exact selected period and book.");
        if (from > to || from < period.StartDate || to > period.EndDate)
            throw new ArgumentException("The accounting-date range must be ordered and inside the selected period.");
        var ids = journalEntryIds?.ToHashSet() ?? [];
        if (ids.Contains(Guid.Empty)) throw new ArgumentException("Selected journal identifiers must be nonempty.");
        if (snapshot.Journals.Select(record => record.Entry.JournalEntryId).Distinct().Count() != snapshot.Journals.Count)
            throw new InvalidOperationException("The captured journal population contains duplicate identities.");
        var records = snapshot.Journals.Where(record =>
        {
            var date = record.Entry.Metadata.EffectiveDate ?? DateOnly.FromDateTime(record.Entry.Timestamp.UtcDateTime);
            return date >= from && date <= to && (ids.Count == 0 || ids.Contains(record.Entry.JournalEntryId));
        }).OrderBy(record => record.GlobalSequence).ThenBy(record => record.Entry.JournalEntryId).ToArray();
        if (ids.Count > 0 && !ids.SetEquals(records.Select(record => record.Entry.JournalEntryId)))
            throw new ArgumentException("Every selected journal must exist in the authorized period and accounting-date range.");
        if (records.Sum(record => (long)record.Entry.Lines.Count) > GeneralLedgerExportBuilder.MaximumRows)
            throw new ArgumentException($"Select at most {GeneralLedgerExportBuilder.MaximumRows} complete journal lines per export.");
        if (records.Any(record => !record.Entry.IsBalanced))
            throw new LedgerValidationException("Unbalanced journals cannot be exported.");
        // Hash source rows independently of caller-selected external mapping and wire format.
        var rowsHash = LedgerJournalExporter.ComputeSourceSha256(records);
        var source = string.Join('|', "journal-selection.v1", bookId.ToString("D"), periodId.ToString("D"),
            period.Version.ToString(CultureInfo.InvariantCulture), from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), rowsHash);
        return new(Array.AsReadOnly(records), Sha256Digest.ComputeUtf8(source));
    }
}
