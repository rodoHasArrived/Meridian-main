using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Models;

/// <summary>
/// An immutable record of a single strategy run (backtest, paper, or live).
/// Stored by <see cref="Storage.StrategyRunStore"/> and used by the promotion workflow.
/// </summary>
public sealed record StrategyRunEntry(
    string RunId,
    string StrategyId,
    string StrategyName,
    RunType RunType,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    BacktestResult? Metrics,
    string? DatasetReference = null,
    string? FeedReference = null,
    string? PortfolioId = null,
    string? LedgerReference = null,
    string? AuditReference = null,
    string? Engine = null,
    IReadOnlyDictionary<string, string>? ParameterSet = null,
    StrategyRunStatus? TerminalStatus = null,
    string? ParentRunId = null,
    string? FundProfileId = null,
    string? FundDisplayName = null)
{
    /// <summary>Creates a new run entry with a generated run ID and current timestamp.</summary>
    public static StrategyRunEntry Start(string strategyId, string strategyName, RunType runType) =>
        new(
            RunId: Guid.NewGuid().ToString("N"),
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: runType,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: null,
            Metrics: null,
            PortfolioId: $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            Engine: runType switch
            {
                RunType.Backtest => "MeridianNative",
                RunType.Paper => "BrokerPaper",
                RunType.Live => "BrokerLive",
                _ => "Unknown"
            });

    /// <summary>Creates a new run entry with an explicit run ID and optional metadata fields.</summary>
    public static StrategyRunEntry Start(
        string strategyId,
        string strategyName,
        RunType runType,
        string runId,
        string? datasetReference = null,
        string? feedReference = null,
        string? engine = null,
        IReadOnlyDictionary<string, string>? parameterSet = null) =>
        new(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: runType,
            StartedAt: DateTimeOffset.UtcNow,
            EndedAt: null,
            Metrics: null,
            DatasetReference: datasetReference,
            FeedReference: feedReference,
            PortfolioId: $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio",
            LedgerReference: $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger",
            Engine: engine,
            ParameterSet: parameterSet);

    /// <summary>Returns a copy of this entry marked as ended with the provided metrics.</summary>
    public StrategyRunEntry Complete(BacktestResult? metrics) =>
        this with { EndedAt = DateTimeOffset.UtcNow, Metrics = metrics };

    /// <summary>Returns a copy of this entry marked as failed at the current time.</summary>
    public StrategyRunEntry Fail() =>
        this with { EndedAt = DateTimeOffset.UtcNow, TerminalStatus = StrategyRunStatus.Failed };

    /// <summary>Returns a copy of this entry marked as cancelled at the current time.</summary>
    public StrategyRunEntry Cancel() =>
        this with { EndedAt = DateTimeOffset.UtcNow, TerminalStatus = StrategyRunStatus.Cancelled };
}
