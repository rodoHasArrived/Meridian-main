namespace Meridian.Contracts.RuleEvaluation;

/// <summary>
/// Identifies a kernel input subject and supporting context.
/// </summary>
public sealed record DecisionInput(
    string SubjectId,
    string? SubjectType = null,
    string? Context = null,
    IReadOnlyDictionary<string, string?>? Attributes = null);

/// <summary>
/// Indicates how severe a specific rule reason is for downstream automation and UI.
/// </summary>
public enum DecisionSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
    Critical = 3
}

/// <summary>
/// Explains a single weighted rule contribution to a kernel decision.
/// </summary>
public sealed record DecisionReason(
    string RuleId,
    double Weight,
    string ReasonCode,
    string HumanExplanation,
    DecisionSeverity Severity = DecisionSeverity.Info,
    IReadOnlyList<string>? EvidenceRefs = null);

/// <summary>
/// Versioned and traceable metadata for decision kernel outputs.
/// </summary>
public sealed record DecisionTrace(
    string SchemaVersion,
    string KernelVersion,
    DateTimeOffset EvaluatedAt,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string?>? Metadata = null);

/// <summary>
/// Standardized output envelope for all rule-evaluation kernels.
/// </summary>
public sealed record DecisionResult<TScore>(
    TScore Score,
    IReadOnlyList<DecisionReason> Reasons,
    DecisionTrace Trace);

/// <summary>
/// Contract for rule kernels that evaluate a decision subject into a standardized envelope.
/// </summary>
public interface IDecisionKernel<in TInput, TScore>
{
    DecisionResult<TScore> Evaluate(TInput input, CancellationToken ct = default);
}
