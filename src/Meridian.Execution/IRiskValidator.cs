using Meridian.Execution.Sdk;

namespace Meridian.Execution;

/// <summary>
/// Pre-trade risk validation. Called by the OMS before routing orders to the gateway.
/// </summary>
public interface IRiskValidator
{
    /// <summary>
    /// Validates an order against risk rules.
    /// <para>
    /// The returned result carries ownership of any capacity a stateful rule reserved while
    /// reaching the decision, so the caller can settle it at the routing boundary. Passing the
    /// risk gate is not the same as being routed — client-order-id registration, journaling, or
    /// gateway submission can still fail afterwards — so committing inside the validator would
    /// consume capacity for orders that never reach a venue. See
    /// <see cref="RiskValidationResult.Reservations"/>.
    /// </para>
    /// </summary>
    Task<RiskValidationResult> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>Result of a risk validation check.</summary>
public sealed record RiskValidationResult
{
    private readonly IReadOnlyList<string> _warnings = [];
    private readonly IReadOnlyList<RiskViolation> _violations = [];
    private readonly IReadOnlyList<IRiskReservation> _reservations = [];

    public required bool IsApproved { get; init; }
    public string? RejectReason { get; init; }

    /// <summary>
    /// Stable SCREAMING_SNAKE identifier for the breach, e.g. <c>ORDER_NOTIONAL_EXCEEDED</c>.
    /// Carried alongside <see cref="RejectReason"/> so audit and operator surfaces can attribute
    /// a decision without parsing the rendered sentence.
    /// </summary>
    public string? Code { get; init; }

    /// <summary>What the rule measured, when expressible as a number.</summary>
    public decimal? ObservedValue { get; init; }

    /// <summary>What the rule measured against, when expressible as a number.</summary>
    public decimal? LimitValue { get; init; }

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
    public IReadOnlyList<string> Warnings
    {
        get => _warnings;
        init => _warnings = Snapshot(value);
    }

    /// <summary>
    /// Every breach the evaluation recorded, attributed to the rule that raised it and ordered by
    /// severity descending, then by the declaring rule's priority ascending.
    /// <para>
    /// Structured counterpart to <see cref="Warnings"/>. A rule-level result carries at most its
    /// own breach; the aggregate the validator returns carries every rule's. Populated on the
    /// rejected path too, which is what lets an order ticket show all of an order's breaches
    /// rather than only the first one that blocked.
    /// </para>
    /// </summary>
    public IReadOnlyList<RiskViolation> Violations
    {
        get => _violations;
        init => _violations = Snapshot(value);
    }

    /// <summary>
    /// Capacity a stateful rule reserved while reaching this decision, transferred to the caller.
    /// Empty when no rule reserved anything.
    /// <para>
    /// The caller must settle these exactly once on every path — commit once the order is routed,
    /// roll back on any earlier failure. A leaked reservation permanently consumes capacity and
    /// eventually blocks every later order.
    /// </para>
    /// </summary>
    public IReadOnlyList<IRiskReservation> Reservations
    {
        get => _reservations;
        init => _reservations = Snapshot(value);
    }

    /// <summary>
    /// When <see langword="true"/> the rule refused an order it could not measure rather than
    /// measuring a breach. A Critical rule's breach halts the desk; its inability to price one
    /// order must not, or a stale feed becomes a trading halt. The order is still rejected.
    /// </summary>
    public bool IsUnmeasurable { get; init; }

    /// <summary>
    /// The aggregate verdict, derived from <see cref="IsApproved"/>,
    /// <see cref="RequiresApproval"/>, and whether any breach was recorded — never stored, so it
    /// cannot drift from the fields the OMS actually routes on.
    /// </summary>
    public RiskDecisionKind Decision => !IsApproved
        ? RequiresApproval ? RiskDecisionKind.Escalated : RiskDecisionKind.Rejected
        : Violations.Count > 0 || Warnings.Count > 0
            ? RiskDecisionKind.ApprovedWithWarnings
            : RiskDecisionKind.Approved;

    /// <summary>
    /// The violation that actually blocked the order. Selected by
    /// <see cref="RiskViolation.IsBlocking"/> rather than by position, so a non-blocking finding
    /// that happens to sort first can never be reported as the rejection reason.
    /// </summary>
    public RiskViolation? BlockingViolation => IsApproved
        ? null
        : Violations.FirstOrDefault(static violation => violation.IsBlocking);

    /// <summary>Compact form for transport back to the submitter.</summary>
    public RiskDecisionSummary ToSummary() => new(Decision, Violations);

    /// <summary>
    /// Keeps every reserved slot — the order routed. Idempotent, so overlapping settlement paths
    /// cannot double-count.
    /// </summary>
    public void CommitReservations() => SettleAll(commit: true);

    /// <summary>
    /// Returns every reserved slot — nothing reached a venue. Idempotent, so overlapping
    /// settlement paths cannot double-release.
    /// </summary>
    public void RollbackReservations() => SettleAll(commit: false);

    /// <summary>
    /// Settles every reservation even if one throws, then reports the failures together. Stopping
    /// at the first fault would strand the remaining slots, which is the leak this whole mechanism
    /// exists to prevent.
    /// </summary>
    private void SettleAll(bool commit)
    {
        List<Exception>? failures = null;

        foreach (var reservation in _reservations)
        {
            try
            {
                if (commit)
                {
                    reservation.Commit();
                }
                else
                {
                    reservation.Rollback();
                }
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                $"{failures.Count} risk reservation(s) failed to {(commit ? "commit" : "roll back")}.",
                failures);
        }
    }

    /// <summary>
    /// Copies at construction. <see cref="IReadOnlyList{T}"/> is a read-only view, not an immutable
    /// collection: an array or list handed in through it can be cast back to its concrete type and
    /// written through. Without the copy a validator reusing a working list would have the OMS
    /// settle whatever that list held by the time the order reached the gateway — leaking the
    /// reservations this evaluation took and settling another evaluation's in their place.
    /// </summary>
    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? value) =>
        value is null || value.Count == 0 ? [] : value.ToList().AsReadOnly();

    public static RiskValidationResult Approved() => new() { IsApproved = true };

    public static RiskValidationResult Rejected(string reason) =>
        new() { IsApproved = false, RejectReason = reason };

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
