namespace Meridian.Risk;

/// <summary>
/// Follow-up action invoked after <see cref="CompositeRiskValidator"/> trips the execution
/// circuit breaker on a critical breach. The production registration sweeps the open order
/// book (<c>IOrderManager.CancelAllAsync</c>), coupling the automated halt to the same
/// cancel-all the operator kill-switch endpoint performs — a breaker that merely blocks new
/// submissions leaves resting orders filling while routing is "halted".
/// <para>
/// The handler runs strictly after the trip: it must never gate, delay, or revert the halt.
/// Implementations should report their own outcome (structured logs, audit trail) rather than
/// throw; the validator suppresses any escaping exception so a failed sweep cannot leak back
/// into the pre-trade risk path. Hosts without an order manager simply leave this unregistered
/// and keep trip-only behavior.
/// </para>
/// </summary>
public interface ICircuitBreakerTripHandler
{
    /// <summary>
    /// Invoked once per demanded halt, after the breaker trip has been applied (or latched
    /// fail-closed when the durable flip could not persist).
    /// </summary>
    /// <param name="reason">The full trip reason, naming the critical rule and its breach.</param>
    /// <param name="trippedBy">The acting identity (e.g. <c>risk-engine/&lt;rule&gt;</c>) for audit attribution.</param>
    /// <param name="ct">
    /// Cancellation for the follow-up work. The validator passes <see cref="CancellationToken.None"/>:
    /// the sweep is owed to the desk, not to the order submission that exposed the breach.
    /// </param>
    Task OnCircuitBreakerTrippedAsync(string reason, string trippedBy, CancellationToken ct);
}
