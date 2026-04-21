using Meridian.Contracts.Banking;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Services;

public sealed class ReconciliationRunService : IReconciliationRunService
{
    private readonly StrategyRunReadService _runReadService;
    private readonly ReconciliationProjectionService _projectionService;
    private readonly IReconciliationRunRepository _repository;
    private readonly IBankTransactionSource? _bankTransactionSource;

    public ReconciliationRunService(
        StrategyRunReadService runReadService,
        ReconciliationProjectionService projectionService,
        IReconciliationRunRepository repository,
        IBankTransactionSource? bankTransactionSource = null)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _bankTransactionSource = bankTransactionSource;
    }

    public async Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runDetail = await _runReadService.GetRunDetailAsync(request.RunId, ct).ConfigureAwait(false);
        if (runDetail is null)
        {
            return null;
        }

        // --- Portfolio / Ledger checks (existing) ---------------------------
        var checks = _projectionService.BuildChecks(runDetail, request);

        // --- Banking checks (new, optional) ---------------------------------
        IReadOnlyList<BankTransactionDto> bankTransactions = Array.Empty<BankTransactionDto>();
        if (request.BankEntityId.HasValue && _bankTransactionSource is not null)
        {
            bankTransactions = await _bankTransactionSource
                .GetBankTransactionsAsync(request.BankEntityId.Value, ct)
                .ConfigureAwait(false);
        }

        var bankChecks = request.BankEntityId.HasValue
            ? _projectionService.BuildBankingChecks(bankTransactions, runDetail.Ledger)
            : Array.Empty<PortfolioLedgerCheckDto>();

        // Combine all checks and run through the F# reconciliation engine
        var allChecks = checks.Count > 0 || bankChecks.Count > 0
            ? [.. checks, .. bankChecks]
            : checks;

        var results = LedgerInterop.ReconcilePortfolioLedgerChecks(
            request.AmountTolerance, request.MaxAsOfDriftMinutes, allChecks);

        // Track which check IDs originated from the banking layer
        var bankCheckIds = new HashSet<string>(
            bankChecks.Select(static c => c.CheckId), StringComparer.Ordinal);

        var matches = new List<ReconciliationMatchDto>(results.Length);
        var breaks = new List<ReconciliationBreakDto>();
        foreach (var result in results)
        {
            if (result.IsMatch)
            {
                matches.Add(new ReconciliationMatchDto(
                    result.CheckId,
                    result.Label,
                    result.ExpectedSource,
                    result.ActualSource,
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
                MapCategory(result.Category, result.MissingSource, result.CheckId),
                MapStatus(result.Status),
                result.MissingSource,
                result.HasExpectedAmount ? result.ExpectedAmount : null,
                result.HasActualAmount ? result.ActualAmount : null,
                result.Variance,
                result.Reason,
                result.HasExpectedAsOf ? result.ExpectedAsOf : null,
                result.HasActualAsOf ? result.ActualAsOf : null,
                MapSeverity(result.Category, result.MissingSource, result.CheckId, result.ExpectedSource, result.ActualSource)));
        }

        var securityCoverageIssues = BuildSecurityCoverageIssues(runDetail);
        var bankBreakCount = breaks.Count(b => bankCheckIds.Contains(b.CheckId));

        // Build Security Master classification map from already-resolved security references
        // in the portfolio and ledger read models (populated by PortfolioReadService /
        // LedgerReadService when ISecurityReferenceLookup is wired into those services).
        var securityClassifications = BuildSecurityClassifications(runDetail);

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
            securityCoverageIssues.Count > 0,
            bankTransactions.Count,
            bankBreakCount);

        var detail = new ReconciliationRunDetail(summary, matches, breaks, securityCoverageIssues,
            bankTransactions.Count > 0 ? bankTransactions : null,
            securityClassifications.Count > 0 ? securityClassifications : null);
        await _repository.SaveAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
        _repository.GetByIdAsync(reconciliationRunId, ct);

    public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetLatestForRunAsync(runId, ct);

    public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetHistoryForRunAsync(runId, ct);

    private static ReconciliationBreakCategory MapCategory(string category, string missingSource = "", string checkId = "") => category switch
    {
        "amount_mismatch" when IsCashCheck(checkId)
            => ReconciliationBreakCategory.CashMismatch,
        "amount_mismatch" when IsExternalStatementSource(missingSource)
            => ReconciliationBreakCategory.ExternalStatementMismatch,
        "amount_mismatch" => ReconciliationBreakCategory.AmountMismatch,
        "missing_ledger_coverage" when IsCashCheck(checkId)
            => ReconciliationBreakCategory.MissingCashCoverage,
        "missing_ledger_coverage" when IsExternalStatementSource(missingSource)
            => ReconciliationBreakCategory.MissingExternalStatementCoverage,
        "missing_ledger_coverage" => ReconciliationBreakCategory.MissingLedgerCoverage,
        "missing_portfolio_coverage" when string.Equals(missingSource, "bank", StringComparison.OrdinalIgnoreCase)
            => ReconciliationBreakCategory.MissingBankCoverage,
        "missing_portfolio_coverage" when IsExternalStatementSource(missingSource)
            => ReconciliationBreakCategory.MissingExternalStatementCoverage,
        "missing_portfolio_coverage" when IsCashCheck(checkId)
            => ReconciliationBreakCategory.MissingCashCoverage,
        "missing_portfolio_coverage" => ReconciliationBreakCategory.MissingPortfolioCoverage,
        "classification_gap" => ReconciliationBreakCategory.ClassificationGap,
        "timing_mismatch" => ReconciliationBreakCategory.TimingMismatch,
        _ => ReconciliationBreakCategory.ClassificationGap
    };

    private static ReconciliationBreakSeverity MapSeverity(
        string category,
        string missingSource,
        string checkId,
        string expectedSource,
        string actualSource)
    {
        if (string.Equals(category, "timing_mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakSeverity.Info;
        }

        if (string.Equals(category, "classification_gap", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakSeverity.Warning;
        }

        if (string.Equals(category, "amount_mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return IsCashCheck(checkId) ||
                   IsExternalStatementSource(expectedSource) ||
                   IsExternalStatementSource(actualSource)
                ? ReconciliationBreakSeverity.Critical
                : ReconciliationBreakSeverity.Warning;
        }

        if (string.Equals(category, "missing_ledger_coverage", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "missing_portfolio_coverage", StringComparison.OrdinalIgnoreCase))
        {
            return IsExternalStatementSource(missingSource)
                ? ReconciliationBreakSeverity.Critical
                : ReconciliationBreakSeverity.Warning;
        }

        return ReconciliationBreakSeverity.Warning;
    }

    private static bool IsCashCheck(string checkId) =>
        checkId.Contains("cash", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalStatementSource(string source) =>
        string.Equals(source, "bank", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "external_statement", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "statement", StringComparison.OrdinalIgnoreCase);

    private static ReconciliationBreakStatus MapStatus(string status) => status switch
    {
        "matched" => ReconciliationBreakStatus.Matched,
        "investigating" => ReconciliationBreakStatus.Investigating,
        "resolved" => ReconciliationBreakStatus.Resolved,
        _ => ReconciliationBreakStatus.Open
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

    /// <summary>
    /// Builds a symbol-keyed Security Master classification map from security references that
    /// were already resolved by <see cref="PortfolioReadService"/> and <see cref="LedgerReadService"/>.
    /// Only symbols with a non-null <c>Security</c> property are included.
    /// </summary>
    private static IReadOnlyDictionary<string, SecurityClassificationSummaryDto> BuildSecurityClassifications(
        StrategyRunDetail detail)
    {
        var map = new Dictionary<string, SecurityClassificationSummaryDto>(StringComparer.OrdinalIgnoreCase);

        if (detail.Portfolio is not null)
        {
            foreach (var position in detail.Portfolio.Positions)
            {
                if (position.Security is not null &&
                    !string.IsNullOrWhiteSpace(position.Symbol) &&
                    !map.ContainsKey(position.Symbol))
                {
                    map[position.Symbol] = new SecurityClassificationSummaryDto(
                        AssetClass: position.Security.AssetClass,
                        SubType: position.Security.SubType,
                        PrimaryIdentifierKind: "Ticker",
                        PrimaryIdentifierValue: position.Security.PrimaryIdentifier ?? position.Symbol);
                }
            }
        }

        if (detail.Ledger is not null)
        {
            foreach (var line in detail.Ledger.TrialBalance)
            {
                if (line.Security is not null &&
                    !string.IsNullOrWhiteSpace(line.Symbol) &&
                    !map.ContainsKey(line.Symbol))
                {
                    map[line.Symbol] = new SecurityClassificationSummaryDto(
                        AssetClass: line.Security.AssetClass,
                        SubType: line.Security.SubType,
                        PrimaryIdentifierKind: "Ticker",
                        PrimaryIdentifierValue: line.Security.PrimaryIdentifier ?? line.Symbol);
                }
            }
        }

        return map;
    }
}
