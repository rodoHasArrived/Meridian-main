using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Application.OperationsContinuity;
using Meridian.Application.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Application.Services;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.Export;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared governance and fund-operations workspace projection used by
/// the local API and future cross-workspace surfaces.
/// </summary>
public sealed class FundOperationsWorkspaceReadService
{
    private static readonly JsonSerializerOptions ReportArtifactJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly string[] TrialBalanceHeaders =
    [
        "accountName",
        "accountType",
        "symbol",
        "currency",
        "assetClass",
        "primaryIdentifierKind",
        "primaryIdentifierValue",
        "subType",
        "assetFamily",
        "issuerType",
        "riskCountry",
        "lookupQuality",
        "displayName",
        "netBalance"
    ];

    private static readonly string[] AssetClassHeaders =
    [
        "assetClass",
        "total",
        "rowCount"
    ];

    private readonly IFundAccountService _fundAccountService;
    private readonly IStrategyRepository _strategyRepository;
    private readonly PortfolioReadService _portfolioReadService;
    private readonly ISecurityReferenceLookup? _securityReferenceLookup;
    private readonly IReconciliationRunService? _strategyReconciliationService;
    private readonly IReconciliationBreakQueueRepository? _breakQueueRepository;
    private readonly NavAttributionService _navAttributionService;
    private readonly ReportGenerationService _reportGenerationService;
    private readonly IGovernanceReportPackRepository? _reportPackRepository;
    private readonly ReportPackValidationService _reportPackValidationService;
    private readonly ISecurityValidationGateService? _securityValidationGate;
    private readonly IOperationsContinuityWorkflowService? _operationsContinuityWorkflowService;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;

    public FundOperationsWorkspaceReadService(
        IFundAccountService fundAccountService,
        IStrategyRepository strategyRepository,
        PortfolioReadService portfolioReadService,
        NavAttributionService navAttributionService,
        ReportGenerationService reportGenerationService,
        ISecurityReferenceLookup? securityReferenceLookup = null,
        IReconciliationRunService? strategyReconciliationService = null,
        IReconciliationBreakQueueRepository? breakQueueRepository = null,
        IGovernanceReportPackRepository? reportPackRepository = null,
        ReportPackValidationService? reportPackValidationService = null,
        ISecurityValidationGateService? securityValidationGate = null,
        IOperationsContinuityWorkflowService? operationsContinuityWorkflowService = null,
        ReportPackWorkflowService? reportPackWorkflowService = null)
    {
        _fundAccountService = fundAccountService ?? throw new ArgumentNullException(nameof(fundAccountService));
        _strategyRepository = strategyRepository ?? throw new ArgumentNullException(nameof(strategyRepository));
        _portfolioReadService = portfolioReadService ?? throw new ArgumentNullException(nameof(portfolioReadService));
        _navAttributionService = navAttributionService ?? throw new ArgumentNullException(nameof(navAttributionService));
        _reportGenerationService = reportGenerationService ?? throw new ArgumentNullException(nameof(reportGenerationService));
        _securityReferenceLookup = securityReferenceLookup;
        _strategyReconciliationService = strategyReconciliationService;
        _breakQueueRepository = breakQueueRepository;
        _reportPackRepository = reportPackRepository;
        _reportPackValidationService = reportPackValidationService ?? new ReportPackValidationService();
        _securityValidationGate = securityValidationGate;
        _operationsContinuityWorkflowService = operationsContinuityWorkflowService;
        _reportPackWorkflowService = reportPackWorkflowService;
    }

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = query.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);

        var runsTask = LoadRunsAsync(normalizedFundProfileId, ct);
        var accountProjectionsTask = GetAccountProjectionsAsync(fundId, ct);
        var bankSnapshotsTask = GetBankSnapshotsAsync(fundId, ct);

        await Task.WhenAll(runsTask, accountProjectionsTask, bankSnapshotsTask).ConfigureAwait(false);

        var runs = await runsTask.ConfigureAwait(false);
        var selectedLedgerIds = NormalizeSelectedLedgerIds(query.SelectedLedgerIds);
        if (selectedLedgerIds.Count > 0)
        {
            runs = runs.Where(run => selectedLedgerIds.Contains(run.RunId)).ToArray();
        }

        var accountProjections = await accountProjectionsTask.ConfigureAwait(false);
        var bankSnapshots = await bankSnapshotsTask.ConfigureAwait(false);
        var accountSummaries = accountProjections
            .Select(static projection => projection.Summary)
            .ToArray();

        var baseCurrency = ResolveCurrency(query.Currency, accountSummaries);
        var asOf = query.AsOf ?? DateTimeOffset.UtcNow;
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var ledgerBook = BuildLedgerBook(normalizedFundProfileId, runs);
        var ledger = await BuildLedgerSummaryAsync(
            normalizedFundProfileId,
            displayName,
            query.ScopeKind,
            query.ScopeId,
            asOf,
            ledgerBook,
            ct).ConfigureAwait(false);
        var ledgerReconciliationSnapshot = ProjectReconciliationSnapshot(ledgerBook.ReconciliationSnapshot(asOf));

        var cashTask = BuildCashFinancingSummaryAsync(
            baseCurrency,
            accountProjections,
            runs,
            ct);
        var reconciliationTask = BuildReconciliationSummaryAsync(
            normalizedFundProfileId,
            accountSummaries,
            runs,
            ct);
        var navTask = BuildNavSummaryAsync(
            normalizedFundProfileId,
            baseCurrency,
            ledgerBook,
            asOf,
            ct);

        await Task.WhenAll(cashTask, reconciliationTask, navTask).ConfigureAwait(false);

        var cashFinancing = await cashTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var reporting = BuildReportingSummary(accountSummaries, asOf);
        var governance = await BuildGovernanceLifecycleProjectionAsync(
            normalizedFundProfileId,
            accountSummaries,
            reconciliation,
            ct).ConfigureAwait(false);
        var workspace = BuildWorkspaceSummary(
            normalizedFundProfileId,
            displayName,
            baseCurrency,
            asOf,
            accountSummaries,
            cashFinancing,
            reconciliation,
            ledger);

        return new FundOperationsWorkspaceDto(
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            RecordedRunCount: runs.Count,
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            Workspace: workspace,
            Ledger: ledger,
            LedgerReconciliationSnapshot: ledgerReconciliationSnapshot,
            Accounts: accountSummaries,
            BankSnapshots: bankSnapshots,
            CashFinancing: cashFinancing,
            Reconciliation: reconciliation,
            Nav: nav,
            Reporting: reporting,
            Governance: governance);
    }

    public async Task<FundReportPackPreviewDto> PreviewReportPackAsync(
        FundReportPackPreviewRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = request.FundProfileId.Trim();
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var currency = ResolveCurrency(request.Currency, []);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var ledgerBook = BuildLedgerBook(normalizedFundProfileId, runs);

        var report = await _reportGenerationService.GenerateAsync(
            new ReportRequest(
                FundId: normalizedFundProfileId,
                AsOf: asOf,
                FundLedger: ledgerBook,
                ReportKind: MapReportKind(request.ReportKind)),
            ct).ConfigureAwait(false);

        var assetClassSections = report.AssetClassSections
            .Select(static section => new FundReportAssetClassSectionDto(
                AssetClass: section.AssetClass,
                Total: section.Total))
            .ToArray();

        return new FundReportPackPreviewDto(
            ReportId: report.ReportId,
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            ReportKind: request.ReportKind,
            Currency: currency,
            AsOf: asOf,
            GeneratedAt: report.GeneratedAt,
            TotalNetAssets: report.TotalNetAssets,
            TrialBalanceLineCount: report.TrialBalance.Count,
            AssetClassSectionCount: report.AssetClassSections.Count,
            AssetClassSections: assetClassSections);
    }

    public async Task<FundReportPackSnapshotDto> GenerateReportPackAsync(
        FundReportPackGenerateRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AuditActor);
        ct.ThrowIfCancellationRequested();

        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Governance report-pack repository has not been configured.");
        var schemaVersion = ResolveReportPackSchemaVersion(request.ExpectedSchemaVersion);
        var formats = NormalizeReportFormats(request.Formats);
        var normalizedFundProfileId = request.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);
        var runs = await LoadRunsAsync(normalizedFundProfileId, ct).ConfigureAwait(false);
        var displayName = ResolveDisplayName(normalizedFundProfileId, runs);
        var asOf = request.AsOf ?? DateTimeOffset.UtcNow;
        var accountProjections = await GetAccountProjectionsAsync(fundId, ct).ConfigureAwait(false);
        var accountSummaries = accountProjections.Select(static projection => projection.Summary).ToArray();
        var currency = ResolveCurrency(request.Currency, accountSummaries);
        var ledgerBook = BuildLedgerBook(normalizedFundProfileId, runs);

        var reportTask = _reportGenerationService.GenerateAsync(
            new ReportRequest(normalizedFundProfileId, asOf, ledgerBook, MapReportKind(request.ReportKind)),
            ct);
        var ledgerTask = BuildLedgerSummaryAsync(
            normalizedFundProfileId,
            displayName,
            FundLedgerScope.Consolidated,
            scopeId: null,
            asOf,
            ledgerBook,
            ct);
        var reconciliationTask = BuildReconciliationSummaryAsync(
            normalizedFundProfileId,
            accountSummaries,
            runs,
            ct);
        var navTask = BuildNavSummaryAsync(normalizedFundProfileId, currency, ledgerBook, asOf, ct);

        await Task.WhenAll(reportTask, ledgerTask, reconciliationTask, navTask).ConfigureAwait(false);

        var report = await reportTask.ConfigureAwait(false);
        var ledger = await ledgerTask.ConfigureAwait(false);
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var securityResolvedCount = report.TrialBalance.Count(static row =>
            !string.Equals(row.LookupQuality, "missing", StringComparison.OrdinalIgnoreCase));
        var securityMissingCount = report.TrialBalance.Count(static row =>
            !string.IsNullOrWhiteSpace(row.Symbol)
            && string.Equals(row.LookupQuality, "missing", StringComparison.OrdinalIgnoreCase));
        var auditActor = request.AuditActor.Trim();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : request.CorrelationId.Trim();
        var securityValidationResults = await ValidateReportPackSecuritiesAsync(
            report,
            auditActor,
            ct).ConfigureAwait(false);
        var validationIssues = _reportPackValidationService.Validate(new ReportPackValidationContext(
            ReportId: report.ReportId,
            AsOf: asOf,
            Report: report,
            Ledger: ledger,
            Reconciliation: reconciliation,
            RunCount: runs.Count,
            SecurityMissingCount: securityMissingCount,
            Formats: formats,
            StaleReplayCount: 0,
            UnresolvedSecurityMasterConflictCount: securityValidationResults.Count(result =>
                result.Report.Issues.Any(issue => issue.Severity is SecurityValidationSeverityDto.Critical or SecurityValidationSeverityDto.Error)),
            SecurityValidationResults: securityValidationResults));
        var status = _reportPackValidationService.ResolveStatus(validationIssues);
        var lifecycleEvents = _reportPackValidationService.BuildGenerationLifecycle(
            auditActor,
            correlationId,
            report.GeneratedAt,
            status);
        var provenance = new FundReportPackProvenanceDto(
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            JournalEntryCount: ledger.JournalEntryCount,
            LedgerEntryCount: ledger.LedgerEntryCount,
            TrialBalanceLineCount: report.TrialBalance.Count,
            ReconciliationRunCount: reconciliation.RunCount,
            OpenReconciliationBreakCount: reconciliation.OpenBreakCount,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            LineagePointers: BuildLineagePointers(report, ledgerBook, runs, reconciliation, asOf),
            SourceSnapshotHash: ComputeSourceSnapshotHash(
                normalizedFundProfileId,
                asOf,
                report,
                ledger,
                reconciliation,
                nav,
                runs),
            SchemaVersion: schemaVersion);
        var snapshot = new FundReportPackSnapshotDto(
            ReportId: report.ReportId,
            FundProfileId: normalizedFundProfileId,
            DisplayName: displayName,
            ReportKind: request.ReportKind,
            Currency: currency,
            AsOf: asOf,
            GeneratedAt: report.GeneratedAt,
            TotalNetAssets: report.TotalNetAssets,
            AuditActor: auditActor,
            CorrelationId: correlationId,
            DecisionRationale: string.IsNullOrWhiteSpace(request.DecisionRationale)
                ? null
                : request.DecisionRationale.Trim(),
            Provenance: provenance,
            Artifacts: [],
            Warnings: BuildReportPackWarnings(report, reconciliation, runs.Count, securityMissingCount),
            ContractName: GovernanceReportPackContract.ContractName,
            SchemaVersion: schemaVersion)
        {
            Status = status,
            ValidationIssues = validationIssues,
            LifecycleEvents = lifecycleEvents
        };

        return await repository
            .SaveAsync(snapshot, BuildReportPackArtifacts(report, formats, ct), ct)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetReportPackHistoryAsync(
        string fundProfileId,
        int limit = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Governance report-pack repository has not been configured.");
        return repository.GetHistoryAsync(fundProfileId.Trim(), limit, ct);
    }

    public Task<FundReportPackSnapshotDto?> GetReportPackAsync(
        Guid reportId,
        CancellationToken ct = default)
    {
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Governance report-pack repository has not been configured.");
        return repository.GetAsync(reportId, ct);
    }

    private async Task<IReadOnlyList<SecurityValidationGateResultDto>> ValidateReportPackSecuritiesAsync(
        ReportPack report,
        string auditActor,
        CancellationToken ct)
    {
        if (_securityValidationGate is null)
        {
            return [];
        }

        var symbols = report.TrialBalance
            .Select(static row => row.Symbol)
            .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))
            .Select(static symbol => symbol!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static symbol => symbol, StringComparer.Ordinal)
            .ToArray();

        var results = new List<SecurityValidationGateResultDto>(symbols.Length);
        foreach (var symbol in symbols)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await _securityValidationGate
                .ValidateSymbolAsync(
                    symbol,
                    SecurityValidationWorkflowDto.ReportPackEvidence,
                    workflowReference: report.ReportId.ToString("N"),
                    actor: auditActor,
                    persistSnapshot: true,
                    ct)
                .ConfigureAwait(false));
        }

        return results;
    }

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadRunsAsync(
        string fundProfileId,
        CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            if (string.Equals(run.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                runs.Add(run);
            }
        }

        return runs
            .OrderByDescending(static run => run.StartedAt)
            .ThenByDescending(static run => run.RunId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> GetAccountProjectionsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var accounts = grouped.CustodianAccounts
            .Concat(grouped.BankAccounts)
            .Concat(grouped.BrokerageAccounts)
            .Concat(grouped.OtherAccounts)
            .ToArray();

        var projections = new List<AccountWorkspaceProjection>(accounts.Length);
        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var reconciliationRuns = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var openBreaks = reconciliationRuns
                .Where(run => !string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                           && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                .Sum(static run => run.TotalBreaks);

            var summary = new FundAccountSummary(
                AccountId: account.AccountId,
                AccountType: account.AccountType,
                AccountCode: account.AccountCode,
                DisplayName: account.DisplayName,
                BaseCurrency: account.BaseCurrency,
                Institution: account.Institution,
                IsActive: account.IsActive,
                CashBalance: latestSnapshot?.CashBalance ?? 0m,
                SecuritiesMarketValue: latestSnapshot?.SecuritiesMarketValue ?? 0m,
                NetAssetValue: (latestSnapshot?.CashBalance ?? 0m) + (latestSnapshot?.SecuritiesMarketValue ?? 0m),
                LastSnapshotDate: latestSnapshot?.AsOfDate,
                ReconciliationRuns: reconciliationRuns.Count,
                OpenBreaks: openBreaks,
                PortfolioId: account.PortfolioId,
                LedgerReference: account.LedgerReference,
                BankName: account.BankDetails?.BankName,
                AccountNumberMasked: MaskAccountNumber(account.BankDetails?.AccountNumber),
                EntityId: account.EntityId,
                SleeveId: account.SleeveId,
                VehicleId: account.VehicleId,
                StrategyId: account.StrategyId,
                RunId: account.RunId,
                StructureLabel: BuildStructureLabel(account),
                WorkflowLabel: BuildWorkflowLabel(account));

            projections.Add(new AccountWorkspaceProjection(summary, latestSnapshot));
        }

        return projections
            .OrderBy(projection => projection.Summary.AccountType.ToString(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(projection => projection.Summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyList<BankAccountSnapshot>> GetBankSnapshotsAsync(
        Guid fundId,
        CancellationToken ct)
    {
        var grouped = await _fundAccountService.GetFundAccountsAsync(fundId, ct).ConfigureAwait(false);
        var bankingAccounts = grouped.BankAccounts
            .Concat(grouped.BrokerageAccounts.Where(static account => account.BankDetails is not null))
            .ToArray();

        var snapshots = new List<BankAccountSnapshot>(bankingAccounts.Length);
        foreach (var account in bankingAccounts)
        {
            ct.ThrowIfCancellationRequested();

            var latestSnapshot = await _fundAccountService
                .GetLatestBalanceSnapshotAsync(account.AccountId, ct)
                .ConfigureAwait(false);
            var bankLines = await _fundAccountService
                .GetBankStatementLinesAsync(account.AccountId, ct: ct)
                .ConfigureAwait(false);
            var latestLine = bankLines
                .OrderByDescending(static line => line.StatementDate)
                .ThenByDescending(static line => line.ValueDate)
                .FirstOrDefault();

            snapshots.Add(new BankAccountSnapshot(
                AccountId: account.AccountId,
                DisplayName: account.DisplayName,
                AccountCode: account.AccountCode,
                BankName: account.BankDetails?.BankName ?? account.Institution ?? "Bank account",
                Currency: account.BaseCurrency,
                CurrentBalance: latestSnapshot?.CashBalance ?? latestLine?.RunningBalance ?? 0m,
                PendingSettlement: latestSnapshot?.PendingSettlement ?? 0m,
                LastStatementDate: latestLine?.StatementDate,
                StatementLineCount: bankLines.Count,
                LatestTransactionType: latestLine?.TransactionType,
                LatestTransactionAmount: latestLine?.Amount));
        }

        return snapshots
            .OrderBy(static snapshot => snapshot.BankName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static snapshot => snapshot.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<CashFinancingSummary> BuildCashFinancingSummaryAsync(
        string currency,
        IReadOnlyList<AccountWorkspaceProjection> accountProjections,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var totalCash = accountProjections.Sum(static projection => projection.Summary.CashBalance);
        var pendingSettlement = accountProjections.Sum(static projection => projection.LatestSnapshot?.PendingSettlement ?? 0m);
        var financing = 0m;
        var realized = 0m;
        var unrealized = 0m;
        var longMarketValue = 0m;
        var shortMarketValue = 0m;
        var grossExposure = 0m;
        var netExposure = 0m;
        var totalEquity = 0m;
        var contributingRunCount = 0;

        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            if (portfolio is null)
            {
                continue;
            }

            contributingRunCount++;
            financing += portfolio.Financing;
            realized += portfolio.RealizedPnl;
            unrealized += portfolio.UnrealizedPnl;
            longMarketValue += portfolio.LongMarketValue;
            shortMarketValue += portfolio.ShortMarketValue;
            grossExposure += portfolio.GrossExposure;
            netExposure += portfolio.NetExposure;
            totalEquity += portfolio.TotalEquity;
        }

        var highlights = new List<string>
        {
            accountProjections.Count == 0
                ? "No linked fund accounts have been configured yet."
                : $"{accountProjections.Count} fund account(s) are contributing banking and custody balances.",
            contributingRunCount == 0
                ? "No recorded fund-scoped runs are contributing portfolio posture yet."
                : $"{contributingRunCount} recorded run(s) are contributing capital posture.",
            financing == 0m
                ? "No financing costs have been recorded for the current fund scope."
                : $"Financing costs total {financing:C2} across linked runs."
        };

        return new CashFinancingSummary(
            Currency: currency,
            TotalCash: totalCash,
            PendingSettlement: pendingSettlement,
            FinancingCost: financing,
            MarginBalance: 0m,
            RealizedPnl: realized,
            UnrealizedPnl: unrealized,
            LongMarketValue: longMarketValue,
            ShortMarketValue: shortMarketValue,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            TotalEquity: totalEquity,
            Highlights: highlights);
    }

    private async Task<ReconciliationSummary> BuildReconciliationSummaryAsync(
        string fundProfileId,
        IReadOnlyList<FundAccountSummary> accounts,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var items = new List<FundReconciliationItem>();
        var openBreaks = 0;
        decimal breakAmountTotal = 0m;
        var securityCoverageIssues = 0;

        foreach (var account in accounts)
        {
            ct.ThrowIfCancellationRequested();

            var accountRuns = await _fundAccountService
                .GetReconciliationRunsAsync(account.AccountId, ct)
                .ConfigureAwait(false);

            foreach (var run in accountRuns)
            {
                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: run.ReconciliationRunId,
                    AccountId: run.AccountId,
                    AccountDisplayName: account.DisplayName,
                    AsOfDate: run.AsOfDate,
                    Status: run.Status,
                    TotalChecks: run.TotalChecks,
                    TotalMatched: run.TotalMatched,
                    TotalBreaks: run.TotalBreaks,
                    BreakAmountTotal: run.BreakAmountTotal,
                    RequestedAt: run.RequestedAt,
                    CompletedAt: run.CompletedAt,
                    ScopeLabel: "Account",
                    CoverageLabel: "Account-level reconciliation"));

                if (!string.Equals(run.Status, "Matched", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(run.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                {
                    openBreaks += run.TotalBreaks;
                    breakAmountTotal += run.BreakAmountTotal;
                }
            }
        }

        if (_strategyReconciliationService is not null)
        {
            foreach (var run in runs)
            {
                ct.ThrowIfCancellationRequested();

                var detail = await _strategyReconciliationService
                    .GetLatestForRunAsync(run.RunId, ct)
                    .ConfigureAwait(false)
                    ?? await _strategyReconciliationService
                        .RunAsync(new ReconciliationRunRequest(run.RunId), ct)
                        .ConfigureAwait(false);

                if (detail is null)
                {
                    continue;
                }

                var asOf = detail.Summary.PortfolioAsOf
                    ?? detail.Summary.LedgerAsOf
                    ?? detail.Summary.CreatedAt;
                var strategyBreakAmount = detail.Breaks.Sum(static result => Math.Abs(result.Variance));
                var status = MapStrategyStatus(detail.Summary);

                items.Add(new FundReconciliationItem(
                    ReconciliationRunId: ParseGuid(detail.Summary.ReconciliationRunId),
                    AccountId: Guid.Empty,
                    AccountDisplayName: run.StrategyName,
                    AsOfDate: DateOnly.FromDateTime(asOf.UtcDateTime),
                    Status: status,
                    TotalChecks: detail.Summary.MatchCount + detail.Summary.BreakCount,
                    TotalMatched: detail.Summary.MatchCount,
                    TotalBreaks: detail.Summary.BreakCount,
                    BreakAmountTotal: strategyBreakAmount,
                    RequestedAt: detail.Summary.CreatedAt,
                    CompletedAt: detail.Summary.CreatedAt,
                    ScopeLabel: "Strategy Run",
                    StrategyName: run.StrategyName,
                    RunId: run.RunId,
                    SecurityIssueCount: detail.Summary.SecurityIssueCount,
                    HasSecurityCoverageIssues: detail.Summary.HasSecurityCoverageIssues,
                    CoverageLabel: detail.Summary.HasSecurityCoverageIssues
                        ? $"{detail.Summary.SecurityIssueCount} security issue(s)"
                        : "Security Master aligned"));

                if (detail.Summary.BreakCount > 0)
                {
                    openBreaks += detail.Summary.BreakCount;
                    breakAmountTotal += strategyBreakAmount;
                }

                securityCoverageIssues += detail.Summary.SecurityIssueCount;
            }
        }

        var ordered = items
            .OrderByDescending(static item => item.RequestedAt)
            .ToArray();

        return new ReconciliationSummary(
            RunCount: ordered.Length,
            OpenBreakCount: openBreaks,
            BreakAmountTotal: breakAmountTotal,
            RecentRuns: ordered,
            SecurityCoverageIssueCount: securityCoverageIssues,
            BreakQueue: await BuildBreakQueueProjectionAsync(fundProfileId, runs, ct).ConfigureAwait(false),
            LedgerImpactPreview: BuildLedgerImpactPreview(ordered),
            HasCriticalBreakOpen: await HasCriticalBreakOpenAsync(fundProfileId, runs, ct).ConfigureAwait(false));
    }

    private async Task<ReconciliationBreakQueueProjectionDto?> BuildBreakQueueProjectionAsync(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return null;
        }

        var scopedRunIds = runs
            .Select(static run => run.RunId)
            .Where(static runId => !string.IsNullOrWhiteSpace(runId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = (await _breakQueueRepository.GetAllAsync(null, ct).ConfigureAwait(false))
            .Where(item =>
                string.Equals(item.FundAccountId, fundProfileId, StringComparison.OrdinalIgnoreCase) &&
                scopedRunIds.Contains(item.RunId))
            .ToArray();
        var projected = items
            .OrderByDescending(static item => item.LastUpdatedAt)
            .Select(item => new ReconciliationBreakQueueProjectionItemDto(
                BreakId: item.BreakId,
                WorkflowId: item.RunId,
                Severity: item.Severity,
                Status: item.Status,
                Owner: item.AssignedTo,
                RequiredSignoffRole: item.RequiredSignoffRole,
                SignoffStatus: item.SignoffStatus,
                RoutingTarget: item.RoutingTarget,
                RoutingDetail: item.RoutingDetail,
                EvidenceReference: item.UpstreamSyncCursor ?? item.ExternalAccountId,
                LastUpdatedAt: item.LastUpdatedAt,
                Priority: item.Priority,
                SlaState: item.SlaState,
                AgeBand: item.AgeBand,
                RootCauseCode: item.RootCauseCode,
                ResolutionCode: item.ResolutionCode,
                CommentCount: item.CommentCount,
                EvidenceCount: item.EvidenceCount,
                LastActivityAt: item.LastActivityAt,
                LastCommentExcerpt: BuildLastCommentExcerpt(item),
                RelatedCaseCount: items.Count(candidate =>
                    !string.Equals(candidate.BreakId, item.BreakId, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(candidate.ExternalAccountId, item.ExternalAccountId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(candidate.Counterparty, item.Counterparty, StringComparison.OrdinalIgnoreCase) ||
                     candidate.Category == item.Category)),
                SlaBadgeLabel: BuildSlaBadgeLabel(item),
                SlaBadgeTone: BuildSlaBadgeTone(item.SlaState)))
            .ToArray();

        return new ReconciliationBreakQueueProjectionDto(
            TotalCount: projected.Length,
            OpenCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open),
            InReviewCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview),
            ResolvedCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Resolved),
            DismissedCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Dismissed),
            CriticalOpenCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.Open && item.Severity == ReconciliationBreakSeverity.Critical),
            Items: projected,
            BreachedCount: projected.Count(static item => item.SlaState == ReconciliationCaseSlaState.Breached),
            AwaitingEvidenceCount: projected.Count(static item => item.Status == ReconciliationBreakQueueStatus.InReview && string.Equals(item.SignoffStatus, "awaiting-evidence", StringComparison.OrdinalIgnoreCase)),
            SignedOffEvidenceCount: projected.Where(static item => item.Status == ReconciliationBreakQueueStatus.SignedOff).Sum(static item => item.EvidenceCount));
    }


    private static string? BuildLastCommentExcerpt(ReconciliationBreakQueueItem item)
    {
        var body = item.Comments?
            .Where(static comment => comment.DeletedAt is null && !string.IsNullOrWhiteSpace(comment.Body))
            .OrderByDescending(static comment => comment.EditedAt ?? comment.CreatedAt)
            .Select(static comment => comment.Body.Trim())
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return body.Length <= 120 ? body : string.Concat(body.AsSpan(0, 117), "...");
    }

    private static string BuildSlaBadgeLabel(ReconciliationBreakQueueItem item)
        => item.SlaDueAt.HasValue
            ? $"SLA {item.SlaState}; due {item.SlaDueAt.Value:O}"
            : $"SLA {item.SlaState}";

    private static string BuildSlaBadgeTone(ReconciliationCaseSlaState state)
        => state switch
        {
            ReconciliationCaseSlaState.Breached => "danger",
            ReconciliationCaseSlaState.Warning => "warning",
            ReconciliationCaseSlaState.Paused => "neutral",
            ReconciliationCaseSlaState.Stopped => "success",
            _ => "info"
        };

    private static LedgerImpactPreviewDto BuildLedgerImpactPreview(IReadOnlyList<FundReconciliationItem> items)
    {
        var draftEntryCount = items.Sum(static item => item.TotalBreaks);
        var netDebit = items.Where(static item => item.BreakAmountTotal >= 0m).Sum(static item => item.BreakAmountTotal);
        var netCredit = items.Where(static item => item.BreakAmountTotal < 0m).Sum(static item => Math.Abs(item.BreakAmountTotal));
        var flags = new List<string>();
        if (draftEntryCount > 0)
        {
            flags.Add("draft-entries-present");
        }

        if (items.Any(static item => item.TotalBreaks > 0 && !string.Equals(item.Status, "Resolved", StringComparison.OrdinalIgnoreCase)))
        {
            flags.Add("unresolved-breaks-blocking-close");
        }

        return new LedgerImpactPreviewDto(
            DraftEntryCount: draftEntryCount,
            NetDebitEffect: netDebit,
            NetCreditEffect: netCredit,
            NetBalanceDelta: netDebit - netCredit,
            HasValidationWarnings: flags.Count > 0,
            ValidationFlags: flags);
    }

    private async Task<bool> HasCriticalBreakOpenAsync(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        if (_breakQueueRepository is null)
        {
            return false;
        }

        var scopedRunIds = runs
            .Select(static run => run.RunId)
            .Where(static runId => !string.IsNullOrWhiteSpace(runId))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items = await _breakQueueRepository.GetAllAsync(ReconciliationBreakQueueStatus.Open, ct).ConfigureAwait(false);
        return items.Any(item =>
            item.Severity == ReconciliationBreakSeverity.Critical &&
            string.Equals(item.FundAccountId, fundProfileId, StringComparison.OrdinalIgnoreCase) &&
            scopedRunIds.Contains(item.RunId));
    }

    private async Task<FundNavAttributionSummaryDto> BuildNavSummaryAsync(
        string fundProfileId,
        string currency,
        FundLedgerBook fundLedgerBook,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var result = await _navAttributionService
            .AttributeAsync(
                new NavAttributionRequest(
                    FundId: fundProfileId,
                    AsOf: asOf,
                    FundLedger: fundLedgerBook,
                    Currency: currency),
                ct)
            .ConfigureAwait(false);

        var assetClassExposure = result.Consolidated.ByAssetClass
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => new FundNavAssetClassExposureDto(
                AssetClass: pair.Key,
                NetBalance: pair.Value))
            .ToArray();

        return new FundNavAttributionSummaryDto(
            Currency: result.Currency,
            TotalNav: result.Consolidated.TotalNav,
            ComponentCount: result.Consolidated.Components.Count,
            EntityCount: result.ByEntity.Count,
            SleeveCount: result.BySleeve.Count,
            VehicleCount: result.ByVehicle.Count,
            AssetClassExposure: assetClassExposure);
    }

    private async Task<FundLedgerSummary> BuildLedgerSummaryAsync(
        string fundProfileId,
        string displayName,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        FundLedgerBook fundLedgerBook,
        CancellationToken ct)
    {
        var journal = BuildJournal(fundLedgerBook, scopeKind, scopeId, asOf);
        var trialBalance = await BuildTrialBalanceAsync(fundLedgerBook, scopeKind, scopeId, asOf, ct).ConfigureAwait(false);
        var entityCount = fundLedgerBook.EntitySnapshotsAsOf(asOf).Count;
        var sleeveCount = fundLedgerBook.SleeveSnapshotsAsOf(asOf).Count;
        var vehicleCount = fundLedgerBook.VehicleSnapshotsAsOf(asOf).Count;

        return new FundLedgerSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            ScopeKind: scopeKind,
            ScopeId: scopeId,
            AsOf: asOf,
            JournalEntryCount: journal.Count,
            LedgerEntryCount: trialBalance.Sum(static line => line.EntryCount),
            AssetBalance: SumBalance(trialBalance, LedgerAccountType.Asset),
            LiabilityBalance: SumBalance(trialBalance, LedgerAccountType.Liability),
            EquityBalance: SumBalance(trialBalance, LedgerAccountType.Equity),
            RevenueBalance: SumBalance(trialBalance, LedgerAccountType.Revenue),
            ExpenseBalance: SumBalance(trialBalance, LedgerAccountType.Expense),
            TrialBalance: trialBalance,
            Journal: journal,
            EntityCount: entityCount,
            SleeveCount: sleeveCount,
            VehicleCount: vehicleCount);
    }

    public static FundLedgerReconciliationSnapshot ProjectReconciliationSnapshot(FundLedgerSnapshot snapshot)
    {
        return new FundLedgerReconciliationSnapshot(
            FundProfileId: snapshot.FundId,
            AsOf: snapshot.AsOf,
            Consolidated: ProjectDimensionSnapshot(snapshot.Consolidated),
            Entities: snapshot.Entities.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Sleeves: snapshot.Sleeves.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            Vehicles: snapshot.Vehicles.ToDictionary(
                static pair => pair.Key,
                static pair => ProjectDimensionSnapshot(pair.Value),
                StringComparer.OrdinalIgnoreCase));
    }

    private static FundLedgerDimensionSnapshot ProjectDimensionSnapshot(LedgerSnapshot snapshot) =>
        new(
            Timestamp: snapshot.Timestamp,
            JournalEntryCount: snapshot.JournalEntryCount,
            LedgerEntryCount: snapshot.LedgerEntryCount,
            Balances: snapshot.Balances
                .OrderBy(static pair => pair.Key.AccountType)
                .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
                .Select(static pair => new FundLedgerSnapshotBalanceLine(
                    AccountName: pair.Key.Name,
                    AccountType: pair.Key.AccountType.ToString(),
                    Symbol: pair.Key.Symbol,
                    FinancialAccountId: pair.Key.FinancialAccountId,
                    Balance: pair.Value))
                .ToArray());

    private static FundLedgerBook BuildLedgerBook(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs)
    {
        var fundLedgerBook = new FundLedgerBook(fundProfileId);

        foreach (var run in runs)
        {
            foreach (var journalEntry in run.Metrics?.Ledger?.Journal ?? [])
            {
                fundLedgerBook.FundLedger.Post(journalEntry);
            }
        }

        return fundLedgerBook;
    }

    private async Task<IReadOnlyList<FundTrialBalanceLine>> BuildTrialBalanceAsync(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        IReadOnlyDictionary<LedgerAccount, decimal> balances = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedSnapshotAsOf(asOf).Balances,
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).SnapshotAsOf(asOf).Balances,
            _ => book.ConsolidatedSnapshotAsOf(asOf).Balances
        };

        var entryCounts = BuildEntryCounts(book, scopeKind, scopeId, asOf);
        var securityLookup = await ResolveSecurityReferencesAsync(
            balances.Keys
                .Select(static account => account.Symbol)
                .Where(static symbol => !string.IsNullOrWhiteSpace(symbol))!
                .Select(static symbol => symbol!),
            ct).ConfigureAwait(false);

        return balances
            .OrderBy(static pair => pair.Key.AccountType)
            .ThenBy(static pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new FundTrialBalanceLine(
                AccountName: pair.Key.Name,
                AccountType: pair.Key.AccountType.ToString(),
                Symbol: pair.Key.Symbol,
                FinancialAccountId: pair.Key.FinancialAccountId,
                Balance: pair.Value,
                EntryCount: entryCounts.TryGetValue(pair.Key, out var count) ? count : 0,
                Security: pair.Key.Symbol is not null ? securityLookup.GetValueOrDefault(pair.Key.Symbol) : null))
            .ToArray();
    }

    private static IReadOnlyList<FundJournalLine> BuildJournal(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .OrderByDescending(static entry => entry.Timestamp)
            .ThenByDescending(static entry => entry.JournalEntryId)
            .Select(entry => new FundJournalLine(
                JournalEntryId: entry.JournalEntryId,
                Timestamp: entry.Timestamp,
                Description: entry.Description,
                TotalDebits: entry.Lines.Sum(static line => line.Debit),
                TotalCredits: entry.Lines.Sum(static line => line.Credit),
                LineCount: entry.Lines.Count))
            .ToArray();
    }

    private static Dictionary<LedgerAccount, int> BuildEntryCounts(
        FundLedgerBook book,
        FundLedgerScope scopeKind,
        string? scopeId,
        DateTimeOffset asOf)
    {
        IEnumerable<JournalEntry> source = scopeKind switch
        {
            FundLedgerScope.Consolidated => book.ConsolidatedJournalEntries(),
            FundLedgerScope.Entity => book.EntityLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Sleeve => book.SleeveLedger(scopeId ?? string.Empty).GetJournalEntries(),
            FundLedgerScope.Vehicle => book.VehicleLedger(scopeId ?? string.Empty).GetJournalEntries(),
            _ => book.ConsolidatedJournalEntries()
        };

        return source
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(static entry => entry.Lines)
            .GroupBy(static line => line.Account)
            .ToDictionary(static group => group.Key, static group => group.Count());
    }

    private static IReadOnlyList<GovernanceReportArtifactFormatDto> NormalizeReportFormats(
        IReadOnlyList<GovernanceReportArtifactFormatDto>? requestedFormats)
    {
        if (requestedFormats is null)
        {
            return
            [
                GovernanceReportArtifactFormatDto.Json,
                GovernanceReportArtifactFormatDto.Csv,
                GovernanceReportArtifactFormatDto.Xlsx
            ];
        }

        if (requestedFormats.Count == 0)
        {
            throw new ArgumentException("At least one report-pack artifact format is required.", nameof(requestedFormats));
        }

        var formats = new List<GovernanceReportArtifactFormatDto>(requestedFormats.Count);
        foreach (var format in requestedFormats)
        {
            if (!Enum.IsDefined(format))
            {
                throw new ArgumentException($"Unsupported report-pack artifact format '{format}'.", nameof(requestedFormats));
            }

            if (!formats.Contains(format))
            {
                formats.Add(format);
            }
        }

        return formats;
    }

    private static int ResolveReportPackSchemaVersion(int? expectedSchemaVersion)
    {
        var schemaVersion = expectedSchemaVersion ?? GovernanceReportPackContract.CurrentSchemaVersion;
        if (schemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported governance report-pack schema version '{schemaVersion}'. Current version is {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(expectedSchemaVersion));
        }

        return schemaVersion;
    }

    private static IReadOnlyList<GovernanceReportPackArtifactContent> BuildReportPackArtifacts(
        ReportPack report,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        CancellationToken ct)
    {
        var artifacts = new List<GovernanceReportPackArtifactContent>();
        if (formats.Contains(GovernanceReportArtifactFormatDto.Json))
        {
            ct.ThrowIfCancellationRequested();
            artifacts.Add(new GovernanceReportPackArtifactContent("trial-balance", GovernanceReportArtifactFormatDto.Json, "trial-balance.json", SerializeJsonArtifact(OrderTrialBalanceRows(report.TrialBalance))));
            artifacts.Add(new GovernanceReportPackArtifactContent("asset-class-sections", GovernanceReportArtifactFormatDto.Json, "asset-class-sections.json", SerializeJsonArtifact(OrderAssetClassSections(report.AssetClassSections))));
        }

        if (formats.Contains(GovernanceReportArtifactFormatDto.Csv))
        {
            ct.ThrowIfCancellationRequested();
            artifacts.Add(new GovernanceReportPackArtifactContent("trial-balance", GovernanceReportArtifactFormatDto.Csv, "trial-balance.csv", BuildTrialBalanceCsv(report.TrialBalance, ct)));
            artifacts.Add(new GovernanceReportPackArtifactContent("asset-class-sections", GovernanceReportArtifactFormatDto.Csv, "asset-class-sections.csv", BuildAssetClassSectionsCsv(report.AssetClassSections, ct)));
        }

        if (formats.Contains(GovernanceReportArtifactFormatDto.Xlsx))
        {
            ct.ThrowIfCancellationRequested();
            artifacts.Add(new GovernanceReportPackArtifactContent("workbook", GovernanceReportArtifactFormatDto.Xlsx, "report-pack.xlsx", XlsxWorkbookWriter.CreateWorkbook(BuildWorkbookSheets(report), ct)));
        }

        return artifacts.OrderBy(static artifact => artifact.FileName, StringComparer.Ordinal).ToArray();
    }

    private static byte[] SerializeJsonArtifact<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, ReportArtifactJsonOptions);

    private static IReadOnlyList<XlsxWorksheet> BuildWorkbookSheets(ReportPack report)
    {
        var trialBalanceRows = OrderTrialBalanceRows(report.TrialBalance)
            .Select(static row => (IReadOnlyList<object?>)MapTrialBalanceWorkbookRow(row))
            .ToArray();
        var assetClassRows = OrderAssetClassSections(report.AssetClassSections)
            .Select(static section => (IReadOnlyList<object?>)
            [
                section.AssetClass,
                section.Total,
                section.Rows.Count
            ])
            .ToArray();

        return
        [
            new XlsxWorksheet("Trial Balance", TrialBalanceHeaders, trialBalanceRows),
            new XlsxWorksheet("Asset Classes", AssetClassHeaders, assetClassRows)
        ];
    }

    private static IReadOnlyList<EnrichedLedgerRow> OrderTrialBalanceRows(IEnumerable<EnrichedLedgerRow> rows)
        => rows
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<AssetClassSection> OrderAssetClassSections(IEnumerable<AssetClassSection> sections)
        => sections
            .OrderBy(static section => section.AssetClass, StringComparer.Ordinal)
            .ToArray();

    private static object?[] MapTrialBalanceWorkbookRow(EnrichedLedgerRow row) =>
    [
        row.AccountName,
        row.AccountType,
        row.Symbol,
        row.Currency,
        row.AssetClass,
        row.PrimaryIdentifierKind,
        row.PrimaryIdentifierValue,
        row.SubType,
        row.AssetFamily,
        row.IssuerType,
        row.RiskCountry,
        row.LookupQuality,
        row.DisplayName,
        row.NetBalance
    ];

    private static byte[] BuildTrialBalanceCsv(IReadOnlyList<EnrichedLedgerRow> rows, CancellationToken ct)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, TrialBalanceHeaders);
        foreach (var row in OrderTrialBalanceRows(rows))
        {
            ct.ThrowIfCancellationRequested();
            AppendCsvRow(builder, MapTrialBalanceWorkbookRow(row));
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildAssetClassSectionsCsv(IReadOnlyList<AssetClassSection> sections, CancellationToken ct)
    {
        var builder = new StringBuilder();
        AppendCsvRow(builder, AssetClassHeaders);
        foreach (var section in OrderAssetClassSections(sections))
        {
            ct.ThrowIfCancellationRequested();
            AppendCsvRow(builder,
            [
                section.AssetClass,
                section.Total,
                section.Rows.Count
            ]);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendCsvRow(StringBuilder builder, IEnumerable<object?> values)
    {
        var first = true;
        foreach (var value in values)
        {
            if (!first)
            {
                builder.Append(',');
            }

            builder.Append(EscapeCsvValue(value));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeCsvValue(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        if (IsPotentialSpreadsheetFormula(text))
        {
            text = $"'{text}";
        }

        return text.IndexOfAny(['"', ',', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool IsPotentialSpreadsheetFormula(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';
    }

    private static IReadOnlyList<string> BuildReportPackWarnings(
        ReportPack report,
        ReconciliationSummary reconciliation,
        int runCount,
        int securityMissingCount)
    {
        var warnings = new List<string>();
        if (runCount == 0)
        {
            warnings.Add("No recorded fund-scoped runs contributed ledger data for this report pack.");
        }

        if (report.TrialBalance.Count == 0)
        {
            warnings.Add("The generated report pack contains no trial-balance rows.");
        }

        if (report.AssetClassSections.Count == 0)
        {
            warnings.Add("The generated report pack contains no asset-class sections.");
        }

        if (securityMissingCount > 0)
        {
            warnings.Add($"{securityMissingCount} trial-balance row(s) could not be resolved through Security Master.");
        }

        if (reconciliation.OpenBreakCount > 0)
        {
            warnings.Add($"{reconciliation.OpenBreakCount} open reconciliation break(s) were present at generation time.");
        }

        return warnings;
    }

    private static string ComputeSourceSnapshotHash(
        string fundProfileId,
        DateTimeOffset asOf,
        ReportPack report,
        FundLedgerSummary ledger,
        ReconciliationSummary reconciliation,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<StrategyRunEntry> runs)
    {
        var source = new
        {
            FundProfileId = fundProfileId,
            AsOf = asOf,
            ReportKind = report.ReportKind.ToString(),
            Runs = runs.OrderBy(static run => run.RunId, StringComparer.Ordinal).Select(static run => new { run.RunId, run.StrategyName, run.FundProfileId, run.StartedAt }).ToArray(),
            Ledger = new { ledger.JournalEntryCount, ledger.LedgerEntryCount, ledger.AssetBalance, ledger.LiabilityBalance, ledger.EquityBalance, ledger.RevenueBalance, ledger.ExpenseBalance },
            Reconciliation = new { reconciliation.RunCount, reconciliation.OpenBreakCount, reconciliation.BreakAmountTotal, reconciliation.SecurityCoverageIssueCount },
            Nav = new
            {
                nav.Currency,
                nav.TotalNav,
                nav.ComponentCount,
                nav.EntityCount,
                nav.SleeveCount,
                nav.VehicleCount,
                AssetClassExposure = nav.AssetClassExposure.OrderBy(static exposure => exposure.AssetClass, StringComparer.Ordinal).ToArray()
            },
            TrialBalance = OrderTrialBalanceRows(report.TrialBalance),
            AssetClassSections = OrderAssetClassSections(report.AssetClassSections).Select(static section => new { section.AssetClass, section.Total, RowCount = section.Rows.Count }).ToArray()
        };

        var json = JsonSerializer.SerializeToUtf8Bytes(source, ReportArtifactJsonOptions);
        return Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
    }

    private static IReadOnlyList<FundReportPackLineagePointerDto> BuildLineagePointers(
        ReportPack report,
        FundLedgerBook ledgerBook,
        IReadOnlyList<StrategyRunEntry> runs,
        ReconciliationSummary reconciliation,
        DateTimeOffset asOf)
    {
        var pointers = new List<FundReportPackLineagePointerDto>();
        var ledgerEvidence = BuildLedgerLineEvidence(ledgerBook, asOf);
        foreach (var run in runs)
        {
            pointers.Add(new FundReportPackLineagePointerDto(
                "report",
                "summary",
                "run",
                run.RunId,
                DisplayLabel: $"{run.StrategyName} ({run.RunId})",
                Route: UiApiRoutes.WithParam(UiApiRoutes.RunsContinuity, "runId", run.RunId),
                SourceSystem: "strategy-run"));
        }

        foreach (var line in report.TrialBalance)
        {
            var lineKey = string.IsNullOrWhiteSpace(line.Symbol)
                ? line.AccountName
                : $"{line.AccountName}:{line.Symbol}";
            var evidenceKey = BuildLedgerEvidenceKey(line.AccountName, line.Symbol);
            ledgerEvidence.TryGetValue(evidenceKey, out var lineEvidence);
            pointers.Add(new FundReportPackLineagePointerDto(
                "line",
                lineKey,
                "ledger-account",
                line.AccountName,
                DisplayLabel: BuildLedgerLineLabel(line),
                Route: BuildLedgerLineRoute(runs, line.AccountName, line.Symbol),
                SourceSystem: "ledger",
                RelatedEvidenceIds: lineEvidence?.LedgerEntryIds,
                EvidenceCount: lineEvidence?.LedgerEntryIds.Count,
                Amount: line.NetBalance,
                CapturedAt: lineEvidence?.LatestTimestamp));
            if (!string.IsNullOrWhiteSpace(line.Symbol))
            {
                var symbol = line.Symbol.Trim();
                pointers.Add(new FundReportPackLineagePointerDto(
                    "line",
                    lineKey,
                    "security",
                    symbol,
                    DisplayLabel: BuildSecurityLineLabel(line),
                    Route: BuildSecurityMasterSearchRoute(symbol),
                    SourceSystem: "security-master",
                    RelatedEvidenceIds: lineEvidence?.JournalEntryIds,
                    EvidenceCount: lineEvidence?.JournalEntryIds.Count,
                    Amount: line.NetBalance,
                    CapturedAt: lineEvidence?.LatestTimestamp));
            }
        }

        if (reconciliation.RunCount > 0)
        {
            pointers.Add(new FundReportPackLineagePointerDto(
                "section",
                "reconciliation",
                "reconciliation-summary",
                $"runs:{reconciliation.RunCount};open-breaks:{reconciliation.OpenBreakCount}",
                DisplayLabel: $"{reconciliation.RunCount} reconciliation run(s), {reconciliation.OpenBreakCount} open break(s)",
                Route: UiApiRoutes.ReconciliationRuns,
                SourceSystem: "reconciliation"));
        }

        return pointers;
    }

    private static IReadOnlyDictionary<string, LedgerLineEvidence> BuildLedgerLineEvidence(
        FundLedgerBook ledgerBook,
        DateTimeOffset asOf)
        => ledgerBook
            .ConsolidatedJournalEntries()
            .Where(entry => entry.Timestamp <= asOf)
            .SelectMany(entry => entry.Lines.Select(line => new { entry.JournalEntryId, entry.Timestamp, Line = line }))
            .GroupBy(item => BuildLedgerEvidenceKey(item.Line.Account.Name, item.Line.Account.Symbol), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group =>
                {
                    var ordered = group
                        .OrderBy(static item => item.Timestamp)
                        .ThenBy(static item => item.Line.EntryId)
                        .ToArray();
                    return new LedgerLineEvidence(
                        ordered.Select(static item => item.Line.EntryId.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        ordered.Select(static item => item.JournalEntryId.ToString("D")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                        ordered.Length == 0 ? null : ordered[^1].Timestamp);
                },
                StringComparer.OrdinalIgnoreCase);

    private static string BuildLedgerEvidenceKey(string accountName, string? symbol)
        => string.IsNullOrWhiteSpace(symbol)
            ? accountName.Trim()
            : $"{accountName.Trim()}:{symbol.Trim()}";

    private static string BuildLedgerLineRoute(
        IReadOnlyList<StrategyRunEntry> runs,
        string accountName,
        string? symbol)
    {
        var baseRoute = runs.Count == 1
            ? UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", runs[0].RunId)
            : UiApiRoutes.FundReportPacks;
        var queryParts = new List<string>
        {
            $"accountName={Uri.EscapeDataString(accountName.Trim())}"
        };
        if (!string.IsNullOrWhiteSpace(symbol))
        {
            queryParts.Add($"symbol={Uri.EscapeDataString(symbol.Trim())}");
        }

        return UiApiRoutes.WithQuery(baseRoute, string.Join("&", queryParts));
    }

    private static string BuildSecurityMasterSearchRoute(string symbol)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WorkstationSecurityMasterSearch,
            $"query={Uri.EscapeDataString(symbol.Trim())}");

    private static string BuildLedgerLineLabel(EnrichedLedgerRow line)
        => string.IsNullOrWhiteSpace(line.Symbol)
            ? $"{line.AccountName} ledger line"
            : $"{line.AccountName} / {line.Symbol.Trim()} ledger line";

    private static string BuildSecurityLineLabel(EnrichedLedgerRow line)
    {
        var symbol = line.Symbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return "Security Master lookup";
        }

        return string.IsNullOrWhiteSpace(line.DisplayName)
            ? symbol
            : $"{line.DisplayName.Trim()} ({symbol})";
    }

    private sealed record LedgerLineEvidence(
        IReadOnlyList<string> LedgerEntryIds,
        IReadOnlyList<string> JournalEntryIds,
        DateTimeOffset? LatestTimestamp);

    private static decimal SumBalance(IEnumerable<FundTrialBalanceLine> lines, LedgerAccountType accountType)
        => lines
            .Where(line => string.Equals(line.AccountType, accountType.ToString(), StringComparison.Ordinal))
            .Sum(static line => line.Balance);

    private static HashSet<string> NormalizeSelectedLedgerIds(IReadOnlyList<string>? selectedLedgerIds) =>
        selectedLedgerIds?
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
        ?? [];

    private static FundWorkspaceSummary BuildWorkspaceSummary(
        string fundProfileId,
        string displayName,
        string baseCurrency,
        DateTimeOffset asOf,
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashFinancing,
        ReconciliationSummary reconciliation,
        FundLedgerSummary ledger)
    {
        var totalEquity = cashFinancing.TotalEquity != 0m
            ? cashFinancing.TotalEquity
            : accounts.Sum(static account => account.NetAssetValue);

        var securityResolvedCount = ledger.TrialBalance.Count(static line => line.Security is not null);
        var securityMissingCount = ledger.TrialBalance.Count(static line =>
            !string.IsNullOrWhiteSpace(line.Symbol) &&
            line.Security is null);

        return new FundWorkspaceSummary(
            FundProfileId: fundProfileId,
            FundDisplayName: displayName,
            BaseCurrency: baseCurrency,
            AsOf: asOf,
            TotalAccounts: accounts.Count,
            BankAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Bank),
            BrokerageAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Brokerage),
            CustodyAccountCount: accounts.Count(static account => account.AccountType == AccountTypeDto.Custody),
            TotalCash: cashFinancing.TotalCash,
            GrossExposure: cashFinancing.GrossExposure,
            NetExposure: cashFinancing.NetExposure,
            TotalEquity: totalEquity,
            FinancingCost: cashFinancing.FinancingCost,
            PendingSettlement: cashFinancing.PendingSettlement,
            OpenReconciliationBreaks: reconciliation.OpenBreakCount,
            ReconciliationRuns: reconciliation.RunCount,
            JournalEntryCount: ledger.JournalEntryCount,
            TrialBalanceLineCount: ledger.TrialBalance.Count,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            SecurityCoverageIssues: reconciliation.SecurityCoverageIssueCount);
    }

    private async Task<GovernanceLifecycleProjectionDto> BuildGovernanceLifecycleProjectionAsync(
        string fundProfileId,
        IReadOnlyList<FundAccountSummary> accounts,
        ReconciliationSummary reconciliation,
        CancellationToken ct)
    {
        OperationsContinuityWorkflowSummaryDto? activeWorkflowSummary = null;
        OperationsContinuityWorkflowDto? activeWorkflow = null;
        IReadOnlyList<OperationsTimelineEntryDto> timeline = [];
        if (_operationsContinuityWorkflowService is not null)
        {
            var summaries = await _operationsContinuityWorkflowService
                .ListAsync(ct: ct)
                .ConfigureAwait(false);
            var accountIds = accounts
                .Select(static account => account.AccountId)
                .ToHashSet();
            var scopedSummaries = summaries
                .Where(summary => accountIds.Contains(summary.FundAccountId))
                .ToArray();

            activeWorkflowSummary = scopedSummaries
                .OrderByDescending(static item => item.UpdatedAtUtc)
                .FirstOrDefault();

            if (activeWorkflowSummary is not null)
            {
                activeWorkflow = await _operationsContinuityWorkflowService
                    .GetAsync(activeWorkflowSummary.WorkflowId, ct)
                    .ConfigureAwait(false);

                timeline = await _operationsContinuityWorkflowService
                    .GetTimelineAsync(activeWorkflowSummary.WorkflowId, ct)
                    .ConfigureAwait(false);
            }
        }

        FundReportPackHistoryItemDto? latestReportPack = null;
        if (_reportPackRepository is not null)
        {
            var history = await _reportPackRepository
                .GetHistoryAsync(fundProfileId, limit: 1, ct)
                .ConfigureAwait(false);
            latestReportPack = history.FirstOrDefault();
        }

        var evidenceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var traceReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var evidence in activeWorkflow?.EvidenceLinks ?? [])
        {
            if (!string.IsNullOrWhiteSpace(evidence.EvidenceId))
            {
                evidenceReferences.Add(evidence.EvidenceId);
            }

            if (!string.IsNullOrWhiteSpace(evidence.Route))
            {
                traceReferences.Add(evidence.Route);
            }
        }

        foreach (var entry in timeline)
        {
            traceReferences.Add(entry.AuditId.ToString("N"));
            traceReferences.Add(entry.CurrentHash);
            foreach (var reference in entry.References)
            {
                if (!string.IsNullOrWhiteSpace(reference.EvidenceId))
                {
                    evidenceReferences.Add(reference.EvidenceId);
                }
            }
        }

        foreach (var queueItem in reconciliation.BreakQueue?.Items ?? [])
        {
            if (!string.IsNullOrWhiteSpace(queueItem.EvidenceReference))
            {
                evidenceReferences.Add(queueItem.EvidenceReference);
            }

            if (!string.IsNullOrWhiteSpace(queueItem.WorkflowId))
            {
                traceReferences.Add(queueItem.WorkflowId);
            }
        }

        var decisionPosture = activeWorkflow?.ReconciliationState switch
        {
            OperationsReconciliationStateDto.ExceptionsOpen or OperationsReconciliationStateDto.InReview
                => "Reconciliation decisions are still open in the shared workflow queue.",
            OperationsReconciliationStateDto.Cleared or OperationsReconciliationStateDto.Complete
                => "Reconciliation decisions are cleared in shared lifecycle state.",
            _ when reconciliation.OpenBreakCount > 0
                => $"{reconciliation.OpenBreakCount} reconciliation break(s) still need shared decision records.",
            _ => "Reconciliation decision posture is aligned with shared governance state."
        };

        var signoffPosture = activeWorkflow?.ApprovalState switch
        {
            OperationsApprovalStateDto.Approved => "Sign-off is approved in shared continuity lifecycle.",
            OperationsApprovalStateDto.Rejected => "Sign-off is rejected; approval lifecycle requires remediation.",
            OperationsApprovalStateDto.Submitted or OperationsApprovalStateDto.ReviewerAssigned
                => "Sign-off is submitted and awaiting reviewer decision in shared lifecycle.",
            _ => "Sign-off is pending shared approval evidence."
        };

        var reportPackReady =
            activeWorkflow?.ReportPackReadiness.IsReady == true ||
            latestReportPack?.Status is GovernanceReportPackStatusDto.Approved
                or GovernanceReportPackStatusDto.Exported
                or GovernanceReportPackStatusDto.Retained;
        var closeReadyByLifecycle = activeWorkflow?.Status is OperationsWorkflowStatusDto.ReadyForClose or OperationsWorkflowStatusDto.Closed;
        var closeReadyByBreaks = reconciliation.OpenBreakCount == 0 && !reconciliation.HasCriticalBreakOpen;
        var closeReadiness = closeReadyByLifecycle && reportPackReady && closeReadyByBreaks
            ? "Close readiness is satisfied by shared lifecycle, reconciliation, and report-pack evidence."
            : "Close readiness remains blocked until shared lifecycle gates, reconciliation decisions, and report-pack evidence align.";

        var auditTraceability = timeline.Count > 0 || evidenceReferences.Count > 0
            ? $"Traceability is backed by {timeline.Count} lifecycle event(s) and {evidenceReferences.Count} evidence reference(s)."
            : "Traceability references are pending shared timeline and evidence responses.";

        return new GovernanceLifecycleProjectionDto(
            DecisionPosture: decisionPosture,
            SignoffPosture: signoffPosture,
            CloseReadiness: closeReadiness,
            AuditTraceability: auditTraceability,
            ActiveWorkflowId: activeWorkflowSummary?.WorkflowId.ToString(),
            WorkflowStatus: activeWorkflow?.Status ?? activeWorkflowSummary?.Status,
            ApprovalState: activeWorkflow?.ApprovalState,
            WorkflowUpdatedAtUtc: activeWorkflow?.UpdatedAtUtc ?? activeWorkflowSummary?.UpdatedAtUtc,
            TimelineEventCount: timeline.Count,
            EvidenceReferenceCount: evidenceReferences.Count,
            EvidenceReferences: evidenceReferences.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            AuditReferences: traceReferences.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private async Task<Dictionary<string, WorkstationSecurityReference?>> ResolveSecurityReferencesAsync(
        IEnumerable<string> symbols,
        CancellationToken ct)
    {
        var lookup = new Dictionary<string, WorkstationSecurityReference?>(StringComparer.OrdinalIgnoreCase);
        if (_securityReferenceLookup is null)
        {
            return lookup;
        }

        foreach (var symbol in symbols.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            lookup[symbol] = await _securityReferenceLookup
                .GetBySymbolAsync(symbol, ct)
                .ConfigureAwait(false);
        }

        return lookup;
    }

    private FundReportingSummaryDto BuildReportingSummary(
        IReadOnlyList<FundAccountSummary> accounts,
        DateTimeOffset asOf)
    {
        var profiles = ExportProfile.GetBuiltInProfiles()
            .Select(static profile => new FundReportingProfileDto(
                Id: profile.Id,
                Name: profile.Name,
                TargetTool: profile.TargetTool,
                Format: profile.Format.ToString(),
                Description: profile.Description ?? string.Empty,
                LoaderScript: profile.IncludeLoaderScript,
                DataDictionary: profile.IncludeDataDictionary))
            .OrderBy(static profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var recommended = profiles
            .Where(static profile => profile.Id is "excel" or "python-pandas" or "postgresql" or "arrow-feather")
            .Select(static profile => profile.Id)
            .ToArray();
        var workflowRecords = BuildReportPackWorkflowRecords(accounts, asOf);

        return new FundReportingSummaryDto(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            ReportPackTargets: ["board", "investor", "compliance", "fund-ops"],
            Profiles: profiles,
            Summary: $"{profiles.Length} export/reporting profiles are available for governance workflows.",
            WorkflowRecords: workflowRecords);
    }

    private IReadOnlyList<ReportPackWorkflowRecordDto> BuildReportPackWorkflowRecords(
        IReadOnlyList<FundAccountSummary> accounts,
        DateTimeOffset asOf)
    {
        if (_reportPackWorkflowService is null || accounts.Count == 0)
        {
            return [];
        }

        var period = asOf.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        return accounts
            .SelectMany(account => _reportPackWorkflowService.GetHistory(period, account.AccountId.ToString("D")))
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenByDescending(static record => record.Version)
            .Take(8)
            .ToArray();
    }

    private static string ResolveDisplayName(
        string fundProfileId,
        IReadOnlyList<StrategyRunEntry> runs)
        => runs
            .Select(static run => run.FundDisplayName)
            .FirstOrDefault(static name => !string.IsNullOrWhiteSpace(name))
            ?? fundProfileId;

    private static string ResolveCurrency(
        string? requestedCurrency,
        IReadOnlyList<FundAccountSummary> accounts)
        => !string.IsNullOrWhiteSpace(requestedCurrency)
            ? requestedCurrency.Trim().ToUpperInvariant()
            : accounts.Select(static account => account.BaseCurrency)
                .FirstOrDefault(static currency => !string.IsNullOrWhiteSpace(currency))
            ?? "USD";

    private static string? MaskAccountNumber(string? accountNumber)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
        {
            return null;
        }

        var trimmed = accountNumber.Trim();
        if (trimmed.Length <= 4)
        {
            return trimmed;
        }

        return $"****{trimmed[^4..]}";
    }

    private static string BuildStructureLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(3);

        if (account.EntityId.HasValue)
        {
            segments.Add($"Entity {FormatKey(account.EntityId.Value)}");
        }

        if (account.SleeveId.HasValue)
        {
            segments.Add($"Sleeve {FormatKey(account.SleeveId.Value)}");
        }

        if (account.VehicleId.HasValue)
        {
            segments.Add($"Vehicle {FormatKey(account.VehicleId.Value)}");
        }

        return segments.Count == 0 ? "Fund-level" : string.Join(" • ", segments);
    }

    private static string BuildWorkflowLabel(AccountSummaryDto account)
    {
        var segments = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(account.StrategyId))
        {
            segments.Add($"Strategy {account.StrategyId}");
        }

        if (!string.IsNullOrWhiteSpace(account.RunId))
        {
            segments.Add($"Run {account.RunId}");
        }

        if (!string.IsNullOrWhiteSpace(account.PortfolioId))
        {
            segments.Add($"Portfolio {account.PortfolioId}");
        }

        if (!string.IsNullOrWhiteSpace(account.LedgerReference))
        {
            segments.Add($"Ledger {account.LedgerReference}");
        }

        return segments.Count == 0 ? "Manual / external" : string.Join(" • ", segments);
    }

    private static string FormatKey(Guid value)
        => value.ToString("N")[..8].ToUpperInvariant();

    private static Guid TranslateFundProfileId(string fundProfileId)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(fundProfileId));
        return new Guid(bytes);
    }

    private static string MapStrategyStatus(ReconciliationRunSummary summary)
        => summary.HasSecurityCoverageIssues
            ? "SecurityCoverageOpen"
            : summary.BreakCount > 0
                ? "BreaksOpen"
                : "Matched";

    private static Guid ParseGuid(string value)
    {
        if (Guid.TryParse(value, out var guid))
        {
            return guid;
        }

        return new Guid(MD5.HashData(Encoding.UTF8.GetBytes(value)));
    }

    private static ReportKind MapReportKind(GovernanceReportKindDto reportKind)
        => reportKind switch
        {
            GovernanceReportKindDto.TrialBalance => ReportKind.TrialBalance,
            GovernanceReportKindDto.NavSummary => ReportKind.NavSummary,
            GovernanceReportKindDto.AssetAllocation => ReportKind.AssetAllocation,
            GovernanceReportKindDto.ReconciliationPack => ReportKind.ReconciliationPack,
            _ => ReportKind.TrialBalance
        };

    private sealed record AccountWorkspaceProjection(
        FundAccountSummary Summary,
        AccountBalanceSnapshotDto? LatestSnapshot);
}
