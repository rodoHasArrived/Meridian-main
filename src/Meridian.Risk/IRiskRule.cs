using Meridian.Execution.Sdk;

namespace Meridian.Risk;

/// <summary>
/// Individual risk rule that evaluates a single constraint (position limit, drawdown, etc.).
/// </summary>
public interface IRiskRule
{
    /// <summary>Human-readable name, used for attribution on violations and in logs.</summary>
    string RuleName { get; }

    /// <summary>
    /// Lower values run first. Rules with the same priority preserve registration order.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// How this rule's findings are treated. Fixed per rule, not per order: the validator resolves
    /// admission from this value alone, which is what makes severity decisional rather than
    /// decorative.
    /// </summary>
    RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Evaluates one constraint. Returns <see langword="null"/> when the rule is satisfied.
    /// <para>
    /// Implementations must not mutate observable state here. The validator evaluates every rule
    /// before the admit/block decision is known, so a rule that recorded state during evaluation
    /// would record orders that a later rule subsequently blocked. A rule that needs to consume
    /// finite capacity implements <see cref="IReservingRiskRule"/> instead.
    /// </para>
    /// </summary>
    Task<RiskFinding?> EvaluateAsync(OrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Optional synchronous fast path for rules that need no I/O or F# interop. Only consulted
    /// when <see cref="HasSyncFastPath"/> is <see langword="true"/>, because a
    /// <see langword="null"/> return is otherwise ambiguous with "no finding".
    /// </summary>
    RiskFinding? TryEvaluate(OrderRequest request) => null;

    /// <summary>
    /// True when <see cref="TryEvaluate"/> is authoritative and the async path may be skipped.
    /// </summary>
    bool HasSyncFastPath => false;
}

/// <summary>
/// Implemented by rules that consume finite capacity across orders (rate windows, burst counters).
/// <para>
/// The reservation is taken <em>during</em> evaluation, under whatever lock the rule already holds,
/// so the check and the consumption stay atomic. Splitting them across two calls would let
/// concurrent submissions each observe room below the cap and all commit, overshooting it.
/// </para>
/// </summary>
public interface IReservingRiskRule : IRiskRule
{
    /// <summary>
    /// Atomically evaluates and, when the rule is satisfied, reserves the capacity this order
    /// would consume. The reservation is non-null whenever capacity was taken, and must be settled
    /// by the caller on every path.
    /// <para>
    /// <b>An implementation that has taken capacity must not let an exception escape before
    /// returning it.</b> The handle reaches the validator only through the returned result, so a
    /// task that faults or cancels after reserving strands the capacity where nothing can release
    /// it — not the validator, which never saw it, and not the abandonment cleanup, which needs a
    /// completed task to read the handle from. The leak is permanent, and repeated failures would
    /// eventually block every order. Observe the token before reserving, not after; reserve and
    /// return in one uninterruptible step, as <c>OrderRateThrottle</c> does under its lock; or
    /// release the capacity in a <see langword="catch"/> before rethrowing.
    /// </para>
    /// </summary>
    Task<RiskRuleReservationResult> EvaluateAndReserveAsync(
        OrderRequest request,
        CancellationToken ct = default);
}

/// <summary>Outcome of an atomic evaluate-and-reserve step.</summary>
/// <param name="Finding">The rule's finding, or <see langword="null"/> when satisfied.</param>
/// <param name="Reservation">
/// Capacity held for this evaluation, or <see langword="null"/> when nothing was reserved
/// (typically because the rule reported a finding instead).
/// </param>
public readonly record struct RiskRuleReservationResult(
    RiskFinding? Finding,
    IRiskReservation? Reservation);
