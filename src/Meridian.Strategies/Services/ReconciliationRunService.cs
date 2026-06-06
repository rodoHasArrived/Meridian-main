using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Banking;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.FSharp.Ledger;

namespace Meridian.Strategies.Services;

public sealed class ReconciliationRunService : IReconciliationRunService
{
    private readonly StrategyRunReadService _runReadService;
    private readonly ReconciliationProjectionService _projectionService;
    private readonly IReconciliationRunRepository _repository;
    private readonly IStrategyLedgerReconciliationSourceAdapter _ledgerAdapter;
    private readonly IStrategyPortfolioReconciliationSourceAdapter _portfolioAdapter;
    private readonly IInternalCashReconciliationSourceAdapter _internalCashAdapter;
    private readonly IExternalStatementReconciliationSourceAdapter _externalStatementAdapter;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly ISecurityMasterAccountingEventService? _securityMasterAccountingEventService;
    private readonly ISecurityMasterAccountingEventSourceAdapter? _securityMasterAccountingEventSourceAdapter;

    public ReconciliationRunService(
        StrategyRunReadService runReadService,
        ReconciliationProjectionService projectionService,
        IReconciliationRunRepository repository,
        IBankTransactionSource? bankTransactionSource = null,
        IStrategyLedgerReconciliationSourceAdapter? ledgerAdapter = null,
        IStrategyPortfolioReconciliationSourceAdapter? portfolioAdapter = null,
        IInternalCashReconciliationSourceAdapter? internalCashAdapter = null,
        IExternalStatementReconciliationSourceAdapter? externalStatementAdapter = null,
        ISecurityValidationGateService? securityValidationGate = null,
        ISecurityMasterAccountingEventService? securityMasterAccountingEventService = null,
        ISecurityMasterAccountingEventSourceAdapter? securityMasterAccountingEventSourceAdapter = null)
    {
        _runReadService = runReadService ?? throw new ArgumentNullException(nameof(runReadService));
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _ledgerAdapter = ledgerAdapter ?? new StrategyLedgerReconciliationSourceAdapter();
        _portfolioAdapter = portfolioAdapter ?? new StrategyPortfolioReconciliationSourceAdapter();
        _internalCashAdapter = internalCashAdapter ?? new BankInternalCashReconciliationSourceAdapter(bankTransactionSource);
        _externalStatementAdapter = externalStatementAdapter ?? new ExternalStatementReconciliationSourceAdapter(new NullExternalStatementSource());
        _securityValidationGate = securityValidationGate;
        _securityMasterAccountingEventService = securityMasterAccountingEventService;
        _securityMasterAccountingEventSourceAdapter = securityMasterAccountingEventSourceAdapter;
    }

    public async Task<ReconciliationRunDetail?> RunAsync(ReconciliationRunRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runDetail = await _runReadService.GetRunDetailAsync(request.RunId, ct).ConfigureAwait(false);
        if (runDetail is null)
        {
            return null;
        }

        var normalizedInputs = new ReconciliationNormalizedInputs(
            Portfolio: _portfolioAdapter.Adapt(runDetail),
            Ledger: _ledgerAdapter.Adapt(runDetail),
            InternalCashMovements: await _internalCashAdapter.GetCashMovementsAsync(request, ct).ConfigureAwait(false),
            ExternalStatementRows: await _externalStatementAdapter.GetStatementRowsAsync(request, ct).ConfigureAwait(false));

        // ReconciliationProjectionService now operates on normalized inputs from adapter seams,
        // keeping run-model traversal out of the hot-path check projection logic.
        var allChecks = _projectionService.BuildChecks(normalizedInputs);

        var results = LedgerInterop.ReconcilePortfolioLedgerChecks(
            request.AmountTolerance, request.MaxAsOfDriftMinutes, allChecks);

        // Track which check IDs originated from the banking/cash layer.
        var bankCheckIds = new HashSet<string>(
            ["bank-net-vs-ledger-cash", "bank-ledger-coverage-missing", "bank-coverage-missing"],
            StringComparer.Ordinal);

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
                MapCategory(result.Category, result.MissingSource, result.CheckId, result.ExpectedSource, result.ActualSource),
                MapStatus(result.Status, result.Category),
                result.MissingSource,
                result.HasExpectedAmount ? result.ExpectedAmount : null,
                result.HasActualAmount ? result.ActualAmount : null,
                result.Variance,
                MapSeverity(result.Severity),
                result.Reason,
                result.HasExpectedAsOf ? result.ExpectedAsOf : null,
                result.HasActualAsOf ? result.ActualAsOf : null));
        }

        var securityCoverageIssues = await BuildSecurityCoverageIssuesAsync(runDetail, request.RunId, ct).ConfigureAwait(false);
        var bankBreakCount = breaks.Count(b => bankCheckIds.Contains(b.CheckId));
        var bankTransactionCount = normalizedInputs.InternalCashMovements.Count + normalizedInputs.ExternalStatementRows.Count;

        // Build Security Master classification map from already-resolved security references
        // in the portfolio and ledger read models (populated by PortfolioReadService /
        // LedgerReadService when ISecurityReferenceLookup is wired into those services).
        var securityClassifications = BuildSecurityClassifications(runDetail);
        var securityMasterAccountingResult = await GenerateSecurityMasterAccountingEventsAsync(runDetail, request, ct)
            .ConfigureAwait(false);

        var summary = new ReconciliationRunSummary(
            Guid.NewGuid().ToString("N"),
            request.RunId,
            DateTimeOffset.UtcNow,
            runDetail.Portfolio?.AsOf,
            runDetail.Ledger?.AsOf,
            matches.Count,
            breaks.Count,
            breaks.Count(static item => IsUnresolvedStatus(item.Status)),
            breaks.Any(static item => item.Category == ReconciliationBreakCategory.TimingMismatch),
            request.AmountTolerance,
            request.MaxAsOfDriftMinutes,
            securityCoverageIssues.Count,
            securityCoverageIssues.Count > 0,
            bankTransactionCount,
            bankBreakCount,
            securityMasterAccountingResult?.ExpectedEvents.Count ?? 0,
            securityMasterAccountingResult?.JournalPreviews.Count ?? 0,
            securityMasterAccountingResult?.Issues.Count ?? 0,
            securityMasterAccountingResult?.Issues.Count > 0);

        var detail = new ReconciliationRunDetail(summary, matches, breaks, securityCoverageIssues,
            securityClassifications.Count > 0 ? securityClassifications : null,
            securityMasterAccountingResult?.ExpectedEvents.Count > 0 ? securityMasterAccountingResult.ExpectedEvents : null,
            securityMasterAccountingResult?.AccrualCalculations.Count > 0 ? securityMasterAccountingResult.AccrualCalculations : null,
            securityMasterAccountingResult?.JournalPreviews.Count > 0 ? securityMasterAccountingResult.JournalPreviews : null,
            securityMasterAccountingResult?.Issues.Count > 0 ? securityMasterAccountingResult.Issues : null);
        await _repository.SaveAsync(detail, ct).ConfigureAwait(false);
        return detail;
    }

    public Task<ReconciliationRunDetail?> GetByIdAsync(string reconciliationRunId, CancellationToken ct = default) =>
        _repository.GetByIdAsync(reconciliationRunId, ct);

    public Task<ReconciliationRunDetail?> GetLatestForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetLatestForRunAsync(runId, ct);

    public Task<IReadOnlyList<ReconciliationRunSummary>> GetHistoryForRunAsync(string runId, CancellationToken ct = default) =>
        _repository.GetHistoryForRunAsync(runId, ct);

    private static ReconciliationBreakCategory MapCategory(
        string category,
        string missingSource = "",
        string checkId = "",
        string expectedSource = "",
        string actualSource = "") => category switch
        {
            "amount_mismatch" when IsCashCheck(checkId)
                => ReconciliationBreakCategory.CashMismatch,
            "amount_mismatch" when IsExternalStatementSource(expectedSource) || IsExternalStatementSource(actualSource)
                => ReconciliationBreakCategory.ExternalStatementMismatch,
            "amount_mismatch" => ReconciliationBreakCategory.AmountMismatch,
            "missing_ledger_coverage" when IsCashCheck(checkId)
                => ReconciliationBreakCategory.MissingCashCoverage,
            "missing_ledger_coverage" when IsExternalStatementSource(expectedSource) || IsExternalStatementSource(actualSource)
                => ReconciliationBreakCategory.MissingExternalStatementCoverage,
            "missing_ledger_coverage" => ReconciliationBreakCategory.MissingLedgerCoverage,
            "missing_portfolio_coverage" when string.Equals(missingSource, "bank", StringComparison.OrdinalIgnoreCase)
                => ReconciliationBreakCategory.MissingBankCoverage,
            "missing_portfolio_coverage" when IsExternalStatementSource(expectedSource) || IsExternalStatementSource(actualSource)
                => ReconciliationBreakCategory.MissingExternalStatementCoverage,
            "missing_portfolio_coverage" when IsCashCheck(checkId)
                => ReconciliationBreakCategory.MissingCashCoverage,
            "missing_portfolio_coverage" => ReconciliationBreakCategory.MissingPortfolioCoverage,
            "classification_gap" => ReconciliationBreakCategory.ClassificationGap,
            "timing_mismatch" => ReconciliationBreakCategory.TimingMismatch,
            "partial_match" => ReconciliationBreakCategory.PartialMatch,
            _ => ReconciliationBreakCategory.ClassificationGap
        };

    private static bool IsCashCheck(string checkId) =>
        checkId.Contains("cash", StringComparison.OrdinalIgnoreCase);

    private static bool IsExternalStatementSource(string source) =>
        string.Equals(source, "bank", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "external_statement", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(source, "statement", StringComparison.OrdinalIgnoreCase);

    private static ReconciliationBreakStatus MapStatus(string status, string category = "")
    {
        if (string.Equals(status, "partial_match", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, "partial_match", StringComparison.OrdinalIgnoreCase))
        {
            return ReconciliationBreakStatus.PartialMatch;
        }

        return status switch
        {
            "matched" => ReconciliationBreakStatus.Matched,
            "investigating" => ReconciliationBreakStatus.Investigating,
            "resolved" => ReconciliationBreakStatus.Resolved,
            _ => ReconciliationBreakStatus.Open
        };
    }

    private static ReconciliationBreakSeverity MapSeverity(string severity) => severity switch
    {
        "Critical" => ReconciliationBreakSeverity.Critical,
        "High" => ReconciliationBreakSeverity.High,
        "Medium" => ReconciliationBreakSeverity.Medium,
        "Low" => ReconciliationBreakSeverity.Low,
        "Info" => ReconciliationBreakSeverity.Info,
        _ => ReconciliationBreakSeverity.Medium
    };

    private static bool IsUnresolvedStatus(ReconciliationBreakStatus status) =>
        status is not ReconciliationBreakStatus.Matched and not ReconciliationBreakStatus.Resolved;

    private async Task<IReadOnlyList<ReconciliationSecurityCoverageIssueDto>> BuildSecurityCoverageIssuesAsync(
        StrategyRunDetail detail,
        string runId,
        CancellationToken ct)
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
                        Reason: $"Portfolio position '{position.Symbol}' is missing a Security Master match.",
                        Code: "SM_RECON_SECURITY_UNRESOLVED",
                        Severity: ReconciliationBreakSeverity.High,
                        EvidenceLink: "/workstation/data/security-master")));
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
                        Reason: $"Ledger coverage for '{line.Symbol}' in '{line.AccountName}' is missing a Security Master match.",
                        Code: "SM_RECON_SECURITY_UNRESOLVED",
                        Severity: ReconciliationBreakSeverity.High,
                        EvidenceLink: "/workstation/data/security-master")));
        }

        if (_securityValidationGate is not null)
        {
            foreach (var scope in EnumerateSecurityScopes(detail))
            {
                ct.ThrowIfCancellationRequested();
                var validation = await _securityValidationGate
                    .ValidateSymbolAsync(
                        scope.Symbol,
                        SecurityValidationWorkflowDto.ReconciliationBreakIntake,
                        workflowReference: runId,
                        actor: "reconciliation-run-service",
                        persistSnapshot: false,
                        ct)
                    .ConfigureAwait(false);

                foreach (var issue in validation.Report.Issues)
                {
                    issues.Add(new ReconciliationSecurityCoverageIssueDto(
                        Source: scope.Source,
                        Symbol: validation.Symbol ?? scope.Symbol,
                        AccountName: scope.AccountName,
                        Reason: $"Security Master validation {issue.Code}: {issue.Message}",
                        Code: issue.Code,
                        Severity: MapSecurityValidationSeverity(issue.Severity),
                        EvidenceLink: issue.EvidenceLinks.FirstOrDefault()?.Route ?? "/workstation/data/security-master"));
                }
            }
        }

        return issues
            .DistinctBy(static issue => $"{issue.Source}|{issue.Symbol}|{issue.AccountName}|{issue.Code}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<(string Source, string Symbol, string? AccountName)> EnumerateSecurityScopes(StrategyRunDetail detail)
    {
        if (detail.Portfolio is not null)
        {
            foreach (var position in detail.Portfolio.Positions)
            {
                if (!string.IsNullOrWhiteSpace(position.Symbol))
                {
                    yield return ("portfolio", position.Symbol.Trim().ToUpperInvariant(), null);
                }
            }
        }

        if (detail.Ledger is not null)
        {
            foreach (var line in detail.Ledger.TrialBalance)
            {
                if (!string.IsNullOrWhiteSpace(line.Symbol))
                {
                    yield return ("ledger", line.Symbol!.Trim().ToUpperInvariant(), line.AccountName);
                }
            }
        }
    }

    private static ReconciliationBreakSeverity MapSecurityValidationSeverity(SecurityValidationSeverityDto severity)
        => severity switch
        {
            SecurityValidationSeverityDto.Critical => ReconciliationBreakSeverity.Critical,
            SecurityValidationSeverityDto.Error => ReconciliationBreakSeverity.High,
            SecurityValidationSeverityDto.Warning => ReconciliationBreakSeverity.Medium,
            SecurityValidationSeverityDto.Info => ReconciliationBreakSeverity.Info,
            _ => ReconciliationBreakSeverity.Medium
        };

    private async Task<SecurityMasterAccountingEventResult?> GenerateSecurityMasterAccountingEventsAsync(
        StrategyRunDetail detail,
        ReconciliationRunRequest request,
        CancellationToken ct)
    {
        if (_securityMasterAccountingEventService is null || _securityMasterAccountingEventSourceAdapter is null)
        {
            return null;
        }

        var accountingRequest = await _securityMasterAccountingEventSourceAdapter
            .BuildRequestAsync(detail, request, ct)
            .ConfigureAwait(false);
        if (accountingRequest is null)
        {
            return null;
        }

        ct.ThrowIfCancellationRequested();
        return _securityMasterAccountingEventService.Generate(accountingRequest);
    }

    /// <summary>
    /// Builds a symbol-keyed authoritative Security Master map from security references that were
    /// already resolved by <see cref="PortfolioReadService"/> and <see cref="LedgerReadService"/>.
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
                        PrimaryIdentifierValue: position.Security.PrimaryIdentifier ?? position.Symbol,
                        MatchedIdentifierKind: position.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: position.Security.MatchedIdentifierValue,
                        MatchedProvider: position.Security.MatchedProvider);
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
                        PrimaryIdentifierValue: line.Security.PrimaryIdentifier ?? line.Symbol,
                        MatchedIdentifierKind: line.Security.MatchedIdentifierKind,
                        MatchedIdentifierValue: line.Security.MatchedIdentifierValue,
                        MatchedProvider: line.Security.MatchedProvider);
                }
            }
        }

        return map;
    }
}
