using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;

namespace Meridian.Strategies.Services;

/// <summary>
/// Provides the shared Phase 12 run browser/read model for backtest, paper, and live history.
/// </summary>
public sealed class StrategyRunReadService
{
    private readonly IStrategyRepository _repository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly LedgerReadService _ledgerReadService;

    public StrategyRunReadService(
        IStrategyRepository repository,
        PortfolioReadService portfolioReadService,
        LedgerReadService ledgerReadService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _ledgerReadService = ledgerReadService ?? throw new ArgumentNullException(nameof(ledgerReadService));
    }

    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(string? strategyId = null, CancellationToken ct = default)
    {
        var results = new List<StrategyRunSummary>();

        var runs = string.IsNullOrWhiteSpace(strategyId)
            ? _repository.GetAllRunsAsync(ct)
            : _repository.GetRunsAsync(strategyId, ct);

        await foreach (var run in runs.WithCancellation(ct).ConfigureAwait(false))
        {
            results.Add(ToSummary(run));
        }

        return results
            .OrderByDescending(static run => run.StartedAt)
            .ToArray();
    }

    public async Task<StrategyRunDetail?> GetRunDetailAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                continue;
            }

            var portfolioTask = _portfolioReadService.BuildSummaryAsync(run, ct);
            var ledgerTask = _ledgerReadService.BuildSummaryAsync(run, ct);

            await Task.WhenAll(portfolioTask, ledgerTask).ConfigureAwait(false);

            return new StrategyRunDetail(
                Summary: ToSummary(run),
                Parameters: run.ParameterSet ?? EmptyParameters,
                Portfolio: await portfolioTask.ConfigureAwait(false),
                Ledger: await ledgerTask.ConfigureAwait(false),
                Execution: BuildExecutionSummary(run),
                Promotion: BuildPromotionSummary(run),
                Governance: BuildGovernanceSummary(run));
        }

        return null;
    }

    public async Task<LedgerSummary?> GetLedgerSummaryAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
            {
                continue;
            }

            return await _ledgerReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
        }

        return null;
    }

    public async Task<IReadOnlyList<StrategyRunComparison>> CompareRunsAsync(
        IEnumerable<string> runIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        var selectedIds = new HashSet<string>(
            runIds.Where(static id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        if (selectedIds.Count == 0)
        {
            return Array.Empty<StrategyRunComparison>();
        }

        var results = new List<StrategyRunComparison>();
        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!selectedIds.Contains(run.RunId))
            {
                continue;
            }

            var metrics = run.Metrics?.Metrics;
            results.Add(new StrategyRunComparison(
                RunId: run.RunId,
                StrategyName: run.StrategyName,
                Mode: MapMode(run.RunType),
                Engine: MapEngine(run),
                Status: MapStatus(run),
                NetPnl: metrics?.NetPnl,
                TotalReturn: metrics?.TotalReturn,
                FinalEquity: metrics?.FinalEquity,
                MaxDrawdown: metrics?.MaxDrawdown,
                SharpeRatio: metrics?.SharpeRatio,
                FillCount: run.Metrics?.Fills.Count ?? 0,
                LastUpdatedAt: GetLastUpdatedAt(run),
                PromotionState: BuildPromotionSummary(run).State,
                HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
                HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference)));
        }

        return results
            .OrderByDescending(static result => result.FinalEquity ?? decimal.MinValue)
            .ThenBy(static result => result.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private static StrategyRunSummary ToSummary(StrategyRunEntry run)
    {
        var metrics = run.Metrics?.Metrics;
        return new StrategyRunSummary(
            RunId: run.RunId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            Mode: MapMode(run.RunType),
            Engine: MapEngine(run),
            Status: MapStatus(run),
            StartedAt: run.StartedAt,
            CompletedAt: run.EndedAt,
            DatasetReference: run.DatasetReference,
            FeedReference: run.FeedReference,
            PortfolioId: run.PortfolioId,
            LedgerReference: run.LedgerReference,
            NetPnl: metrics?.NetPnl,
            TotalReturn: metrics?.TotalReturn,
            FinalEquity: metrics?.FinalEquity,
            FillCount: run.Metrics?.Fills.Count ?? 0,
            LastUpdatedAt: GetLastUpdatedAt(run),
            AuditReference: run.AuditReference,
            Execution: BuildExecutionSummary(run),
            Promotion: BuildPromotionSummary(run),
            Governance: BuildGovernanceSummary(run));
    }

    private static StrategyRunExecutionSummary BuildExecutionSummary(StrategyRunEntry run)
    {
        var metrics = run.Metrics?.Metrics;
        return new StrategyRunExecutionSummary(
            FillCount: run.Metrics?.Fills.Count ?? 0,
            TotalTrades: metrics?.TotalTrades ?? 0,
            WinningTrades: metrics?.WinningTrades ?? 0,
            LosingTrades: metrics?.LosingTrades ?? 0,
            TotalCommissions: metrics?.TotalCommissions ?? 0m,
            TotalMarginInterest: metrics?.TotalMarginInterest ?? 0m,
            TotalShortRebates: metrics?.TotalShortRebates ?? 0m,
            HasPortfolio: !string.IsNullOrWhiteSpace(run.PortfolioId),
            HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
            HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference),
            AuditReference: run.AuditReference);
    }

    private static StrategyRunPromotionSummary BuildPromotionSummary(StrategyRunEntry run)
    {
        if (run.RunType == RunType.Live)
        {
            return new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.LiveManaged,
                SuggestedNextMode: null,
                RequiresReview: false,
                Reason: "Live runs are already at the terminal operating mode.");
        }

        if (!run.EndedAt.HasValue)
        {
            return new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.RequiresCompletion,
                SuggestedNextMode: null,
                RequiresReview: true,
                Reason: "Run completion is required before promotion review can begin.");
        }

        return run.RunType switch
        {
            RunType.Backtest => new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.CandidateForPaper,
                SuggestedNextMode: StrategyRunMode.Paper,
                RequiresReview: true,
                Reason: "Completed backtests can be reviewed for paper promotion."),
            RunType.Paper => new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.CandidateForLive,
                SuggestedNextMode: StrategyRunMode.Live,
                RequiresReview: true,
                Reason: "Completed paper runs can be reviewed for live promotion."),
            _ => new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.None,
                SuggestedNextMode: null,
                RequiresReview: false,
                Reason: "No promotion guidance is available for this run type.")
        };
    }

    private static StrategyRunGovernanceSummary BuildGovernanceSummary(StrategyRunEntry run)
    {
        return new StrategyRunGovernanceSummary(
            LastUpdatedAt: GetLastUpdatedAt(run),
            HasParameters: run.ParameterSet is { Count: > 0 },
            HasPortfolio: !string.IsNullOrWhiteSpace(run.PortfolioId),
            HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
            HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference),
            AuditReference: run.AuditReference,
            DatasetReference: run.DatasetReference,
            FeedReference: run.FeedReference);
    }

    private static DateTimeOffset GetLastUpdatedAt(StrategyRunEntry run) => run.EndedAt ?? run.StartedAt;

    private static StrategyRunMode MapMode(RunType runType) => runType switch
    {
        RunType.Backtest => StrategyRunMode.Backtest,
        RunType.Paper => StrategyRunMode.Paper,
        RunType.Live => StrategyRunMode.Live,
        _ => StrategyRunMode.Backtest
    };

    private static StrategyRunEngine MapEngine(StrategyRunEntry run)
    {
        var engine = run.Engine;
        if (string.IsNullOrWhiteSpace(engine))
        {
            return run.RunType switch
            {
                RunType.Backtest => StrategyRunEngine.MeridianNative,
                RunType.Paper => StrategyRunEngine.BrokerPaper,
                RunType.Live => StrategyRunEngine.BrokerLive,
                _ => StrategyRunEngine.Unknown
            };
        }

        return engine.ToLowerInvariant() switch
        {
            "meridiannative" => StrategyRunEngine.MeridianNative,
            "lean" => StrategyRunEngine.Lean,
            "brokerpaper" => StrategyRunEngine.BrokerPaper,
            "brokerlive" => StrategyRunEngine.BrokerLive,
            _ => StrategyRunEngine.Unknown
        };
    }

    private static StrategyRunStatus MapStatus(StrategyRunEntry run)
    {
        if (run.TerminalStatus.HasValue)
            return run.TerminalStatus.Value;

        if (run.EndedAt.HasValue)
            return StrategyRunStatus.Completed;

        return StrategyRunStatus.Running;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters = new Dictionary<string, string>();
}
