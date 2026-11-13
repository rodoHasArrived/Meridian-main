using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Reads account balances from an authenticated immutable close checkpoint and the remaining
/// journal population without materializing every historical journal.
/// </summary>
public interface ILedgerBalanceSnapshotSource
{
    Task<IReadOnlyDictionary<LedgerAccount, decimal>> ReadTrialBalanceAsOfAsync(
        Guid ledgerBookId,
        DateTimeOffset asOfUtc,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default);
}
