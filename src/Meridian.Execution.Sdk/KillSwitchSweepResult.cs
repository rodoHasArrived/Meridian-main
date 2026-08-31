namespace Meridian.Execution.Sdk;

/// <summary>What a kill-switch cancel-all sweep established about the open book.</summary>
public enum KillSwitchSweepOutcome
{
    /// <summary>Every order the sweep found was cancelled. The book is empty.</summary>
    Completed,

    /// <summary>
    /// Some orders were cancelled and some are still working. The desk is not halted: whatever
    /// remains can still fill.
    /// </summary>
    Partial,

    /// <summary>Nothing could be cancelled, or the sweep could not establish the book at all.</summary>
    Failed
}

/// <summary>One order the sweep could not cancel, named so an operator can act on it.</summary>
/// <param name="OrderId">The order still working.</param>
/// <param name="Symbol">Its symbol, when known, so the operator does not have to look it up.</param>
/// <param name="Reason">Why the cancellation did not take.</param>
public readonly record struct KillSwitchSweepFailure(string OrderId, string? Symbol, string Reason);

/// <summary>
/// The outcome of a kill-switch cancel-all sweep, per order rather than in aggregate.
/// <para>
/// This type exists because the sweep used to report success by completing. It awaited a
/// cancellation for every order and discarded each result, so a broker that refused one
/// cancellation left that order working while the sweep returned normally and the audit trail
/// recorded <c>Completed</c>. The exit criterion is that activation <em>cancels</em> open orders,
/// which a call that merely ran cannot establish — so the sweep now reports what it actually
/// achieved, and names what it did not.
/// </para>
/// </summary>
/// <param name="Outcome">Whether the book was emptied, partly emptied, or not established.</param>
/// <param name="Requested">How many orders the sweep attempted to cancel.</param>
/// <param name="Cancelled">How many cancellations the sweep confirmed.</param>
/// <param name="StillWorking">
/// The orders that survived the sweep. Empty on <see cref="KillSwitchSweepOutcome.Completed"/>.
/// </param>
public sealed record KillSwitchSweepResult(
    KillSwitchSweepOutcome Outcome,
    int Requested,
    int Cancelled,
    IReadOnlyList<KillSwitchSweepFailure> StillWorking)
{
    /// <summary>
    /// True when the broker's own open-order book could not be enumerated, so the sweep covered
    /// only the in-memory book. Distinct from <see cref="Outcome"/> because the two failures call
    /// for different operator responses: <see cref="StillWorking"/> names orders to cancel by
    /// hand, while this flag means the broker may hold orders the sweep never even saw — after an
    /// OMS restart or for legs the OMS does not track — and the broker book must be verified
    /// directly.
    /// </summary>
    public bool BrokerViewUnavailable { get; init; }

    /// <summary>Why the broker book could not be enumerated, when <see cref="BrokerViewUnavailable"/>.</summary>
    public string? BrokerViewError { get; init; }

    /// <summary>A sweep over an empty book. Vacuously complete, and honestly so.</summary>
    public static KillSwitchSweepResult Empty { get; } =
        new(KillSwitchSweepOutcome.Completed, 0, 0, []);

    /// <summary>
    /// A sweep whose outcome an order manager did not report. Failed rather than completed,
    /// because "we were told nothing" and "the book is empty" are the same value only if you
    /// assume the answer you wanted — and on a kill switch that assumption routes orders.
    /// </summary>
    /// <param name="openCount">How many orders were open when the sweep was requested.</param>
    public static KillSwitchSweepResult Unestablished(int openCount) => new(
        openCount == 0 ? KillSwitchSweepOutcome.Completed : KillSwitchSweepOutcome.Failed,
        openCount,
        0,
        openCount == 0
            ? []
            : [new KillSwitchSweepFailure(
                "unknown",
                null,
                "The order manager did not report what the sweep cancelled; verify the broker book by hand.")]);

    /// <summary>
    /// Whether an operator has to act. True whenever anything is still working — or whenever the
    /// broker book could not be enumerated, because a sweep that never saw the broker's view
    /// cannot establish the book is empty, which is the question the audit trail and the endpoint
    /// response both need answered.
    /// </summary>
    public bool RequiresOperatorAction =>
        Outcome is not KillSwitchSweepOutcome.Completed || BrokerViewUnavailable;

    /// <summary>
    /// Builds the aggregate from per-order outcomes. Partial rather than failed whenever anything
    /// was cancelled, because the two call for different operator responses: a partial sweep leaves
    /// a named list to chase, while a failed one means the kill switch did not fire at all.
    /// </summary>
    public static KillSwitchSweepResult From(int requested, int cancelled, IReadOnlyList<KillSwitchSweepFailure> stillWorking)
    {
        ArgumentNullException.ThrowIfNull(stillWorking);

        if (stillWorking.Count == 0)
        {
            return requested == 0
                ? Empty
                : new KillSwitchSweepResult(KillSwitchSweepOutcome.Completed, requested, cancelled, stillWorking);
        }

        return new KillSwitchSweepResult(
            cancelled > 0 ? KillSwitchSweepOutcome.Partial : KillSwitchSweepOutcome.Failed,
            requested,
            cancelled,
            stillWorking);
    }

    /// <summary>
    /// Operator-facing summary naming the orders still working, because a count alone tells an
    /// operator that something survived without telling them what to cancel by hand.
    /// </summary>
    public string Describe()
    {
        // The broker-view warning is appended to whichever sentence applies below: a sweep that
        // could not see the broker book has not established an empty book no matter how many
        // in-memory orders it cancelled, and every rendering of the outcome must say so.
        var brokerViewWarning = BrokerViewUnavailable
            ? " The broker's open-order book could not be enumerated"
              + (string.IsNullOrWhiteSpace(BrokerViewError) ? string.Empty : $" ({BrokerViewError})")
              + "; broker-side orders may still be working — verify the broker book by hand."
            : string.Empty;

        if (StillWorking.Count == 0)
        {
            return $"Kill-switch cancel-all cancelled {Cancelled} of {Requested} open order(s).{brokerViewWarning}";
        }

        // Bounded, and bounded only here: this is the audit/log rendering, while the structured
        // StillWorking list travels intact on the result and on the endpoint response. An operator
        // told "and 40 more" with no ids cannot carry out the manual cancellation this very
        // sentence instructs, so the ids have to survive somewhere that is not prose.
        const int named = 10;
        var names = string.Join(
            ", ",
            StillWorking.Take(named).Select(static failure =>
                failure.Symbol is { Length: > 0 }
                    ? $"{failure.OrderId} ({failure.Symbol}): {failure.Reason}"
                    : $"{failure.OrderId}: {failure.Reason}"));

        var overflow = StillWorking.Count > named
            ? $", and {StillWorking.Count - named} more"
            : string.Empty;

        return $"Kill-switch cancel-all cancelled {Cancelled} of {Requested} open order(s); "
            + $"{StillWorking.Count} still working and requiring manual cancellation: {names}{overflow}."
            + brokerViewWarning;
    }
}
