using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.FinancialOperations.OperationsContinuity;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Services;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Reporting;
using Meridian.Storage.Export;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Models;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Builds the shared Accounting and fund-operations workspace projection used by
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

    private const int StructuredReportingExportSchemaVersion = 1;
    private const string RegulatoryTrialBalanceExportId = "regulatory-trial-balance";
    private const string WarehouseLedgerFactsExportId = "warehouse-ledger-facts";
    private const string InvestmentPortfolioCutsExportId = "investment-portfolio-cuts";
    private const string InvestmentTopNContributionAnalyticsExportId = "investment-topn-contribution-analytics";
    private const string CrossFundConsolidationExportId = "cross-fund-consolidation";
    private static readonly TimeSpan LivePortfolioViewFreshnessWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LivePortfolioViewStaleWindow = TimeSpan.FromHours(24);

    private static readonly StructuredReportingExportColumnDto[] RegulatoryTrialBalanceColumns =
    [
        new("accountName", "string", "Ledger account display name."),
        new("accountType", "string", "Ledger account type."),
        new("symbol", "string", "Security symbol when the line is security-scoped."),
        new("financialAccountId", "string", "Linked financial account identifier."),
        new("currency", "string", "Workspace base currency."),
        new("balance", "decimal", "Signed trial-balance amount."),
        new("entryCount", "integer", "Ledger entry count behind the balance."),
        new("securityId", "guid", "Resolved Security Master identifier when available."),
        new("securityDisplayName", "string", "Resolved Security Master display name."),
        new("sourceAsOfUtc", "datetime", "Workspace as-of timestamp in UTC.")
    ];

    private static readonly StructuredReportingExportColumnDto[] WarehouseLedgerFactColumns =
    [
        new("scope", "string", "Ledger consolidation scope."),
        new("accountName", "string", "Ledger account display name."),
        new("accountType", "string", "Ledger account type."),
        new("symbol", "string", "Security symbol when the balance is security-scoped."),
        new("financialAccountId", "string", "Linked financial account identifier."),
        new("balance", "decimal", "Signed ledger snapshot amount."),
        new("journalEntryCount", "integer", "Journal entries in the source snapshot."),
        new("ledgerEntryCount", "integer", "Ledger entries in the source snapshot."),
        new("sourceAsOfUtc", "datetime", "Snapshot as-of timestamp in UTC.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentPortfolioCutColumns =
    [
        new("cutId", "string", "Stable fund, strategy, or user-tag cut identifier."),
        new("label", "string", "Human-readable cut label."),
        new("kind", "string", "Cut kind: Fund, Strategy, or UserTag."),
        new("currency", "string", "Reporting currency."),
        new("grossExposure", "decimal", "Gross exposure for the cut."),
        new("netExposure", "decimal", "Net exposure for the cut."),
        new("totalCash", "decimal", "Cash balance included in the cut."),
        new("pendingSettlement", "decimal", "Pending settlement amount."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Shadow NAV for the cut."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against source equity."),
        new("sourceCount", "integer", "Number of contributing source records."),
        new("versionStamp", "string", "Deterministic export/cut version marker.")
    ];

    private static readonly StructuredReportingExportColumnDto[] CrossFundConsolidationColumns =
    [
        new("consolidationId", "string", "Stable cross-fund consolidation row identifier."),
        new("label", "string", "Human-readable consolidation label."),
        new("scope", "string", "Consolidation scope: Company, Fund, or Entity."),
        new("currency", "string", "Reporting currency."),
        new("isReady", "boolean", "Whether the row has source-backed records."),
        new("fundCount", "integer", "Distinct fund count represented by the row."),
        new("entityCount", "integer", "Distinct legal/entity count represented by the row."),
        new("accountCount", "integer", "Contributing account source count."),
        new("runCount", "integer", "Contributing strategy-run source count."),
        new("grossExposure", "decimal", "Aggregated gross exposure."),
        new("netExposure", "decimal", "Aggregated net exposure."),
        new("totalCash", "decimal", "Aggregated cash balance."),
        new("pendingSettlement", "decimal", "Aggregated pending settlement amount."),
        new("totalPnl", "decimal", "Aggregated realized plus unrealized P&L."),
        new("shadowNav", "decimal", "Aggregated source-backed shadow NAV."),
        new("shadowNavVariance", "decimal", "Shadow NAV variance against aggregate net exposure."),
        new("sourceCount", "integer", "Contributing account plus run source count."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic export/consolidation version marker.")
    ];

    private static readonly StructuredReportingExportColumnDto[] InvestmentTopNContributionAnalyticsColumns =
    [
        new("analyticsId", "string", "Stable Top-N or contribution analytics row identifier."),
        new("kind", "string", "Analytics row kind: TopWinner, TopLaggard, or Contribution."),
        new("scope", "string", "Analytics scope: Security, Strategy, or AssetClass."),
        new("rank", "integer", "Rank within the analytics kind and scope."),
        new("label", "string", "Human-readable row label."),
        new("symbol", "string", "Security symbol when the row is security-scoped."),
        new("classification", "string", "Security asset class, strategy, or asset-class classification."),
        new("currency", "string", "Reporting currency."),
        new("realizedPnl", "decimal", "Realized P&L included in the analytics row."),
        new("unrealizedPnl", "decimal", "Unrealized P&L included in the analytics row."),
        new("totalPnl", "decimal", "Realized plus unrealized P&L."),
        new("contributionPercent", "decimal", "Percent contribution to total portfolio P&L."),
        new("heatMapIntensity", "decimal", "Absolute P&L intensity used by reporting heat maps."),
        new("sourceCount", "integer", "Number of contributing source runs."),
        new("asOfUtc", "datetime", "Analytics as-of timestamp in UTC."),
        new("readinessSummary", "string", "Source-backed readiness description."),
        new("versionStamp", "string", "Deterministic analytics version marker."),
        new("tags", "string", "Pipe-delimited analytics tags.")
    ];

    private static readonly ReportBrandingThemeDto[] BuiltInReportBrandingThemes =
    [
        new(
            "meridian-standard",
            "Meridian Standard",
            "Meridian",
            "#195E63",
            "#2F8F83",
            "#1F2933",
            "#F8FAFC",
            FooterText: "Generated by Meridian Reporting",
            Disclaimer: "Confidential report pack generated from retained Meridian source evidence."),
        new(
            "allocator-clean",
            "Allocator Clean",
            "Meridian",
            "#243B53",
            "#3E7C59",
            "#102A43",
            "#FFFFFF",
            FooterText: "Investor-ready governed reporting",
            Disclaimer: "For authorized recipients only; values remain subject to approved report-pack workflow state."),
        new(
            "compliance-file",
            "Compliance File",
            "Meridian Compliance",
            "#334E68",
            "#B7791F",
            "#1A202C",
            "#F7FAFC",
            FooterText: "Retained compliance export",
            Disclaimer: "Retain with the generated manifest, provenance, and approval evidence.")
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
    private readonly ReportPackDeliveryService? _reportPackDeliveryService;
    private readonly ReportingScheduleService? _reportingScheduleService;
    private readonly ReportPackRunReadService? _reportPackRunReadService;

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
        ReportPackWorkflowService? reportPackWorkflowService = null,
        ReportPackDeliveryService? reportPackDeliveryService = null,
        ReportingScheduleService? reportingScheduleService = null,
        ReportPackRunReadService? reportPackRunReadService = null)
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
        _reportPackDeliveryService = reportPackDeliveryService;
        _reportingScheduleService = reportingScheduleService;
        _reportPackRunReadService = reportPackRunReadService;
    }

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        CancellationToken ct = default) =>
        await GetWorkspaceAsync(query, accessContext: null, ct).ConfigureAwait(false);

    public async Task<FundOperationsWorkspaceDto> GetWorkspaceAsync(
        FundOperationsWorkspaceQuery query,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.FundProfileId);
        ct.ThrowIfCancellationRequested();

        var normalizedFundProfileId = query.FundProfileId.Trim();
        var fundId = TranslateFundProfileId(normalizedFundProfileId);

        var runsTask = LoadRunsAsync(normalizedFundProfileId, ct);
        var allRunsTask = LoadFundScopedRunsAsync(ct);
        var accountProjectionsTask = GetAccountProjectionsAsync(fundId, ct);
        var allAccountProjectionsTask = GetAllAccountProjectionsAsync(ct);
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

        var cashBuild = await cashTask.ConfigureAwait(false);
        var cashFinancing = cashBuild.Summary;
        var reconciliation = await reconciliationTask.ConfigureAwait(false);
        var nav = await navTask.ConfigureAwait(false);
        var allRuns = await allRunsTask.ConfigureAwait(false);
        var allAccountProjections = await allAccountProjectionsTask.ConfigureAwait(false);
        var crossFundConsolidations = await BuildCrossFundReportingConsolidationsAsync(
            normalizedFundProfileId,
            baseCurrency,
            allAccountProjections,
            allRuns,
            asOf,
            ct).ConfigureAwait(false);
        var reporting = BuildReportingSummary(
            normalizedFundProfileId,
            accountSummaries,
            ledger,
            ledgerReconciliationSnapshot,
            cashFinancing,
            nav,
            cashBuild.RunSources,
            crossFundConsolidations,
            asOf,
            accessContext);
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

    public async Task<StructuredReportingExportPayloadDto> GetStructuredReportingExportAsync(
        StructuredReportingExportRequestDto request,
        CancellationToken ct = default) =>
        await GetStructuredReportingExportAsync(request, accessContext: null, ct).ConfigureAwait(false);

    public async Task<StructuredReportingExportPayloadDto> GetStructuredReportingExportAsync(
        StructuredReportingExportRequestDto request,
        ReportAccessQueryContext? accessContext,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExportId);
        ct.ThrowIfCancellationRequested();

        var exportId = NormalizeStructuredExportId(request.ExportId);
        var workspace = await GetWorkspaceAsync(
            new FundOperationsWorkspaceQuery(
                request.FundProfileId,
                request.AsOf,
                request.Currency),
            accessContext,
            ct).ConfigureAwait(false);

        var descriptor = workspace.Reporting.StructuredExports?
            .FirstOrDefault(export => string.Equals(export.ExportId, exportId, StringComparison.OrdinalIgnoreCase));
        if (descriptor is null)
        {
            throw new KeyNotFoundException($"Structured reporting export '{request.ExportId}' is not available.");
        }

        var columns = GetStructuredExportColumns(exportId);
        var rows = BuildStructuredExportRows(exportId, workspace, ct);
        var warnings = descriptor.IsReady
            ? Array.Empty<string>()
            : [$"Export '{descriptor.ExportId}' is not ready. {descriptor.ValidationSummary}"];

        return new StructuredReportingExportPayloadDto(
            descriptor,
            columns,
            rows,
            warnings,
            DateTimeOffset.UtcNow);
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

        var generationStopwatch = Stopwatch.StartNew();
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        var schemaVersion = ResolveReportPackSchemaVersion(request.ExpectedSchemaVersion);
        var formats = NormalizeReportFormats(request.Formats);
        var brandingTheme = ResolveReportBrandingTheme(request.BrandingThemeId, request.BrandingThemeOverride);
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
        var lineagePointers = BuildLineagePointers(report, ledgerBook, runs, reconciliation, asOf);
        var provenance = new FundReportPackProvenanceDto(
            RelatedRunIds: runs.Select(static run => run.RunId).ToArray(),
            JournalEntryCount: ledger.JournalEntryCount,
            LedgerEntryCount: ledger.LedgerEntryCount,
            TrialBalanceLineCount: report.TrialBalance.Count,
            ReconciliationRunCount: reconciliation.RunCount,
            OpenReconciliationBreakCount: reconciliation.OpenBreakCount,
            SecurityResolvedCount: securityResolvedCount,
            SecurityMissingCount: securityMissingCount,
            LineagePointers: lineagePointers,
            SourceSnapshotHash: ComputeSourceSnapshotHash(
                normalizedFundProfileId,
                asOf,
                report,
                ledger,
                reconciliation,
                nav,
                runs),
            SchemaVersion: schemaVersion);
        var artifactContents = BuildReportPackArtifacts(report, formats, brandingTheme, ct);
        generationStopwatch.Stop();
        var warnings = BuildReportPackWarnings(report, reconciliation, runs.Count, securityMissingCount);
        var auditPackReadiness = BuildAuditPackReadiness(
            generationStopwatch.Elapsed,
            warnings,
            validationIssues,
            lifecycleEvents,
            provenance,
            artifactContents,
            report,
            ledger,
            reconciliation,
            runs.Count);
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
            Warnings: warnings,
            ContractName: GovernanceReportPackContract.ContractName,
            SchemaVersion: schemaVersion,
            BrandingTheme: brandingTheme)
        {
            Status = status,
            ValidationIssues = validationIssues,
            LifecycleEvents = lifecycleEvents,
            AuditPackReadiness = auditPackReadiness
        };

        return await repository
            .SaveAsync(snapshot, artifactContents, ct)
            .ConfigureAwait(false);
    }

    public Task<IReadOnlyList<FundReportPackHistoryItemDto>> GetReportPackHistoryAsync(
        string fundProfileId,
        int limit = 20,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fundProfileId);
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        return repository.GetHistoryAsync(fundProfileId.Trim(), limit, ct);
    }

    public Task<FundReportPackSnapshotDto?> GetReportPackAsync(
        Guid reportId,
        CancellationToken ct = default)
    {
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        return repository.GetAsync(reportId, ct);
    }

    public async Task<FundReportPackEvidenceBundleDto?> ExportReportPackEvidenceBundleAsync(
        Guid reportId,
        string? auditActor = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var repository = _reportPackRepository
            ?? throw new InvalidOperationException("Report-pack repository has not been configured.");
        var snapshot = await repository.GetAsync(reportId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            return null;
        }

        var bundleId = Guid.NewGuid();
        var actor = string.IsNullOrWhiteSpace(auditActor)
            ? snapshot.AuditActor
            : auditActor.Trim();
        var manifestPath = ResolveReportPackArtifactPath(snapshot, "manifest") ?? $"{BuildReportPackFundKey(snapshot.FundProfileId)}/{snapshot.ReportId:N}/manifest.json";
        var provenancePath = ResolveReportPackArtifactPath(snapshot, "provenance") ?? $"{BuildReportPackFundKey(snapshot.FundProfileId)}/{snapshot.ReportId:N}/provenance.json";
        var bundle = new FundReportPackEvidenceBundleDto(
            BundleId: bundleId,
            ReportId: snapshot.ReportId,
            FundProfileId: snapshot.FundProfileId,
            DisplayName: snapshot.DisplayName,
            ReportKind: snapshot.ReportKind,
            Currency: snapshot.Currency,
            AsOf: snapshot.AsOf,
            GeneratedAt: snapshot.GeneratedAt,
            ExportedAtUtc: DateTimeOffset.UtcNow,
            ExportedBy: actor,
            ManifestPath: manifestPath,
            ProvenancePath: provenancePath,
            SourceSnapshotHash: snapshot.Provenance.SourceSnapshotHash,
            Manifest: snapshot,
            Provenance: snapshot.Provenance,
            Approvals: BuildEvidenceBundleApprovals(snapshot),
            SourceLinks: BuildEvidenceBundleSourceLinks(snapshot),
            Artifacts: snapshot.Artifacts,
            Warnings: BuildEvidenceBundleWarnings(snapshot),
            BundleArtifact: null,
            ContractName: snapshot.ContractName,
            SchemaVersion: snapshot.SchemaVersion);

        return await repository
            .SaveEvidenceBundleAsync(snapshot, bundle, ct)
            .ConfigureAwait(false);
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

    private async Task<IReadOnlyList<StrategyRunEntry>> LoadFundScopedRunsAsync(CancellationToken ct)
    {
        var runs = new List<StrategyRunEntry>();
        await foreach (var run in _strategyRepository.GetAllRunsAsync(ct).WithCancellation(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(run.FundProfileId))
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

        return await BuildAccountWorkspaceProjectionsAsync(accounts, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> GetAllAccountProjectionsAsync(CancellationToken ct)
    {
        var accounts = await _fundAccountService
            .QueryAccountsAsync(new AccountStructureQuery(ActiveOnly: true), ct)
            .ConfigureAwait(false);

        return await BuildAccountWorkspaceProjectionsAsync(accounts, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<AccountWorkspaceProjection>> BuildAccountWorkspaceProjectionsAsync(
        IReadOnlyList<AccountSummaryDto> accounts,
        CancellationToken ct)
    {
        var projections = new List<AccountWorkspaceProjection>(accounts.Count);
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

            projections.Add(new AccountWorkspaceProjection(summary, latestSnapshot, account.FundId));
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

    private async Task<CashFinancingBuildResult> BuildCashFinancingSummaryAsync(
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
        var runSources = new List<PortfolioReportingRunSource>();

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
            runSources.Add(new PortfolioReportingRunSource(
                run.RunId,
                run.StrategyId,
                run.StrategyName,
                run.FundProfileId,
                run.FundDisplayName,
                portfolio));
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

        return new CashFinancingBuildResult(
            new CashFinancingSummary(
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
                Highlights: highlights),
            runSources);
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
                GovernanceReportArtifactFormatDto.Xlsx,
                GovernanceReportArtifactFormatDto.Html,
                GovernanceReportArtifactFormatDto.Pdf
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

    private static ReportBrandingThemeDto ResolveReportBrandingTheme(
        string? requestedThemeId,
        ReportBrandingThemeDto? overrideTheme)
    {
        if (overrideTheme is not null)
        {
            return NormalizeReportBrandingTheme(overrideTheme, isBuiltIn: false);
        }

        var themeId = string.IsNullOrWhiteSpace(requestedThemeId)
            ? BuiltInReportBrandingThemes[0].ThemeId
            : requestedThemeId.Trim();
        var theme = BuiltInReportBrandingThemes.FirstOrDefault(candidate =>
            string.Equals(candidate.ThemeId, themeId, StringComparison.OrdinalIgnoreCase));
        if (theme is null)
        {
            throw new ArgumentException($"Unknown report branding theme '{requestedThemeId}'.", nameof(requestedThemeId));
        }

        return theme;
    }

    private static ReportBrandingThemeDto NormalizeReportBrandingTheme(
        ReportBrandingThemeDto theme,
        bool isBuiltIn)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme.ThemeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(theme.FirmName);

        return new ReportBrandingThemeDto(
            NormalizeThemeId(theme.ThemeId),
            theme.Name.Trim(),
            theme.FirmName.Trim(),
            NormalizeHexColor(theme.PrimaryColor, nameof(theme.PrimaryColor)),
            NormalizeHexColor(theme.AccentColor, nameof(theme.AccentColor)),
            NormalizeHexColor(theme.TextColor, nameof(theme.TextColor)),
            NormalizeHexColor(theme.BackgroundColor, nameof(theme.BackgroundColor)),
            string.IsNullOrWhiteSpace(theme.LogoUri) ? null : theme.LogoUri.Trim(),
            string.IsNullOrWhiteSpace(theme.FooterText) ? null : theme.FooterText.Trim(),
            string.IsNullOrWhiteSpace(theme.Disclaimer) ? null : theme.Disclaimer.Trim(),
            isBuiltIn);
    }

    private static string NormalizeThemeId(string value)
    {
        var normalized = new string(value
            .Trim()
            .Where(static character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(static character => char.ToLowerInvariant(character))
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized)
            ? throw new ArgumentException("Report branding theme id must contain at least one letter, number, dash, or underscore.", nameof(value))
            : normalized;
    }

    private static string NormalizeHexColor(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#' || !trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            throw new ArgumentException($"{parameterName} must be a #RRGGBB color.", parameterName);
        }

        return trimmed.ToUpperInvariant();
    }

    private static int ResolveReportPackSchemaVersion(int? expectedSchemaVersion)
    {
        var schemaVersion = expectedSchemaVersion ?? GovernanceReportPackContract.CurrentSchemaVersion;
        if (schemaVersion != GovernanceReportPackContract.CurrentSchemaVersion)
        {
            throw new ArgumentException(
                $"Unsupported governed report-pack schema version '{schemaVersion}'. Current version is {GovernanceReportPackContract.CurrentSchemaVersion}.",
                nameof(expectedSchemaVersion));
        }

        return schemaVersion;
    }

    private static IReadOnlyList<GovernanceReportPackArtifactContent> BuildReportPackArtifacts(
        ReportPack report,
        IReadOnlyList<GovernanceReportArtifactFormatDto> formats,
        ReportBrandingThemeDto brandingTheme,
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
            artifacts.Add(new GovernanceReportPackArtifactContent("workbook", GovernanceReportArtifactFormatDto.Xlsx, "report-pack.xlsx", XlsxWorkbookWriter.CreateWorkbook(BuildWorkbookSheets(report, brandingTheme), ct)));
        }

        if (formats.Contains(GovernanceReportArtifactFormatDto.Html))
        {
            ct.ThrowIfCancellationRequested();
            artifacts.Add(new GovernanceReportPackArtifactContent("rendered-statement", GovernanceReportArtifactFormatDto.Html, "report-pack.html", BuildRenderedStatementHtml(report, brandingTheme)));
        }

        if (formats.Contains(GovernanceReportArtifactFormatDto.Pdf))
        {
            ct.ThrowIfCancellationRequested();
            artifacts.Add(new GovernanceReportPackArtifactContent("rendered-statement", GovernanceReportArtifactFormatDto.Pdf, "report-pack.pdf", BuildRenderedStatementPdf(report, brandingTheme)));
        }

        return artifacts.OrderBy(static artifact => artifact.FileName, StringComparer.Ordinal).ToArray();
    }

    private static byte[] BuildRenderedStatementHtml(ReportPack report, ReportBrandingThemeDto brandingTheme)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html lang=\"en\">");
        builder.AppendLine("<head><meta charset=\"utf-8\"><title>Meridian Report Pack</title>");
        builder.AppendLine("<style>");
        builder.AppendLine($":root{{--report-primary:{brandingTheme.PrimaryColor};--report-accent:{brandingTheme.AccentColor};--report-text:{brandingTheme.TextColor};--report-bg:{brandingTheme.BackgroundColor};}}");
        builder.AppendLine("body{margin:0;padding:40px;background:var(--report-bg);color:var(--report-text);font-family:Arial,sans-serif;line-height:1.45;}");
        builder.AppendLine("header{border-bottom:4px solid var(--report-primary);margin-bottom:28px;padding-bottom:18px;}");
        builder.AppendLine(".brand{display:flex;align-items:center;gap:14px;}");
        builder.AppendLine(".brand-mark{display:inline-flex;align-items:center;justify-content:center;min-width:48px;height:48px;border-radius:6px;background:var(--report-primary);color:#fff;font-weight:700;letter-spacing:.08em;}");
        builder.AppendLine(".brand-logo{max-height:48px;max-width:160px;object-fit:contain;}");
        builder.AppendLine("h1{color:var(--report-primary);margin:20px 0 8px;} h2{color:var(--report-primary);margin-top:28px;} table{width:100%;border-collapse:collapse;} th{background:var(--report-primary);color:#fff;text-align:left;} th,td{border:1px solid #d9e2ec;padding:8px;} .accent{color:var(--report-accent);font-weight:700;} footer{border-top:2px solid var(--report-accent);margin-top:32px;padding-top:12px;font-size:12px;}");
        builder.AppendLine("</style></head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<header>");
        builder.AppendLine("<div class=\"brand\">");
        if (!string.IsNullOrWhiteSpace(brandingTheme.LogoUri))
        {
            builder.AppendLine($"<img class=\"brand-logo\" src=\"{EscapeHtml(brandingTheme.LogoUri)}\" alt=\"{EscapeHtml(brandingTheme.FirmName)} logo\">");
        }
        else
        {
            builder.AppendLine($"<span class=\"brand-mark\">{EscapeHtml(BuildBrandMark(brandingTheme.FirmName))}</span>");
        }

        builder.AppendLine("<span>");
        builder.AppendLine($"<strong>{EscapeHtml(brandingTheme.FirmName)}</strong><br>");
        builder.AppendLine($"<span class=\"accent\">{EscapeHtml(brandingTheme.Name)}</span>");
        builder.AppendLine("</span></div>");
        builder.AppendLine("</header>");
        builder.AppendLine($"<h1>{EscapeHtml(report.ReportKind.ToString())}</h1>");
        builder.AppendLine($"<p><strong>Fund:</strong> {EscapeHtml(report.FundId)}</p>");
        builder.AppendLine($"<p><strong>As of:</strong> {report.AsOf:O}</p>");
        builder.AppendLine($"<p><strong>Total net assets:</strong> {report.TotalNetAssets.ToString(CultureInfo.InvariantCulture)}</p>");
        builder.AppendLine("<h2>Asset class sections</h2>");
        builder.AppendLine("<table><thead><tr><th>Asset class</th><th>Total</th><th>Rows</th></tr></thead><tbody>");
        foreach (var section in OrderAssetClassSections(report.AssetClassSections))
        {
            builder.AppendLine($"<tr><td>{EscapeHtml(section.AssetClass)}</td><td>{section.Total.ToString(CultureInfo.InvariantCulture)}</td><td>{section.Rows.Count}</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        builder.AppendLine("<h2>Trial balance</h2>");
        builder.AppendLine("<table><thead><tr><th>Account</th><th>Type</th><th>Symbol</th><th>Lookup</th><th>Net balance</th></tr></thead><tbody>");
        foreach (var row in OrderTrialBalanceRows(report.TrialBalance))
        {
            builder.AppendLine($"<tr><td>{EscapeHtml(row.AccountName)}</td><td>{EscapeHtml(row.AccountType)}</td><td>{EscapeHtml(row.Symbol)}</td><td>{EscapeHtml(row.LookupQuality)}</td><td>{row.NetBalance.ToString(CultureInfo.InvariantCulture)}</td></tr>");
        }

        builder.AppendLine("</tbody></table>");
        builder.AppendLine("<footer>");
        builder.AppendLine($"<p>{EscapeHtml(brandingTheme.FooterText ?? $"Generated for {brandingTheme.FirmName}")}</p>");
        if (!string.IsNullOrWhiteSpace(brandingTheme.Disclaimer))
        {
            builder.AppendLine($"<p>{EscapeHtml(brandingTheme.Disclaimer)}</p>");
        }

        builder.AppendLine("</footer>");
        builder.AppendLine("</body></html>");
        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static byte[] BuildRenderedStatementPdf(ReportPack report, ReportBrandingThemeDto brandingTheme)
    {
        var lines = new[]
        {
            $"{brandingTheme.FirmName} {report.ReportKind} Report Pack",
            $"Branding theme: {brandingTheme.Name} ({brandingTheme.ThemeId})",
            $"Fund: {report.FundId}",
            $"As of: {report.AsOf:yyyy-MM-dd}",
            $"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC",
            $"Total net assets: {report.TotalNetAssets.ToString(CultureInfo.InvariantCulture)}",
            $"Trial balance lines: {report.TrialBalance.Count}",
            $"Asset class sections: {report.AssetClassSections.Count}",
            brandingTheme.FooterText ?? $"Generated for {brandingTheme.FirmName}"
        };
        var content = new StringBuilder();
        content.AppendLine("BT");
        content.AppendLine("/F1 11 Tf");
        content.AppendLine("50 740 Td");
        foreach (var line in lines)
        {
            content.Append('(').Append(EscapePdfText(line)).AppendLine(") Tj");
            content.AppendLine("0 -18 Td");
        }

        content.AppendLine("ET");
        return BuildSinglePagePdf(content.ToString());
    }

    private static byte[] BuildSinglePagePdf(string contentStream)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var builder = new StringBuilder();
        builder.AppendLine("%PDF-1.4");
        var offsets = new List<int> { 0 };
        foreach (var (value, index) in objects.Select((value, index) => (value, index)))
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).AppendLine(" 0 obj");
            builder.AppendLine(value);
            builder.AppendLine("endobj");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Length + 1}");
        builder.AppendLine("0000000000 65535 f ");
        foreach (var offset in offsets.Skip(1))
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).AppendLine(" 00000 n ");
        }

        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Length + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string EscapeHtml(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string BuildBrandMark(string firmName)
    {
        var letters = firmName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static part => part[0])
            .Where(static character => char.IsLetterOrDigit(character))
            .Take(3)
            .ToArray();
        return letters.Length == 0 ? "RPT" : new string(letters).ToUpperInvariant();
    }

    private static string EscapePdfText(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);

    private static byte[] SerializeJsonArtifact<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, ReportArtifactJsonOptions);

    private static IReadOnlyList<XlsxWorksheet> BuildWorkbookSheets(ReportPack report, ReportBrandingThemeDto brandingTheme)
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
        IReadOnlyList<object?>[] brandingRows =
        [
            ["Theme ID", brandingTheme.ThemeId],
            ["Theme name", brandingTheme.Name],
            ["Firm name", brandingTheme.FirmName],
            ["Primary color", brandingTheme.PrimaryColor],
            ["Accent color", brandingTheme.AccentColor],
            ["Text color", brandingTheme.TextColor],
            ["Background color", brandingTheme.BackgroundColor],
            ["Logo URI", brandingTheme.LogoUri],
            ["Footer text", brandingTheme.FooterText],
            ["Disclaimer", brandingTheme.Disclaimer],
            ["Built in", brandingTheme.IsBuiltIn]
        ];

        return
        [
            new XlsxWorksheet("Trial Balance", TrialBalanceHeaders, trialBalanceRows),
            new XlsxWorksheet("Asset Classes", AssetClassHeaders, assetClassRows),
            new XlsxWorksheet("Branding", ["Field", "Value"], brandingRows)
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

    private static IReadOnlyList<FundReportPackEvidenceBundleApprovalDto> BuildEvidenceBundleApprovals(
        FundReportPackSnapshotDto snapshot)
        => snapshot.LifecycleEvents
            .Where(static item => item.ToStatus is
                GovernanceReportPackStatusDto.Generated or
                GovernanceReportPackStatusDto.Validated or
                GovernanceReportPackStatusDto.ReviewRequired or
                GovernanceReportPackStatusDto.Approved or
                GovernanceReportPackStatusDto.Published)
            .Select(static item => new FundReportPackEvidenceBundleApprovalDto(
                FromStatus: item.FromStatus,
                ToStatus: item.ToStatus,
                ApprovedAtUtc: item.ChangedAt,
                Actor: item.Actor,
                Reason: item.Reason,
                CorrelationId: item.CorrelationId))
            .OrderBy(static item => item.ApprovedAtUtc)
            .ToArray();

    private static IReadOnlyList<FundReportPackEvidenceBundleSourceLinkDto> BuildEvidenceBundleSourceLinks(
        FundReportPackSnapshotDto snapshot)
        => snapshot.Provenance.LineagePointers
            .Select(static pointer => new FundReportPackEvidenceBundleSourceLinkDto(
                SourceType: pointer.EvidenceType,
                SourceId: pointer.EvidenceId,
                Label: string.IsNullOrWhiteSpace(pointer.DisplayLabel)
                    ? $"{pointer.ScopeType} {pointer.ScopeKey}"
                    : pointer.DisplayLabel.Trim(),
                Route: pointer.Route,
                SourceSystem: string.IsNullOrWhiteSpace(pointer.SourceSystem)
                    ? pointer.ScopeType
                    : pointer.SourceSystem.Trim(),
                RelatedEvidenceIds: pointer.RelatedEvidenceIds ?? [],
                CapturedAtUtc: pointer.CapturedAt))
            .OrderBy(static item => item.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> BuildEvidenceBundleWarnings(FundReportPackSnapshotDto snapshot)
    {
        var warnings = new List<string>(snapshot.Warnings);
        if (snapshot.AuditPackReadiness is null)
        {
            warnings.Add("Audit-pack readiness metadata is not present on the retained manifest.");
        }

        if (snapshot.LifecycleEvents.Count == 0)
        {
            warnings.Add("No lifecycle approval events are retained on the report-pack manifest.");
        }

        if (snapshot.Provenance.LineagePointers.Count == 0)
        {
            warnings.Add("No source lineage pointers are retained on the report-pack provenance record.");
        }

        return warnings.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ResolveReportPackArtifactPath(FundReportPackSnapshotDto snapshot, string artifactKind)
        => snapshot.Artifacts
            .FirstOrDefault(artifact => string.Equals(artifact.ArtifactKind, artifactKind, StringComparison.OrdinalIgnoreCase))
            ?.RelativePath;

    private static string BuildReportPackFundKey(string fundProfileId)
    {
        var trimmed = fundProfileId.Trim();
        var buffer = new StringBuilder(trimmed.Length);
        foreach (var character in trimmed.ToLowerInvariant())
        {
            buffer.Append(char.IsLetterOrDigit(character) || character is '-' or '_' or '.'
                ? character
                : '-');
        }

        var key = buffer.ToString().Trim('-');
        return key.Length == 0 ? "fund" : key.Length <= 96 ? key : key[..96];
    }

    private static FundAuditPackReadinessDto BuildAuditPackReadiness(
        TimeSpan generatedIn,
        IReadOnlyList<string> reportWarnings,
        IReadOnlyList<FundReportPackValidationIssueDto> validationIssues,
        IReadOnlyList<FundReportPackLifecycleEventDto> lifecycleEvents,
        FundReportPackProvenanceDto provenance,
        IReadOnlyList<GovernanceReportPackArtifactContent> artifacts,
        ReportPack report,
        FundLedgerSummary ledger,
        ReconciliationSummary reconciliation,
        int runCount)
    {
        const int slaTargetSeconds = 60;
        var criticalValidationCount = validationIssues.Count(static issue =>
            issue.Severity == GovernanceReportValidationSeverityDto.Critical);
        var approvalEvidenceIds = lifecycleEvents
            .Where(static item => item.ToStatus is GovernanceReportPackStatusDto.Approved
                or GovernanceReportPackStatusDto.Published
                or GovernanceReportPackStatusDto.Retained)
            .Select(static item => item.CorrelationId)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var categories = new[]
        {
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.SourceRecords,
                "Source records",
                runCount > 0,
                runCount > 0
                    ? $"{runCount} fund-scoped run source record(s) contributed to the pack."
                    : "No retained fund-scoped source run is linked.",
                EvidenceIds(provenance.LineagePointers, "run"),
                UiApiRoutes.RunHistory),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.NormalizedActivity,
                "Normalized activity",
                report.TrialBalance.Count > 0,
                report.TrialBalance.Count > 0
                    ? $"{report.TrialBalance.Count} normalized trial-balance row(s) are included."
                    : "No normalized trial-balance rows are included.",
                EvidenceIds(provenance.LineagePointers, "ledger-account"),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.ReconciliationCases,
                "Reconciliation cases",
                reconciliation.RunCount > 0 && reconciliation.OpenBreakCount == 0,
                reconciliation.RunCount == 0
                    ? "No retained reconciliation run is linked."
                    : reconciliation.OpenBreakCount == 0
                        ? $"{reconciliation.RunCount} reconciliation run(s) are linked with no open breaks."
                        : $"{reconciliation.OpenBreakCount} open reconciliation break(s) remain.",
                EvidenceIds(provenance.LineagePointers, "reconciliation-summary"),
                UiApiRoutes.ReconciliationRuns),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.LedgerEvidence,
                "Ledger evidence",
                ledger.JournalEntryCount > 0 && ledger.LedgerEntryCount > 0,
                ledger.LedgerEntryCount > 0
                    ? $"{ledger.JournalEntryCount} journal entr{(ledger.JournalEntryCount == 1 ? "y" : "ies")} and {ledger.LedgerEntryCount} ledger entr{(ledger.LedgerEntryCount == 1 ? "y" : "ies")} are linked."
                    : "No journal or ledger evidence is linked.",
                EvidenceIds(provenance.LineagePointers, "ledger-account"),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.Approvals,
                "Approvals",
                approvalEvidenceIds.Length > 0,
                approvalEvidenceIds.Length > 0
                    ? "Approved, published, or retained lifecycle evidence is linked."
                    : "Report-pack approval or publication lifecycle evidence is still required.",
                approvalEvidenceIds,
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.ReportPack,
                "Report pack",
                report.TrialBalance.Count > 0 && criticalValidationCount == 0,
                criticalValidationCount == 0
                    ? "Report-pack manifest and provenance are ready for review."
                    : $"{criticalValidationCount} critical validation issue(s) block the report pack.",
                [report.ReportId.ToString("D"), provenance.SourceSnapshotHash],
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.Exports,
                "Exports",
                artifacts.Count > 0,
                artifacts.Count > 0
                    ? $"{artifacts.Count} export artifact(s) are prepared for retention."
                    : "No export artifacts were requested.",
                artifacts.Select(static item => item.FileName).ToArray(),
                UiApiRoutes.FundReportPacks),
            BuildAuditCategory(
                FundAuditEvidenceCategoryKeyDto.RestatementLineage,
                "Restatement lineage",
                true,
                "Source snapshot hash is retained so future restatements can point to this frozen baseline.",
                [provenance.SourceSnapshotHash],
                UiApiRoutes.FundReportPacks)
        };
        var missing = categories
            .Where(static category => !category.IsComplete)
            .Select(static category => category.Key)
            .ToArray();
        var readinessWarnings = reportWarnings
            .Concat(validationIssues
                .Where(static issue => issue.Severity != GovernanceReportValidationSeverityDto.Info)
                .Select(static issue => issue.Message))
            .Concat(categories
                .Where(static category => !category.IsComplete)
                .Select(static category => category.Status))
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FundAuditPackReadinessDto(
            IsComplete: missing.Length == 0,
            GeneratedInSeconds: Math.Round(generatedIn.TotalSeconds, 3, MidpointRounding.AwayFromZero),
            SlaTargetSeconds: slaTargetSeconds,
            SlaMet: generatedIn.TotalSeconds <= slaTargetSeconds,
            MissingEvidenceCategories: missing,
            Warnings: readinessWarnings,
            EvidenceCategorySummaries: categories);
    }

    private static FundAuditEvidenceCategorySummaryDto BuildAuditCategory(
        FundAuditEvidenceCategoryKeyDto key,
        string label,
        bool isComplete,
        string status,
        IReadOnlyList<string> evidenceIds,
        string? route)
        => new(
            key,
            label,
            isComplete,
            status,
            evidenceIds.Count,
            evidenceIds
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Select(static item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            route);

    private static IReadOnlyList<string> EvidenceIds(
        IReadOnlyList<FundReportPackLineagePointerDto> pointers,
        string evidenceType)
        => pointers
            .Where(pointer => string.Equals(pointer.EvidenceType, evidenceType, StringComparison.OrdinalIgnoreCase))
            .SelectMany(static pointer => new[] { pointer.EvidenceId }.Concat(pointer.RelatedEvidenceIds ?? []))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private sealed record CashFinancingBuildResult(
        CashFinancingSummary Summary,
        IReadOnlyList<PortfolioReportingRunSource> RunSources);

    private sealed record PortfolioReportingRunSource(
        string RunId,
        string StrategyId,
        string StrategyName,
        string? FundProfileId,
        string? FundDisplayName,
        PortfolioSummary Portfolio);

    private sealed record PortfolioReportingAnalyticsSource(
        PortfolioReportingAnalyticsScopeDto Scope,
        string Key,
        string Label,
        string? Symbol,
        string? Classification,
        string RunId,
        decimal RealizedPnl,
        decimal UnrealizedPnl);

    private sealed record PortfolioReportingAnalyticsGroup(
        PortfolioReportingAnalyticsScopeDto Scope,
        string Key,
        string Label,
        string? Symbol,
        string? Classification,
        decimal RealizedPnl,
        decimal UnrealizedPnl,
        decimal TotalPnl,
        int SourceCount);

    private sealed record CrossFundReportingSource(
        string FundKey,
        string FundLabel,
        string? EntityKey,
        string? EntityLabel,
        int AccountCount,
        int RunCount,
        decimal GrossExposure,
        decimal NetExposure,
        decimal LongMarketValue,
        decimal ShortMarketValue,
        decimal TotalCash,
        decimal PendingSettlement,
        decimal TotalPnl,
        decimal ShadowNav,
        DateTimeOffset? SourceAsOfUtc);

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
            _ => "Reconciliation decision posture is aligned with shared Accounting state."
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
            AuditReferences: traceReferences.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            AccountingRecordSummary: activeWorkflow?.AccountingRecordSummary);
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
        string fundProfileId,
        IReadOnlyList<FundAccountSummary> accounts,
        FundLedgerSummary ledger,
        FundLedgerReconciliationSnapshot ledgerReconciliationSnapshot,
        CashFinancingSummary cashFinancing,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations,
        DateTimeOffset asOf,
        ReportAccessQueryContext? accessContext = null)
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
        var workflowRecords = FilterReportPackWorkflowRecords(BuildReportPackWorkflowRecords(accounts, asOf), accessContext);
        var filteredReportingPayload = accessContext is not null
            ? _reportPackRunReadService?.BuildPayload(accessContext)
            : null;
        var deliveryAttempts = filteredReportingPayload?.DeliveryAttempts
            ?? _reportPackDeliveryService?.ListAttempts(500)
            ?? [];
        var schedules = filteredReportingPayload?.Schedules
            ?? _reportingScheduleService?.ListSchedules(100)
            ?? [];
        var scheduleDeliveryPlans = filteredReportingPayload?.ScheduleDeliveryPlans
            ?? ReportPackRunReadService.BuildScheduleDeliveryPlans(schedules, deliveryAttempts);
        var distributions = filteredReportingPayload?.ReportPackDistributions
            ?? ReportPackRunReadService.BuildDistributionRecords(workflowRecords, deliveryAttempts);
        var portfolioCuts = BuildPortfolioReportingCuts(accounts, cashFinancing, nav, runSources, asOf);
        var livePortfolioViews = BuildPortfolioReportingLiveViews(accounts.Count, portfolioCuts, runSources, asOf);
        var pnlSlices = BuildPortfolioReportingPnlSlices(runSources, cashFinancing.Currency, asOf);
        var analyticsRows = BuildPortfolioReportingAnalyticsRows(runSources, cashFinancing.Currency, asOf);
        var structuredExports = BuildStructuredReportingExports(
            fundProfileId,
            ledger,
            ledgerReconciliationSnapshot,
            portfolioCuts,
            analyticsRows,
            crossFundConsolidations,
            cashFinancing.Currency,
            asOf);

        return new FundReportingSummaryDto(
            ProfileCount: profiles.Length,
            RecommendedProfiles: recommended,
            ReportPackDistributions: distributions,
            Profiles: profiles,
            Summary: $"{profiles.Length} export/reporting profiles are available for Accounting and Reporting workflows.",
            WorkflowRecords: workflowRecords,
            Schedules: schedules,
            DeliveryAttempts: deliveryAttempts,
            PortfolioCuts: portfolioCuts,
            StructuredExports: structuredExports,
            BrandingThemes: BuiltInReportBrandingThemes,
            LivePortfolioViews: livePortfolioViews,
            CrossFundConsolidations: crossFundConsolidations,
            PnlSlices: pnlSlices,
            AnalyticsRows: analyticsRows,
            FundProfileId: fundProfileId,
            ScheduleDeliveryPlans: scheduleDeliveryPlans);
    }

    private async Task<IReadOnlyList<CrossFundReportingConsolidationDto>> BuildCrossFundReportingConsolidationsAsync(
        string currentFundProfileId,
        string currency,
        IReadOnlyList<AccountWorkspaceProjection> allAccountProjections,
        IReadOnlyList<StrategyRunEntry> allRuns,
        DateTimeOffset asOf,
        CancellationToken ct)
    {
        var sources = new List<CrossFundReportingSource>(allAccountProjections.Count + allRuns.Count);
        var currentFundId = TranslateFundProfileId(currentFundProfileId);
        var fundProfileByFundId = allRuns
            .Where(static run => !string.IsNullOrWhiteSpace(run.FundProfileId))
            .GroupBy(run => TranslateFundProfileId(run.FundProfileId!.Trim()))
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static run => run.FundProfileId!.Trim())
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                    .First(),
                EqualityComparer<Guid>.Default);

        foreach (var projection in allAccountProjections)
        {
            ct.ThrowIfCancellationRequested();
            var account = projection.Summary;
            var fundKey = ResolveCrossFundAccountKey(projection.FundId, currentFundProfileId, currentFundId, fundProfileByFundId);
            var entityKey = account.EntityId?.ToString("D");
            var securitiesMarketValue = account.SecuritiesMarketValue;
            sources.Add(new CrossFundReportingSource(
                FundKey: fundKey,
                FundLabel: BuildCrossFundFundLabel(fundKey, currentFundProfileId),
                EntityKey: entityKey,
                EntityLabel: entityKey is null ? null : $"Entity {entityKey}",
                AccountCount: 1,
                RunCount: 0,
                GrossExposure: Math.Abs(securitiesMarketValue),
                NetExposure: securitiesMarketValue,
                LongMarketValue: Math.Max(securitiesMarketValue, 0m),
                ShortMarketValue: Math.Min(securitiesMarketValue, 0m),
                TotalCash: account.CashBalance,
                PendingSettlement: projection.LatestSnapshot?.PendingSettlement ?? 0m,
                TotalPnl: 0m,
                ShadowNav: account.NetAssetValue,
                SourceAsOfUtc: projection.LatestSnapshot is null
                    ? null
                    : new DateTimeOffset(projection.LatestSnapshot.AsOfDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)));
        }

        foreach (var runSource in await BuildPortfolioReportingRunSourcesAsync(allRuns, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var fundKey = string.IsNullOrWhiteSpace(runSource.FundProfileId)
                ? "fund:unassigned-runs"
                : runSource.FundProfileId.Trim();
            var portfolio = runSource.Portfolio;
            sources.Add(new CrossFundReportingSource(
                FundKey: fundKey,
                FundLabel: string.IsNullOrWhiteSpace(runSource.FundDisplayName)
                    ? fundKey
                    : runSource.FundDisplayName.Trim(),
                EntityKey: null,
                EntityLabel: null,
                AccountCount: 0,
                RunCount: 1,
                GrossExposure: portfolio.GrossExposure,
                NetExposure: portfolio.NetExposure,
                LongMarketValue: portfolio.LongMarketValue,
                ShortMarketValue: portfolio.ShortMarketValue,
                TotalCash: portfolio.Cash,
                PendingSettlement: 0m,
                TotalPnl: portfolio.RealizedPnl + portfolio.UnrealizedPnl,
                ShadowNav: portfolio.TotalEquity,
                SourceAsOfUtc: portfolio.AsOf));
        }

        if (sources.Count == 0)
        {
            return
            [
                BuildCrossFundConsolidationRow(
                    "cross-fund:company",
                    "Company-wide consolidation",
                    CrossFundReportingConsolidationScopeDto.Company,
                    currency,
                    [],
                    asOf,
                    ["company", "cross-fund", "blocked"])
            ];
        }

        var rows = new List<CrossFundReportingConsolidationDto>
        {
            BuildCrossFundConsolidationRow(
                "cross-fund:company",
                "Company-wide consolidation",
                CrossFundReportingConsolidationScopeDto.Company,
                currency,
                sources,
                asOf,
                ["company", "cross-fund", "consolidated"])
        };

        rows.AddRange(sources
            .GroupBy(static source => source.FundKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildCrossFundConsolidationRow(
                $"cross-fund:fund:{NormalizeCutId(group.Key)}",
                group.OrderByDescending(static source => source.RunCount)
                    .Select(static source => source.FundLabel)
                    .FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label))
                    ?? group.Key,
                CrossFundReportingConsolidationScopeDto.Fund,
                currency,
                group.ToArray(),
                asOf,
                ["fund", "cross-fund", "consolidated"])));

        rows.AddRange(sources
            .Where(static source => !string.IsNullOrWhiteSpace(source.EntityKey))
            .GroupBy(static source => source.EntityKey!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => BuildCrossFundConsolidationRow(
                $"cross-fund:entity:{NormalizeCutId(group.Key)}",
                group.Select(static source => source.EntityLabel).FirstOrDefault(static label => !string.IsNullOrWhiteSpace(label)) ?? $"Entity {group.Key}",
                CrossFundReportingConsolidationScopeDto.Entity,
                currency,
                group.ToArray(),
                asOf,
                ["entity", "cross-fund", "consolidated"])));

        return rows;
    }

    private async Task<IReadOnlyList<PortfolioReportingRunSource>> BuildPortfolioReportingRunSourcesAsync(
        IReadOnlyList<StrategyRunEntry> runs,
        CancellationToken ct)
    {
        var runSources = new List<PortfolioReportingRunSource>(runs.Count);
        foreach (var run in runs)
        {
            ct.ThrowIfCancellationRequested();
            var portfolio = await _portfolioReadService.BuildSummaryAsync(run, ct).ConfigureAwait(false);
            if (portfolio is null)
            {
                continue;
            }

            runSources.Add(new PortfolioReportingRunSource(
                run.RunId,
                run.StrategyId,
                run.StrategyName,
                run.FundProfileId,
                run.FundDisplayName,
                portfolio));
        }

        return runSources;
    }

    private static CrossFundReportingConsolidationDto BuildCrossFundConsolidationRow(
        string consolidationId,
        string label,
        CrossFundReportingConsolidationScopeDto scope,
        string currency,
        IReadOnlyList<CrossFundReportingSource> sources,
        DateTimeOffset asOf,
        IReadOnlyList<string> tags)
    {
        var fundCount = sources.Select(static source => source.FundKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var entityCount = sources
            .Where(static source => !string.IsNullOrWhiteSpace(source.EntityKey))
            .Select(static source => source.EntityKey!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var accountCount = sources.Sum(static source => source.AccountCount);
        var runCount = sources.Sum(static source => source.RunCount);
        var sourceCount = accountCount + runCount;
        var grossExposure = sources.Sum(static source => source.GrossExposure);
        var netExposure = sources.Sum(static source => source.NetExposure);
        var shadowNav = sources.Sum(static source => source.ShadowNav);
        var shadowNavVariance = shadowNav - netExposure;
        var isReady = sourceCount > 0;

        return new CrossFundReportingConsolidationDto(
            ConsolidationId: consolidationId,
            Label: label,
            Scope: scope,
            Currency: currency,
            IsReady: isReady,
            FundCount: fundCount,
            EntityCount: entityCount,
            AccountCount: accountCount,
            RunCount: runCount,
            GrossExposure: grossExposure,
            NetExposure: netExposure,
            LongMarketValue: sources.Sum(static source => source.LongMarketValue),
            ShortMarketValue: sources.Sum(static source => source.ShortMarketValue),
            TotalCash: sources.Sum(static source => source.TotalCash),
            PendingSettlement: sources.Sum(static source => source.PendingSettlement),
            TotalPnl: sources.Sum(static source => source.TotalPnl),
            ShadowNav: shadowNav,
            ShadowNavVariance: shadowNavVariance,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: BuildCrossFundConsolidationRoute(consolidationId),
            ReadinessSummary: isReady
                ? $"{sourceCount} source record(s) across {fundCount} fund(s), {entityCount} entity row(s), {accountCount} account(s), and {runCount} run(s)."
                : "No active fund accounts or fund-scoped runs are available for cross-fund consolidation.",
            Tags: tags,
            VersionStamp: BuildCrossFundConsolidationVersionStamp(asOf, fundCount, entityCount, sourceCount));
    }

    private static string ResolveCrossFundAccountKey(
        Guid? accountFundId,
        string currentFundProfileId,
        Guid currentFundId,
        IReadOnlyDictionary<Guid, string> fundProfileByFundId)
    {
        if (accountFundId == currentFundId)
        {
            return currentFundProfileId;
        }

        if (accountFundId.HasValue && fundProfileByFundId.TryGetValue(accountFundId.Value, out var fundProfileId))
        {
            return fundProfileId;
        }

        return accountFundId?.ToString("D") ?? "fund:unassigned-accounts";
    }

    private static string BuildCrossFundFundLabel(string fundKey, string currentFundProfileId)
        => string.Equals(fundKey, currentFundProfileId, StringComparison.OrdinalIgnoreCase)
            ? currentFundProfileId
            : fundKey.StartsWith("fund:", StringComparison.OrdinalIgnoreCase)
                ? fundKey
                : $"Fund {fundKey}";

    private static string BuildCrossFundConsolidationRoute(string consolidationId)
        => UiApiRoutes.WithQuery(
            UiApiRoutes.WorkstationReporting,
            $"consolidationId={Uri.EscapeDataString(consolidationId)}");

    private static string BuildCrossFundConsolidationVersionStamp(
        DateTimeOffset asOf,
        int fundCount,
        int entityCount,
        int sourceCount)
        => $"cross-fund:{asOf.UtcDateTime:yyyyMMddHHmmss}:funds-{fundCount}:entities-{entityCount}:sources-{sourceCount}";

    private static IReadOnlyList<StructuredReportingExportDto> BuildStructuredReportingExports(
        string fundProfileId,
        FundLedgerSummary ledger,
        FundLedgerReconciliationSnapshot ledgerReconciliationSnapshot,
        IReadOnlyList<PortfolioReportingCutDto> portfolioCuts,
        IReadOnlyList<PortfolioReportingAnalyticsRowDto> analyticsRows,
        IReadOnlyList<CrossFundReportingConsolidationDto> crossFundConsolidations,
        string currency,
        DateTimeOffset asOf)
    {
        var trialBalanceSourceCount = ledger.JournalEntryCount + ledger.LedgerEntryCount;
        var ledgerFactSourceCount = ledgerReconciliationSnapshot.Consolidated.JournalEntryCount
            + ledgerReconciliationSnapshot.Consolidated.LedgerEntryCount;
        var portfolioSourceCount = portfolioCuts.Sum(static cut => cut.SourceCount);
        var portfolioReady = portfolioCuts.Any(static cut =>
            cut.GrossExposure != 0m
            || cut.NetExposure != 0m
            || cut.TotalCash != 0m
            || cut.TotalPnl != 0m
            || cut.ShadowNav != 0m);
        var crossFundSourceCount = crossFundConsolidations
            .FirstOrDefault(static row => row.Scope == CrossFundReportingConsolidationScopeDto.Company)
            ?.SourceCount
            ?? crossFundConsolidations.Sum(static row => row.SourceCount);
        var crossFundReady = crossFundConsolidations.Any(static row => row.IsReady);
        var analyticsReady = analyticsRows.Any(static row =>
            !string.Equals(row.AnalyticsId, "analytics:blocked", StringComparison.OrdinalIgnoreCase)
            && row.SourceCount > 0);
        var analyticsSourceCount = analyticsRows
            .Where(static row => !string.Equals(row.AnalyticsId, "analytics:blocked", StringComparison.OrdinalIgnoreCase))
            .Sum(static row => row.SourceCount);

        return
        [
            BuildStructuredExportDescriptor(
                fundProfileId,
                RegulatoryTrialBalanceExportId,
                "Regulatory trial balance",
                StructuredReportingExportPurposeDto.Regulatory,
                GovernanceReportArtifactFormatDto.Csv,
                "fund-trial-balance",
                "Regulatory and compliance reporting",
                ledger.TrialBalance.Count,
                RegulatoryTrialBalanceColumns.Length,
                trialBalanceSourceCount,
                currency,
                asOf,
                ledger.TrialBalance.Count > 0 && trialBalanceSourceCount > 0,
                "Exports normalized trial-balance rows with ledger source counts and Security Master identifiers.",
                "No normalized trial-balance rows are available for this as-of date.",
                ["regulatory", "trial-balance", "ledger"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                WarehouseLedgerFactsExportId,
                "Warehouse ledger facts",
                StructuredReportingExportPurposeDto.DataWarehouse,
                GovernanceReportArtifactFormatDto.Json,
                "ledger-reconciliation-facts",
                "Data warehouse and lakehouse ingestion",
                ledgerReconciliationSnapshot.Consolidated.Balances.Count,
                WarehouseLedgerFactColumns.Length,
                ledgerFactSourceCount,
                currency,
                asOf,
                ledgerReconciliationSnapshot.Consolidated.Balances.Count > 0 && ledgerFactSourceCount > 0,
                "Exports consolidated ledger snapshot facts for downstream warehouse loading.",
                "No consolidated ledger snapshot facts are available for this as-of date.",
                ["warehouse", "ledger", "reconciliation"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentPortfolioCutsExportId,
                "Investment portfolio cuts",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "portfolio-reporting-cuts",
                "Investment and risk decision workflows",
                portfolioCuts.Count,
                InvestmentPortfolioCutColumns.Length,
                portfolioSourceCount,
                currency,
                asOf,
                portfolioReady,
                "Exports fund, strategy, and user-tag exposure, cash, P&L, and shadow-NAV cuts.",
                "No source-backed portfolio cut values are available for this as-of date.",
                ["investment", "portfolio-cuts", "shadow-nav"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                InvestmentTopNContributionAnalyticsExportId,
                "Top-N contribution analytics",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Csv,
                "portfolio-topn-contribution-analytics",
                "Investment and risk decision workflows",
                analyticsRows.Count,
                InvestmentTopNContributionAnalyticsColumns.Length,
                analyticsSourceCount,
                currency,
                asOf,
                analyticsReady,
                "Exports source-backed Top-N winners, laggards, and contribution rows with P&L percentages and heat-map intensities.",
                "No source-backed Top-N or contribution analytics rows are available for this as-of date.",
                ["investment", "top-n", "contribution", "analytics"]),
            BuildStructuredExportDescriptor(
                fundProfileId,
                CrossFundConsolidationExportId,
                "Cross-fund consolidation",
                StructuredReportingExportPurposeDto.InvestmentDecision,
                GovernanceReportArtifactFormatDto.Xlsx,
                "cross-fund-reporting-consolidation",
                "Investment and operating committee reporting",
                crossFundConsolidations.Count,
                CrossFundConsolidationColumns.Length,
                crossFundSourceCount,
                currency,
                asOf,
                crossFundReady,
                "Exports company, fund, and entity-level consolidated exposure and performance rows.",
                "No source-backed cross-fund consolidation rows are available for this as-of date.",
                ["investment", "cross-fund", "consolidation"])
        ];
    }

    private static StructuredReportingExportDto BuildStructuredExportDescriptor(
        string fundProfileId,
        string exportId,
        string label,
        StructuredReportingExportPurposeDto purpose,
        GovernanceReportArtifactFormatDto format,
        string dataset,
        string consumer,
        int rowCount,
        int fieldCount,
        int sourceCount,
        string currency,
        DateTimeOffset asOf,
        bool isReady,
        string readySummary,
        string blockedSummary,
        IReadOnlyList<string> tags)
    {
        return new StructuredReportingExportDto(
            exportId,
            label,
            purpose,
            format,
            dataset,
            consumer,
            StructuredReportingExportSchemaVersion,
            rowCount,
            fieldCount,
            sourceCount,
            currency,
            asOf,
            isReady,
            BuildStructuredExportRetainedPath(fundProfileId, exportId, asOf, format),
            BuildStructuredExportRoute(fundProfileId, exportId, asOf, currency, format),
            UiApiRoutes.WorkstationReporting,
            isReady
                ? $"{readySummary} {rowCount} row(s), {fieldCount} field(s), and {sourceCount} source record(s) are ready."
                : blockedSummary,
            UiApiRoutes.FundReportPacks,
            BuildStructuredExportVersionStamp(asOf, rowCount, sourceCount),
            tags);
    }

    private static IReadOnlyList<PortfolioReportingCutDto> BuildPortfolioReportingCuts(
        IReadOnlyList<FundAccountSummary> accounts,
        CashFinancingSummary cashFinancing,
        FundNavAttributionSummaryDto nav,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        DateTimeOffset asOf)
    {
        var cuts = new List<PortfolioReportingCutDto>
        {
            new(
                "fund:consolidated",
                "Consolidated fund",
                PortfolioReportingCutKindDto.Fund,
                cashFinancing.Currency,
                cashFinancing.GrossExposure,
                cashFinancing.NetExposure,
                cashFinancing.LongMarketValue,
                cashFinancing.ShortMarketValue,
                cashFinancing.TotalCash,
                cashFinancing.PendingSettlement,
                cashFinancing.RealizedPnl,
                cashFinancing.UnrealizedPnl,
                cashFinancing.RealizedPnl + cashFinancing.UnrealizedPnl,
                nav.TotalNav,
                nav.TotalNav - cashFinancing.TotalEquity,
                Math.Max(1, accounts.Count + runSources.Count),
                ["fund", "consolidated"],
                asOf,
                UiApiRoutes.FundReportPacks,
                "Shadow NAV is sourced from the shared NAV attribution service and compared with run equity.",
                BuildPortfolioCutVersionStamp(asOf, runSources.Count, accounts.Count))
        };

        cuts.AddRange(runSources
            .GroupBy(static source => string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var ordered = group.OrderBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase).ToArray();
                var first = ordered[0];
                var summary = ordered.Select(static source => source.Portfolio).ToArray();
                return new PortfolioReportingCutDto(
                    $"strategy:{NormalizeCutId(group.Key)}",
                    first.StrategyName,
                    PortfolioReportingCutKindDto.Strategy,
                    cashFinancing.Currency,
                    summary.Sum(static item => item.GrossExposure),
                    summary.Sum(static item => item.NetExposure),
                    summary.Sum(static item => item.LongMarketValue),
                    summary.Sum(static item => item.ShortMarketValue),
                    summary.Sum(static item => item.Cash),
                    0m,
                    summary.Sum(static item => item.RealizedPnl),
                    summary.Sum(static item => item.UnrealizedPnl),
                    summary.Sum(static item => item.RealizedPnl + item.UnrealizedPnl),
                    summary.Sum(static item => item.TotalEquity),
                    0m,
                    ordered.Length,
                    ordered.Select(static source => source.RunId).ToArray(),
                    asOf,
                    UiApiRoutes.FundReportPacks,
                    "Strategy shadow NAV uses the latest contributing run equity.",
                    BuildPortfolioCutVersionStamp(asOf, ordered.Length, 0));
            }));

        cuts.AddRange(accounts
            .GroupBy(static account => string.IsNullOrWhiteSpace(account.StructureLabel) ? "Unassigned" : account.StructureLabel.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupedAccounts = group.ToArray();
                var securitiesMarketValue = groupedAccounts.Sum(static account => account.SecuritiesMarketValue);
                var cash = groupedAccounts.Sum(static account => account.CashBalance);
                var shadowNav = groupedAccounts.Sum(static account => account.NetAssetValue);
                return new PortfolioReportingCutDto(
                    $"tag:{NormalizeCutId(group.Key)}",
                    group.Key,
                    PortfolioReportingCutKindDto.UserTag,
                    cashFinancing.Currency,
                    Math.Abs(securitiesMarketValue),
                    securitiesMarketValue,
                    Math.Max(securitiesMarketValue, 0m),
                    Math.Min(securitiesMarketValue, 0m),
                    cash,
                    0m,
                    0m,
                    0m,
                    0m,
                    shadowNav,
                    0m,
                    groupedAccounts.Length,
                    groupedAccounts.Select(static account => account.AccountCode).ToArray(),
                    asOf,
                    UiApiRoutes.FundReportPacks,
                    "Tag shadow NAV uses account-level net asset value until administrator NAV statements are linked.",
                    BuildPortfolioCutVersionStamp(asOf, 0, groupedAccounts.Length));
            }));

        return cuts
            .OrderBy(static cut => cut.Kind)
            .ThenBy(static cut => cut.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PortfolioReportingLiveViewDto> BuildPortfolioReportingLiveViews(
        int accountSourceCount,
        IReadOnlyList<PortfolioReportingCutDto> cuts,
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        DateTimeOffset asOf)
    {
        var strategySources = runSources
            .GroupBy(static source => string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => $"strategy:{NormalizeCutId(group.Key)}",
                group =>
                {
                    var ordered = group.OrderBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase).ToArray();
                    var latestSourceAsOf = ordered.Max(static source => source.Portfolio.AsOf);
                    var cashLadderRoute = ordered.Length == 1
                        ? UiApiRoutes.WithParam(UiApiRoutes.PortfolioCashFlows, "runId", ordered[0].RunId)
                        : null;
                    return (StrategyId: ordered[0].StrategyId, LatestSourceAsOf: latestSourceAsOf, CashLadderRoute: cashLadderRoute);
                },
                StringComparer.OrdinalIgnoreCase);
        var latestFundSourceAsOf = runSources.Count == 0
            ? (DateTimeOffset?)null
            : runSources.Max(static source => source.Portfolio.AsOf);

        return cuts
            .Select(cut =>
            {
                strategySources.TryGetValue(cut.CutId, out var strategySource);
                var actualSourceCount = cut.Kind == PortfolioReportingCutKindDto.Fund
                    ? accountSourceCount + runSources.Count
                    : cut.SourceCount;
                var sourceAsOf = cut.Kind switch
                {
                    PortfolioReportingCutKindDto.Fund => latestFundSourceAsOf ?? (accountSourceCount > 0 ? asOf : null),
                    PortfolioReportingCutKindDto.Strategy => strategySource.LatestSourceAsOf == default ? null : strategySource.LatestSourceAsOf,
                    PortfolioReportingCutKindDto.UserTag => actualSourceCount > 0 ? asOf : null,
                    _ => null
                };
                var state = ResolveLivePortfolioViewState(actualSourceCount, sourceAsOf, asOf);
                var route = BuildLivePortfolioViewRoute(cut, strategySource.StrategyId);
                var cashLadderRoute = strategySource.CashLadderRoute;

                return new PortfolioReportingLiveViewDto(
                    ViewId: $"live:{cut.CutId}",
                    Label: cut.Label,
                    Kind: cut.Kind,
                    State: state,
                    Currency: cut.Currency,
                    GrossExposure: cut.GrossExposure,
                    NetExposure: cut.NetExposure,
                    TotalCash: cut.TotalCash,
                    PendingSettlement: cut.PendingSettlement,
                    TotalPnl: cut.TotalPnl,
                    ShadowNav: cut.ShadowNav,
                    AsOf: cut.AsOf,
                    SourceAsOfUtc: sourceAsOf,
                    SourceCount: actualSourceCount,
                    Route: route,
                    LiquiditySummary: BuildLivePortfolioLiquiditySummary(cut),
                    CashLadderSummary: BuildLivePortfolioCashLadderSummary(cut, cashLadderRoute),
                    TelemetrySummary: BuildLivePortfolioTelemetrySummary(state, sourceAsOf, asOf),
                    Tags: cut.Tags,
                    CashLadderRoute: cashLadderRoute,
                    VersionStamp: cut.VersionStamp,
                    ReadinessBlockers: BuildLivePortfolioReadinessBlockers(state, sourceAsOf, asOf, cut, cashLadderRoute));
            })
            .OrderBy(static view => view.Kind)
            .ThenBy(static view => view.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<PortfolioReportingPnlSliceDto> BuildPortfolioReportingPnlSlices(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        string currency,
        DateTimeOffset asOf)
    {
        var endDate = DateOnly.FromDateTime(asOf.UtcDateTime);
        var boundedSources = runSources
            .Where(source => source.Portfolio.AsOf <= asOf)
            .ToArray();

        return
        [
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Daily,
                "Daily P&L",
                currency,
                endDate,
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Weekly,
                "Weekly P&L",
                currency,
                endDate.AddDays(-6),
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Monthly,
                "Monthly P&L",
                currency,
                new DateOnly(endDate.Year, endDate.Month, 1),
                endDate,
                asOf),
            BuildPortfolioReportingPnlSlice(
                boundedSources,
                PortfolioReportingPnlSlicePeriodDto.Yearly,
                "Yearly P&L",
                currency,
                new DateOnly(endDate.Year, 1, 1),
                endDate,
                asOf)
        ];
    }

    private static PortfolioReportingPnlSliceDto BuildPortfolioReportingPnlSlice(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        PortfolioReportingPnlSlicePeriodDto period,
        string label,
        string currency,
        DateOnly startDate,
        DateOnly endDate,
        DateTimeOffset asOf)
    {
        var currentSources = SelectPortfolioReportingSourcesInWindow(runSources, startDate, endDate);
        var priorWindow = BuildPriorPnlWindow(period, startDate, endDate);
        var priorSources = SelectPortfolioReportingSourcesInWindow(runSources, priorWindow.Start, priorWindow.End);
        var realized = currentSources.Sum(static source => source.Portfolio.RealizedPnl);
        var unrealized = currentSources.Sum(static source => source.Portfolio.UnrealizedPnl);
        var total = realized + unrealized;
        var priorTotal = priorSources.Sum(static source => source.Portfolio.RealizedPnl + source.Portfolio.UnrealizedPnl);
        var normalizedPeriod = period.ToString().ToLowerInvariant();
        var sourceCount = currentSources.Count;

        return new PortfolioReportingPnlSliceDto(
            SliceId: $"pnl:{normalizedPeriod}",
            Period: period,
            Label: label,
            Currency: currency,
            StartDate: startDate,
            EndDate: endDate,
            RealizedPnl: realized,
            UnrealizedPnl: unrealized,
            TotalPnl: total,
            PriorTotalPnl: priorTotal,
            PnlChange: total - priorTotal,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"pnlSlice={Uri.EscapeDataString(normalizedPeriod)}"),
            ReadinessSummary: BuildPortfolioReportingPnlSliceReadiness(period, sourceCount, priorSources.Count),
            Tags: sourceCount == 0
                ? ["pnl", normalizedPeriod, "blocked"]
                : ["pnl", normalizedPeriod, "source-backed"],
            VersionStamp: BuildPortfolioReportingPnlSliceVersionStamp(asOf, period, sourceCount, priorSources.Count));
    }

    private static (DateOnly Start, DateOnly End) BuildPriorPnlWindow(
        PortfolioReportingPnlSlicePeriodDto period,
        DateOnly startDate,
        DateOnly endDate)
        => period switch
        {
            PortfolioReportingPnlSlicePeriodDto.Daily => (startDate.AddDays(-1), endDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Weekly => (startDate.AddDays(-7), startDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Monthly => (startDate.AddMonths(-1), startDate.AddDays(-1)),
            PortfolioReportingPnlSlicePeriodDto.Yearly => (startDate.AddYears(-1), startDate.AddDays(-1)),
            _ => (startDate, endDate)
        };

    private static IReadOnlyList<PortfolioReportingRunSource> SelectPortfolioReportingSourcesInWindow(
        IReadOnlyList<PortfolioReportingRunSource> sources,
        DateOnly startDate,
        DateOnly endDate)
        => sources
            .Where(source =>
            {
                var sourceDate = DateOnly.FromDateTime(source.Portfolio.AsOf.UtcDateTime);
                return sourceDate.DayNumber >= startDate.DayNumber && sourceDate.DayNumber <= endDate.DayNumber;
            })
            .OrderBy(static source => source.Portfolio.AsOf)
            .ThenBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildPortfolioReportingPnlSliceReadiness(
        PortfolioReportingPnlSlicePeriodDto period,
        int sourceCount,
        int priorSourceCount)
    {
        var periodLabel = period.ToString().ToLowerInvariant();
        if (sourceCount <= 0)
        {
            return $"Blocked: no source-backed P&L runs are available for the {periodLabel} window.";
        }

        return priorSourceCount == 0
            ? $"{sourceCount} source-backed run(s) in the {periodLabel} window; no prior-period source run is available for comparison."
            : $"{sourceCount} source-backed run(s) in the {periodLabel} window; compared with {priorSourceCount} prior-period run(s).";
    }

    private static IReadOnlyList<PortfolioReportingAnalyticsRowDto> BuildPortfolioReportingAnalyticsRows(
        IReadOnlyList<PortfolioReportingRunSource> runSources,
        string currency,
        DateTimeOffset asOf)
    {
        var boundedSources = runSources
            .Where(source => source.Portfolio.AsOf <= asOf)
            .ToArray();
        var positionSources = boundedSources
            .SelectMany(source => source.Portfolio.Positions.Select(position =>
            {
                var classification = ResolvePortfolioAnalyticsClassification(position);
                return new PortfolioReportingAnalyticsSource(
                    Scope: PortfolioReportingAnalyticsScopeDto.Security,
                    Key: position.Symbol,
                    Label: position.Security?.DisplayName ?? position.Symbol,
                    Symbol: position.Symbol,
                    Classification: classification,
                    RunId: source.RunId,
                    RealizedPnl: position.RealizedPnl,
                    UnrealizedPnl: position.UnrealizedPnl);
            }))
            .ToArray();

        if (positionSources.Length == 0)
        {
            return
            [
                BuildPortfolioReportingAnalyticsBlockedRow(boundedSources.Length, currency, asOf)
            ];
        }

        var strategySources = boundedSources
            .Select(source => new PortfolioReportingAnalyticsSource(
                Scope: PortfolioReportingAnalyticsScopeDto.Strategy,
                Key: string.IsNullOrWhiteSpace(source.StrategyId) ? source.StrategyName : source.StrategyId,
                Label: source.StrategyName,
                Symbol: null,
                Classification: "Strategy",
                RunId: source.RunId,
                RealizedPnl: source.Portfolio.RealizedPnl,
                UnrealizedPnl: source.Portfolio.UnrealizedPnl))
            .ToArray();
        var assetClassSources = positionSources
            .Select(source => source with
            {
                Scope = PortfolioReportingAnalyticsScopeDto.AssetClass,
                Key = source.Classification ?? "Unclassified",
                Label = source.Classification ?? "Unclassified",
                Symbol = null
            })
            .ToArray();
        var securityGroups = GroupPortfolioReportingAnalyticsSources(positionSources);
        var allGroups = securityGroups
            .Concat(GroupPortfolioReportingAnalyticsSources(strategySources))
            .Concat(GroupPortfolioReportingAnalyticsSources(assetClassSources))
            .ToArray();
        var totalPnl = securityGroups.Sum(static group => group.TotalPnl);
        var absolutePnl = securityGroups.Sum(static group => Math.Abs(group.TotalPnl));
        var rows = new List<PortfolioReportingAnalyticsRowDto>();

        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.TopWinner,
            securityGroups
                .Where(static group => group.TotalPnl > 0m)
                .OrderByDescending(static group => group.TotalPnl)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(5),
            totalPnl,
            absolutePnl,
            currency,
            asOf));
        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.TopLaggard,
            securityGroups
                .Where(static group => group.TotalPnl < 0m)
                .OrderBy(static group => group.TotalPnl)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(5),
            totalPnl,
            absolutePnl,
            currency,
            asOf));
        rows.AddRange(BuildPortfolioReportingAnalyticsRows(
            PortfolioReportingAnalyticsKindDto.Contribution,
            allGroups
                .Where(static group => group.TotalPnl != 0m)
                .OrderByDescending(static group => Math.Abs(group.TotalPnl))
                .ThenBy(static group => group.Scope)
                .ThenBy(static group => group.Label, StringComparer.OrdinalIgnoreCase)
                .Take(12),
            totalPnl,
            absolutePnl,
            currency,
            asOf));

        return rows.Count == 0
            ?
            [
                BuildPortfolioReportingAnalyticsBlockedRow(boundedSources.Length, currency, asOf)
            ]
            : rows;
    }

    private static IReadOnlyList<PortfolioReportingAnalyticsGroup> GroupPortfolioReportingAnalyticsSources(
        IReadOnlyList<PortfolioReportingAnalyticsSource> sources)
        => sources
            .GroupBy(
                static source => (source.Scope, Key: source.Key.Trim()),
                source => source,
                (key, group) =>
                {
                    var ordered = group
                        .OrderBy(static source => source.Label, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static source => source.RunId, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var first = ordered[0];
                    var realized = ordered.Sum(static source => source.RealizedPnl);
                    var unrealized = ordered.Sum(static source => source.UnrealizedPnl);
                    return new PortfolioReportingAnalyticsGroup(
                        Scope: key.Scope,
                        Key: key.Key,
                        Label: first.Label,
                        Symbol: first.Symbol,
                        Classification: first.Classification,
                        RealizedPnl: realized,
                        UnrealizedPnl: unrealized,
                        TotalPnl: realized + unrealized,
                        SourceCount: ordered.Select(static source => source.RunId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
                })
            .ToArray();

    private static IReadOnlyList<PortfolioReportingAnalyticsRowDto> BuildPortfolioReportingAnalyticsRows(
        PortfolioReportingAnalyticsKindDto kind,
        IEnumerable<PortfolioReportingAnalyticsGroup> groups,
        decimal totalPnl,
        decimal absolutePnl,
        string currency,
        DateTimeOffset asOf)
    {
        var rank = 0;
        return groups
            .Select(group =>
            {
                rank++;
                var contributionPercent = totalPnl == 0m
                    ? 0m
                    : decimal.Round(group.TotalPnl / totalPnl * 100m, 4, MidpointRounding.AwayFromZero);
                var heatMapIntensity = absolutePnl == 0m
                    ? 0m
                    : decimal.Round(Math.Abs(group.TotalPnl) / absolutePnl * 100m, 4, MidpointRounding.AwayFromZero);
                var analyticsId = BuildPortfolioReportingAnalyticsId(kind, group.Scope, group.Key);
                return new PortfolioReportingAnalyticsRowDto(
                    AnalyticsId: analyticsId,
                    Kind: kind,
                    Scope: group.Scope,
                    Rank: rank,
                    Label: group.Label,
                    Symbol: group.Symbol,
                    Classification: group.Classification,
                    Currency: currency,
                    RealizedPnl: group.RealizedPnl,
                    UnrealizedPnl: group.UnrealizedPnl,
                    TotalPnl: group.TotalPnl,
                    ContributionPercent: contributionPercent,
                    HeatMapIntensity: heatMapIntensity,
                    SourceCount: group.SourceCount,
                    AsOf: asOf,
                    Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"analyticsId={Uri.EscapeDataString(analyticsId)}"),
                    ReadinessSummary: BuildPortfolioReportingAnalyticsReadiness(kind, group, contributionPercent, heatMapIntensity),
                    Tags: BuildPortfolioReportingAnalyticsTags(kind, group),
                    VersionStamp: BuildPortfolioReportingAnalyticsVersionStamp(asOf, kind, group.Scope, group.SourceCount));
            })
            .ToArray();
    }

    private static PortfolioReportingAnalyticsRowDto BuildPortfolioReportingAnalyticsBlockedRow(
        int sourceCount,
        string currency,
        DateTimeOffset asOf)
    {
        const string analyticsId = "analytics:blocked";
        return new PortfolioReportingAnalyticsRowDto(
            AnalyticsId: analyticsId,
            Kind: PortfolioReportingAnalyticsKindDto.Contribution,
            Scope: PortfolioReportingAnalyticsScopeDto.Security,
            Rank: 0,
            Label: "No source-backed position P&L",
            Symbol: null,
            Classification: null,
            Currency: currency,
            RealizedPnl: 0m,
            UnrealizedPnl: 0m,
            TotalPnl: 0m,
            ContributionPercent: 0m,
            HeatMapIntensity: 0m,
            SourceCount: sourceCount,
            AsOf: asOf,
            Route: UiApiRoutes.WithQuery(UiApiRoutes.WorkstationReporting, $"analyticsId={Uri.EscapeDataString(analyticsId)}"),
            ReadinessSummary: sourceCount == 0
                ? "Blocked: no source-backed portfolio runs are available for Top-N and contribution analytics."
                : $"Blocked: {sourceCount} portfolio run(s) are available, but none include position-level P&L rows for analytics.",
            Tags: ["analytics", "blocked"],
            VersionStamp: BuildPortfolioReportingAnalyticsVersionStamp(
                asOf,
                PortfolioReportingAnalyticsKindDto.Contribution,
                PortfolioReportingAnalyticsScopeDto.Security,
                sourceCount));
    }

    private static string ResolvePortfolioAnalyticsClassification(PortfolioPositionSummary position)
        => string.IsNullOrWhiteSpace(position.Security?.AssetClass)
            ? "Unclassified"
            : position.Security.AssetClass.Trim();

    private static string BuildPortfolioReportingAnalyticsReadiness(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsGroup group,
        decimal contributionPercent,
        decimal heatMapIntensity)
        => kind switch
        {
            PortfolioReportingAnalyticsKindDto.TopWinner => $"Top-N winner from {group.SourceCount} source-backed run(s); contributes {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L.",
            PortfolioReportingAnalyticsKindDto.TopLaggard => $"Top-N laggard from {group.SourceCount} source-backed run(s); contributes {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L.",
            _ => $"{group.SourceCount} source-backed run(s); contribution is {FormatAnalyticsPercent(contributionPercent)} of portfolio P&L with {FormatAnalyticsPercent(heatMapIntensity)} heat-map intensity."
        };

    private static IReadOnlyList<string> BuildPortfolioReportingAnalyticsTags(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsGroup group)
        =>
        [
            "analytics",
            kind.ToString().ToLowerInvariant(),
            group.Scope.ToString().ToLowerInvariant(),
            NormalizeCutId(group.Classification ?? group.Label)
        ];

    private static string BuildPortfolioReportingAnalyticsId(
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsScopeDto scope,
        string key)
        => $"analytics:{kind.ToString().ToLowerInvariant()}:{scope.ToString().ToLowerInvariant()}:{NormalizeCutId(key)}";

    private static string FormatAnalyticsPercent(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static PortfolioReportingLiveViewStateDto ResolveLivePortfolioViewState(
        int sourceCount,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf)
    {
        if (sourceCount <= 0 || sourceAsOf is null)
        {
            return PortfolioReportingLiveViewStateDto.Blocked;
        }

        var age = requestedAsOf - sourceAsOf.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age <= LivePortfolioViewFreshnessWindow)
        {
            return PortfolioReportingLiveViewStateDto.LiveLinked;
        }

        if (age > LivePortfolioViewStaleWindow)
        {
            return PortfolioReportingLiveViewStateDto.Stale;
        }

        return PortfolioReportingLiveViewStateDto.SourceBacked;
    }

    private static string BuildLivePortfolioViewRoute(PortfolioReportingCutDto cut, string? strategyId)
    {
        var query = cut.Kind switch
        {
            PortfolioReportingCutKindDto.Strategy when !string.IsNullOrWhiteSpace(strategyId) => string.Join('&',
                "fundAccountId=all",
                $"strategyId={Uri.EscapeDataString(strategyId.Trim())}",
                "entity=portfolio"),
            PortfolioReportingCutKindDto.UserTag => string.Join('&',
                "fundAccountId=all",
                "strategyId=all",
                $"entity={Uri.EscapeDataString(cut.Label)}"),
            _ => "fundAccountId=all&strategyId=all&entity=portfolio"
        };

        return UiApiRoutes.WithQuery(UiApiRoutes.WorkstationPortfolioSummary, query);
    }

    private static string BuildLivePortfolioLiquiditySummary(PortfolioReportingCutDto cut)
        => cut.PendingSettlement == 0m
            ? $"{cut.TotalCash:N2} {cut.Currency} cash is available with no pending settlement in this reporting cut."
            : $"{cut.TotalCash:N2} {cut.Currency} cash with {cut.PendingSettlement:N2} pending settlement in this reporting cut.";

    private static string BuildLivePortfolioCashLadderSummary(PortfolioReportingCutDto cut, string? cashLadderRoute)
        => string.IsNullOrWhiteSpace(cashLadderRoute)
            ? "Open the live portfolio summary route for consolidated cash and liquidity posture."
            : $"Run cash-ladder evidence is available for {cut.Label}.";

    private static string BuildLivePortfolioTelemetrySummary(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf)
        => state switch
        {
            PortfolioReportingLiveViewStateDto.Blocked => "No source-backed portfolio records are available for this live reporting view.",
            PortfolioReportingLiveViewStateDto.Stale => $"Latest source snapshot is older than 24 hours as of {FormatStructuredDateTime(requestedAsOf)}.",
            PortfolioReportingLiveViewStateDto.LiveLinked => sourceAsOf is null
                ? "Live-linked portfolio telemetry is available through the live portfolio summary route."
                : $"Live-linked portfolio telemetry is current through {FormatStructuredDateTime(sourceAsOf.Value)}; open the route for the latest portfolio-summary telemetry.",
            _ => sourceAsOf is null
                ? "Source-backed portfolio telemetry is available through the live portfolio summary route."
                : $"Source-backed portfolio telemetry is current through {FormatStructuredDateTime(sourceAsOf.Value)}; open the route for latest portfolio-summary telemetry."
        };

    private static IReadOnlyList<string> BuildLivePortfolioReadinessBlockers(
        PortfolioReportingLiveViewStateDto state,
        DateTimeOffset? sourceAsOf,
        DateTimeOffset requestedAsOf,
        PortfolioReportingCutDto cut,
        string? cashLadderRoute)
    {
        var blockers = new List<string>();
        switch (state)
        {
            case PortfolioReportingLiveViewStateDto.Blocked:
                blockers.Add("No source-backed portfolio records are available for this live reporting view.");
                break;
            case PortfolioReportingLiveViewStateDto.Stale:
                blockers.Add($"Latest source snapshot is older than 24 hours as of {FormatStructuredDateTime(requestedAsOf)}.");
                break;
            case PortfolioReportingLiveViewStateDto.SourceBacked when sourceAsOf is not null:
                blockers.Add($"Latest source snapshot is outside the {LivePortfolioViewFreshnessWindow.TotalMinutes:0}-minute live-link window.");
                break;
        }

        if (cut.PendingSettlement != 0m && string.IsNullOrWhiteSpace(cashLadderRoute))
        {
            blockers.Add($"Pending settlement of {cut.PendingSettlement:N2} {cut.Currency} requires cash-ladder evidence before treating this live view as fully delivery-ready.");
        }

        return blockers.ToArray();
    }

    private static IReadOnlyList<StructuredReportingExportColumnDto> GetStructuredExportColumns(string exportId) =>
        NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => RegulatoryTrialBalanceColumns,
            WarehouseLedgerFactsExportId => WarehouseLedgerFactColumns,
            InvestmentPortfolioCutsExportId => InvestmentPortfolioCutColumns,
            InvestmentTopNContributionAnalyticsExportId => InvestmentTopNContributionAnalyticsColumns,
            CrossFundConsolidationExportId => CrossFundConsolidationColumns,
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildStructuredExportRows(
        string exportId,
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
        => NormalizeStructuredExportId(exportId) switch
        {
            RegulatoryTrialBalanceExportId => BuildRegulatoryTrialBalanceRows(workspace, ct),
            WarehouseLedgerFactsExportId => BuildWarehouseLedgerFactRows(workspace, ct),
            InvestmentPortfolioCutsExportId => BuildInvestmentPortfolioCutRows(workspace, ct),
            InvestmentTopNContributionAnalyticsExportId => BuildInvestmentTopNContributionAnalyticsRows(workspace, ct),
            CrossFundConsolidationExportId => BuildCrossFundConsolidationRows(workspace, ct),
            _ => throw new KeyNotFoundException($"Structured reporting export '{exportId}' is not available.")
        };

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildRegulatoryTrialBalanceRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return workspace.Ledger.TrialBalance
            .OrderBy(static row => row.AccountType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("accountName", row.AccountName),
                    ("accountType", row.AccountType),
                    ("symbol", row.Symbol),
                    ("financialAccountId", row.FinancialAccountId),
                    ("currency", workspace.BaseCurrency),
                    ("balance", FormatStructuredDecimal(row.Balance)),
                    ("entryCount", row.EntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("securityId", row.Security?.SecurityId.ToString("D")),
                    ("securityDisplayName", row.Security?.DisplayName),
                    ("sourceAsOfUtc", FormatStructuredDateTime(workspace.AsOf)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildWarehouseLedgerFactRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        var snapshot = workspace.LedgerReconciliationSnapshot.Consolidated;
        return snapshot.Balances
            .OrderBy(static row => row.AccountType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.AccountName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static row => row.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("scope", workspace.Ledger.ScopeKind.ToString()),
                    ("accountName", row.AccountName),
                    ("accountType", row.AccountType),
                    ("symbol", row.Symbol),
                    ("financialAccountId", row.FinancialAccountId),
                    ("balance", FormatStructuredDecimal(row.Balance)),
                    ("journalEntryCount", snapshot.JournalEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("ledgerEntryCount", snapshot.LedgerEntryCount.ToString(CultureInfo.InvariantCulture)),
                    ("sourceAsOfUtc", FormatStructuredDateTime(snapshot.Timestamp)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentPortfolioCutRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.PortfolioCuts ?? [])
            .OrderBy(static cut => cut.Kind)
            .ThenBy(static cut => cut.Label, StringComparer.OrdinalIgnoreCase)
            .Select(cut =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("cutId", cut.CutId),
                    ("label", cut.Label),
                    ("kind", cut.Kind.ToString()),
                    ("currency", cut.Currency),
                    ("grossExposure", FormatStructuredDecimal(cut.GrossExposure)),
                    ("netExposure", FormatStructuredDecimal(cut.NetExposure)),
                    ("totalCash", FormatStructuredDecimal(cut.TotalCash)),
                    ("pendingSettlement", FormatStructuredDecimal(cut.PendingSettlement)),
                    ("totalPnl", FormatStructuredDecimal(cut.TotalPnl)),
                    ("shadowNav", FormatStructuredDecimal(cut.ShadowNav)),
                    ("shadowNavVariance", FormatStructuredDecimal(cut.ShadowNavVariance)),
                    ("sourceCount", cut.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("versionStamp", cut.VersionStamp));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildInvestmentTopNContributionAnalyticsRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.AnalyticsRows ?? [])
            .OrderBy(static row => row.Kind)
            .ThenBy(static row => row.Scope)
            .ThenBy(static row => row.Rank)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("analyticsId", row.AnalyticsId),
                    ("kind", row.Kind.ToString()),
                    ("scope", row.Scope.ToString()),
                    ("rank", row.Rank.ToString(CultureInfo.InvariantCulture)),
                    ("label", row.Label),
                    ("symbol", row.Symbol),
                    ("classification", row.Classification),
                    ("currency", row.Currency),
                    ("realizedPnl", FormatStructuredDecimal(row.RealizedPnl)),
                    ("unrealizedPnl", FormatStructuredDecimal(row.UnrealizedPnl)),
                    ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                    ("contributionPercent", FormatStructuredPercent(row.ContributionPercent)),
                    ("heatMapIntensity", FormatStructuredPercent(row.HeatMapIntensity)),
                    ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("asOfUtc", FormatStructuredDateTime(row.AsOf)),
                    ("readinessSummary", row.ReadinessSummary),
                    ("versionStamp", row.VersionStamp),
                    ("tags", string.Join("|", row.Tags)));
            })
            .ToArray();
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> BuildCrossFundConsolidationRows(
        FundOperationsWorkspaceDto workspace,
        CancellationToken ct)
    {
        return (workspace.Reporting.CrossFundConsolidations ?? [])
            .OrderBy(static row => row.Scope)
            .ThenBy(static row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(row =>
            {
                ct.ThrowIfCancellationRequested();
                return BuildStructuredRow(
                    ("consolidationId", row.ConsolidationId),
                    ("label", row.Label),
                    ("scope", row.Scope.ToString()),
                    ("currency", row.Currency),
                    ("isReady", row.IsReady ? "true" : "false"),
                    ("fundCount", row.FundCount.ToString(CultureInfo.InvariantCulture)),
                    ("entityCount", row.EntityCount.ToString(CultureInfo.InvariantCulture)),
                    ("accountCount", row.AccountCount.ToString(CultureInfo.InvariantCulture)),
                    ("runCount", row.RunCount.ToString(CultureInfo.InvariantCulture)),
                    ("grossExposure", FormatStructuredDecimal(row.GrossExposure)),
                    ("netExposure", FormatStructuredDecimal(row.NetExposure)),
                    ("totalCash", FormatStructuredDecimal(row.TotalCash)),
                    ("pendingSettlement", FormatStructuredDecimal(row.PendingSettlement)),
                    ("totalPnl", FormatStructuredDecimal(row.TotalPnl)),
                    ("shadowNav", FormatStructuredDecimal(row.ShadowNav)),
                    ("shadowNavVariance", FormatStructuredDecimal(row.ShadowNavVariance)),
                    ("sourceCount", row.SourceCount.ToString(CultureInfo.InvariantCulture)),
                    ("readinessSummary", row.ReadinessSummary),
                    ("versionStamp", row.VersionStamp));
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string?> BuildStructuredRow(params (string Key, string? Value)[] values)
    {
        var row = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            row[key] = value;
        }

        return row;
    }

    private static string BuildStructuredExportRoute(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf,
        string currency,
        GovernanceReportArtifactFormatDto format)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.ReportingStructuredExport, "exportId", exportId);
        var queryParts = new List<string>
        {
            $"fundProfileId={Uri.EscapeDataString(fundProfileId)}",
            $"asOf={Uri.EscapeDataString(FormatStructuredDateTime(asOf))}",
            $"currency={Uri.EscapeDataString(currency)}"
        };
        if (format == GovernanceReportArtifactFormatDto.Csv)
        {
            queryParts.Add("format=csv");
        }
        else if (format == GovernanceReportArtifactFormatDto.Xlsx)
        {
            queryParts.Add("format=xlsx");
        }

        var query = string.Join('&', queryParts);
        return UiApiRoutes.WithQuery(route, query);
    }

    private static string BuildStructuredExportRetainedPath(
        string fundProfileId,
        string exportId,
        DateTimeOffset asOf,
        GovernanceReportArtifactFormatDto format)
    {
        var extension = format switch
        {
            GovernanceReportArtifactFormatDto.Csv => "csv",
            GovernanceReportArtifactFormatDto.Xlsx => "xlsx",
            GovernanceReportArtifactFormatDto.Json => "json",
            GovernanceReportArtifactFormatDto.Html => "html",
            GovernanceReportArtifactFormatDto.Pdf => "pdf",
            _ => "dat"
        };
        return $"exports/reporting/{NormalizeCutId(fundProfileId)}/{asOf.UtcDateTime:yyyyMMddHHmmss}/{exportId}.{extension}";
    }

    private static string BuildStructuredExportVersionStamp(DateTimeOffset asOf, int rowCount, int sourceCount) =>
        $"structured-export:{asOf.UtcDateTime:yyyyMMddHHmmss}:rows-{rowCount}:sources-{sourceCount}:schema-{StructuredReportingExportSchemaVersion}";

    private static string NormalizeStructuredExportId(string value) =>
        value.Trim().ToLowerInvariant();

    private static string FormatStructuredDecimal(decimal value) =>
        value.ToString("0.#############################", CultureInfo.InvariantCulture);

    private static string FormatStructuredPercent(decimal value) =>
        value.ToString("0.0###", CultureInfo.InvariantCulture);

    private static string FormatStructuredDateTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static string NormalizeCutId(string value)
    {
        var normalized = new string(value
            .Where(static character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .Select(static character => char.ToLowerInvariant(character))
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? "unassigned" : normalized;
    }

    private static string BuildPortfolioCutVersionStamp(DateTimeOffset asOf, int runCount, int accountCount) =>
        $"portfolio-cut:{asOf.UtcDateTime:yyyyMMddHHmmss}:runs-{runCount}:accounts-{accountCount}";

    private static string BuildPortfolioReportingPnlSliceVersionStamp(
        DateTimeOffset asOf,
        PortfolioReportingPnlSlicePeriodDto period,
        int sourceCount,
        int priorSourceCount)
        => $"pnl-slice:{asOf.UtcDateTime:yyyyMMddHHmmss}:{period.ToString().ToLowerInvariant()}:sources-{sourceCount}:prior-{priorSourceCount}";

    private static string BuildPortfolioReportingAnalyticsVersionStamp(
        DateTimeOffset asOf,
        PortfolioReportingAnalyticsKindDto kind,
        PortfolioReportingAnalyticsScopeDto scope,
        int sourceCount)
        => $"analytics:{asOf.UtcDateTime:yyyyMMddHHmmss}:{kind.ToString().ToLowerInvariant()}:{scope.ToString().ToLowerInvariant()}:sources-{sourceCount}";

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

    private static IReadOnlyList<ReportPackWorkflowRecordDto> FilterReportPackWorkflowRecords(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        ReportAccessQueryContext? accessContext)
    {
        if (accessContext is null)
        {
            return records;
        }

        return records
            .Where(record => ReportAccessPolicyEvaluator.Evaluate(record.AccessPolicy, accessContext).IsAccessible)
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
            GovernanceReportKindDto.PerformanceReport => ReportKind.PerformanceReport,
            GovernanceReportKindDto.HoldingsReport => ReportKind.HoldingsReport,
            GovernanceReportKindDto.CapitalAccountStatement => ReportKind.CapitalAccountStatement,
            GovernanceReportKindDto.InvestorStatement => ReportKind.InvestorStatement,
            GovernanceReportKindDto.BoardPacket => ReportKind.BoardPacket,
            GovernanceReportKindDto.AuditPackage => ReportKind.AuditPackage,
            GovernanceReportKindDto.CertifiedDataset => ReportKind.CertifiedDataset,
            GovernanceReportKindDto.CustomReport => ReportKind.CustomReport,
            _ => ReportKind.TrialBalance
        };

    private sealed record AccountWorkspaceProjection(
        FundAccountSummary Summary,
        AccountBalanceSnapshotDto? LatestSnapshot,
        Guid? FundId);
}
