using Meridian.Execution.Models;
using Meridian.Execution.Sdk;
using Meridian.Execution.Services;

namespace Meridian.Ui.Shared.Endpoints;

// --- DTOs for execution endpoints ---

/// <summary>Account-level snapshot returned by the execution cockpit.</summary>
public sealed record ExecutionAccountSnapshot(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    int PositionCount,
    DateTimeOffset AsOf);

/// <summary>Full portfolio snapshot including all positions.</summary>
public sealed record ExecutionPortfolioSnapshot(
    decimal Cash,
    decimal PortfolioValue,
    decimal UnrealisedPnl,
    decimal RealisedPnl,
    IReadOnlyList<ExecutionPosition> Positions,
    DateTimeOffset AsOf);

/// <summary>Gateway health summary.</summary>
public sealed record ExecutionGatewayHealth(
    string BrokerName,
    string Mode,
    bool IsAvailable,
    DateTimeOffset AsOf,
    string? SelectedGatewayId = null);

/// <summary>Request to create a new paper trading session.</summary>
public sealed record CreatePaperSessionRequest(
    string StrategyId,
    string? StrategyName,
    decimal InitialCash = 100_000m,
    IReadOnlyList<string>? Symbols = null);

/// <summary>
/// Structured result returned by every Trading write action (cancel, close, pause, etc.).
/// Carries a correlation ID so UI and backend audit logs can be cross-referenced.
/// </summary>
/// <summary>
/// Circuit-breaker activation result: the control state, plus what the coupled kill-switch sweep
/// achieved. The two travel together so opening the breaker cannot report success over a book that
/// still has working orders.
/// <para>
/// The snapshot's members are repeated at the top level rather than nested under a wrapper
/// property, so every existing consumer of this route — the browser workstation and the WPF halt
/// client among them — keeps deserializing the shape it already expects and simply ignores the
/// added sweep. Nesting them would have reported "Stop did not take effect" on a breaker the server
/// had just opened.
/// </para>
/// </summary>
public sealed record ExecutionCircuitBreakerActivationResponse(
    ExecutionCircuitBreakerState CircuitBreaker,
    decimal? DefaultMaxPositionSize,
    IReadOnlyDictionary<string, decimal> SymbolPositionLimits,
    IReadOnlyList<ExecutionManualOverride> ManualOverrides,
    DateTimeOffset AsOf,
    long Version,
    KillSwitchSweepResult Sweep)
{
    public static ExecutionCircuitBreakerActivationResponse From(
        ExecutionControlSnapshot snapshot,
        KillSwitchSweepResult sweep) => new(
            snapshot.CircuitBreaker,
            snapshot.DefaultMaxPositionSize,
            snapshot.SymbolPositionLimits,
            snapshot.ManualOverrides,
            snapshot.AsOf,
            snapshot.Version,
            sweep);
}

public sealed record TradingActionResult(
    string ActionId,
    string Status,
    string Message,
    DateTimeOffset OccurredAt,
    string? AuditId = null,
    /// <summary>
    /// Orders a kill-switch sweep could not cancel, in full. The rendered <paramref name="Message"/>
    /// names only the first few, so this is what a caller uses to finish the job by hand.
    /// </summary>
    IReadOnlyList<KillSwitchSweepFailure>? StillWorking = null,
    /// <summary>
    /// True when the kill-switch sweep could not enumerate the broker's own open-order book and
    /// covered only the in-memory view — the broker may hold working orders the sweep never saw,
    /// so the broker book must be verified by hand even when <c>Status</c> reads Completed.
    /// Null on actions that carry no sweep, and omitted when the broker view was established.
    /// </summary>
    bool? BrokerViewUnavailable = null);

/// <summary>Request to update the global execution circuit breaker.</summary>
public sealed record UpdateExecutionCircuitBreakerRequest(
    bool IsOpen,
    string? Reason = null,
    string? CorrelationId = null);

/// <summary>Request to update the default execution position limit.</summary>
public sealed record UpdateExecutionDefaultPositionLimitRequest(
    decimal? MaxPositionSize,
    string? Reason = null);

/// <summary>Request to update or clear a symbol-specific execution position limit.</summary>
public sealed record UpdateExecutionSymbolPositionLimitRequest(
    decimal? MaxPositionSize,
    string? Reason = null);

/// <summary>Request to create an execution manual override.</summary>
public sealed record CreateExecutionManualOverrideRequest(
    string Kind,
    string Reason,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null,
    string? CorrelationId = null);

/// <summary>Request to clear an existing execution manual override.</summary>
public sealed record ClearExecutionManualOverrideRequest(
    string? Reason = null,
    string? CorrelationId = null);

/// <summary>Legacy request alias preserved for older callers.</summary>
public sealed record CreateManualOverrideCommandRequest(
    string Kind,
    string Reason,
    string? Symbol = null,
    string? StrategyId = null,
    string? RunId = null,
    DateTimeOffset? ExpiresAt = null,
    string? CorrelationId = null);

/// <summary>Legacy request alias preserved for older callers.</summary>
public sealed record ClearManualOverrideCommandRequest(
    string? Reason = null,
    string? CorrelationId = null);
