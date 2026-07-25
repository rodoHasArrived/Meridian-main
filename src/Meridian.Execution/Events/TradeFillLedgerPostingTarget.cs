using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.Execution.Events;

/// <summary>
/// Exact accounting scope for one trade-fill posting consumer. Period and book rollover must create
/// a new context (and therefore a separately partitioned posting store) rather than reusing a prior
/// period's handoff.
/// </summary>
public sealed record TradeFillLedgerPostingContext(
    string PostingScope,
    Guid AggregateId,
    Guid PeriodId,
    Guid LedgerBookId,
    string AccountingPolicyId = "execution-trade-fill",
    string AccountingPolicyVersion = "1",
    long? ExpectedPeriodVersion = null)
{
    public TradeFillLedgerPostingContext Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(PostingScope);
        if (AggregateId == Guid.Empty)
            throw new ArgumentException("An accounting aggregate id is required.", nameof(AggregateId));
        if (PeriodId == Guid.Empty)
            throw new ArgumentException("An accounting period id is required.", nameof(PeriodId));
        if (LedgerBookId == Guid.Empty)
            throw new ArgumentException("A ledger book id is required.", nameof(LedgerBookId));
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountingPolicyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(AccountingPolicyVersion);
        if (ExpectedPeriodVersion is < 0)
            throw new ArgumentOutOfRangeException(nameof(ExpectedPeriodVersion));

        return this with
        {
            PostingScope = PostingScope.Trim(),
            AccountingPolicyId = AccountingPolicyId.Trim(),
            AccountingPolicyVersion = AccountingPolicyVersion.Trim()
        };
    }
}

/// <summary>
/// Confirmation that an authoritative ledger store retained the requested journal. The retained
/// record is returned so replay can compare exact economics instead of trusting a fill-id hit.
/// </summary>
public sealed record TradeFillLedgerPostingConfirmation(
    LedgerJournalEntryRecord RetainedRecord,
    bool WasAppended);

/// <summary>
/// Durable trade-fill posting boundary. Implementations must not complete until the authoritative
/// journal store has persisted the write and can read the retained record back.
/// </summary>
public interface ITradeFillLedgerPostingTarget
{
    Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default);
}

/// <summary>
/// Bridges the governed durable posting target to trade-fill replay. A successful governed append
/// is followed by an authoritative collision lookup; absence of the retained journal fails closed.
/// </summary>
public sealed class GovernedTradeFillLedgerPostingTarget : ITradeFillLedgerPostingTarget
{
    private readonly IGovernedLedgerPostingTarget _postingTarget;
    private readonly ILedgerJournalStore _journalStore;

    public GovernedTradeFillLedgerPostingTarget(
        IGovernedLedgerPostingTarget postingTarget,
        ILedgerJournalStore journalStore)
    {
        _postingTarget = postingTarget ?? throw new ArgumentNullException(nameof(postingTarget));
        _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
    }

    public async Task<TradeFillLedgerPostingConfirmation> PostAndConfirmAsync(
        LedgerJournalEntryWrite write,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(write.Entry);

        var result = await _postingTarget.PostAsync(write, ct).ConfigureAwait(false);
        var retained = await _journalStore
            .FindPostingIdentityCollisionsAsync(LedgerPostingIdentity.FromWrite(write), ct)
            .ConfigureAwait(false);
        var retainedRecord = retained.SingleOrDefault(record =>
            record.Entry.JournalEntryId == result.JournalEntryId);
        if (retainedRecord is null)
        {
            throw new InvalidDataException(
                $"Durable ledger target reported journal '{result.JournalEntryId:D}' but the authoritative store could not read it back.");
        }

        return new TradeFillLedgerPostingConfirmation(retainedRecord, result.WasAppended);
    }
}
