namespace Meridian.Execution.Services;

/// <summary>
/// Configuration for the brokerage position sync service.
/// Set <see cref="ProviderPriority"/> to control which brokerage connection is queried
/// first when multiple <c>IBrokeragePositionSync</c> implementations are registered.
/// </summary>
public sealed class PositionSyncOptions
{
    /// <summary>
    /// Ordered list of brokerage provider names to attempt for position syncing.
    /// The first entry in the list that has an active connection is used.
    /// <para>
    /// Recognised values (case-insensitive): <c>"Alpaca"</c>, <c>"InteractiveBrokers"</c>.
    /// Any name not matching a registered provider is silently skipped.
    /// Leave empty to accept the default discovery order (all registered providers are queried
    /// in registration order).
    /// </para>
    /// </summary>
    public IReadOnlyList<string> ProviderPriority { get; init; } = [];

    /// <summary>
    /// How often position data is refreshed from the brokerage.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan SyncInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of consecutive failures before the sync service backs off.
    /// </summary>
    public int MaxConsecutiveFailures { get; init; } = 3;

    /// <summary>
    /// Back-off interval applied after <see cref="MaxConsecutiveFailures"/> are reached.
    /// Default: 15 minutes.
    /// </summary>
    public TimeSpan BackOffInterval { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Tolerance threshold for position quantity divergence before a reconciliation alert
    /// is emitted.  E.g. 0.001 means a 0.1% difference is tolerated.
    /// Default: 0 (any difference triggers an alert).
    /// </summary>
    public decimal DivergenceTolerance { get; init; } = 0m;
}
