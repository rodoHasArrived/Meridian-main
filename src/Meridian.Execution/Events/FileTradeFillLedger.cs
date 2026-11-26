using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

/// <summary>
/// Composes trade-fill posting over the file-backed journal used when no Postgres ledger is
/// configured. Posting reuses the same governed target chain as Postgres, so posting-identity
/// collisions, equivalent-retry handling and replay comparison behave identically.
/// </summary>
public static class FileTradeFillLedger
{
    /// <summary>Creates the trade-fill posting target over <paramref name="store"/>.</summary>
    public static ITradeFillLedgerPostingTarget CreatePostingTarget(FileTradeFillLedgerJournalStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new GovernedTradeFillLedgerPostingTarget(new DurableLedgerPostingTarget(store), store);
    }

    /// <summary>
    /// Rebuilds and verifies the on-disk journal before the host starts, so a damaged or tampered
    /// ledger stops startup instead of failing the first fill after trading has begun.
    /// </summary>
    /// <exception cref="InvalidDataException">The journal chain or an envelope binding fails verification.</exception>
    public static async Task<LedgerJournalChainVerification> VerifyBeforeHostStartupAsync(
        FileTradeFillLedgerJournalStore store,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        var verification = await store.VerifyAsync(ct).ConfigureAwait(false);
        if (!verification.IsIntact)
        {
            throw new InvalidDataException(
                $"Trade-fill ledger journal '{verification.ScopeId}' failed verification at record " +
                $"{verification.FirstBreakSequence?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(unknown)"}: {verification.Reason}");
        }

        return verification;
    }
}
