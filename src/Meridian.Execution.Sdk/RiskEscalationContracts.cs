using Meridian.Execution.Sdk;

namespace Meridian.Execution.Services;

/// <summary>Lifecycle of a parked risk escalation.</summary>
public enum RiskEscalationStatus
{
    PendingApproval,
    Approved,
    Denied,
    Released
}

/// <summary>
/// An order parked by the pre-trade risk gate awaiting governed operator approval.
/// The original <see cref="OrderRequest"/> is retained so an approval can release
/// exactly the order that was evaluated, never a caller-supplied variant.
/// </summary>
public sealed record RiskEscalationEntry(
    string EscalationId,
    OrderRequest Request,
    string Reason,
    string? RuleName,
    string? Actor,
    string? RunId,
    string? CorrelationId,
    DateTimeOffset ParkedAt,
    RiskEscalationStatus Status,
    string? ResolvedBy = null,
    string? ResolutionReason = null,
    DateTimeOffset? ResolvedAt = null,
    // Release is a distinct act from approval: overwriting the approver's identity and
    // timestamp when the order is later released would claim the approver performed the
    // release and lose when the decision was actually made.
    string? ReleasedBy = null,
    DateTimeOffset? ReleasedAt = null,
    bool ReleaseInFlight = false,
    // Server-written marker recording that the entry's Actor binding was established by
    // the current Park path. Clients never controlled this record field — unlike request
    // metadata, which was unreserved before the retained-submitter migration — so a null
    // value reliably identifies a pre-migration entry whose identity binding cannot be
    // trusted.
    string? SubmitterProvenance = null);

/// <summary>Persisted snapshot of the governed-approval queue.</summary>
public sealed record RiskEscalationSnapshot(IReadOnlyList<RiskEscalationEntry> Entries);

/// <summary>Governed approval authority consumed by the risk engine.</summary>
public interface IRiskEscalationQueue
{
    IReadOnlyList<RiskEscalationEntry> TryConsumeApprovals(OrderRequest request);
    bool TryRestoreApproval(string escalationId);
    RiskEscalationEntry Park(OrderRequest request, string reason, string? ruleName = null,
        string? actor = null, string? runId = null, string? correlationId = null,
        IReadOnlyList<RiskEscalationEntry>? consumedApprovals = null);
}

/// <summary>Shared metadata vocabulary; no queue implementation dependency.</summary>
public static class RiskEscalationMetadata
{
    public const string EvaluationOnlyMetadataKey = "riskEvaluationOnly";
    public const string IncrementalNotionalMetadataKey = "riskIncrementalNotional";
    public static string JoinTokens(IEnumerable<string> escalationIds) =>
        string.Join(",", escalationIds.Distinct(StringComparer.OrdinalIgnoreCase));
}
