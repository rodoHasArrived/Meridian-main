using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Durable posting seam used after a governed workflow has approved a journal write.
/// The workflow remains responsible for validation and approval; this target owns the
/// idempotent handoff to the authoritative journal store.
/// </summary>
public interface IGovernedLedgerPostingTarget
{
    Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default);
}

public sealed record GovernedLedgerPostingResult(
    Guid JournalEntryId,
    bool WasAppended);

/// <summary>
/// Serializes the check-and-append handoff for one process and treats an equivalent
/// retained journal entry as a successful retry. A reused journal id with different
/// accounting content fails closed.
/// </summary>
public sealed class DurableLedgerPostingTarget : IGovernedLedgerPostingTarget, IDisposable
{
    private readonly ILedgerJournalStore _store;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public DurableLedgerPostingTarget(ILedgerJournalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<GovernedLedgerPostingResult> PostAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var retained = await _store
                .GetByAggregateAsync(write.AggregateId, ct)
                .ConfigureAwait(false);
            var existing = retained.FirstOrDefault(record =>
                record.Entry.JournalEntryId == write.Entry.JournalEntryId);
            if (existing is not null)
            {
                EnsureEquivalent(existing, write);
                return new GovernedLedgerPostingResult(write.Entry.JournalEntryId, WasAppended: false);
            }

            await _store.AppendAsync(write, ct).ConfigureAwait(false);
            return new GovernedLedgerPostingResult(write.Entry.JournalEntryId, WasAppended: true);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public void Dispose() => _writeGate.Dispose();

    private static void EnsureEquivalent(
        LedgerJournalEntryRecord existing,
        LedgerJournalEntryWrite requested)
    {
        var retained = existing.Entry;
        var candidate = requested.Entry;
        var equivalent = existing.AggregateId == requested.AggregateId
            && existing.PeriodId == requested.PeriodId
            && existing.AccountingBasis == requested.AccountingBasis
            && retained.Timestamp == candidate.Timestamp
            && string.Equals(retained.Description, candidate.Description, StringComparison.Ordinal)
            && string.Equals(
                retained.Metadata.IdempotencyKey,
                candidate.Metadata.IdempotencyKey,
                StringComparison.OrdinalIgnoreCase)
            && retained.Lines.Count == candidate.Lines.Count
            && retained.Lines.Zip(candidate.Lines, LinesEquivalent).All(static matches => matches);

        if (!equivalent)
        {
            throw new LedgerValidationException(
                $"Journal entry '{candidate.JournalEntryId}' is already retained with different accounting content.");
        }
    }

    private static bool LinesEquivalent(LedgerEntry retained, LedgerEntry candidate)
        => retained.EntryId == candidate.EntryId
           && retained.Account.AccountType == candidate.Account.AccountType
           && string.Equals(retained.Account.Name, candidate.Account.Name, StringComparison.Ordinal)
           && string.Equals(retained.Account.Symbol, candidate.Account.Symbol, StringComparison.OrdinalIgnoreCase)
           && string.Equals(
               retained.Account.FinancialAccountId,
               candidate.Account.FinancialAccountId,
               StringComparison.OrdinalIgnoreCase)
           && retained.Debit == candidate.Debit
           && retained.Credit == candidate.Credit;
}
