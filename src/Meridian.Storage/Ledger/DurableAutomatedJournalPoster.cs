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
    private readonly IGovernedLedgerPostingTarget _postingTarget;
    private readonly Meridian.Ledger.Ledger? _projectionLedger;

    public DurableAutomatedJournalPoster(ILedgerJournalStore store, Meridian.Ledger.Ledger? projectionLedger = null)
        : this(new DurableLedgerPostingTarget(store), projectionLedger)
    {
    }

    public DurableAutomatedJournalPoster(
        IGovernedLedgerPostingTarget postingTarget,
        Meridian.Ledger.Ledger? projectionLedger = null)
    {
        _postingTarget = postingTarget ?? throw new ArgumentNullException(nameof(postingTarget));
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
        if (approval.Status == AutomatedJournalApprovalStatus.Posted)
        {
            return approval;
        }
        if (approval.Status != AutomatedJournalApprovalStatus.Approved)
        {
            throw new InvalidOperationException("Only approved automated journal drafts can be posted.");
        }
        if (periodId == Guid.Empty)
            throw new ArgumentException("Period id is required.", nameof(periodId));
        if (evidenceLinks.Count == 0)
            throw new ArgumentException("Posting evidence is required.", nameof(evidenceLinks));

        var entry = approval.ToJournalEntry();

        await _postingTarget.PostAsync(
                new LedgerJournalEntryWrite(
                entry,
                aggregateId ?? approval.ApprovalId,
                periodId),
                ct)
            .ConfigureAwait(false);

        var projection = projectionLedger ?? _projectionLedger;
        if (projection is not null &&
            !projection.Journal.Any(journal => journal.JournalEntryId == entry.JournalEntryId))
        {
            projection.Post(entry);
        }

        return approval.MarkPosted(actor, occurredAtUtc, reason, evidenceLinks);
    }
}
