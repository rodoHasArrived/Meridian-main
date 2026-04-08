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
                MapCategory(result.Category, result.MissingSource),
                MapStatus(result.Status),
                result.MissingSource,
                result.HasExpectedAmount ? result.ExpectedAmount : null,
                result.HasActualAmount ? result.ActualAmount : null,
                result.Variance,
                result.Reason,
                result.HasExpectedAsOf ? result.ExpectedAsOf : null,
                result.HasActualAsOf ? result.ActualAsOf : null));
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

    private static ReconciliationBreakCategory MapCategory(string category, string missingSource = "") => category switch
    {
        "amount_mismatch" => ReconciliationBreakCategory.AmountMismatch,
        "missing_ledger_coverage" => ReconciliationBreakCategory.MissingLedgerCoverage,
        "missing_portfolio_coverage" when string.Equals(missingSource, "bank", StringComparison.OrdinalIgnoreCase)
            => ReconciliationBreakCategory.MissingBankCoverage,
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

    private static IReadOnlyList<ReconciliationSecurityCoverageIssueDto> BuildSecurityCoverageIssues(StrategyRunDetail detail)
    {
        var issues = new List<ReconciliationSecurityCoverageIssueDto>();

        if (detail.Portfolio is not null)
        {
            issues.AddRange(
                detail.Portfolio.Positions
                    .Where(static position =>
                        !string.IsNullOrWhiteSpace(position.Symbol) &&
                        NeedsCoverageReview(position.Security))
                    .Select(static position => new ReconciliationSecurityCoverageIssueDto(
                        Source: "portfolio",
                        Symbol: position.Symbol,
                        AccountName: null,
                        Reason: BuildCoverageReason("portfolio", position.Symbol, null, position.Security),
                        SecurityId: FormatSecurityId(position.Security),
                        DisplayName: position.Security?.DisplayName,
                        AssetClass: position.Security?.AssetClass,
                        SubType: position.Security?.SubType,
                        CoverageStatus: position.Security?.CoverageStatus ?? WorkstationSecurityCoverageStatus.Missing,
                        CoverageReason: position.Security?.ResolutionReason
                            ?? BuildCoverageReason("portfolio", position.Symbol, null, position.Security),
                        Currency: position.Security?.Currency,
                        MatchedIdentifierKind: position.Security?.MatchedIdentifierKind,
                        MatchedIdentifierValue: position.Security?.MatchedIdentifierValue,
                        MatchedProvider: position.Security?.MatchedProvider)));
        }

        if (detail.Ledger is not null)
        {
            issues.AddRange(
                detail.Ledger.TrialBalance
                    .Where(static line =>
                        !string.IsNullOrWhiteSpace(line.Symbol) &&
                        NeedsCoverageReview(line.Security))
                    .Select(static line => new ReconciliationSecurityCoverageIssueDto(
                        Source: "ledger",
                        Symbol: line.Symbol!,
                        AccountName: line.AccountName,
                        Reason: BuildCoverageReason("ledger", line.Symbol!, line.AccountName, line.Security),
                        SecurityId: FormatSecurityId(line.Security),
                        DisplayName: line.Security?.DisplayName,
                        AssetClass: line.Security?.AssetClass,
                        SubType: line.Security?.SubType,
                        CoverageStatus: line.Security?.CoverageStatus ?? WorkstationSecurityCoverageStatus.Missing,
                        CoverageReason: line.Security?.ResolutionReason
                            ?? BuildCoverageReason("ledger", line.Symbol!, line.AccountName, line.Security),
                        Currency: line.Security?.Currency,
                        MatchedIdentifierKind: line.Security?.MatchedIdentifierKind,
                        MatchedIdentifierValue: line.Security?.MatchedIdentifierValue,
                        MatchedProvider: line.Security?.MatchedProvider)));
        }

        return issues
            .DistinctBy(static issue => $"{issue.Source}|{issue.Symbol}|{issue.AccountName}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, SecurityClassificationSummaryDto> BuildSecurityClassifications(
        StrategyRunDetail detail)
    {
        var map = new Dictionary<string, SecurityClassificationSummaryDto>(StringComparer.OrdinalIgnoreCase);

        if (detail.Portfolio is not null)
        {
            foreach (var position in detail.Portfolio.Positions)
            {
                if (HasAuthoritativeSecurity(position.Security) &&
                    !string.IsNullOrWhiteSpace(position.Symbol) &&
                    !map.ContainsKey(position.Symbol))
                {
                    map[position.Symbol] = new SecurityClassificationSummaryDto(
                        AssetClass: position.Security.AssetClass,
                        SubType: position.Security.SubType,
                        PrimaryIdentifierKind: position.Security.MatchedIdentifierKind ?? "Ticker",
                        PrimaryIdentifierValue: position.Security.MatchedIdentifierValue ?? position.Security.PrimaryIdentifier ?? position.Symbol);
                }
            }
        }

        if (detail.Ledger is not null)
        {
            foreach (var line in detail.Ledger.TrialBalance)
            {
                if (HasAuthoritativeSecurity(line.Security) &&
                    !string.IsNullOrWhiteSpace(line.Symbol) &&
                    !map.ContainsKey(line.Symbol))
                {
                    map[line.Symbol] = new SecurityClassificationSummaryDto(
                        AssetClass: line.Security.AssetClass,
                        SubType: line.Security.SubType,
                        PrimaryIdentifierKind: line.Security.MatchedIdentifierKind ?? "Ticker",
                        PrimaryIdentifierValue: line.Security.MatchedIdentifierValue ?? line.Security.PrimaryIdentifier ?? line.Symbol);
                }
            }
        }

        return map;
    }

    private static bool HasAuthoritativeSecurity(WorkstationSecurityReference? reference)
        => reference is not null &&
           reference.SecurityId != Guid.Empty &&
           reference.CoverageStatus is WorkstationSecurityCoverageStatus.Resolved
               or WorkstationSecurityCoverageStatus.Partial;

    private static bool NeedsCoverageReview(WorkstationSecurityReference? reference)
        => reference is null ||
           reference.CoverageStatus is WorkstationSecurityCoverageStatus.Partial
               or WorkstationSecurityCoverageStatus.Missing
               or WorkstationSecurityCoverageStatus.Unavailable;

    private static string BuildCoverageReason(
        string source,
        string symbol,
        string? accountName,
        WorkstationSecurityReference? reference)
    {
        var referenceReason = reference?.ResolutionReason;
        if (!string.IsNullOrWhiteSpace(referenceReason))
        {
            return referenceReason!;
        }

        return source switch
        {
            "ledger" when !string.IsNullOrWhiteSpace(accountName)
                => $"Ledger coverage for '{symbol}' in '{accountName}' requires Security Master review.",
            "portfolio"
                => $"Portfolio position '{symbol}' requires Security Master review.",
            _ => $"Security Master coverage for '{symbol}' requires review."
        };
    }

    private static string? FormatSecurityId(WorkstationSecurityReference? reference)
        => reference is null || reference.SecurityId == Guid.Empty
            ? null
            : reference.SecurityId.ToString("N");
}
