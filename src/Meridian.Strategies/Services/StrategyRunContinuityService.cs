using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds a shared run-centered continuity drill-in across portfolio, ledger, cash-flow, and reconciliation seams.
/// </summary>
public sealed class StrategyRunContinuityService
{
    private const int ReconciliationTimingToleranceMinutes = 5;
    private const int StaleRunWindowMinutes = 30;

    private readonly StrategyRunReadService _runReadService;
    private readonly CashFlowProjectionService _cashFlowProjectionService;
    private readonly IReconciliationRunService _reconciliationRunService;

    public StrategyRunContinuityService(
        StrategyRunReadService runReadService,
        CashFlowProjectionService cashFlowProjectionService,
        IReconciliationRunService reconciliationRunService)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _cashFlowProjectionService = cashFlowProjectionService ?? throw new ArgumentNullException(nameof(cashFlowProjectionService));
        _reconciliationRunService = reconciliationRunService ?? throw new ArgumentNullException(nameof(reconciliationRunService));
    }

    public async Task<StrategyRunContinuityDetail?> GetRunContinuityAsync(string runId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var runTask = _runReadService.GetRunDetailAsync(runId, ct);
        var runsTask = _runReadService.GetRunsAsync(ct: ct);
        var cashFlowTask = _cashFlowProjectionService.GetAsync(runId, ct: ct);
        var reconciliationTask = _reconciliationRunService.GetLatestForRunAsync(runId, ct);

        await Task.WhenAll(runTask, runsTask, cashFlowTask, reconciliationTask).ConfigureAwait(false);

        var run = await runTask.ConfigureAwait(false);
        if (run is null)
        {
            return null;
        }

        var runs = await runsTask.ConfigureAwait(false);
        var cashFlow = await cashFlowTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);

        return new StrategyRunContinuityDetail(
            Run: run,
            Lineage: BuildLineage(run.Summary, runs),
            CashFlow: BuildCashFlowDigest(cashFlow),
            Reconciliation: reconciliation?.Summary,
            ContinuityStatus: BuildContinuityStatus(run, cashFlow, reconciliation));
    }

    private static StrategyRunContinuityLineage BuildLineage(
        StrategyRunSummary run,
        IReadOnlyList<StrategyRunSummary> runs)
    {
        var parent = string.IsNullOrWhiteSpace(run.ParentRunId)
            ? null
            : runs.FirstOrDefault(candidate => string.Equals(candidate.RunId, run.ParentRunId, StringComparison.Ordinal));

        var children = runs
            .Where(candidate => string.Equals(candidate.ParentRunId, run.RunId, StringComparison.Ordinal))
            .OrderBy(candidate => candidate.StartedAt)
            .Select(MapLink)
            .ToArray();

        return new StrategyRunContinuityLineage(
            ParentRunId: run.ParentRunId,
            ParentRun: parent is null ? null : MapLink(parent),
            ChildRuns: children);
    }

    private static StrategyRunContinuityLink MapLink(StrategyRunSummary run) =>
        new(
            RunId: run.RunId,
            StrategyId: run.StrategyId,
            StrategyName: run.StrategyName,
            Mode: run.Mode,
            Status: run.Status,
            StartedAt: run.StartedAt,
            CompletedAt: run.CompletedAt,
            PromotionState: run.Promotion?.State ?? StrategyRunPromotionState.None,
            FundProfileId: run.FundProfileId,
            FundDisplayName: run.FundDisplayName);

    private static StrategyRunCashFlowDigest? BuildCashFlowDigest(RunCashFlowSummary? cashFlow)
    {
        if (cashFlow is null)
        {
            return null;
        }

        var nextBucket = cashFlow.Ladder.Buckets
            .OrderBy(static bucket => bucket.BucketStart)
            .FirstOrDefault();

        return new StrategyRunCashFlowDigest(
            AsOf: cashFlow.AsOf,
            Currency: cashFlow.Currency,
            TotalEntries: cashFlow.TotalEntries,
            TotalInflows: cashFlow.TotalInflows,
            TotalOutflows: cashFlow.TotalOutflows,
            NetCashFlow: cashFlow.NetCashFlow,
            ProjectedNetPosition: cashFlow.Ladder.NetPosition,
            BucketCount: cashFlow.Ladder.Buckets.Count,
            NextBucketStart: nextBucket?.BucketStart,
            NextBucketEnd: nextBucket?.BucketEnd,
            NextBucketNetFlow: nextBucket?.NetFlow);
    }

    private StrategyRunContinuityStatus BuildContinuityStatus(
        StrategyRunDetail run,
        RunCashFlowSummary? cashFlow,
        ReconciliationRunDetail? reconciliation)
    {
        var promotion = run.Summary.Promotion;
        var hasParent = !string.IsNullOrWhiteSpace(run.Summary.ParentRunId);
        var promotionSource = promotion?.SourceRunId;
        var promotionTarget = promotion?.TargetRunId;
        var promotionState = promotion?.State ?? StrategyRunPromotionState.None;
        var hasPortfolio = run.Portfolio is not null;
        var hasLedger = run.Ledger is not null;
        var hasCashFlow = cashFlow is { TotalEntries: > 0 };
        var hasFills = (run.Execution?.FillCount ?? run.Summary.FillCount) > 0;
        var hasReconciliation = reconciliation is not null;
        var hasRun = run.Summary is not null;
        var hasFills = run.Summary.FillCount > 0;
        var runHealth = GetRunHealth(run);
        var fillsHealth = hasFills ? StrategyRunContinuitySeamHealthStatus.Healthy : StrategyRunContinuitySeamHealthStatus.Missing;
        var portfolioHealth = hasPortfolio ? StrategyRunContinuitySeamHealthStatus.Healthy : StrategyRunContinuitySeamHealthStatus.Missing;
        var ledgerHealth = hasLedger ? StrategyRunContinuitySeamHealthStatus.Healthy : StrategyRunContinuitySeamHealthStatus.Missing;
        var cashFlowHealth = hasCashFlow ? StrategyRunContinuitySeamHealthStatus.Healthy : StrategyRunContinuitySeamHealthStatus.Missing;
        var reconciliationHealth = hasReconciliation ? StrategyRunContinuitySeamHealthStatus.Healthy : StrategyRunContinuitySeamHealthStatus.Missing;
        var asOfDriftMinutes = CalculateAsOfDriftMinutes(run.Portfolio?.AsOf, run.Ledger?.AsOf);
        var openBreaks = reconciliation?.Summary.OpenBreakCount ?? 0;
        var securityCoverageIssues = reconciliation?.Summary.SecurityIssueCount
            ?? ((run.Portfolio?.SecurityMissingCount ?? 0) + (run.Ledger?.SecurityMissingCount ?? 0));

        var warnings = new List<StrategyRunContinuityWarning>();
        warnings.AddRange(_runReadService.GetPortfolioContinuityWarnings(run));
        warnings.AddRange(_runReadService.GetLedgerContinuityWarnings(run));

        if (!hasCashFlow)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-cash-flow",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: "Run has no recorded cash flows for cash-financing continuity.",
                SourceSeam: "cash-flow"));
        }

        if (!hasFills)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-fills",
                Message: "Run has no execution fills recorded for continuity review."));
        }

        if (!hasReconciliation)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-reconciliation",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: "Run has not been linked to a reconciliation result yet.",
                SourceSeam: "reconciliation"));
        }
        if (!hasFills)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-fills",
                Severity: StrategyRunContinuityWarningSeverity.Info,
                Message: "Run has no recorded fills yet.",
                SourceSeam: "fills"));
        }

        if (asOfDriftMinutes > ReconciliationTimingToleranceMinutes)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "as-of-drift",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: $"Portfolio and ledger timestamps drift by {asOfDriftMinutes} minutes.",
                SourceSeam: "portfolio-ledger"));
        }

        if (openBreaks > 0)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "open-reconciliation-breaks",
                Severity: StrategyRunContinuityWarningSeverity.Critical,
                Message: $"Run has {openBreaks} open reconciliation break(s).",
                SourceSeam: "reconciliation"));
        }

        if (securityCoverageIssues > 0)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "security-coverage",
                Severity: StrategyRunContinuityWarningSeverity.Warning,
                Message: $"Run has {securityCoverageIssues} security coverage issue(s) across continuity surfaces.",
                SourceSeam: "security-master"));
        }

        if (run.Summary.Promotion?.State is StrategyRunPromotionState.CandidateForLive && run.Summary.Mode is StrategyRunMode.Backtest)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "lineage-promotion-gap",
                Message: "Run is promoted toward live operations from a backtest lineage; validate paper lineage continuity."));
        if (hasParent
            && !string.IsNullOrWhiteSpace(promotionSource)
            && !string.Equals(run.Summary.ParentRunId, promotionSource, StringComparison.Ordinal))
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "lineage-parent-source-mismatch",
                Message: "Run has a parent link, but promotion source does not match the recorded parent run."));
        }

        if (!hasParent && !string.IsNullOrWhiteSpace(promotionSource))
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "lineage-missing-parent-with-source",
                Message: "Run has no parent link, but promotion source claims upstream ancestry."));
        }

        if ((promotionState is StrategyRunPromotionState.CandidateForPaper or StrategyRunPromotionState.CandidateForLive)
            && string.IsNullOrWhiteSpace(promotionTarget))
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "promotion-target-run-missing",
                Message: "Promotion candidate is missing an expected target run identifier."));
        }

        var lineageInconsistent = promotionState switch
        {
            StrategyRunPromotionState.CandidateForPaper => hasParent,
            StrategyRunPromotionState.CandidateForLive => !hasParent,
            StrategyRunPromotionState.LiveManaged => !hasParent,
            _ => false
        };

        if (lineageInconsistent)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "promotion-lineage-shape-inconsistent",
                Message: $"Promotion state '{promotionState}' is inconsistent with the run lineage shape."));
        }

        return new StrategyRunContinuityStatus(
            HasRun: hasRun,
            RunHealth: runHealth,
            HasFills: hasFills,
            FillsHealth: fillsHealth,
            HasPortfolio: hasPortfolio,
            PortfolioHealth: portfolioHealth,
            HasLedger: hasLedger,
            LedgerHealth: ledgerHealth,
            HasCashFlow: hasCashFlow,
            CashFlowHealth: cashFlowHealth,
            HasFills: hasFills,
            HasReconciliation: hasReconciliation,
            ReconciliationHealth: reconciliationHealth,
            AsOfDriftMinutes: asOfDriftMinutes,
            OpenReconciliationBreaks: openBreaks,
            SecurityCoverageIssueCount: securityCoverageIssues,
            HasWarnings: warnings.Count > 0,
            Warnings: warnings);
    }

    private static StrategyRunContinuitySeamHealthStatus GetRunHealth(StrategyRunDetail run)
        => DateTimeOffset.UtcNow - run.Summary.LastUpdatedAt > TimeSpan.FromMinutes(StaleRunWindowMinutes)
            ? StrategyRunContinuitySeamHealthStatus.Stale
            : StrategyRunContinuitySeamHealthStatus.Healthy;

    private static int CalculateAsOfDriftMinutes(DateTimeOffset? portfolioAsOf, DateTimeOffset? ledgerAsOf)
    {
        if (!portfolioAsOf.HasValue || !ledgerAsOf.HasValue)
        {
            return 0;
        }

        return (int)Math.Abs((portfolioAsOf.Value - ledgerAsOf.Value).TotalMinutes);
    }
}
