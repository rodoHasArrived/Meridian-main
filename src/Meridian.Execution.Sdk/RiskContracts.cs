using System.Text.Json.Serialization;

namespace Meridian.Execution.Sdk;

/// <summary>
/// How seriously a rule treats its own findings.
/// <para>
/// This is the single lever that decides admission. The validator maps it to an outcome and no
/// rule can override that mapping per order, so a rule's declared severity and its effect can
/// never disagree.
/// </para>
/// <para>
/// Lives here rather than in <c>Meridian.Risk</c> because <see cref="RiskViolation"/> carries it
/// and <c>Meridian.Execution</c>'s <c>RiskValidationResult</c> exposes those violations.
/// <c>Meridian.Risk</c> references <c>Meridian.Execution</c>, so the reverse reference would be a
/// project cycle.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskRuleSeverity>))]
public enum RiskRuleSeverity
{
    /// <summary>Recorded and surfaced. Admits the order.</summary>
    Info,

    /// <summary>Recorded and surfaced more prominently. Admits the order.</summary>
    Warning,

    /// <summary>Blocks the order.</summary>
    Error,

    /// <summary>Blocks the order. Reserved for guardrails whose breach implies a halt.</summary>
    Critical
}

/// <summary>Aggregate verdict for one pre-trade evaluation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RiskDecisionKind>))]
public enum RiskDecisionKind
{
    /// <summary>Admitted with no findings.</summary>
    Approved,

    /// <summary>Admitted; findings recorded for operator visibility.</summary>
    ApprovedWithWarnings,

    /// <summary>Admitted; a finding asked for operator acknowledgement.</summary>
    Escalated,

    /// <summary>Blocked.</summary>
    Rejected
}

/// <summary>
/// What a rule measured, and against what.
/// <para>
/// A finding never states what should happen about it — that is <see cref="RiskRuleSeverity"/>'s
/// job, resolved by the validator. Returning <see langword="null"/> from a rule means "no finding".
/// </para>
/// </summary>
/// <param name="Code">Stable SCREAMING_SNAKE identifier, e.g. <c>POSITION_LIMIT_EXCEEDED</c>.</param>
/// <param name="Message">Human-readable summary for operator surfaces.</param>
/// <param name="ObservedValue">What the rule measured, when expressible as a number.</param>
/// <param name="LimitValue">What the rule measured against, when expressible as a number.</param>
/// <param name="RequiresAcknowledgement">
/// Marks the decision <see cref="RiskDecisionKind.Escalated"/> instead of
/// <see cref="RiskDecisionKind.ApprovedWithWarnings"/>. Escalation is a separate axis from severity:
/// a finding may be low-severity yet still warrant a human looking at it.
/// <para>
/// <b>This does not hold the order.</b> <c>Escalated</c> is an admitting decision — the OMS routes
/// on <c>IsApproved</c>, which is true for every decision except
/// <see cref="RiskDecisionKind.Rejected"/> — so the flag records that acknowledgement is wanted and
/// surfaces it to the operator afterwards. It does not gate, queue, or await anything. A rule that
/// needs an order stopped must declare a blocking <see cref="RiskRuleSeverity"/>; that is the only
/// lever that prevents routing.
/// </para>
/// <para>
/// Ignored when the declaring rule's severity blocks, since a blocked order is never admitted and
/// there is nothing to acknowledge.
/// </para>
/// </param>
public sealed record RiskFinding(
    string Code,
    string Message,
    decimal? ObservedValue = null,
    decimal? LimitValue = null,
    bool RequiresAcknowledgement = false);

/// <summary>
/// A finding attributed to the rule that raised it, as the validator records it.
/// <para>
/// Constructed by the validator, which is what guarantees <see cref="Severity"/> is the declaring
/// rule's own and not a value chosen per order.
/// </para>
/// </summary>
public sealed record RiskViolation(
    string RuleName,
    RiskRuleSeverity Severity,
    string Code,
    string Message,
    decimal? ObservedValue = null,
    decimal? LimitValue = null,
    bool RequiresAcknowledgement = false)
{
    /// <summary>
    /// True when this violation is one that blocks. Derived from <see cref="Severity"/> so it can
    /// never disagree with the validator's own admission logic.
    /// <para>
    /// Stated as "not explicitly non-blocking" rather than "Error or Critical". A C# enum can hold
    /// any value of its underlying type — a cast or a numeric deserialization in host code is
    /// enough — and this is a public SDK contract on a fail-closed gate. Listing the blocking
    /// severities would admit an unrecognised one with warnings; listing the safe ones rejects it.
    /// </para>
    /// </summary>
    public bool IsBlocking => Severity is not (RiskRuleSeverity.Info or RiskRuleSeverity.Warning);
}

/// <summary>
/// Compact decision summary returned to the submitter on <see cref="OrderResult"/>, so an order
/// ticket can render findings for both admitted and rejected submissions without a second query.
/// </summary>
public sealed record RiskDecisionSummary(
    RiskDecisionKind Decision,
    IReadOnlyList<RiskViolation> Violations);

/// <summary>
/// Capacity held for one in-flight evaluation by a rule that consumes a finite resource
/// (rate windows, burst counters).
/// <para>
/// Exactly one of <see cref="Commit"/> or <see cref="Rollback"/> takes effect; both are idempotent
/// so a cleanup path can settle unconditionally without double-settling.
/// </para>
/// <para>
/// Ownership moves: the validator rolls reservations back if evaluation throws or is cancelled, and
/// transfers them to the caller on a normal return. Only the caller commits, and only once the
/// order has actually been routed.
/// </para>
/// </summary>
public interface IRiskReservation
{
    /// <summary>Keeps the reserved capacity — the order was routed.</summary>
    void Commit();

    /// <summary>
    /// Returns the reserved capacity — the order was blocked, threw, was cancelled, or failed
    /// somewhere downstream of the risk gate.
    /// </summary>
    void Rollback();
}
