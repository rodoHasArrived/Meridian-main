namespace Meridian.Ui.Shared.Services.CoveredCall;

/// <summary>
/// Host-level options for the covered-call backtest service.
/// Hot-reloadable via <c>IOptionsMonitor&lt;CoveredCallBacktestOptions&gt;</c>.
/// </summary>
public sealed class CoveredCallBacktestOptions
{
    /// <summary>Configuration section name (<c>Strategies:CoveredCall</c>).</summary>
    public const string SectionName = "Strategies:CoveredCall";

    /// <summary>Soft cap on simultaneous in-flight covered-call runs.</summary>
    public int MaxConcurrentRuns { get; init; } = 3;

    /// <summary>How long a completed run's full result stays cached in memory before re-hydration.</summary>
    public TimeSpan ResultCacheDuration { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>Max strikes per expiration to materialise from the production chain provider.</summary>
    public int MaxStrikesPerExpiration { get; init; } = 80;

    /// <summary>
    /// Optional override of the underlying-bar <c>DataRoot</c> for the backtest engine.
    /// When null, callers must supply a data root in the request (or the engine falls back to its default).
    /// </summary>
    public string? DataRootOverride { get; init; }
}
