using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Services;

/// <summary>
/// Builds a shared run-centered continuity drill-in across portfolio, ledger, cash-flow, and reconciliation seams.
/// </summary>
public sealed class StrategyRunContinuityService
{
    private const int ReconciliationTimingToleranceMinutes = 5;

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

    private static StrategyRunContinuityStatus BuildContinuityStatus(
        StrategyRunDetail run,
        RunCashFlowSummary? cashFlow,
        ReconciliationRunDetail? reconciliation)
    {
        var hasPortfolio = run.Portfolio is not null;
        var hasLedger = run.Ledger is not null;
        var hasCashFlow = cashFlow is { TotalEntries: > 0 };
        var hasReconciliation = reconciliation is not null;
        var asOfDriftMinutes = CalculateAsOfDriftMinutes(run.Portfolio?.AsOf, run.Ledger?.AsOf);
        var openBreaks = reconciliation?.Summary.OpenBreakCount ?? 0;
        var securityCoverageIssues = reconciliation?.Summary.SecurityIssueCount
            ?? ((run.Portfolio?.SecurityMissingCount ?? 0) + (run.Ledger?.SecurityMissingCount ?? 0));

        var warnings = new List<StrategyRunContinuityWarning>();
        if (!hasPortfolio)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-portfolio",
                Message: "Run does not have a shared portfolio summary yet."));
        }

        if (!hasLedger)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-ledger",
                Message: "Run does not have a shared ledger summary yet."));
        }

        if (!hasCashFlow)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-cash-flow",
                Message: "Run has no recorded cash flows for cash-financing continuity."));
        }

        if (!hasReconciliation)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "missing-reconciliation",
                Message: "Run has not been linked to a reconciliation result yet."));
        }

        if (asOfDriftMinutes > ReconciliationTimingToleranceMinutes)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "as-of-drift",
                Message: $"Portfolio and ledger timestamps drift by {asOfDriftMinutes} minutes."));
        }

        if (openBreaks > 0)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "open-reconciliation-breaks",
                Message: $"Run has {openBreaks} open reconciliation break(s)."));
        }

        if (securityCoverageIssues > 0)
        {
            warnings.Add(new StrategyRunContinuityWarning(
                Code: "security-coverage",
                Message: $"Run has {securityCoverageIssues} security coverage issue(s) across continuity surfaces."));
        }

        return new StrategyRunContinuityStatus(
            HasPortfolio: hasPortfolio,
            HasLedger: hasLedger,
            HasCashFlow: hasCashFlow,
            HasReconciliation: hasReconciliation,
            AsOfDriftMinutes: asOfDriftMinutes,
            OpenReconciliationBreaks: openBreaks,
            SecurityCoverageIssueCount: securityCoverageIssues,
            HasWarnings: warnings.Count > 0,
            Warnings: warnings);
    }

    private static int CalculateAsOfDriftMinutes(DateTimeOffset? portfolioAsOf, DateTimeOffset? ledgerAsOf)
    {
        if (!portfolioAsOf.HasValue || !ledgerAsOf.HasValue)
        {
            return 0;
        }

        return (int)Math.Abs((portfolioAsOf.Value - ledgerAsOf.Value).TotalMinutes);
    }
}
