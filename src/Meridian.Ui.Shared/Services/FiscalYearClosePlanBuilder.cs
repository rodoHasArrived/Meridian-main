using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed record FiscalYearCloseSnapshotRead(LedgerPeriodCloseSnapshot? Snapshot, bool IntegrityFailed = false);

/// <summary>Compatibility entry point for fiscal close projection from supplied retained snapshots.</summary>
public static class FiscalYearClosePlanBuilder
{
    public static FiscalYearClosePlanDto Build(
        Guid ledgerBookId,
        int fiscalYear,
        IReadOnlyList<LedgerAccountingPeriod> bookPeriods,
        IReadOnlyDictionary<Guid, FiscalYearCloseSnapshotRead> snapshots)
        => FiscalYearCloseProjection.Build(ledgerBookId, fiscalYear, bookPeriods,
            snapshots.ToDictionary(pair => pair.Key, pair => new FiscalYearCloseRead(pair.Value.Snapshot, pair.Value.IntegrityFailed)));
}
