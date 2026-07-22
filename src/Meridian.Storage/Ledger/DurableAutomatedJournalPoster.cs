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
            context.LedgerBookId,
            context.ExpectedPeriodVersion,
            projectionLedger: _projectionLedger,
            aggregateId: context.AggregateId,
            accountingPolicyId: context.AccountingPolicyId,
            accountingPolicyVersion: context.AccountingPolicyVersion,
            ct);
    }

    public async Task<AutomatedJournalApproval> PostAsync(
        AutomatedJournalApproval approval,
        Guid periodId,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks,
        Guid ledgerBookId,
        long expectedPeriodVersion,
        Meridian.Ledger.Ledger? projectionLedger = null,
        Guid? aggregateId = null,
        string accountingPolicyId = "automated-journal",
        string accountingPolicyVersion = "1",
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
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("Ledger book id is required.", nameof(ledgerBookId));
        if (expectedPeriodVersion < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedPeriodVersion));
        if (evidenceLinks.Count == 0)
            throw new ArgumentException("Posting evidence is required.", nameof(evidenceLinks));

        var entry = approval.ToJournalEntry();
        var resolvedAggregateId = aggregateId ?? approval.ApprovalId;
        var commandId = approval.ApprovalId;
        var sourceEventId = approval.JournalEntryId;
        var idempotencyKey = entry.Metadata.IdempotencyKey
            ?? $"automated-journal:{ledgerBookId:N}:{approval.ApprovalId:N}";
        var postingCommand = new Meridian.Contracts.Ledger.AccountingPostingCommandDto(
            commandId,
            resolvedAggregateId,
            periodId,
            DateOnly.FromDateTime(occurredAtUtc.UtcDateTime),
            occurredAtUtc,
            idempotencyKey,
            Meridian.Contracts.Ledger.AccountingPostingIntentDto.Originating,
            SourceEventId: sourceEventId,
            CorrelationId: approval.ApprovalId,
            CausationId: approval.ApprovalId,
            ExpectedVersion: expectedPeriodVersion,
            SourceEventType: "AutomatedJournalApproval",
            ApprovalState: Meridian.Contracts.Ledger.AccountingPostingApprovalStateDto.Approved,
            ApprovalId: approval.ApprovalId.ToString("D"),
            OperatorRationale: reason,
            Evidence: evidenceLinks.Select(link => new Meridian.Contracts.Ledger.AccountingPostingEvidenceReferenceDto(
                link,
                link,
                Meridian.Contracts.Ledger.AccountingPostingEvidenceKindDto.Approval,
                "AutomatedJournalApproval",
                occurredAtUtc,
                actor,
                SubjectId: approval.ApprovalId.ToString("D"))).ToArray(),
            LedgerBookId: ledgerBookId);

        // CRASH-RETRY INVARIANT: a crash between this durable append and the caller
        // persisting the MarkPosted status re-enters here with Status=Approved. That retry
        // is safe only because the posting target pre-flights the posting identity
        // (journal-entry id / aggregate) and resolves an equivalent retained entry as a
        // successful no-op append (see DurableLedgerPostingTarget.PostAsync) — the DB
        // uniqueness constraint is the backstop, not the guard. Any replacement posting
        // target must preserve that idempotent-retry contract.
        await _postingTarget.PostAsync(
                new LedgerJournalEntryWrite(
                entry,
                resolvedAggregateId,
                periodId,
                CommandId: commandId,
                CorrelationId: approval.ApprovalId,
                AccountingPolicyId: accountingPolicyId,
                AccountingPolicyVersion: accountingPolicyVersion,
                SourceEventId: sourceEventId,
                PostingCommand: postingCommand,
                LedgerBookId: ledgerBookId),
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
