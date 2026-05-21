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
    private const string ContinuityStatusHealthy = "Healthy";
    private const string ContinuityStatusWarning = "Warning";
    private const string ContinuityStatusMissing = "Missing";
    private const string ContinuityStatusUnknown = "Unknown";
    private const string LedgerCoverageStatusCovered = "Covered";
    private const string LedgerCoverageStatusMissing = "Missing";
    private const string CashFlowHealthHealthy = "Healthy";
    private const string CashFlowHealthMissing = "Missing";

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters = new Dictionary<string, string>();
    private static readonly IReadOnlyDictionary<string, StrategyPromotionRecord> EmptyPromotionLookup =
        new Dictionary<string, StrategyPromotionRecord>(StringComparer.Ordinal);

    private readonly IStrategyRepository _repository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly LedgerReadService _ledgerReadService;
    private readonly IPromotionRecordStore? _promotionRecordStore;

    internal PortfolioReadService PortfolioReadService => _portfolioReadService;
    internal LedgerReadService LedgerReadService => _ledgerReadService;

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

    public async Task<IReadOnlyList<StrategyRunLineageTimelineEntry>> GetLineageTimelineAsync(
        StrategyRunHistoryQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var runs = await GetRunsAsync(query, ct).ConfigureAwait(false);

        return runs
            .GroupBy(static run => run.Identity?.CanonicalRunKey ?? run.RunId, StringComparer.Ordinal)
            .SelectMany(BuildLineageTimelineEntries)
            .OrderBy(static entry => entry.EventTimestamp)
            .ThenBy(static entry => entry.CanonicalRunKey, StringComparer.Ordinal)
            .ThenBy(static entry => entry.RunId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.EventType)
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
            Governance: BuildGovernanceSummary(run),
            // PR-02: governance hooks for approval/audit/compliance seams
            GovernanceHooks: BuildGovernanceHooks(run, promotionLookup));
    }

    // ── PR-02: mode-filtered and active-run queries ──────────────────────────

    /// <summary>
    /// Returns all runs currently in a <see cref="StrategyRunStatus.Running"/> or
    /// <see cref="StrategyRunStatus.Paused"/> state, ordered most-recently-updated first.
    /// Used by workspace shell tiles and summary stats to surface active run counts.
    /// </summary>
    public async Task<IReadOnlyList<StrategyRunSummary>> GetActiveRunsAsync(CancellationToken ct = default)
    {
        var query = new StrategyRunHistoryQuery(
            Modes: null,
            Status: null,
            Limit: 500);
        var all = await GetRunsAsync(query, ct).ConfigureAwait(false);
        return all
            .Where(static run => run.Status is StrategyRunStatus.Running or StrategyRunStatus.Paused)
            .OrderByDescending(static run => run.LastUpdatedAt)
            .ToArray();
    }

    /// <summary>
    /// Returns runs filtered to a single <paramref name="mode"/>, ordered most-recently-started first.
    /// Used by research/trading surfaces that display only backtest, paper, or live rows.
    /// </summary>
    public async Task<IReadOnlyList<StrategyRunSummary>> GetRunsByModeAsync(
        StrategyRunMode mode,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = new StrategyRunHistoryQuery(
            Modes: [mode],
            Status: null,
            Limit: Math.Clamp(limit, 1, 500));
        return await GetRunsAsync(query, ct).ConfigureAwait(false);
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
        bool governanceFirst = false,
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
            var continuity = BuildComparisonContinuity(run);
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
                HasAuditTrail: !string.IsNullOrWhiteSpace(run.AuditReference),
                ContinuityStatus: continuity.ContinuityStatus,
                ReconciliationBreakCount: continuity.ReconciliationBreakCount,
                ReconciliationHighestSeverity: continuity.HighestSeverity,
                HasLedgerEntryCoverage: continuity.HasLedgerEntryCoverage,
                LedgerCoverageStatus: continuity.LedgerCoverageStatus,
                CashFlowHealth: continuity.CashFlowHealth));
        }

        var ordered = governanceFirst
            ? results
                .OrderByDescending(static run => run.ReconciliationBreakCount)
                .ThenByDescending(static run => run.ReconciliationHighestSeverity)
                .ThenBy(static run => run.ContinuityStatus, StringComparer.Ordinal)
                .ThenByDescending(static run => run.FinalEquity ?? decimal.MinValue)
                .ThenBy(static run => run.RunId, StringComparer.Ordinal)
            : results
                .OrderByDescending(static run => run.FinalEquity ?? decimal.MinValue)
                .ThenBy(static run => run.RunId, StringComparer.Ordinal);

        return ordered.ToArray();
    }

    private static ComparisonContinuitySignals BuildComparisonContinuity(StrategyRunEntry run)
    {
        var hasLedgerReference = !string.IsNullOrWhiteSpace(run.LedgerReference);
        var ledgerEntryCount = run.Metrics?.Ledger?.Journal?.Count ?? 0;
        var hasLedgerEntryCoverage = hasLedgerReference && ledgerEntryCount > 0;
        var cashFlowEntries = run.Metrics?.CashFlows?.Count ?? 0;
        var hasCashFlowCoverage = cashFlowEntries > 0;
        var hasAuditTrail = !string.IsNullOrWhiteSpace(run.AuditReference);

        var continuityStatus = (hasLedgerEntryCoverage, hasCashFlowCoverage, hasAuditTrail) switch
        {
            (true, true, true) => ContinuityStatusHealthy,
            (false, false, false) => ContinuityStatusMissing,
            _ => ContinuityStatusWarning
        };

        return new ComparisonContinuitySignals(
            ContinuityStatus: string.IsNullOrWhiteSpace(continuityStatus) ? ContinuityStatusUnknown : continuityStatus,
            ReconciliationBreakCount: 0,
            HighestSeverity: ReconciliationBreakSeverity.Info,
            HasLedgerEntryCoverage: hasLedgerEntryCoverage,
            LedgerCoverageStatus: hasLedgerEntryCoverage ? LedgerCoverageStatusCovered : LedgerCoverageStatusMissing,
            CashFlowHealth: hasCashFlowCoverage ? CashFlowHealthHealthy : CashFlowHealthMissing);
    }

    private sealed record ComparisonContinuitySignals(
        string ContinuityStatus,
        int ReconciliationBreakCount,
        ReconciliationBreakSeverity HighestSeverity,
        bool HasLedgerEntryCoverage,
        string LedgerCoverageStatus,
        string CashFlowHealth);

    public async Task<IReadOnlyList<StrategySweepResultGroup>> GetSweepResultGroupsAsync(int limit = 25, CancellationToken ct = default)
    {
        var runs = await GetRunsAsync(new StrategyRunHistoryQuery(Limit: Math.Clamp(limit * 20, 1, 500)), ct).ConfigureAwait(false);
        return runs
            .Where(static run => !string.IsNullOrWhiteSpace(run.SweepId) && !string.IsNullOrWhiteSpace(run.SweepDefinitionHash))
            .GroupBy(static run => $"{run.SweepId}|{run.SweepDefinitionHash}", StringComparer.Ordinal)
            .OrderByDescending(static group => group.Max(run => run.StartedAt))
            .Take(Math.Clamp(limit, 1, 100))
            .Select(group =>
            {
                var groupedRuns = group.OrderByDescending(static run => run.StartedAt).ToArray();
                var topObjective = groupedRuns
                    .Where(static run => run.Status == StrategyRunStatus.Completed)
                    .OrderByDescending(static run => run.FinalEquity ?? decimal.MinValue)
                    .ThenByDescending(static run => run.NetPnl ?? decimal.MinValue)
                    .Take(10)
                    .Select(static run => new StrategySweepObjectiveRanking(
                        run.RunId,
                        run.StrategyName,
                        run.FinalEquity,
                        run.NetPnl,
                        run.TotalReturn,
                        run.FinalEquity,
                        run.LastUpdatedAt))
                    .ToArray();

                var head = groupedRuns[0];
                return new StrategySweepResultGroup(
                    SweepId: head.SweepId!,
                    SweepDefinitionHash: head.SweepDefinitionHash!,
                    Objective: head.SweepObjective,
                    StartedAt: groupedRuns.Min(static run => run.StartedAt),
                    RunCount: groupedRuns.Length,
                    Runs: groupedRuns,
                    ObjectiveRankings: topObjective);
            })
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
            Identity: BuildIdentity(run, promotionLookup),
            Execution: BuildExecutionSummary(run),
            Promotion: BuildPromotionSummary(run, promotionLookup),
            Governance: BuildGovernanceSummary(run),
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName,
            ParentRunId: run.ParentRunId,
            SweepId: run.SweepId,
            SweepDefinitionHash: run.SweepDefinitionHash,
            SweepObjective: run.SweepObjective);
    }


    private static StrategyRunIdentity BuildIdentity(
        StrategyRunEntry run,
        IReadOnlyDictionary<string, StrategyPromotionRecord> promotionLookup)
    {
        promotionLookup.TryGetValue(run.RunId, out var matchedRecord);
        var replayReference = run.AuditReference;
        return new StrategyRunIdentity(
            CanonicalRunKey: run.RunId,
            ParentCanonicalRunKey: run.ParentRunId,
            DerivedCanonicalRunKeys: Array.Empty<string>(),
            Mode: MapMode(run.RunType),
            PromotionSourceRunId: matchedRecord?.SourceRunId ?? run.ParentRunId,
            PromotionTargetRunId: matchedRecord?.TargetRunId,
            PromotionDecision: matchedRecord?.Decision,
            ReplayAuditReference: replayReference,
            ReplayVerifiedAt: run.EndedAt,
            HasReplayAudit: !string.IsNullOrWhiteSpace(replayReference));
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

    private static IEnumerable<StrategyRunLineageTimelineEntry> BuildLineageTimelineEntries(
        IGrouping<string, StrategyRunSummary> lineageGroup)
    {
        foreach (var run in lineageGroup)
        {
            var canonicalRunKey = run.Identity?.CanonicalRunKey ?? run.RunId;
            var parentCanonicalRunKey = run.Identity?.ParentCanonicalRunKey;
            var promotionDecision = run.Identity?.PromotionDecision;
            var replayAuditReference = run.Identity?.ReplayAuditReference;
            var replayVerifiedAt = run.Identity?.ReplayVerifiedAt;
            var sourceRunId = run.Identity?.PromotionSourceRunId;
            var targetRunId = run.Identity?.PromotionTargetRunId;

            yield return new StrategyRunLineageTimelineEntry(
                CanonicalRunKey: canonicalRunKey,
                ParentCanonicalRunKey: parentCanonicalRunKey,
                RunId: run.RunId,
                StrategyId: run.StrategyId,
                StrategyName: run.StrategyName,
                Mode: run.Mode,
                Status: run.Status,
                EventTimestamp: run.StartedAt,
                EventType: StrategyRunLineageEventType.RunStarted,
                PromotionDecision: promotionDecision,
                CrossModeTransition: BuildTransitionMetadata(run.Mode, run.Mode, sourceRunId, targetRunId, run.Promotion?.AuditReference, replayAuditReference, replayVerifiedAt, run.Identity?.HasReplayAudit ?? false));

            if (run.CompletedAt is { } completedAt)
            {
                yield return new StrategyRunLineageTimelineEntry(
                    CanonicalRunKey: canonicalRunKey,
                    ParentCanonicalRunKey: parentCanonicalRunKey,
                    RunId: run.RunId,
                    StrategyId: run.StrategyId,
                    StrategyName: run.StrategyName,
                    Mode: run.Mode,
                    Status: run.Status,
                    EventTimestamp: completedAt,
                    EventType: StrategyRunLineageEventType.RunCompleted,
                    PromotionDecision: promotionDecision,
                    CrossModeTransition: null);
            }

            if (!string.IsNullOrWhiteSpace(promotionDecision))
            {
                var targetMode = run.Promotion?.SuggestedNextMode;
                var eventType = targetMode.HasValue && targetMode.Value != run.Mode
                    ? StrategyRunLineageEventType.CrossModeTransition
                    : StrategyRunLineageEventType.PromotionDecision;

                yield return new StrategyRunLineageTimelineEntry(
                    CanonicalRunKey: canonicalRunKey,
                    ParentCanonicalRunKey: parentCanonicalRunKey,
                    RunId: run.RunId,
                    StrategyId: run.StrategyId,
                    StrategyName: run.StrategyName,
                    Mode: run.Mode,
                    Status: run.Status,
                    EventTimestamp: run.LastUpdatedAt,
                    EventType: eventType,
                    PromotionDecision: promotionDecision,
                    CrossModeTransition: BuildTransitionMetadata(run.Mode, targetMode, sourceRunId, targetRunId, run.Promotion?.AuditReference, replayAuditReference, replayVerifiedAt, run.Identity?.HasReplayAudit ?? false));
            }

            if ((run.Identity?.HasReplayAudit ?? false) && replayVerifiedAt is { } verifiedAt)
            {
                yield return new StrategyRunLineageTimelineEntry(
                    CanonicalRunKey: canonicalRunKey,
                    ParentCanonicalRunKey: parentCanonicalRunKey,
                    RunId: run.RunId,
                    StrategyId: run.StrategyId,
                    StrategyName: run.StrategyName,
                    Mode: run.Mode,
                    Status: run.Status,
                    EventTimestamp: verifiedAt,
                    EventType: StrategyRunLineageEventType.ReplayVerified,
                    PromotionDecision: promotionDecision,
                    CrossModeTransition: BuildTransitionMetadata(run.Mode, run.Mode, sourceRunId, targetRunId, run.Promotion?.AuditReference, replayAuditReference, replayVerifiedAt, true));
            }
        }
    }

    private static StrategyRunCrossModeTransitionMetadata BuildTransitionMetadata(
        StrategyRunMode? sourceMode,
        StrategyRunMode? targetMode,
        string? sourceRunId,
        string? targetRunId,
        string? promotionReference,
        string? replayAuditReference,
        DateTimeOffset? replayVerifiedAt,
        bool hasReplayAudit) =>
        new(
            SourceMode: sourceMode,
            TargetMode: targetMode,
            SourceRunId: sourceRunId,
            TargetRunId: targetRunId,
            PromotionReference: promotionReference,
            ReplayAuditReference: replayAuditReference,
            ReplayVerifiedAt: replayVerifiedAt,
            HasReplayAudit: hasReplayAudit);

    internal IReadOnlyList<StrategyRunContinuityWarning> GetPortfolioContinuityWarnings(StrategyRunDetail run) =>
        _portfolioReadService.BuildContinuityWarnings(run.Summary.RunId, run.Portfolio);

    internal IReadOnlyList<StrategyRunContinuityWarning> GetLedgerContinuityWarnings(StrategyRunDetail run) =>
        _ledgerReadService.BuildContinuityWarnings(run.Summary.RunId, run.Ledger);

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
            ApprovedBy = matchedRecord?.ApprovedBy,
            ApprovalChecklist = matchedRecord?.ApprovalChecklist
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

    /// <summary>
    /// Builds the list of governance hooks for a run detail response.
    /// Each hook captures a single governance seam (parameters, portfolio, ledger, audit, promotion).
    /// Seams that are satisfied are included with <see cref="StrategyRunGovernanceHook.IsSatisfied"/> = true;
    /// missing seams are included so callers can surface gaps.
    /// </summary>
    private static IReadOnlyList<StrategyRunGovernanceHook> BuildGovernanceHooks(
        StrategyRunEntry run,
        IReadOnlyDictionary<string, StrategyPromotionRecord> promotionLookup)
    {
        var hooks = new List<StrategyRunGovernanceHook>(5);

        // Parameters seam
        var hasParams = run.ParameterSet is { Count: > 0 };
        hooks.Add(new StrategyRunGovernanceHook(
            SeamId: "parameters",
            Label: "Parameters",
            IsSatisfied: hasParams,
            StatusLabel: hasParams ? "Recorded" : "Missing",
            ExternalReference: null,
            LastEvaluatedAt: hasParams ? run.StartedAt : null,
            Note: hasParams ? null : "Run was started without a captured parameter set."));

        // Portfolio seam
        var hasPortfolio = !string.IsNullOrWhiteSpace(run.PortfolioId);
        hooks.Add(new StrategyRunGovernanceHook(
            SeamId: "portfolio",
            Label: "Portfolio",
            IsSatisfied: hasPortfolio,
            StatusLabel: hasPortfolio ? "Covered" : "Missing",
            ExternalReference: run.PortfolioId,
            LastEvaluatedAt: GetLastUpdatedAt(run),
            Note: hasPortfolio ? null : "No portfolio seam is associated with this run."));

        // Ledger seam
        var hasLedger = !string.IsNullOrWhiteSpace(run.LedgerReference);
        hooks.Add(new StrategyRunGovernanceHook(
            SeamId: "ledger",
            Label: "Ledger",
            IsSatisfied: hasLedger,
            StatusLabel: hasLedger ? "Covered" : "Missing",
            ExternalReference: run.LedgerReference,
            LastEvaluatedAt: GetLastUpdatedAt(run),
            Note: hasLedger ? null : "No ledger reference is associated with this run."));

        // Audit seam
        var hasAudit = !string.IsNullOrWhiteSpace(run.AuditReference);
        hooks.Add(new StrategyRunGovernanceHook(
            SeamId: "audit",
            Label: "Audit Trail",
            IsSatisfied: hasAudit,
            StatusLabel: hasAudit ? "Present" : "Missing",
            ExternalReference: run.AuditReference,
            LastEvaluatedAt: run.EndedAt ?? run.StartedAt,
            Note: hasAudit ? null : "No audit reference was captured for this run."));

        // Promotion seam (only meaningful for non-live runs)
        if (run.RunType != RunType.Live)
        {
            promotionLookup.TryGetValue(run.RunId, out var promo);
            var promoSatisfied = promo is not null;
            hooks.Add(new StrategyRunGovernanceHook(
                SeamId: "promotion",
                Label: "Promotion Review",
                IsSatisfied: promoSatisfied,
                StatusLabel: promo?.Decision ?? (run.EndedAt.HasValue ? "Pending Review" : "Awaiting Completion"),
                ExternalReference: promo?.AuditReference,
                LastEvaluatedAt: promo is not null ? promo.PromotedAt : null,
                Note: promo?.ApprovedBy is not null ? $"Approved by {promo.ApprovedBy}." : null));
        }

        return hooks;
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
