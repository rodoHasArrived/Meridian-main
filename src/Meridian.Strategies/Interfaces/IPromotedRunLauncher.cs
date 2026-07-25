using Meridian.Strategies.Models;

namespace Meridian.Strategies.Interfaces;

/// <summary>Outcome of attempting to activate a promoted run on the live trading engine.</summary>
public sealed record RunLaunchResult(bool Launched, string? Reason = null)
{
    public static RunLaunchResult Success() => new(true);

    public static RunLaunchResult Deferred(string reason) => new(false, reason);
}

/// <summary>
/// Activates a promoted paper/live run on an execution engine. Implemented by the live
/// trading engine and invoked by the promotion workflow once the target run entry has been
/// durably recorded, closing the loop between promotion governance and execution.
/// </summary>
public interface IPromotedRunLauncher
{
    /// <summary>
    /// Attempts to start executing the given run. A failed attempt must not throw for
    /// operational reasons (missing strategy implementation, disabled engine): the run entry
    /// stays retained and the result carries the reason so callers can audit and retry.
    /// </summary>
    Task<RunLaunchResult> TryLaunchAsync(StrategyRunEntry run, CancellationToken ct = default);
}
