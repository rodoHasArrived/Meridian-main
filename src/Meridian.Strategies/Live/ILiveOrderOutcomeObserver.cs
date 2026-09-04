namespace Meridian.Strategies.Live;

/// <summary>
/// How an order ended without completing.
/// </summary>
public enum LiveOrderOutcome
{
    /// <summary>The broker acknowledged a cancellation.</summary>
    Cancelled,

    /// <summary>The broker refused the order.</summary>
    Rejected,

    /// <summary>The order expired under its time-in-force.</summary>
    Expired,

    /// <summary>A governed risk approval was declined, so the parked order will never be released.</summary>
    ApprovalDeclined
}

/// <summary>
/// Optional seam for a strategy that tracks its own working orders and needs to know when one
/// reached a terminal state without filling.
/// </summary>
/// <remarks>
/// <para>
/// <c>IBacktestStrategy</c> surfaces fills only, because the replay engine has no notion of a
/// broker refusing an order. A strategy promoted to live execution does: an order can be rejected,
/// expire, be cancelled, or have its governed approval declined, and none of those produce a fill.
/// A strategy that blocks a symbol while its order is working — the only safe way to avoid a double
/// fill when the outcome is unknown — would otherwise block that symbol for the life of the run
/// after a single transient rejection.
/// </para>
/// <para>
/// The seam is deliberately optional and lives outside the backtesting SDK contract: strategies
/// that do not track working orders ignore it, and adding it to <c>IBacktestStrategy</c> would
/// break every existing implementation for a concern that only exists off the replay path.
/// <see cref="BacktestStrategyLiveAdapter"/> forwards to the wrapped strategy when it implements
/// this interface.
/// </para>
/// </remarks>
public interface ILiveOrderOutcomeObserver
{
    /// <summary>
    /// Called once when <paramref name="orderId"/> reaches a terminal state without a completing
    /// fill. Any quantity that did fill before the order ended has already been reported through
    /// <c>OnOrderFill</c>.
    /// </summary>
    void OnOrderTerminated(Guid orderId, LiveOrderOutcome outcome);
}
