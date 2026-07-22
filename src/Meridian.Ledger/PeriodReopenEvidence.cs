namespace Meridian.Ledger;

/// <summary>
/// Evidence required to reopen a locked accounting period. Reopening a closed period is a privileged,
/// exceptional action, so it must carry a reason, an approver, and at least one evidence reference
/// (the approval ticket, the restated working paper, the regulator instruction, ...).
/// </summary>
public sealed record PeriodReopenEvidence
{
    public PeriodReopenEvidence(
        string reopenId,
        string reason,
        string approvedBy,
        DateTimeOffset approvedAtUtc,
        IReadOnlyList<JournalEvidenceReference> evidenceReferences,
        string? ticketReference = null)
    {
        if (string.IsNullOrWhiteSpace(reopenId))
            throw new ArgumentException("Reopen identifier must not be null or whitespace.", nameof(reopenId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A period reopen must record a reason.", nameof(reason));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("A period reopen must record an approver.", nameof(approvedBy));
        ArgumentNullException.ThrowIfNull(evidenceReferences);
        if (evidenceReferences.Count == 0)
            throw new ArgumentException("A period reopen must carry at least one evidence reference.", nameof(evidenceReferences));

        ReopenId = reopenId.Trim();
        Reason = reason.Trim();
        ApprovedBy = approvedBy.Trim();
        ApprovedAtUtc = approvedAtUtc.ToUniversalTime();
        EvidenceReferences = evidenceReferences.Select(static reference => reference.Normalize()).ToArray();
        TicketReference = string.IsNullOrWhiteSpace(ticketReference) ? null : ticketReference.Trim();
    }

    public string ReopenId { get; }

    public string Reason { get; }

    public string ApprovedBy { get; }

    public DateTimeOffset ApprovedAtUtc { get; }

    public IReadOnlyList<JournalEvidenceReference> EvidenceReferences { get; }

    public string? TicketReference { get; }
}

/// <summary>
/// Immutable audit record of a locked accounting period being reopened, capturing the period that was
/// unlocked, who reopened it and when, and the supporting <see cref="PeriodReopenEvidence"/>.
/// </summary>
public sealed record ReopenedAccountingPeriod(
    LockedAccountingPeriod Period,
    DateTimeOffset ReopenedAtUtc,
    string ReopenedBy,
    PeriodReopenEvidence Evidence);
