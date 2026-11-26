using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>Uses native balance checkpoints when supported, retaining journal replay for compatibility stores.</summary>
public static class LedgerBalanceSnapshotExtensions
{
    public static async Task<IReadOnlyDictionary<LedgerAccount, decimal>> ReadTrialBalanceAsOfAsync(
        this ILedgerJournalStore store,
        Guid ledgerBookId,
        DateTimeOffset asOfUtc,
        string? financialAccountId = null,
        LedgerLineDimensionSet? lineDimensions = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        ct.ThrowIfCancellationRequested();

        if (store is ILedgerBalanceSnapshotSource source)
            return await source.ReadTrialBalanceAsOfAsync(
                ledgerBookId, asOfUtc, financialAccountId, lineDimensions, ct)
                .ConfigureAwait(false);

        // Apply the same case-insensitive dimension matcher after replay. A SQL containment
        // prefilter can be case-sensitive and must not discard matching historical accounts.
        var ledger = await store.HydrateLedgerAsOfAsync(ledgerBookId, asOfUtc, ct: ct)
            .ConfigureAwait(false);
        return ledger.TrialBalanceAsOf(asOfUtc, financialAccountId, lineDimensions);
    }
}
