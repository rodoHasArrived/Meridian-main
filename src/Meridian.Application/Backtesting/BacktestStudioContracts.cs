using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;

namespace Meridian.Application.Backtesting;

/// <summary>
/// Application-level request for launching a Backtest Studio run through a concrete engine.
/// </summary>
public sealed record BacktestStudioRunRequest(
    string StrategyId,
    string StrategyName,
    StrategyRunEngine Engine,
    BacktestRequest NativeRequest,
    IBacktestStrategy? Strategy = null,
    string? DatasetReference = null,
    string? FeedReference = null,
    string? BenchmarkSymbol = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    IReadOnlyDictionary<string, string>? ExternalEngineOptions = null);

/// <summary>
/// Stable handle returned when a Backtest Studio run is accepted by an engine.
/// </summary>
public sealed record BacktestStudioRunHandle(
    string RunId,
    string EngineRunHandle,
    StrategyRunEngine Engine);

/// <summary>
/// Current lifecycle status for a Backtest Studio run.
/// </summary>
public sealed record BacktestStudioRunStatus(
    string RunId,
    StrategyRunStatus Status,
    double Progress,
    DateTimeOffset StartedAt,
    DateTimeOffset? EstimatedCompletionAt,
    string? Message = null);

/// <summary>
/// Contract implemented by each Backtest Studio engine integration.
/// </summary>
public interface IBacktestStudioEngine
{
    /// <summary>Engine identity used by orchestration and persistence.</summary>
    StrategyRunEngine Engine { get; }

    /// <summary>Starts a new run and returns a stable handle for later status/result retrieval.</summary>
    Task<BacktestStudioRunHandle> StartAsync(BacktestStudioRunRequest request, CancellationToken ct);

    /// <summary>Returns the current engine-side status for a previously started run.</summary>
    Task<BacktestStudioRunStatus> GetStatusAsync(string runHandle, CancellationToken ct);

    /// <summary>
    /// Returns the canonical result for a previously started run.
    /// Implementations may await completion before returning.
    /// </summary>
    Task<BacktestResult> GetCanonicalResultAsync(string runHandle, CancellationToken ct);
}
