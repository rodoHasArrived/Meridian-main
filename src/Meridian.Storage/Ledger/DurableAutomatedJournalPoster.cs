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
public sealed class DurableAutomatedJournalPoster : IAutomatedJournalPostingTarget
{
    private readonly ILedgerJournalStore _store;
    private readonly Meridian.Ledger.Ledger? _projectionLedger;

    public DurableAutomatedJournalPoster(ILedgerJournalStore store, Meridian.Ledger.Ledger? projectionLedger = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _projectionLedger = projectionLedger;
    }

    public Task<AutomatedJournalApproval> PostAsync(
        AutomatedJournalApproval approval,
        AutomatedJournalPostingContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return PostAsync(
            approval,
            context.PeriodId,
            context.Actor,
            context.OccurredAtUtc,
            context.Reason,
            context.EvidenceLinks,
            projectionLedger: _projectionLedger,
            aggregateId: context.AggregateId,
            ct);
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
        if (periodId == Guid.Empty)
            throw new ArgumentException("Period id is required.", nameof(periodId));
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

        var projection = projectionLedger ?? _projectionLedger ?? new Meridian.Ledger.Ledger();
        return approval.PostTo(projection, actor, occurredAtUtc, reason, evidenceLinks);
    }
}
