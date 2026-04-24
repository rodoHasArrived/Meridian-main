using Meridian.Contracts.Workstation;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;

namespace Meridian.Strategies.Services;

/// <summary>
/// Provides the shared Phase 12 run browser/read model for backtest, paper, and live history.
/// </summary>
public sealed class StrategyRunReadService
{
    private static readonly IReadOnlyDictionary<string, string> EmptyParameters = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, StrategyPromotionRecord> EmptyPromotionLookup =
        new Dictionary<string, StrategyPromotionRecord>(StringComparer.Ordinal);

    private readonly IStrategyRepository _repository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly LedgerReadService _ledgerReadService;
    private readonly IPromotionRecordStore? _promotionRecordStore;

    public StrategyRunReadService(
        IStrategyRepository repository,
        PortfolioReadService portfolioReadService,
        LedgerReadService ledgerReadService,
        IPromotionRecordStore? promotionRecordStore = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _ledgerReadService = ledgerReadService ?? throw new ArgumentNullException(nameof(ledgerReadService));
        _promotionRecordStore = promotionRecordStore;
    }

    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(
        string? strategyId = null,
        RunType? runType = null,
        CancellationToken ct = default)
    {
        var repositoryQuery = new StrategyRunRepositoryQuery(
            StrategyId: string.IsNullOrWhiteSpace(strategyId) ? null : strategyId,
            RunTypes: runType.HasValue ? [runType.Value] : null,
            Limit: int.MaxValue);
        var runs = await _repository.QueryRunsAsync(repositoryQuery, ct).ConfigureAwait(false);
        var promotionLookup = await LoadPromotionLookupAsync(ct).ConfigureAwait(false);

        return runs
            .Select(run => ToSummary(run, promotionLookup))
            .OrderByDescending(static run => run.StartedAt)
            .ThenBy(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsAsync(
        StrategyRunHistoryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var repositoryQuery = new StrategyRunRepositoryQuery(
            StrategyId: string.IsNullOrWhiteSpace(query.StrategyId) ? null : query.StrategyId,
            RunTypes: MapModesToRunTypes(query.Modes),
            Status: query.Status,
            Limit: Math.Clamp(query.Limit, 1, 500));
        var runs = await _repository.QueryRunsAsync(repositoryQuery, ct).ConfigureAwait(false);
        var promotionLookup = await LoadPromotionLookupAsync(ct).ConfigureAwait(false);

        return runs
            .Select(run => ToSummary(run, promotionLookup))
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

        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var promotionLookup = await LoadPromotionLookupAsync(ct).ConfigureAwait(false);
        var portfolioTask = _portfolioReadService.BuildSummaryAsync(run, ct);
        var ledgerTask = _ledgerReadService.BuildSummaryAsync(run, ct);

        await Task.WhenAll(portfolioTask, ledgerTask).ConfigureAwait(false);

        return new StrategyRunDetail(
            Summary: ToSummary(run, promotionLookup),
            Parameters: run.ParameterSet ?? EmptyParameters,
            Portfolio: await portfolioTask.ConfigureAwait(false),
            Ledger: await ledgerTask.ConfigureAwait(false),
            Execution: BuildExecutionSummary(run),
            Promotion: BuildPromotionSummary(run, promotionLookup),
            Governance: BuildGovernanceSummary(run));
    }

    public async Task<LedgerSummary?> GetLedgerSummaryAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        return run is null
            ? null
            : await _ledgerReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
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

        var runs = await _repository.GetRunsByIdsAsync(selectedIds, ct).ConfigureAwait(false);
        var promotionLookup = await LoadPromotionLookupAsync(ct).ConfigureAwait(false);
        var results = new List<StrategyRunComparison>(runs.Count);

        foreach (var run in runs)
        {
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
                PromotionState: BuildPromotionSummary(run, promotionLookup).State,
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

        var runs = await _repository.GetRunsByIdsAsync(selectedIds, ct).ConfigureAwait(false);
        var promotionLookup = await LoadPromotionLookupAsync(ct).ConfigureAwait(false);
        var results = new List<RunComparisonDto>(runs.Count);

        foreach (var run in runs)
        {
            var metrics = run.Metrics?.Metrics;
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
                EquityCurve: BuildEquityCurve(run),
                LastUpdatedAt: GetLastUpdatedAt(run),
                PromotionState: BuildPromotionSummary(run, promotionLookup).State,
                HasLedger: !string.IsNullOrWhiteSpace(run.LedgerReference),
                HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference)));
        }

        return results
            .OrderByDescending(static run => run.FinalEquity ?? decimal.MinValue)
            .ThenBy(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private StrategyRunSummary ToSummary(
        StrategyRunEntry run,
        IReadOnlyDictionary<string, StrategyPromotionRecord> promotionLookup)
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
            Promotion: BuildPromotionSummary(run, promotionLookup),
            Governance: BuildGovernanceSummary(run),
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName,
            ParentRunId: run.ParentRunId);
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

    private static StrategyRunPromotionSummary BuildPromotionSummary(
        StrategyRunEntry run,
        IReadOnlyDictionary<string, StrategyPromotionRecord> promotionLookup)
    {
        promotionLookup.TryGetValue(run.RunId, out var matchedRecord);

        StrategyRunPromotionSummary summary;
        if (run.RunType == RunType.Live)
        {
            summary = new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.LiveManaged,
                SuggestedNextMode: null,
                RequiresReview: false,
                Reason: "Live runs are already at the terminal operating mode.");
        }
        else if (!run.EndedAt.HasValue)
        {
            summary = new StrategyRunPromotionSummary(
                State: StrategyRunPromotionState.RequiresCompletion,
                SuggestedNextMode: null,
                RequiresReview: true,
                Reason: "Run completion is required before promotion review can begin.");
        }
        else
        {
            summary = run.RunType switch
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

        var sourceRunId = matchedRecord?.SourceRunId ?? run.ParentRunId;
        var targetRunId = matchedRecord?.TargetRunId
            ?? (run.ParentRunId is not null ? run.RunId : null);
        var auditReference = matchedRecord?.AuditReference ?? run.AuditReference;
        var approvalStatus = matchedRecord?.Decision
            ?? (run.ParentRunId is not null ? PromotionDecisionKinds.Approved : null);

        return summary with
        {
            SourceRunId = sourceRunId,
            TargetRunId = targetRunId,
            AuditReference = auditReference,
            ApprovalStatus = approvalStatus,
            ManualOverrideId = matchedRecord?.ManualOverrideId,
            ApprovedBy = matchedRecord?.ApprovedBy
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

    private async Task<IReadOnlyDictionary<string, StrategyPromotionRecord>> LoadPromotionLookupAsync(CancellationToken ct)
    {
        if (_promotionRecordStore is null)
        {
            return EmptyPromotionLookup;
        }

        var records = await _promotionRecordStore.LoadAllAsync(ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return EmptyPromotionLookup;
        }

        var lookup = new Dictionary<string, StrategyPromotionRecord>(records.Count, StringComparer.Ordinal);
        foreach (var record in records)
        {
            UpdatePromotionLookup(lookup, record.SourceRunId, record);
            UpdatePromotionLookup(lookup, record.TargetRunId, record);
        }

        return lookup;
    }

    private static void UpdatePromotionLookup(
        Dictionary<string, StrategyPromotionRecord> lookup,
        string? runId,
        StrategyPromotionRecord record)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        if (!lookup.TryGetValue(runId, out var existing) ||
            record.PromotedAt > existing.PromotedAt)
        {
            lookup[runId] = record;
        }
    }

    private static IReadOnlyList<RunType>? MapModesToRunTypes(IReadOnlyList<StrategyRunMode>? modes)
    {
        if (modes is not { Count: > 0 })
        {
            return null;
        }

        return modes
            .Select(MapRunType)
            .Distinct()
            .ToArray();
    }

    private static StrategyRunMode MapMode(RunType runType) => runType switch
    {
        RunType.Backtest => StrategyRunMode.Backtest,
        RunType.Paper => StrategyRunMode.Paper,
        RunType.Live => StrategyRunMode.Live,
        _ => StrategyRunMode.Backtest
    };

    private static RunType MapRunType(StrategyRunMode mode) => mode switch
    {
        StrategyRunMode.Backtest => RunType.Backtest,
        StrategyRunMode.Paper => RunType.Paper,
        StrategyRunMode.Live => RunType.Live,
        _ => RunType.Backtest
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

    private static StrategyRunStatus MapStatus(StrategyRunEntry run) =>
        StrategyRunRepositoryOrdering.MapStatus(run);

    private static DateTimeOffset GetLastUpdatedAt(StrategyRunEntry run) =>
        StrategyRunRepositoryOrdering.GetLastUpdatedAt(run);

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

        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        return run is null
            ? null
            : BuildEquityCurve(run);
    }

    private static EquityCurveSummary? BuildEquityCurve(StrategyRunEntry run)
    {
        var snapshots = run.Metrics?.Snapshots;
        if (snapshots is not { Count: > 0 })
        {
            return null;
        }

        var metrics = run.Metrics!.Metrics;
        var points = new List<EquityCurvePoint>(snapshots.Count);
        var peak = snapshots[0].TotalEquity;

        foreach (var snapshot in snapshots)
        {
            if (snapshot.TotalEquity > peak)
            {
                peak = snapshot.TotalEquity;
            }

            var drawdown = peak - snapshot.TotalEquity;
            var drawdownPercent = peak > 0m ? drawdown / peak : 0m;

            points.Add(new EquityCurvePoint(
                Date: snapshot.Date,
                TotalEquity: snapshot.TotalEquity,
                Cash: snapshot.Cash,
                DailyReturn: snapshot.DailyReturn,
                DrawdownFromPeak: drawdown,
                DrawdownFromPeakPercent: drawdownPercent));
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

    /// <summary>
    /// Returns all executed fills for the given run, ordered by fill time.
    /// Returns <c>null</c> when the run does not exist.
    /// </summary>
    public async Task<RunFillSummary?> GetFillsAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var fills = run.Metrics?.Fills ?? [];
        var entries = fills
            .OrderBy(static fill => fill.FilledAt)
            .Select(static fill => new RunFillEntry(
                FillId: fill.FillId,
                OrderId: fill.OrderId,
                Symbol: fill.Symbol,
                FilledQuantity: fill.FilledQuantity,
                FillPrice: fill.FillPrice,
                Commission: fill.Commission,
                FilledAt: fill.FilledAt,
                AccountId: fill.AccountId))
            .ToArray();

        return new RunFillSummary(
            RunId: run.RunId,
            Mode: MapMode(run.RunType),
            TotalFills: entries.Length,
            TotalCommissions: entries.Sum(static entry => entry.Commission),
            Fills: entries);
    }

    /// <summary>
    /// Returns per-symbol P&amp;L attribution for the given run.
    /// Returns <c>null</c> when the run does not exist or has no metrics.
    /// </summary>
    public async Task<RunAttributionSummary?> GetAttributionAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var run = await _repository.GetRunByIdAsync(runId, ct).ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var attribution = run.Metrics?.Metrics.SymbolAttribution;
        if (attribution is null)
        {
            return null;
        }

        var bySymbol = attribution.Values
            .OrderByDescending(static item => item.RealizedPnl + item.UnrealizedPnl)
            .Select(static item => new SymbolAttributionEntry(
                Symbol: item.Symbol,
                RealizedPnl: item.RealizedPnl,
                UnrealizedPnl: item.UnrealizedPnl,
                TotalPnl: item.RealizedPnl + item.UnrealizedPnl,
                TradeCount: item.TradeCount,
                Commissions: item.Commissions,
                MarginInterestAllocated: item.MarginInterestAllocated))
            .ToArray();

        return new RunAttributionSummary(
            RunId: run.RunId,
            Mode: MapMode(run.RunType),
            TotalRealizedPnl: bySymbol.Sum(static item => item.RealizedPnl),
            TotalUnrealizedPnl: bySymbol.Sum(static item => item.UnrealizedPnl),
            TotalCommissions: bySymbol.Sum(static item => item.Commissions),
            BySymbol: bySymbol);
    }
}
