namespace Meridian.Storage.Ledger;

/// <summary>
/// Reporting read boundary required by certification, capturing journals and retained disposal evidence
/// from one consistent ledger population. Implementations must preserve all supplied query filters
/// and prevent later commits from appearing in any component of the returned snapshot.
/// </summary>
public interface ILedgerReportingSnapshotSource
{
    /// <summary>
    /// Captures the optional report period independently of the journal query's period filter, so
    /// all-history balance queries can retain the reporting period's status and version atomically.
    /// The requested period must belong to the query's exact ledger book.
    /// </summary>
    Task<LedgerReportingSnapshot> CaptureReportingSnapshotAsync(
        LedgerJournalEntryQuery query,
        Guid? accountingPeriodId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Captured journals, their tax-lot history, corporate-action successor batches and the requested
/// report period in the exact ledger book. Calculations and checkpoint evidence retain these facts
/// rather than reading live state. A null successor history means the source cannot capture it, so
/// retained corporate-action journals cannot be certified from this snapshot.
/// </summary>
public sealed record LedgerReportingSnapshot(
    IReadOnlyList<LedgerJournalEntryRecord> Journals,
    IReadOnlyList<LedgerTaxLotDisposalHistoryRecord> TaxLotDisposalHistory,
    LedgerAccountingPeriod? Period = null,
    IReadOnlyList<AtomicTaxLotJournalResult>? OpenLotSuccessorHistory = null);
