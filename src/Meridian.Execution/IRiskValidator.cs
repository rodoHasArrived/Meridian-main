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
    /// Returns the reservations any stateful rule took, so the caller can settle them at the
    /// routing boundary. Passing the risk gate is not the same as being routed — client-order-id
    /// registration, journaling, or gateway submission can still fail afterwards — so committing
    /// inside the validator would consume capacity for orders that never reach a venue.
    /// </para>
    /// </summary>
    Task<RiskValidationOutcome> ValidateOrderAsync(OrderRequest request, CancellationToken ct = default);
}

/// <summary>
/// What a validator returns: the decision, plus ownership of any capacity reserved while reaching
/// it.
/// </summary>
/// <param name="Result">The aggregate decision and its violations.</param>
/// <param name="Reservations">
/// Reservations transferred to the caller. Empty when no rule reserved anything. The caller must
/// commit these once the order is routed, or roll them back on any earlier failure.
/// </param>
public sealed record RiskValidationOutcome(
    RiskValidationResult Result,
    IReadOnlyList<IRiskReservation> Reservations)
{
    /// <summary>
    /// Snapshotted at construction. <see cref="IReadOnlyList{T}"/> is a read-only view, not an
    /// immutable collection, so a validator that keeps reusing its working list would otherwise
    /// have the OMS settle whatever that list held by the time the order reached the gateway —
    /// leaking the reservations this evaluation actually took and settling another evaluation's in
    /// their place.
    /// </summary>
    public IReadOnlyList<IRiskReservation> Reservations { get; } =
        Reservations?.ToArray() ?? throw new ArgumentNullException(nameof(Reservations));

    /// <summary>An approving outcome that holds no reservations.</summary>
    public static RiskValidationOutcome Approved() =>
        new(RiskValidationResult.Approved(), []);

    /// <summary>Commits every reservation. Call once the order has actually been routed.</summary>
    public void CommitReservations()
    {
        foreach (var reservation in Reservations)
        {
            reservation.Commit();
        }
    }

    /// <summary>
    /// Rolls back every reservation. Call when the order was blocked, or failed anywhere between
    /// the risk gate and the venue.
    /// </summary>
    public void RollbackReservations()
    {
        foreach (var reservation in Reservations)
        {
            reservation.Rollback();
        }
    }
}

/// <summary>
/// Result of a risk validation check.
/// <para>
/// Construction is factory-only, and both members are get-only so a <c>with</c> expression cannot
/// reopen them either. The decision has to follow from the violations — that is the point of
/// severity-driven evaluation — but a public initializer would let any caller, including a
/// host-supplied <see cref="IRiskValidator"/>, pair <see cref="RiskDecisionKind.Approved"/> with a
/// blocking violation. The OMS routes on <see cref="IsApproved"/>, so such a result would send an
/// order carrying an <see cref="RiskRuleSeverity.Error"/> finding to the venue and record it as
/// admitted.
/// </para>
/// </summary>
public sealed record RiskValidationResult
{
    /// <summary>
    /// Takes both values as arguments rather than initialisers so no decision-less instance exists
    /// even briefly. The default <see cref="RiskDecisionKind"/> is
    /// <see cref="RiskDecisionKind.Approved"/>, so an initialiser a later factory forgot to set
    /// would fail open — the one direction a pre-trade gate must never fail.
    /// </summary>
    private RiskValidationResult(RiskDecisionKind decision, IReadOnlyList<RiskViolation> violations)
    {
        Decision = decision;
        Violations = violations;
    }

    /// <summary>The aggregate verdict.</summary>
    public RiskDecisionKind Decision { get; }

    /// <summary>
    /// Every finding, ordered by severity descending, then by the declaring rule's priority
    /// ascending.
    /// </summary>
    public IReadOnlyList<RiskViolation> Violations { get; }

    /// <summary>
    /// True for every decision except <see cref="RiskDecisionKind.Rejected"/>. Preserved for
    /// existing callers.
    /// </summary>
    public bool IsApproved => Decision != RiskDecisionKind.Rejected;

    /// <summary>
    /// The violation that actually blocked the order. Selected by
    /// <see cref="RiskViolation.IsBlocking"/> rather than by position, so a non-blocking finding
    /// that happens to sort first can never be reported as the rejection reason.
    /// </summary>
    public RiskViolation? BlockingViolation => Decision == RiskDecisionKind.Rejected
        ? Violations.FirstOrDefault(static violation => violation.IsBlocking)
        : null;

    /// <summary>The blocking violation's message. Preserved for existing callers.</summary>
    public string? RejectReason => BlockingViolation?.Message;

    /// <summary>Stable code of the blocking violation, for audit attribution.</summary>
    public string? RejectCode => BlockingViolation?.Code;

    /// <summary>Compact form for transport back to the submitter.</summary>
    public RiskDecisionSummary ToSummary() => new(Decision, Violations);

    /// <summary>An approval with no findings.</summary>
    public static RiskValidationResult Approved() =>
        new(RiskDecisionKind.Approved, []);

    /// <summary>
    /// A rejection with a bare reason. Synthesises an unattributed violation so callers that have
    /// not yet migrated to structured findings keep working.
    /// </summary>
    public static RiskValidationResult Rejected(string reason) =>
        new(
            RiskDecisionKind.Rejected,
            [
                new RiskViolation(
                    RuleName: "Unattributed",
                    Severity: RiskRuleSeverity.Error,
                    Code: "RISK_REJECTED",
                    Message: reason)
            ]);

    /// <summary>
    /// Builds an aggregate from an ordered violation set. Blocking severity wins; otherwise an
    /// acknowledgement request escalates; otherwise the findings are annotations.
    /// </summary>
    public static RiskValidationResult FromViolations(IReadOnlyList<RiskViolation> violations)
    {
        ArgumentNullException.ThrowIfNull(violations);

        // Snapshot before deriving anything. IReadOnlyList is a read-only view, not an immutable
        // collection, so a caller holding the underlying List could otherwise add a blocking
        // violation after an empty set produced Approved — leaving IsApproved true over findings
        // that should have rejected. Sealing construction is only half the invariant; this is the
        // other half.
        var snapshot = violations.ToArray();

        if (snapshot.Length == 0)
        {
            return Approved();
        }

        var decision = snapshot.Any(static violation => violation.IsBlocking)
            ? RiskDecisionKind.Rejected
            : snapshot.Any(static violation => violation.RequiresAcknowledgement)
                ? RiskDecisionKind.Escalated
                : RiskDecisionKind.ApprovedWithWarnings;

        return new RiskValidationResult(decision, snapshot);
    }
}
