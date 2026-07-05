using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

/// <summary>
/// Posts approved <see cref="AutomatedJournalApproval"/> drafts onto the single ledger
/// spine: the durable <see cref="ILedgerJournalStore"/> append is the source of truth,
/// and an optional in-memory projection <see cref="Meridian.Ledger.Ledger"/> is kept in
/// step for read models that still consume the projector library directly. The durable
/// append happens first so a storage failure leaves the approval un-posted rather than
/// leaving books that disagree.
/// </summary>
public sealed class DurableAutomatedJournalPoster
{
    private readonly ILedgerJournalStore _store;

    public DurableAutomatedJournalPoster(ILedgerJournalStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<AutomatedJournalApproval> PostAsync(
        AutomatedJournalApproval approval,
        Guid periodId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks,
        Meridian.Ledger.Ledger? projectionLedger = null,
        Guid? aggregateId = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(evidenceLinks);
        if (evidenceLinks.Count == 0)
            throw new ArgumentException("Posting evidence is required.", nameof(evidenceLinks));

        // Throws when the approval is not in Approved status, before anything is written.
        var entry = approval.ToJournalEntry();

        await _store.AppendAsync(
            new LedgerJournalEntryWrite(
                entry,
                aggregateId ?? approval.ApprovalId,
                periodId),
            ct).ConfigureAwait(false);

        var projection = projectionLedger ?? new Meridian.Ledger.Ledger();
        return approval.PostTo(projection, actor, occurredAtUtc, reason, evidenceLinks);
    }
}
