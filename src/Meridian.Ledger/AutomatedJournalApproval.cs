namespace Meridian.Ledger;

/// <summary>
/// Governed approval aggregate for a projected automated journal draft.
/// </summary>
public sealed record AutomatedJournalApproval
{
    private AutomatedJournalApproval(
        Guid approvalId,
        Guid journalEntryId,
        IReadOnlyList<Guid> ledgerEntryIds,
        AutomatedJournalDraft draft,
        AutomatedJournalApprovalStatus status,
        IReadOnlyList<AutomatedJournalApprovalEvent> events)
    {
        ApprovalId = approvalId;
        JournalEntryId = journalEntryId;
        LedgerEntryIds = ledgerEntryIds;
        Draft = draft;
        Status = status;
        Events = events;
    }

    public Guid ApprovalId { get; }

    public Guid JournalEntryId { get; }

    public IReadOnlyList<Guid> LedgerEntryIds { get; }

    public AutomatedJournalDraft Draft { get; }

    public AutomatedJournalApprovalStatus Status { get; init; }

    public IReadOnlyList<AutomatedJournalApprovalEvent> Events { get; init; }

    public static AutomatedJournalApproval Submit(
        AutomatedJournalDraft draft,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string>? evidenceLinks = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!draft.IsBalanced)
            throw new InvalidOperationException("Only balanced automated journal drafts can be submitted.");

        var journalEntryId = Guid.NewGuid();
        var lineIds = draft.Lines.Select(_ => Guid.NewGuid()).ToArray();
        var approval = new AutomatedJournalApproval(
            Guid.NewGuid(),
            journalEntryId,
            lineIds,
            draft,
            AutomatedJournalApprovalStatus.Draft,
            []);

        return approval.Transition(
            AutomatedJournalApprovalStatus.Submitted,
            actor,
            occurredAtUtc,
            reason,
            evidenceLinks,
            requireEvidence: false);
    }

    public AutomatedJournalApproval Approve(
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks)
        => Transition(
            AutomatedJournalApprovalStatus.Approved,
            actor,
            occurredAtUtc,
            reason,
            evidenceLinks,
            requireEvidence: true);

    public AutomatedJournalApproval Reject(
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string>? evidenceLinks = null)
        => Transition(
            AutomatedJournalApprovalStatus.Rejected,
            actor,
            occurredAtUtc,
            reason,
            evidenceLinks,
            requireEvidence: false);

    public JournalEntry ToJournalEntry()
    {
        if (Status is not AutomatedJournalApprovalStatus.Approved and not AutomatedJournalApprovalStatus.Posted)
            throw new InvalidOperationException("Only approved automated journal drafts can be converted to journal entries.");

        var metadata = BuildPostingMetadata();
        var entries = Draft.Lines
            .Select((line, index) => new LedgerEntry(
                LedgerEntryIds[index],
                JournalEntryId,
                Draft.Event.Timestamp,
                line.account,
                line.debit,
                line.credit,
                Draft.Description,
                line.dimensions))
            .ToArray();

        return new JournalEntry(JournalEntryId, Draft.Event.Timestamp, Draft.Description, entries, metadata);
    }

    public AutomatedJournalApproval PostTo(
        Ledger ledger,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (Status is not AutomatedJournalApprovalStatus.Approved)
            throw new InvalidOperationException("Only approved automated journal drafts can be posted.");

        ledger.Post(ToJournalEntry());
        return Transition(
            AutomatedJournalApprovalStatus.Posted,
            actor,
            occurredAtUtc,
            reason,
            evidenceLinks,
            requireEvidence: true);
    }

    private AutomatedJournalApproval Transition(
        AutomatedJournalApprovalStatus toStatus,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string>? evidenceLinks,
        bool requireEvidence)
    {
        if (string.IsNullOrWhiteSpace(actor))
            throw new ArgumentException("Approval actor is required.", nameof(actor));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Approval reason is required.", nameof(reason));

        var normalizedEvidence = NormalizeEvidence(evidenceLinks);
        if (requireEvidence && normalizedEvidence.Count == 0)
            throw new ArgumentException("Approval evidence is required for this transition.", nameof(evidenceLinks));
        if (!IsAllowedTransition(Status, toStatus))
            throw new InvalidOperationException($"Cannot transition automated journal approval from {Status} to {toStatus}.");

        var transitionedEvents = Events.Concat(
        [
            new AutomatedJournalApprovalEvent(
                Status,
                toStatus,
                actor.Trim(),
                occurredAtUtc.ToUniversalTime(),
                reason.Trim(),
                normalizedEvidence)
        ]).ToArray();

        return new AutomatedJournalApproval(
            ApprovalId,
            JournalEntryId,
            LedgerEntryIds,
            Draft,
            toStatus,
            transitionedEvents);
    }

    private JournalEntryMetadata BuildPostingMetadata()
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Draft.Metadata.Tags is not null)
        {
            foreach (var pair in Draft.Metadata.Tags)
            {
                tags[pair.Key] = pair.Value;
            }
        }

        tags["automatedJournalApprovalId"] = ApprovalId.ToString("D");
        tags["automatedJournalStatus"] = Status.ToString();

        var approvedBy = Events.LastOrDefault(item => item.ToStatus == AutomatedJournalApprovalStatus.Approved)?.Actor;
        if (!string.IsNullOrWhiteSpace(approvedBy))
            tags["approvedBy"] = approvedBy;

        return Draft.Metadata with { Tags = tags };
    }

    private static bool IsAllowedTransition(AutomatedJournalApprovalStatus from, AutomatedJournalApprovalStatus to)
        => from switch
        {
            AutomatedJournalApprovalStatus.Draft => to is AutomatedJournalApprovalStatus.Submitted,
            AutomatedJournalApprovalStatus.Submitted => to is AutomatedJournalApprovalStatus.Approved or AutomatedJournalApprovalStatus.Rejected,
            AutomatedJournalApprovalStatus.Approved => to is AutomatedJournalApprovalStatus.Posted,
            _ => false
        };

    private static IReadOnlyList<string> NormalizeEvidence(IReadOnlyList<string>? evidenceLinks)
        => evidenceLinks?
            .Select(static link => link.Trim())
            .Where(static link => link.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
}
