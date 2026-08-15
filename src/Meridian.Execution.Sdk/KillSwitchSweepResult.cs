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
    /// Whether an operator has to act. True whenever anything is still working, which is the
    /// question the audit trail and the endpoint response both need answered.
    /// </summary>
    public bool RequiresOperatorAction => Outcome is not KillSwitchSweepOutcome.Completed;

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
        if (StillWorking.Count == 0)
        {
            return $"Kill-switch cancel-all cancelled {Cancelled} of {Requested} open order(s).";
        }

        // Bounded: an operator reading an audit entry needs the first few names and the count, not
        // a thousand-line message that the audit surface would truncate anyway.
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
            + $"{StillWorking.Count} still working and requiring manual cancellation: {names}{overflow}.";
    }
}
