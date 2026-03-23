using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Services;

public sealed class ReconciliationRunService : IReconciliationRunService
{
    private readonly StrategyRunReadService _runReadService;
    private readonly ReconciliationProjectionService _projectionService;
    private readonly IReconciliationRunRepository _repository;

    public ReconciliationRunService(
        StrategyRunReadService runReadService,
        ReconciliationProjectionService projectionService,
        IReconciliationRunRepository repository)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runDetail = await _runReadService.GetRunDetailAsync(request.RunId, ct).ConfigureAwait(false);
        if (runDetail is null)
        {
            return null;
        }

        var checks = _projectionService.BuildChecks(runDetail, request);
        var results = LedgerInterop.ReconcilePortfolioLedgerChecks(request.AmountTolerance, request.MaxAsOfDriftMinutes, checks);

        var matches = new List<ReconciliationMatchDto>(results.Length);
        var breaks = new List<ReconciliationBreakDto>();
        foreach (var result in results)
        {
            if (result.IsMatch)
            {
                matches.Add(new ReconciliationMatchDto(
                    result.CheckId,
                    result.Label,
                    MapSource(result.ExpectedSource),
                    MapSource(result.ActualSource),
                    result.HasExpectedAmount ? result.ExpectedAmount : null,
                    result.HasActualAmount ? result.ActualAmount : null,
                    result.Variance,
                    result.HasExpectedAsOf ? result.ExpectedAsOf : null,
                    result.HasActualAsOf ? result.ActualAsOf : null));
                continue;
            }

            breaks.Add(new ReconciliationBreakDto(
                result.CheckId,
                result.Label,
                MapCategory(result.Category),
                MapStatus(result.Status),
                MapSource(result.MissingSource),
                result.HasExpectedAmount ? result.ExpectedAmount : null,
                result.HasActualAmount ? result.ActualAmount : null,
                result.Variance,
                result.Reason,
                result.HasExpectedAsOf ? result.ExpectedAsOf : null,
                result.HasActualAsOf ? result.ActualAsOf : null));
        }

        var securityCoverageIssues = BuildSecurityCoverageIssues(runDetail);

        var summary = new ReconciliationRunSummary(
            Guid.NewGuid().ToString("N"),
            request.RunId,
            DateTimeOffset.UtcNow,
            runDetail.Portfolio?.AsOf,
            runDetail.Ledger?.AsOf,
            matches.Count,
            breaks.Count,
            breaks.Count(static item => item.Status == ReconciliationBreakStatus.Open),
            breaks.Any(static item => item.Category == ReconciliationBreakCategory.TimingMismatch),
            request.AmountTolerance,
            request.MaxAsOfDriftMinutes,
            securityCoverageIssues.Count,
            securityCoverageIssues.Count > 0);

        var detail = new ReconciliationRunDetail(summary, matches, breaks, securityCoverageIssues);
        await _repository.SaveAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
        _repository.GetByIdAsync(reconciliationRunId, ct);

    public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetLatestForRunAsync(runId, ct);

    public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetHistoryForRunAsync(runId, ct);

    private static ReconciliationBreakCategory MapCategory(string category) => category switch
    {
        "amount_mismatch" => ReconciliationBreakCategory.AmountMismatch,
        "missing_ledger_coverage" => ReconciliationBreakCategory.MissingLedgerCoverage,
        "missing_portfolio_coverage" => ReconciliationBreakCategory.MissingPortfolioCoverage,
        "classification_gap" => ReconciliationBreakCategory.ClassificationGap,
        "timing_mismatch" => ReconciliationBreakCategory.TimingMismatch,
        _ => ReconciliationBreakCategory.ClassificationGap
    };

    private static ReconciliationBreakStatus MapStatus(string status) => status switch
    {
        "matched" => ReconciliationBreakStatus.Matched,
        "investigating" => ReconciliationBreakStatus.Investigating,
        "resolved" => ReconciliationBreakStatus.Resolved,
        _ => ReconciliationBreakStatus.Open
    };

    private static ReconciliationSourceKind MapSource(string source) => source switch
    {
        "portfolio" => ReconciliationSourceKind.Portfolio,
        "ledger" => ReconciliationSourceKind.Ledger,
        _ => ReconciliationSourceKind.Unknown
    };

    private static IReadOnlyList<ReconciliationSecurityCoverageIssueDto> BuildSecurityCoverageIssues(StrategyRunDetail detail)
    {
        var issues = new List<ReconciliationSecurityCoverageIssueDto>();

        if (detail.Portfolio is not null)
        {
            issues.AddRange(
                detail.Portfolio.Positions
                    .Where(static position =>
                        !string.IsNullOrWhiteSpace(position.Symbol) &&
                        position.Security is null)
                    .Select(static position => new ReconciliationSecurityCoverageIssueDto(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        Reason: $"Portfolio position '{position.Symbol}' is missing a Security Master match.")));
        }

        if (detail.Ledger is not null)
        {
            issues.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line =>
                        !string.IsNullOrWhiteSpace(line.Symbol) &&
                        line.Security is null)
                    .Select(static line => new ReconciliationSecurityCoverageIssueDto(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        Reason: $"Ledger coverage for '{line.Symbol}' in '{line.AccountName}' is missing a Security Master match.")));
        }

        return issues
            .DistinctBy(static issue => $"{issue.Source}|{issue.Symbol}|{issue.AccountName}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
