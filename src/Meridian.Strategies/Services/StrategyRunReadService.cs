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

    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(
        string? strategyId = null,
        RunType? runType = null,
        CancellationToken ct = default)
    {
        var results = new List<StrategyRunSummary>();

        var runs = string.IsNullOrWhiteSpace(strategyId)
            ? _repository.GetAllRunsAsync(ct)
            : _repository.GetRunsAsync(strategyId, ct);

        await foreach (var run in runs.WithCancellation(ct).ConfigureAwait(false))
        {
            if (runType.HasValue && run.RunType != runType.Value)
                continue;

            results.Add(ToSummary(run));
        }

        return results
            .OrderByDescending(static run => run.StartedAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(
        StrategyRunHistoryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var modeFilter = query.Modes is { Count: > 0 }
            ? new HashSet<StrategyRunMode>(query.Modes)
            : null;
        var limit = Math.Clamp(query.Limit, 1, 500);
        var results = new List<StrategyRunSummary>();

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.IsNullOrWhiteSpace(query.StrategyId) &&
                !string.Equals(run.StrategyId, query.StrategyId, StringComparison.Ordinal))
            {
                continue;
            }

            var summary = ToSummary(run);
            if (query.Status.HasValue && summary.Status != query.Status.Value)
            {
                continue;
            }

            if (modeFilter is not null && !modeFilter.Contains(summary.Mode))
            {
                continue;
            }

            results.Add(summary);
        }

        return results
            .OrderByDescending(static run => run.LastUpdatedAt)
            .ThenByDescending(static run => run.StartedAt)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<StrategyRunTimelineEntry>> GetMergedTimelineAsync(
        StrategyRunHistoryQuery query,
        CancellationToken ct = default)
    {
        var runs = await GetRunsAsync(query, ct).ConfigureAwait(false);
        return runs
            .Select(static run => new StrategyRunTimelineEntry(
                RunId: run.RunId,
                StrategyId: run.StrategyId,
                StrategyName: run.StrategyName,
                Mode: run.Mode,
                Status: run.Status,
                StartedAt: run.StartedAt,
                CompletedAt: run.CompletedAt,
                LastUpdatedAt: run.LastUpdatedAt,
                NetPnl: run.NetPnl,
                TotalReturn: run.TotalReturn,
                FillCount: run.FillCount))
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
            .OrderByDescending(static result => result.LastUpdatedAt)
            .ThenByDescending(static result => result.FinalEquity ?? decimal.MinValue)
            .ThenBy(static result => result.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<RunComparisonDto>> GetRunComparisonDtosAsync(
        IEnumerable<string> runIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(runIds);

        var selectedIds = new HashSet<string>(
            runIds.Where(static id => !string.IsNullOrWhiteSpace(id)),
            StringComparer.Ordinal);
        if (selectedIds.Count == 0)
        {
            return Array.Empty<RunComparisonDto>();
        }

        var results = new List<RunComparisonDto>();

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!selectedIds.Contains(run.RunId))
                continue;

            var metrics = run.Metrics?.Metrics;
            var curve = await GetEquityCurveAsync(run.RunId, ct).ConfigureAwait(false);

            results.Add(new RunComparisonDto(
                RunId: run.RunId,
                ParentRunId: run.ParentRunId,
                StrategyName: run.StrategyName,
                Mode: MapMode(run.RunType),
                Engine: MapEngine(run),
                Status: MapStatus(run),
                StartedAt: run.StartedAt,
                CompletedAt: run.EndedAt,
                NetPnl: metrics?.NetPnl,
                TotalReturn: metrics?.TotalReturn,
                AnnualizedReturn: metrics?.AnnualizedReturn,
                FinalEquity: metrics?.FinalEquity,
                SharpeRatio: metrics?.SharpeRatio,
                SortinoRatio: metrics?.SortinoRatio,
                CalmarRatio: metrics?.CalmarRatio,
                MaxDrawdown: metrics?.MaxDrawdown,
                MaxDrawdownPercent: metrics?.MaxDrawdownPercent,
                MaxDrawdownRecoveryDays: metrics?.MaxDrawdownRecoveryDays ?? 0,
                ProfitFactor: metrics?.ProfitFactor,
                WinRate: metrics?.WinRate,
                TotalTrades: metrics?.TotalTrades ?? 0,
                WinningTrades: metrics?.WinningTrades ?? 0,
                LosingTrades: metrics?.LosingTrades ?? 0,
                FillCount: run.Metrics?.Fills.Count ?? 0,
                TotalCommissions: metrics?.TotalCommissions ?? 0m,
                TotalMarginInterest: metrics?.TotalMarginInterest ?? 0m,
                TotalShortRebates: metrics?.TotalShortRebates ?? 0m,
                Xirr: metrics?.Xirr,
                EquityCurve: curve,
                LastUpdatedAt: GetLastUpdatedAt(run),
                PromotionState: BuildPromotionSummary(run).State,
                HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
                HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference)));
        }

        return results
            .OrderByDescending(static r => r.FinalEquity ?? decimal.MinValue)
            .ThenBy(static r => r.RunId, StringComparer.Ordinal)
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
            Governance: BuildGovernanceSummary(run),
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName);
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

    // -----------------------------------------------------------------------
    // Track C: drill-in surfaces
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the equity curve with per-point drawdown for the given run.
    /// Returns <c>null</c> when the run does not exist or has no snapshots recorded.
    /// </summary>
    public async Task<EquityCurveSummary?> GetEquityCurveAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
                continue;

            var snapshots = run.Metrics?.Snapshots;
            if (snapshots is not { Count: > 0 })
                return null;

            var metrics = run.Metrics!.Metrics;
            var points = new List<EquityCurvePoint>(snapshots.Count);
            var peak = snapshots[0].TotalEquity;

            foreach (var snap in snapshots)
            {
                if (snap.TotalEquity > peak)
                    peak = snap.TotalEquity;

                var dd = peak - snap.TotalEquity;
                var ddPct = peak > 0m ? dd / peak : 0m;

                points.Add(new EquityCurvePoint(
                    Date: snap.Date,
                    TotalEquity: snap.TotalEquity,
                    Cash: snap.Cash,
                    DailyReturn: snap.DailyReturn,
                    DrawdownFromPeak: dd,
                    DrawdownFromPeakPercent: ddPct));
            }

            return new EquityCurveSummary(
                RunId: run.RunId,
                InitialEquity: snapshots[0].TotalEquity,
                FinalEquity: snapshots[^1].TotalEquity,
                MaxDrawdown: metrics.MaxDrawdown,
                MaxDrawdownPercent: metrics.MaxDrawdownPercent,
                MaxDrawdownRecoveryDays: metrics.MaxDrawdownRecoveryDays,
                SharpeRatio: metrics.SharpeRatio,
                SortinoRatio: metrics.SortinoRatio,
                Points: points);
        }

        return null;
    }

    /// <summary>
    /// Returns all executed fills for the given run, ordered by fill time.
    /// Returns <c>null</c> when the run does not exist.
    /// </summary>
    public async Task<RunFillSummary?> GetFillsAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
                continue;

            var fills = run.Metrics?.Fills ?? [];
            var entries = fills
                .OrderBy(static f => f.FilledAt)
                .Select(static f => new RunFillEntry(
                    FillId: f.FillId,
                    OrderId: f.OrderId,
                    Symbol: f.Symbol,
                    FilledQuantity: f.FilledQuantity,
                    FillPrice: f.FillPrice,
                    Commission: f.Commission,
                    FilledAt: f.FilledAt,
                    AccountId: f.AccountId))
                .ToArray();

            return new RunFillSummary(
                RunId: run.RunId,
                Mode: MapMode(run.RunType),
                TotalFills: entries.Length,
                TotalCommissions: entries.Sum(static e => e.Commission),
                Fills: entries);
        }

        return null;
    }

    /// <summary>
    /// Returns per-symbol P&amp;L attribution for the given run.
    /// Returns <c>null</c> when the run does not exist or has no metrics.
    /// </summary>
    public async Task<RunAttributionSummary?> GetAttributionAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        await foreach (var run in _repository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (!string.Equals(run.RunId, runId, StringComparison.Ordinal))
                continue;

            var attr = run.Metrics?.Metrics.SymbolAttribution;
            if (attr is null)
                return null;

            var bySymbol = attr.Values
                .OrderByDescending(static a => a.RealizedPnl + a.UnrealizedPnl)
                .Select(static a => new SymbolAttributionEntry(
                    Symbol: a.Symbol,
                    RealizedPnl: a.RealizedPnl,
                    UnrealizedPnl: a.UnrealizedPnl,
                    TotalPnl: a.RealizedPnl + a.UnrealizedPnl,
                    TradeCount: a.TradeCount,
                    Commissions: a.Commissions,
                    MarginInterestAllocated: a.MarginInterestAllocated))
                .ToArray();

            return new RunAttributionSummary(
                RunId: run.RunId,
                Mode: MapMode(run.RunType),
                TotalRealizedPnl: bySymbol.Sum(static a => a.RealizedPnl),
                TotalUnrealizedPnl: bySymbol.Sum(static a => a.UnrealizedPnl),
                TotalCommissions: bySymbol.Sum(static a => a.Commissions),
                BySymbol: bySymbol);
        }

        return null;
    }
}
