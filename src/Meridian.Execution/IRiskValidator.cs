using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Pre-trade risk validation. Called by the OMS before routing orders to the gateway.
/// </summary>
public interface IRiskValidator
{
    /// <summary>Validates an order against risk rules.</summary>
    Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>Result of a risk validation check.</summary>
public sealed record RiskValidationResult
{
    public required bool IsApproved { get; init; }
    public string? RejectReason { get; init; }

    /// <summary>
    /// When <see langword="true"/> the order was not hard-rejected: it should be (or has been)
    /// parked for governed operator approval instead of routed. <see cref="IsApproved"/> stays
    /// <see langword="false"/> so callers that only check approval fail closed.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Identifier of the parked escalation entry when the order was parked for governed approval.
    /// </summary>
    public string? EscalationId { get; init; }

    /// <summary>
    /// Non-blocking rule breaches surfaced alongside an approved (or rejected) order,
    /// e.g. warning-severity rule failures or concentration observe-band notices.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>
    /// When the approval consumed a one-shot governed release token, its escalation id.
    /// The OMS re-arms this approval if the gateway faults before the order routes, so a
    /// transient submission failure never permanently retires an operator's decision.
    /// </summary>
    public string? ConsumedApprovalId { get; init; }

    /// <summary>
    /// When <see langword="true"/> the rule refused an order it could not measure rather than
    /// measuring a breach. A Critical rule's breach halts the desk; its inability to price one
    /// order must not, or a stale feed becomes a trading halt. The order is still rejected.
    /// </summary>
    public bool IsUnmeasurable { get; init; }

    public static RiskValidationResult Approved() => new() { IsApproved = true };
    public static RiskValidationResult Rejected(string reason) => new() { IsApproved = false, RejectReason = reason };

    /// <summary>
    /// Rejects an order the rule could not value. Fails closed for this order without
    /// asserting the ceiling was breached, so a Critical rule does not trip the circuit
    /// breaker on a pricing gap.
    /// </summary>
    public static RiskValidationResult Unmeasurable(string reason) =>
        new() { IsApproved = false, RejectReason = reason, IsUnmeasurable = true };

    /// <summary>Approves the order while carrying non-blocking warning flags.</summary>
    public static RiskValidationResult ApprovedWithWarnings(params string[] warnings) =>
        new() { IsApproved = true, Warnings = warnings };

    /// <summary>Marks the order for governed approval rather than hard rejection.</summary>
    public static RiskValidationResult Escalated(string reason, string? escalationId = null) =>
        new() { IsApproved = false, RequiresApproval = true, RejectReason = reason, EscalationId = escalationId };
}
