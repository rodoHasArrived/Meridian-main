using Meridian.Execution;
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
    /// How this rule's breaches are treated. Fixed per rule, not per order: the validator resolves
    /// admission from this value, which is what makes severity decisional rather than decorative.
    /// <para>
    /// The one per-order exception is <see cref="RiskValidationResult.RequiresApproval"/>, which
    /// escalates a rule that did not declare <see cref="RiskRuleSeverity.Escalate"/>. It can only
    /// make an outcome less permissive than the declared severity, never more.
    /// </para>
    /// </summary>
    RiskRuleSeverity Severity => RiskRuleSeverity.Error;

    /// <summary>
    /// Optional synchronous fast path for rules that do not need I/O or F# interop.
    /// Return <see langword="null"/> to fall back to <see cref="EvaluateAsync"/>.
    /// </summary>
    RiskValidationResult? TryEvaluate(OrderRequest request) => null;

    /// <summary>
    /// Evaluates whether the order passes this risk rule.
    /// <para>
    /// Implementations must not mutate observable state here. The validator evaluates every
    /// blocking rule before the admit/block decision is known, so a rule that recorded state
    /// during evaluation would record orders that a later rule subsequently blocked. A rule that
    /// needs to consume finite capacity implements <see cref="IReservingRiskRule"/> instead.
    /// </para>
    /// </summary>
    Task<RiskValidationResult> EvaluateAsync(OrderRequest request, CancellationToken ct = default);
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
/// <param name="Result">The rule's verdict, exactly as <see cref="IRiskRule.EvaluateAsync"/> would report it.</param>
/// <param name="Reservation">
/// Capacity held for this evaluation, or <see langword="null"/> when nothing was reserved
/// (typically because the rule refused the order instead).
/// </param>
public readonly record struct RiskRuleReservationResult(
    RiskValidationResult Result,
    IRiskReservation? Reservation);
