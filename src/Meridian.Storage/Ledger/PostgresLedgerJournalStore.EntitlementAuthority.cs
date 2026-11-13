using Meridian.Storage.SecurityMaster;
using Npgsql;

namespace Meridian.Storage.Ledger;

public sealed partial class PostgresLedgerJournalStore
{
    /// <summary>Resolve authoritative period mappings before any period lock; callers never choose entitlement lock order.</summary>
    private async Task<IReadOnlyDictionary<Guid, Guid?>> AcquireCashDividendEntitlementsForWritesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyList<LedgerJournalEntryWrite> entries,
        CancellationToken ct)
    {
        var mappings = new Dictionary<Guid, Guid?>();
        foreach (var write in entries.OrderBy(item => item.PeriodId))
        {
            if (!mappings.TryGetValue(write.PeriodId, out var bookId))
            {
                // The scoped non-locking read must precede entitlement. EnsureTenantRowAsync
                // uses FOR SHARE in strict mode and is deliberately deferred until afterward.
                await EnsureTenantRowAsync(connection, transaction, "accounting_periods", "period_id", write.PeriodId, false, ct,
                    retainLock: false).ConfigureAwait(false);
                var period = await LoadPeriodAsync(connection, transaction, write.PeriodId, forUpdate: false,
                    callerTenantId: _tenantScope.IsFailClosed ? ResolveCallerTenant() : null, ct: ct).ConfigureAwait(false)
                    ?? throw new Meridian.Ledger.LedgerValidationException("The posting period does not exist.");
                bookId = period.LedgerBookId;
                mappings.Add(write.PeriodId, bookId);
            }
            if ((write.LedgerBookId ?? write.PostingCommand?.LedgerBookId) is { } claimed && claimed != bookId)
                throw new Meridian.Ledger.LedgerValidationException("The posting book conflicts with the authoritative period book.");
        }
        foreach (var book in mappings.Values.Where(item => item.HasValue).Select(item => item!.Value).Distinct().Order())
            await CashDividendEntitlementLock.AcquireAsync(connection, transaction, book, false, ct).ConfigureAwait(false);
        return mappings;
    }
}
