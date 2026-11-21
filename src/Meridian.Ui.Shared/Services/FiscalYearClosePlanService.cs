using Meridian.Contracts.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Loads a book's periods and the retained hard-close snapshot of each constituent period, then
/// builds the read-only fiscal-year close plan. Reading the plan never posts or changes state.
/// </summary>
public static class FiscalYearClosePlanService
{
    public static async Task<FiscalYearClosePlanDto> BuildAsync(
        ILedgerJournalStore store,
        Guid ledgerBookId,
        int fiscalYear,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (store is IFiscalYearCloseReceiptStore fiscalStore)
            return await fiscalStore.GetFiscalYearClosePlanAsync(ledgerBookId, fiscalYear, ct).ConfigureAwait(false);
        if (store is not IAtomicLedgerPeriodCloseStore snapshots)
            throw new NotSupportedException("This ledger store does not retain close snapshots.");

        var periods = await store.ListPeriodsAsync(ledgerBookId, ct: ct).ConfigureAwait(false);
        var reads = new Dictionary<Guid, FiscalYearCloseSnapshotRead>();
        foreach (var period in periods.Where(p => p.FiscalYear == fiscalYear && p.Status == "HardClosed"))
        {
            try
            {
                reads[period.PeriodId] = new(await snapshots.GetCloseSnapshotAsync(period.PeriodId, ct: ct).ConfigureAwait(false));
            }
            catch (InvalidDataException)
            {
                reads[period.PeriodId] = new(null, IntegrityFailed: true);
            }
        }

        return FiscalYearClosePlanBuilder.Build(ledgerBookId, fiscalYear, periods, reads);
    }
}
