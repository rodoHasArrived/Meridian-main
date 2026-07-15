namespace Meridian.Ledger;

/// <summary>
/// Signed financial reporting bundle derived from point-in-time ledger statements.
/// </summary>
public sealed record LedgerFinancialReportPack(
    LedgerReportPackRequest Request,
    LedgerFinancialStatements Statements,
    IReadOnlyList<LedgerReportPackArtifact> Artifacts,
    LedgerReportPackSignature Signature)
{
    public bool IsBalanced => Statements.AccountingEquationVariance == 0m;

    public LedgerReportPackLifecycleStatus Status { get; init; } = LedgerReportPackLifecycleStatus.Draft;

    public IReadOnlyList<LedgerReportPackLifecycleEvent> LifecycleEvents { get; init; } = [];

    public IReadOnlyList<LedgerReportLineProvenance> LineProvenance { get; init; } = [];

    public IReadOnlyList<LedgerReportPackRestatement> Restatements { get; init; } = [];

    public LedgerFinancialReportPack Validate(string actor, DateTimeOffset occurredAtUtc, string reason, IReadOnlyList<string> evidenceLinks)
        => Transition(LedgerReportPackLifecycleStatus.Validated, actor, occurredAtUtc, reason, evidenceLinks);

    public LedgerFinancialReportPack Approve(string actor, DateTimeOffset occurredAtUtc, string reason, IReadOnlyList<string> evidenceLinks)
        => Transition(LedgerReportPackLifecycleStatus.Approved, actor, occurredAtUtc, reason, evidenceLinks);

    public LedgerFinancialReportPack Publish(string actor, DateTimeOffset occurredAtUtc, string reason, IReadOnlyList<string> evidenceLinks)
        => Transition(LedgerReportPackLifecycleStatus.Published, actor, occurredAtUtc, reason, evidenceLinks);

    public LedgerFinancialReportPack Archive(string actor, DateTimeOffset occurredAtUtc, string reason, IReadOnlyList<string> evidenceLinks)
        => Transition(LedgerReportPackLifecycleStatus.Archived, actor, occurredAtUtc, reason, evidenceLinks);

    public LedgerFinancialReportPack Restate(
        string restatementId,
        string reason,
        string approvedBy,
        DateTimeOffset approvedAtUtc,
        IReadOnlyList<string> changedLineKeys,
        IReadOnlyList<string> evidenceLinks)
    {
        if (Status is not LedgerReportPackLifecycleStatus.Published and not LedgerReportPackLifecycleStatus.Restated)
            throw new InvalidOperationException("Only published report packs can be restated.");
        if (string.IsNullOrWhiteSpace(restatementId))
            throw new ArgumentException("Restatement identifier is required.", nameof(restatementId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Restatement reason is required.", nameof(reason));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Restatement approver is required.", nameof(approvedBy));
        if (changedLineKeys.Count == 0)
            throw new ArgumentException("At least one changed report line is required.", nameof(changedLineKeys));
        var normalizedEvidence = LedgerEvidenceLinks.Normalize(evidenceLinks);
        if (normalizedEvidence.Count == 0)
            throw new ArgumentException("Restatement evidence is required.", nameof(evidenceLinks));

        var restatement = new LedgerReportPackRestatement(
            restatementId.Trim(),
            reason.Trim(),
            approvedBy.Trim(),
            approvedAtUtc.ToUniversalTime(),
            changedLineKeys.Select(static key => key.Trim()).Where(static key => key.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            normalizedEvidence);

        return this with
        {
            Status = LedgerReportPackLifecycleStatus.Restated,
            Restatements = Restatements.Concat([restatement]).ToArray(),
            LifecycleEvents = LifecycleEvents.Concat(
            [
                new LedgerReportPackLifecycleEvent(
                    Status,
                    LedgerReportPackLifecycleStatus.Restated,
                    approvedBy.Trim(),
                    approvedAtUtc.ToUniversalTime(),
                    reason.Trim(),
                    normalizedEvidence)
            ]).ToArray()
        };
    }

    private LedgerFinancialReportPack Transition(
        LedgerReportPackLifecycleStatus toStatus,
        string actor,
        DateTimeOffset occurredAtUtc,
        string reason,
        IReadOnlyList<string> evidenceLinks)
    {
        var (normalizedActor, normalizedReason, normalizedEvidence) = LedgerGovernedLifecycle.PrepareTransition(
            actor,
            reason,
            evidenceLinks,
            requireEvidence: toStatus is LedgerReportPackLifecycleStatus.Approved or LedgerReportPackLifecycleStatus.Published,
            actorRequiredMessage: "Lifecycle actor is required.",
            reasonRequiredMessage: "Lifecycle reason is required.",
            evidenceRequiredMessage: "Approval and publication transitions require retained evidence.",
            transitionAllowed: IsAllowedTransition(Status, toStatus),
            transitionNotAllowedMessage: $"Cannot transition report pack from {Status} to {toStatus}.");

        return this with
        {
            Status = toStatus,
            LifecycleEvents = LifecycleEvents.Concat(
            [
                new LedgerReportPackLifecycleEvent(
                    Status,
                    toStatus,
                    normalizedActor,
                    occurredAtUtc.ToUniversalTime(),
                    normalizedReason,
                    normalizedEvidence)
            ]).ToArray()
        };
    }

    private static bool IsAllowedTransition(LedgerReportPackLifecycleStatus from, LedgerReportPackLifecycleStatus to)
        => from switch
        {
            LedgerReportPackLifecycleStatus.Draft => to is LedgerReportPackLifecycleStatus.Validated or LedgerReportPackLifecycleStatus.Archived,
            LedgerReportPackLifecycleStatus.Validated => to is LedgerReportPackLifecycleStatus.Approved or LedgerReportPackLifecycleStatus.Archived,
            LedgerReportPackLifecycleStatus.Approved => to is LedgerReportPackLifecycleStatus.Published or LedgerReportPackLifecycleStatus.Archived,
            LedgerReportPackLifecycleStatus.Published => to is LedgerReportPackLifecycleStatus.Restated or LedgerReportPackLifecycleStatus.Archived,
            LedgerReportPackLifecycleStatus.Restated => to is LedgerReportPackLifecycleStatus.Archived,
            _ => false
        };
}
