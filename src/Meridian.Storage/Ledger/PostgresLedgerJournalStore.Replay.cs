using Meridian.Ledger;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <summary>
    /// An immutable, audit-verified retained posting is a receipt, not permission for a new write.
    /// Resolve it before current period/lifecycle/rate authority, without trusting any caller flag.
    /// </summary>
    private async Task<bool> TryResolveCommittedReplayAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, LedgerJournalEntryWrite entry, CancellationToken ct, bool verifyAudit = true)
    {
        RequireWriteTenant();
        if (entry.AggregateId == Guid.Empty || entry.Entry.JournalEntryId == Guid.Empty || entry.PeriodId == Guid.Empty)
            throw new LedgerValidationException("Posting identity and period are required.");
        var mappings = await AcquireCashDividendEntitlementsForWritesAsync(connection, transaction, [entry], ct).ConfigureAwait(false);
        var collisions = await FindPostingIdentityCollisionsAsync(connection, transaction,
            LedgerPostingIdentity.FromWrite(entry), ct).ConfigureAwait(false);
        if (collisions.Count == 0)
            return false;
        if (collisions.Count != 1 || !RetainedPostingEquivalence.Matches(collisions[0], entry, out _))
            throw new LedgerValidationException("Posting identity collision: retained accounting content differs from the requested posting.");
        // Retained records identify their book through the authoritative period, not a candidate
        // book claim. The lock precedes audit verification, as for every normal journal append.
        await EnsureTenantRowAsync(connection, transaction, "accounting_periods", "period_id", collisions[0].PeriodId, false, ct).ConfigureAwait(false);
        var period = await LoadPeriodAsync(connection, transaction, collisions[0].PeriodId,
            forUpdate: _options.EnablePeriodLocking,
            callerTenantId: _tenantScope.IsFailClosed ? ResolveCallerTenant() : null, ct: ct).ConfigureAwait(false)
            ?? throw new LedgerValidationException("The retained posting period is unavailable.");
        if (period.LedgerBookId != mappings[entry.PeriodId])
            throw new LedgerValidationException("The retained period book changed while acquiring entitlement authority; retry the transaction.");
        if ((entry.LedgerBookId ?? entry.PostingCommand?.LedgerBookId) is { } claimedBook && claimedBook != period.LedgerBookId)
            throw new LedgerValidationException("The replay book differs from the retained posting's authoritative book.");
        if (verifyAudit)
            _ = await LockAndVerifyLedgerAuditAsync(connection, transaction, ct).ConfigureAwait(false);
        return true;
    }
}
