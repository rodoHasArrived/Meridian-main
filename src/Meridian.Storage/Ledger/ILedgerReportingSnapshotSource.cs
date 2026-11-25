using Meridian.Ledger;

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

/// <summary>Opt-in capture of historical open-lot basis alongside the exact reporting journal transaction.</summary>
public interface ILedgerOpenPositionReportingSnapshotSource : ILedgerReportingSnapshotSource
{
    Task<LedgerReportingSnapshot> CaptureOpenPositionReportingSnapshotAsync(
        LedgerJournalEntryQuery query, Guid accountingPeriodId, CancellationToken ct = default);
}

public sealed record LedgerReportingOpenPositions(DateOnly AsOf, IReadOnlyList<LedgerTaxLotRecord> Lots,
    IReadOnlyList<string> Blockers)
{
    /// <summary>Optional exact occurrence cutoff used for intraday journal/lot agreement.</summary>
    public DateTimeOffset? CutoffUtc { get; init; }
    /// <summary>Canonical zero-quantity after-snapshots prove closure without inferring absent holdings.</summary>
    public IReadOnlyList<LedgerTaxLotRecord> ClosedLots { get; init; } = [];
    /// <summary>Financial account-specific inventory coverage failures; unrelated subledgers do not block each other.</summary>
    public IReadOnlyList<LedgerReportingInventoryCoverageBlocker> CoverageBlockers { get; init; } = [];
    public IReadOnlySet<LedgerAccount> HistoricalLotAccounts { get; init; } = new HashSet<LedgerAccount>();
}

public sealed record LedgerReportingInventoryCoverageBlocker(LedgerAccount Account, string Reason);

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
    IReadOnlyList<AtomicTaxLotJournalResult>? OpenLotSuccessorHistory = null)
{
    public LedgerReportingOpenPositions? OpenPositions { get; init; }
}
