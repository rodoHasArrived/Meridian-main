using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
    public const string LedgerExplorerId = "ledger";
    public const string PortfolioExplorerId = "portfolio";
    public const string SecurityInstrumentExplorerId = "security-instrument";
    public const string ReportLineProvenanceExplorerId = "report-line-provenance";
    private const string LegacySavedViewTenantId = "legacy-global";

    private static readonly string[] KnownExplorerIds =
    [
        LedgerExplorerId,
        PortfolioExplorerId,
        SecurityInstrumentExplorerId,
        ReportLineProvenanceExplorerId
    ];

    private readonly IFinancialRecordExplorerSavedViewStore _savedViewStore;
    private readonly StrategyRunReadService? _runReadService;
    private readonly ReportPackWorkflowService? _reportPackWorkflowService;
    private readonly ReportPackDeliveryService? _reportPackDeliveryService;
    private readonly ISecurityMasterWorkbenchQueryService? _securityMasterWorkbenchQueryService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;
    private readonly DirectLendingOperationsReadService? _directLendingOperationsReadService;
    private readonly ILedgerJournalStore? _ledgerJournalStore;
    private readonly IAssetAccountingEventSpineService? _assetAccountingEventSpineService;

    public FinancialRecordExplorerReadService(
        IFinancialRecordExplorerSavedViewStore savedViewStore,
        StrategyRunReadService? runReadService = null,
        ReportPackWorkflowService? reportPackWorkflowService = null,
        ReportPackDeliveryService? reportPackDeliveryService = null,
        ISecurityMasterWorkbenchQueryService? securityMasterWorkbenchQueryService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null,
        DirectLendingOperationsReadService? directLendingOperationsReadService = null,
        ILedgerJournalStore? ledgerJournalStore = null,
        IAssetAccountingEventSpineService? assetAccountingEventSpineService = null)
    {
        _savedViewStore = savedViewStore ?? throw new ArgumentNullException(nameof(savedViewStore));
        _runReadService = runReadService;
        _reportPackWorkflowService = reportPackWorkflowService;
        _reportPackDeliveryService = reportPackDeliveryService;
        _securityMasterWorkbenchQueryService = securityMasterWorkbenchQueryService;
        _assetOperationsQueryService = assetOperationsQueryService;
        _directLendingOperationsReadService = directLendingOperationsReadService;
        _ledgerJournalStore = ledgerJournalStore;
        _assetAccountingEventSpineService = assetAccountingEventSpineService;
    }

    public static bool IsKnownExplorerId(string explorerId)
        => KnownExplorerIds.Contains(NormalizeExplorerId(explorerId), StringComparer.OrdinalIgnoreCase);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, LegacySavedViewTenantId, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        CancellationToken ct = default)
        => await GetExplorerAsync(explorerId, tenantId, query: null, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(
        string explorerId,
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var normalized = NormalizeExplorerId(explorerId);
        return normalized switch
        {
            LedgerExplorerId => await BuildLedgerExplorerAsync(tenantId, query, ct).ConfigureAwait(false),
            PortfolioExplorerId => await BuildPortfolioExplorerAsync(tenantId, query, ct).ConfigureAwait(false),
            SecurityInstrumentExplorerId => await BuildSecurityInstrumentExplorerAsync(tenantId, query, ct).ConfigureAwait(false),
            ReportLineProvenanceExplorerId => await BuildReportLineProvenanceExplorerAsync(tenantId, query, ct).ConfigureAwait(false),
            _ => null
        };
    }

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        CancellationToken ct = default)
        => await GetRecordAsync(explorerId, recordId, LegacySavedViewTenantId, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        string tenantId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        var explorer = await GetExplorerAsync(explorerId, tenantId, ct).ConfigureAwait(false);
        return explorer?.Rows
            .FirstOrDefault(row => string.Equals(row.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            ?.Detail;
    }

    public async Task<FinancialRecordExplorerSavedViewDto?> SaveViewAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewSaveRequestDto request,
        CancellationToken ct = default)
        => await SaveViewAsync(explorerId, LegacySavedViewTenantId, request, ct).ConfigureAwait(false);

    public async Task<FinancialRecordExplorerSavedViewDto?> SaveViewAsync(
        string explorerId,
        string tenantId,
        FinancialRecordExplorerSavedViewSaveRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(request);
        var normalized = NormalizeExplorerId(explorerId);
        if (!IsKnownExplorerId(normalized))
        {
            return null;
        }

        var label = request.Label?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException("Saved view label is required.");
        }

        var filters = NormalizeFilters(request.Filters);
        var searchText = request.SearchText?.Trim() ?? string.Empty;
        if (filters.Count == 0 && string.IsNullOrWhiteSpace(searchText))
        {
            throw new InvalidOperationException("Saved view requires a search term or at least one filter.");
        }

        var view = new FinancialRecordExplorerSavedViewDto(
            ViewId: $"operator-{Slugify(label)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Label: label,
            Description: request.Description?.Trim() ?? string.Empty,
            IsSystem: false,
            IsActive: false,
            Filters: filters,
            SearchText: searchText,
            ColumnIds: NormalizeColumnIds(request.ColumnIds));

        return await _savedViewStore.SaveAsync(tenantId, normalized, view, ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildLedgerExplorerAsync(
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                LedgerExplorerId,
                "Ledger Explorer",
                "Explore retained trial-balance records and their proof links.",
                "Strategy run read service is not registered.");
        }

        var source = await TryLoadLatestRunDetailAsync(
            readService,
            detail => detail.Ledger?.TrialBalance.Count > 0,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                LedgerExplorerId,
                "Ledger Explorer",
                "Explore retained trial-balance records and their proof links.",
                "No source-backed ledger projection is available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var ledger = source.Ledger!;
        var rows = ledger.TrialBalance
            .Select((line, index) => BuildLedgerRow(run, ledger, line, index))
            .ToArray();

        return await CreateExplorerAsync(
            LedgerExplorerId,
            "Ledger Explorer",
            "Explore retained trial-balance records and their proof links.",
            $"Source-backed ledger projection from run {run.RunId}.",
            BuildScope(run, ledger.AsOf, ledger.LedgerReference),
            BuildLedgerSummary(run, ledger, rows.Length),
            BuildSystemViews(LedgerExplorerId, "Trial balance", "Accounts with retained ledger balances."),
            BuildLedgerFilters(run, ledger),
            [
                new("accountName", "Account", Width: 220),
                new("accountType", "Type", Width: 110),
                new("symbol", "Symbol", Width: 90),
                new("balance", "Balance", "money", 120, IsRightAligned: true),
                new("entryCount", "Entries", "number", 90, IsRightAligned: true),
                new("fundId", "Fund", Width: 130),
                new("entityId", "Entity", Width: 130),
                new("bookId", "Book", Width: 130),
                new("accountId", "Account Scope", Width: 150),
                new("source", "Source", Width: 140)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildPortfolioExplorerAsync(
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                PortfolioExplorerId,
                "Portfolio Explorer",
                "Explore retained account and aggregate position records.",
                "Strategy run read service is not registered.");
        }

        var source = await TryLoadLatestRunDetailAsync(
            readService,
            detail => detail.Portfolio?.Positions.Count > 0,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                PortfolioExplorerId,
                "Portfolio Explorer",
                "Explore retained account and aggregate position records.",
                "No source-backed portfolio projection is available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var portfolio = source.Portfolio!;
        var rows = portfolio.Positions
            .Select((position, index) => BuildPortfolioRow(run, portfolio, position, index))
            .ToArray();

        return await CreateExplorerAsync(
            PortfolioExplorerId,
            "Portfolio Explorer",
            "Explore retained account and aggregate position records.",
            $"Source-backed portfolio projection from run {run.RunId}.",
            BuildScope(run, portfolio.AsOf, portfolio.PortfolioId),
            BuildPortfolioSummary(run, portfolio, rows.Length),
            BuildSystemViews(PortfolioExplorerId, "Positions", "Open retained positions for the selected run."),
            BuildPortfolioFilters(run, portfolio),
            [
                new("symbol", "Symbol", Width: 100),
                new("quantity", "Quantity", "number", 100, IsRightAligned: true),
                new("averageCost", "Average Cost", "money", 120, IsRightAligned: true),
                new("realizedPnl", "Realized PnL", "money", 120, IsRightAligned: true),
                new("unrealizedPnl", "Unrealized PnL", "money", 120, IsRightAligned: true),
                new("security", "Security", Width: 180),
                new("coverage", "Coverage", Width: 110)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.WorkstationPortfolio, "runId", run.RunId)),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildSecurityInstrumentExplorerAsync(
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var readService = _runReadService;
        if (readService is null)
        {
            return CreateBlockedExplorer(
                SecurityInstrumentExplorerId,
                "Security & Instrument Explorer",
                "Explore Security Master references used by retained accounting and portfolio records.",
                "Strategy run read service is not registered.");
        }

        var source = await TryLoadLatestRunDetailAsync(
            readService,
            detail => CollectSecurityReferences(detail).Count > 0,
            ct).ConfigureAwait(false);
        if (source is null)
        {
            return await CreateEmptyExplorerAsync(
                SecurityInstrumentExplorerId,
                "Security & Instrument Explorer",
                "Explore Security Master references used by retained accounting and portfolio records.",
                "No source-backed Security Master references are available.",
                tenantId,
                ct).ConfigureAwait(false);
        }

        var run = source.Summary;
        var references = CollectSecurityReferences(source);
        var reportRecords = _reportPackWorkflowService?.ListRecords(200) ?? [];
        var directLendingOperations = _directLendingOperationsReadService is null
            ? null
            : await _directLendingOperationsReadService.GetOperationsAsync(ct: ct).ConfigureAwait(false);
        var enrichedRows = new List<(FinancialRecordExplorerRowDto Row, SecurityInstrumentEnrichment Enrichment)>(references.Count);
        for (var index = 0; index < references.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var reference = references[index];
            var enrichment = await BuildSecurityInstrumentEnrichmentAsync(reference, reportRecords, directLendingOperations, ct).ConfigureAwait(false);
            enrichedRows.Add((BuildSecurityRow(run, source, reference, index, enrichment), enrichment));
        }

        var rows = enrichedRows.Select(static entry => entry.Row).ToArray();
        var enrichments = enrichedRows.Select(static entry => entry.Enrichment).ToArray();
        var passportCount = enrichments.Count(static enrichment => enrichment.Passport is not null);
        var operationsCount = enrichments.Count(static enrichment => enrichment.Readiness is not null);
        var directLendingCount = enrichments.Count(static enrichment => enrichment.DirectLendingHealth.Count > 0);
        var reportedLineUsageCount = enrichments.Sum(static enrichment => enrichment.ReportLineUsages.Count);
        var termCount = enrichments.Sum(static enrichment => enrichment.Operations?.TermsHistory.Count ?? 0);
        var cashFlowCount = enrichments.Sum(static enrichment => enrichment.Operations?.ProjectedCashFlows.Count ?? 0);
        var reconciliationCount = enrichments.Sum(static enrichment => enrichment.Operations?.ReconciliationResults.Count ?? 0);
        var accountingProjectionCount = enrichments.Sum(static enrichment => enrichment.Operations?.LedgerProjections.Count ?? 0);
        var postedJournalCount = enrichments.Sum(static enrichment => enrichment.InstrumentJournalProofs.Count(static proof => proof.Journal is not null));
        var evidenceCount = enrichments.Sum(static enrichment => CountSecurityEvidenceAnchors(enrichment));
        var auditCount = enrichments.Sum(static enrichment => CountSecurityAuditEvents(enrichment));
        var summaryItems = new List<FinancialRecordExplorerSummaryItemDto>
        {
            new("Securities", rows.Length.ToString(CultureInfo.InvariantCulture), "Distinct resolved or referenced instruments."),
            new("Resolved", references.Count(reference => reference.CoverageStatus == WorkstationSecurityCoverageStatus.Resolved).ToString(CultureInfo.InvariantCulture), "Security Master references with resolved coverage.", FinancialRecordExplorerTone.Success),
            new("Missing", references.Count(reference => reference.CoverageStatus is WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable).ToString(CultureInfo.InvariantCulture), "References requiring operator follow-up.", FinancialRecordExplorerTone.Warning)
        };
        if (passportCount > 0)
        {
            summaryItems.Add(new("Passports", passportCount.ToString(CultureInfo.InvariantCulture), "Security Master passport/trust payloads available.", FinancialRecordExplorerTone.Info));
        }

        if (operationsCount > 0)
        {
            summaryItems.Add(new("Operations", operationsCount.ToString(CultureInfo.InvariantCulture), "AssetOperations readiness payloads available.", FinancialRecordExplorerTone.Info));
        }

        if (directLendingCount > 0)
        {
            summaryItems.Add(new("Direct Lending", directLendingCount.ToString(CultureInfo.InvariantCulture), "Direct-lending operations posture is linked to Security Master instruments.", FinancialRecordExplorerTone.Info));
        }

        if (termCount > 0)
        {
            summaryItems.Add(new("Terms", termCount.ToString(CultureInfo.InvariantCulture), "Instrument terms and obligation roots available.", FinancialRecordExplorerTone.Info));
        }

        if (cashFlowCount > 0)
        {
            summaryItems.Add(new("Cash Flows", cashFlowCount.ToString(CultureInfo.InvariantCulture), "Projected cash-flow obligations available.", FinancialRecordExplorerTone.Info));
        }

        if (reconciliationCount > 0)
        {
            summaryItems.Add(new("Reconciliations", reconciliationCount.ToString(CultureInfo.InvariantCulture), "AssetOperations reconciliation results available.", FinancialRecordExplorerTone.Info));
        }

        if (accountingProjectionCount > 0)
        {
            summaryItems.Add(new("Accounting Projections", accountingProjectionCount.ToString(CultureInfo.InvariantCulture), "Projected accounting-effect rows are available; they are not posted journals.", FinancialRecordExplorerTone.Info));
        }

        if (postedJournalCount > 0)
        {
            summaryItems.Add(new("Posted Journals", postedJournalCount.ToString(CultureInfo.InvariantCulture), "Durable posted journals resolved by exact ledger-book and source-event scope.", FinancialRecordExplorerTone.Success));
        }

        if (reportedLineUsageCount > 0)
        {
            summaryItems.Add(new("Reported Usage", reportedLineUsageCount.ToString(CultureInfo.InvariantCulture), "Published or restated report lines with retained publication evidence reference these instruments.", FinancialRecordExplorerTone.Success));
        }

        if (evidenceCount > 0)
        {
            summaryItems.Add(new("Evidence", evidenceCount.ToString(CultureInfo.InvariantCulture), "Evidence anchors retained for security-instrument proof chains.", FinancialRecordExplorerTone.Info));
        }

        if (auditCount > 0)
        {
            summaryItems.Add(new("Audit Events", auditCount.ToString(CultureInfo.InvariantCulture), "Audit events retained for security-instrument proof chains.", FinancialRecordExplorerTone.Info));
        }

        return await CreateExplorerAsync(
            SecurityInstrumentExplorerId,
            "Security & Instrument Explorer",
            "Explore Security Master references used by retained accounting and portfolio records.",
            $"Source-backed Security Master references from run {run.RunId}.",
            BuildScope(run, source.Portfolio?.AsOf ?? source.Ledger?.AsOf ?? run.LastUpdatedAt, "Security Master"),
            summaryItems,
            BuildSystemViews(SecurityInstrumentExplorerId, "Security references", "Instrument references used by portfolio and ledger records."),
            BuildSecurityFilters(references),
            [
                new("security", "Security", Width: 220),
                new("assetClass", "Asset Class", Width: 120),
                new("currency", "Currency", Width: 90),
                new("status", "Status", Width: 110),
                new("coverage", "Coverage", Width: 120),
                new("trust", "Trust", Width: 120),
                new("identifierConfidence", "Identifier Confidence", Width: 170),
                new("operations", "Operations", Width: 140),
                new("directLending", "Direct Lending", Width: 150),
                new("cashFlow", "Cash Flow", Width: 120),
                new("ledger", "Accounting Projection", Width: 160),
                new("terms", "Terms / Obligations", Width: 170),
                new("reportUsage", "Reported Usage", Width: 170),
                new("evidence", "Evidence", Width: 140),
                new("auditTrail", "Audit Trail", Width: 140),
                new("identifier", "Identifier", Width: 160),
                new("source", "Source", Width: 120)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WorkstationSecurityMasterSearch),
            tenantId,
            query,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildReportLineProvenanceExplorerAsync(
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var workflowService = _reportPackWorkflowService;
        if (workflowService is null)
        {
            return CreateBlockedExplorer(
                ReportLineProvenanceExplorerId,
                "Report-Line Provenance Explorer",
                "Drill from reported lines into retained source records, reconciliations, ledger provenance, approvals, delivery history, and restatement evidence.",
                "Report pack workflow service is not registered.");
        }

        var systemViews = BuildSystemViews(
            ReportLineProvenanceExplorerId,
            "Report lines",
            "Governed report lines with retained source provenance.");
        var savedViews = await LoadSavedViewsAsync(tenantId, ReportLineProvenanceExplorerId, systemViews, ct).ConfigureAwait(false);
        var explorer = BuildReportLineProvenanceExplorer(
            workflowService.ListRecords(200),
            _reportPackDeliveryService?.ListAttempts(500),
            savedViews: savedViews);
        return ApplyExplorerQuery(explorer, query);
    }

    public static FinancialRecordExplorerDto BuildReportLineProvenanceExplorer(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto>? savedViews = null,
        ReportAccessQueryContext? accessContext = null)
    {
        ArgumentNullException.ThrowIfNull(workflowRecords);

        var authorizedRecords = accessContext is null
            ? workflowRecords
            : ReportPackRunReadService.FilterWorkflowRecords(workflowRecords, accessContext);
        var records = authorizedRecords
            .Where(static record => IsAuthoritativelyReported(record) && record.LineProvenance is { Count: > 0 })
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenBy(static record => record.ReportId)
            .ToArray();
        // Unbound (null) context = the legacy tenant-level endpoint: sanitized attempts, matching the unfiltered records above.
        var attempts = accessContext is null
            ? (deliveryAttempts ?? []).Select(ReportingDeliveryReadModelSecurity.SanitizeAttempt).ToArray()
            : ReportingDeliveryReadModelSecurity.FilterVisibleAttempts(deliveryAttempts ?? [], accessContext, records);
        var rows = records
            .SelectMany(record => record.LineProvenance!
                .Select((line, index) => BuildReportLineProvenanceRow(record, line, attempts, index)))
            .ToArray();
        var activeSavedViews = savedViews is { Count: > 0 }
            ? savedViews
            : BuildSystemViews(
                ReportLineProvenanceExplorerId,
                "Report lines",
                "Governed report lines with retained source provenance.");
        var sourceState = rows.Length == 0
            ? "No published or restated report-line provenance with retained publication evidence is available."
            : $"{rows.Length} reported line(s) across {records.Length} evidenced report pack(s) retain instrument, position or transaction, reconciliation, ledger provenance, approval, report, evidence, and audit drill-through links.";

        return new FinancialRecordExplorerDto(
            ReportLineProvenanceExplorerId,
            "Report-Line Provenance Explorer",
            "Drill from published or restated report lines into retained instruments, positions or transactions, reconciliations, ledger provenance, reports, evidence, and audit links.",
            sourceState,
            IsBlocked: false,
            BlockedReason: string.Empty,
            ScopeItems: BuildReportLineProvenanceScope(records, attempts),
            SavedViews: activeSavedViews,
            SummaryItems: BuildReportLineProvenanceSummary(records, attempts, rows.Length),
            Filters: BuildReportLineProvenanceFilters(records),
            Columns:
            [
                new("lineKey", "Report Line", Width: 200),
                new("report", "Report Pack", Width: 190),
                new("value", "Value", "money", 110, IsRightAligned: true),
                new("source", "Source", Width: 170),
                new("instrument", "Instrument", Width: 160),
                new("activity", "Position / Transaction", Width: 190),
                new("reconciliation", "Reconciliation", Width: 140),
                new("journal", "Ledger Reference", Width: 140),
                new("approval", "Approval", Width: 120),
                new("delivery", "Delivery", Width: 140),
                new("restatement", "Restatement", Width: 140)
            ],
            Rows: rows,
            SelectedRecord: rows.FirstOrDefault()?.Detail,
            ProofActions: BuildReportLineProvenanceProofActions(rows.Length),
            RecordGraph: BuildReportLineProvenanceGraph(rows));
    }

    private async Task<StrategyRunDetail?> TryLoadLatestRunDetailAsync(
        StrategyRunReadService readService,
        Func<StrategyRunDetail, bool> predicate,
        CancellationToken ct)
    {
        var runs = await readService.GetRunsAsync(new StrategyRunHistoryQuery(Limit: 100), ct).ConfigureAwait(false);
        foreach (var run in runs)
        {
            var detail = await readService.GetRunDetailAsync(run.RunId, ct).ConfigureAwait(false);
            if (detail is not null && predicate(detail))
            {
                return detail;
            }
        }

        return null;
    }

    private async Task<FinancialRecordExplorerDto> CreateExplorerAsync(
        string explorerId,
        string title,
        string description,
        string sourceState,
        IReadOnlyList<FinancialRecordExplorerScopeItemDto> scopeItems,
        IReadOnlyList<FinancialRecordExplorerSummaryItemDto> summaryItems,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto> systemViews,
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters,
        IReadOnlyList<FinancialRecordExplorerColumnDto> columns,
        IReadOnlyList<FinancialRecordExplorerRowDto> rows,
        IReadOnlyList<FinancialRecordExplorerProofActionDto> proofActions,
        string tenantId,
        FinancialRecordExplorerQueryDto? query,
        CancellationToken ct)
    {
        var savedViews = await LoadSavedViewsAsync(tenantId, explorerId, systemViews, ct).ConfigureAwait(false);
        var scoped = ApplyExplorerQuery(savedViews, summaryItems, rows, query);
        return new FinancialRecordExplorerDto(
            explorerId,
            title,
            description,
            sourceState,
            IsBlocked: false,
            BlockedReason: string.Empty,
            scopeItems,
            scoped.SavedViews,
            scoped.SummaryItems,
            filters,
            columns,
            scoped.Rows,
            scoped.Rows.FirstOrDefault()?.Detail,
            proofActions,
            string.Equals(explorerId, SecurityInstrumentExplorerId, StringComparison.OrdinalIgnoreCase)
                ? BuildSecurityInstrumentGraph(scoped.Rows)
                : BuildGraph(scoped.Rows));
    }

    private static FinancialRecordExplorerDto ApplyExplorerQuery(
        FinancialRecordExplorerDto explorer,
        FinancialRecordExplorerQueryDto? query)
    {
        var scoped = ApplyExplorerQuery(explorer.SavedViews, explorer.SummaryItems, explorer.Rows, query);
        return explorer with
        {
            SavedViews = scoped.SavedViews,
            SummaryItems = scoped.SummaryItems,
            Rows = scoped.Rows,
            SelectedRecord = scoped.Rows.FirstOrDefault()?.Detail,
            RecordGraph = string.Equals(explorer.ExplorerId, ReportLineProvenanceExplorerId, StringComparison.OrdinalIgnoreCase)
                ? BuildReportLineProvenanceGraph(scoped.Rows)
                : string.Equals(explorer.ExplorerId, SecurityInstrumentExplorerId, StringComparison.OrdinalIgnoreCase)
                    ? BuildSecurityInstrumentGraph(scoped.Rows)
                : BuildGraph(scoped.Rows)
        };
    }

    private static (
        IReadOnlyList<FinancialRecordExplorerSavedViewDto> SavedViews,
        IReadOnlyList<FinancialRecordExplorerSummaryItemDto> SummaryItems,
        IReadOnlyList<FinancialRecordExplorerRowDto> Rows) ApplyExplorerQuery(
            IReadOnlyList<FinancialRecordExplorerSavedViewDto> savedViews,
            IReadOnlyList<FinancialRecordExplorerSummaryItemDto> summaryItems,
            IReadOnlyList<FinancialRecordExplorerRowDto> rows,
            FinancialRecordExplorerQueryDto? query)
    {
        if (query is null)
        {
            return (savedViews, summaryItems, rows);
        }

        var requestedViewId = query.ViewId?.Trim() ?? string.Empty;
        var selectedView = string.IsNullOrWhiteSpace(requestedViewId)
            ? null
            : savedViews.FirstOrDefault(view => string.Equals(view.ViewId, requestedViewId, StringComparison.OrdinalIgnoreCase));
        var filters = NormalizeFilters(query.Filters)
            .Concat(selectedView?.Filters ?? [])
            .ToArray();
        var searchText = string.Join(
            " ",
            new[] { selectedView?.SearchText, query.SearchText }
                .Where(static text => !string.IsNullOrWhiteSpace(text))
                .Select(static text => text!.Trim()));

        if (selectedView is null && string.IsNullOrWhiteSpace(searchText) && filters.Length == 0)
        {
            return (savedViews, summaryItems, rows);
        }

        var activeSavedViews = string.IsNullOrWhiteSpace(requestedViewId)
            ? savedViews
            : savedViews
                .Select(view => view with
                {
                    IsActive = string.Equals(view.ViewId, requestedViewId, StringComparison.OrdinalIgnoreCase)
                })
                .ToArray();
        var scopedRows = selectedView is null && !string.IsNullOrWhiteSpace(requestedViewId)
            ? []
            : rows
                .Where(row => RowMatchesFilters(row, filters) && RowMatchesSearch(row, searchText))
                .ToArray();
        var scopedSummary = BuildScopedSummary(summaryItems, rows.Count, scopedRows.Length, requestedViewId, searchText, filters);
        return (activeSavedViews, scopedSummary, scopedRows);
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildScopedSummary(
        IReadOnlyList<FinancialRecordExplorerSummaryItemDto> summaryItems,
        int totalRows,
        int visibleRows,
        string requestedViewId,
        string searchText,
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters)
    {
        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(requestedViewId))
        {
            detailParts.Add($"view {requestedViewId}");
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            detailParts.Add($"search '{searchText}'");
        }

        if (filters.Count > 0)
        {
            detailParts.Add($"{filters.Count} filter(s)");
        }

        return
        [
            new(
                "Visible records",
                visibleRows.ToString(CultureInfo.InvariantCulture),
                detailParts.Count == 0
                    ? $"Showing {visibleRows:N0} of {totalRows:N0} records."
                    : $"Showing {visibleRows:N0} of {totalRows:N0} records after {string.Join(", ", detailParts)}.",
                visibleRows == 0 && totalRows > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Info),
            .. summaryItems
        ];
    }

    private static bool RowMatchesFilters(
        FinancialRecordExplorerRowDto row,
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters)
        => filters.Count == 0 || filters.All(filter =>
        {
            var expected = NormalizeSearchToken(filter.Value);
            if (string.IsNullOrWhiteSpace(expected))
            {
                return true;
            }

            var filterId = NormalizeSearchToken(filter.FilterId);
            var directCell = string.IsNullOrWhiteSpace(filterId)
                ? null
                : row.Cells.FirstOrDefault(cell => string.Equals(NormalizeSearchToken(cell.ColumnId), filterId, StringComparison.Ordinal));
            return directCell is not null
                ? CellMatchesToken(directCell, expected)
                : RowMatchesSearch(row, expected);
        });

    private static bool RowMatchesSearch(FinancialRecordExplorerRowDto row, string? searchText)
    {
        var token = NormalizeSearchToken(searchText);
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        return RowTextValues(row).Any(value => NormalizeSearchToken(value).Contains(token, StringComparison.Ordinal));
    }

    private static IEnumerable<string?> RowTextValues(FinancialRecordExplorerRowDto row)
    {
        yield return row.RecordId;
        yield return row.RecordType;
        yield return row.Label;
        yield return row.Source;
        yield return row.Status;
        foreach (var cell in row.Cells)
        {
            yield return cell.DisplayValue;
            yield return cell.RawValue;
        }

        foreach (var field in row.Detail.Fields)
        {
            yield return field.Label;
            yield return field.Value;
            yield return field.Detail;
        }
    }

    private static bool CellMatchesToken(FinancialRecordExplorerCellDto cell, string token)
        => NormalizeSearchToken(cell.DisplayValue).Contains(token, StringComparison.Ordinal) ||
           NormalizeSearchToken(cell.RawValue).Contains(token, StringComparison.Ordinal);

    private static string NormalizeSearchToken(string? value)
        => new((value ?? string.Empty)
            .Trim()
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private async Task<FinancialRecordExplorerDto> CreateEmptyExplorerAsync(
        string explorerId,
        string title,
        string description,
        string sourceState,
        string tenantId,
        CancellationToken ct)
    {
        var systemViews = BuildSystemViews(explorerId, "Default", "No source-backed records are available.");
        return new FinancialRecordExplorerDto(
            explorerId,
            title,
            description,
            sourceState,
            IsBlocked: false,
            BlockedReason: string.Empty,
            ScopeItems: [new("Source", "No retained projection", FinancialRecordExplorerTone.Warning)],
            await LoadSavedViewsAsync(tenantId, explorerId, systemViews, ct).ConfigureAwait(false),
            SummaryItems: [new("Records", "0", "No retained source projection was found.", FinancialRecordExplorerTone.Warning)],
            Filters: [],
            Columns: [],
            Rows: [],
            SelectedRecord: null,
            ProofActions:
            [
                new(
                    "open-source",
                    "Open source projection",
                    "Disabled until a retained run projection is available.",
                    string.Empty,
                    IsEnabled: false,
                    DisabledReason: "No source-backed projection is available.",
                    Tone: FinancialRecordExplorerTone.Warning)
            ],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));
    }

    private static FinancialRecordExplorerDto CreateBlockedExplorer(
        string explorerId,
        string title,
        string description,
        string reason)
        => new(
            explorerId,
            title,
            description,
            "Explorer source is blocked.",
            IsBlocked: true,
            BlockedReason: reason,
            ScopeItems: [new("Source", "Blocked", FinancialRecordExplorerTone.Danger)],
            SavedViews: BuildSystemViews(explorerId, "Default", "System default view."),
            SummaryItems: [new("State", "Blocked", reason, FinancialRecordExplorerTone.Danger)],
            Filters: [],
            Columns: [],
            Rows: [],
            SelectedRecord: null,
            ProofActions:
            [
                new(
                    "source-blocked",
                    "Source unavailable",
                    reason,
                    string.Empty,
                    IsEnabled: false,
                    DisabledReason: reason,
                    Tone: FinancialRecordExplorerTone.Danger)
            ],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));

    private async Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> LoadSavedViewsAsync(
        string tenantId,
        string explorerId,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto> systemViews,
        CancellationToken ct)
    {
        var operatorViews = await _savedViewStore.LoadAsync(tenantId, explorerId, ct).ConfigureAwait(false);
        return systemViews.Concat(operatorViews).ToArray();
    }

    private static IReadOnlyList<FinancialRecordExplorerSavedViewDto> BuildSystemViews(
        string explorerId,
        string label,
        string description)
        =>
        [
            new(
                $"system-{explorerId}-default",
                label,
                description,
                IsSystem: true,
                IsActive: true,
                Filters: [],
                SearchText: string.Empty,
                ColumnIds: [])
        ];

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> NormalizeFilters(
        IReadOnlyList<FinancialRecordExplorerFilterDto?>? filters)
        => filters?
            .Where(static filter =>
                filter is not null &&
                !string.IsNullOrWhiteSpace(filter.FilterId) &&
                !string.IsNullOrWhiteSpace(filter.Label))
            .Select(static filter => filter! with
            {
                FilterId = filter.FilterId.Trim(),
                Label = filter.Label.Trim(),
                Value = filter.Value?.Trim() ?? string.Empty,
                Operator = string.IsNullOrWhiteSpace(filter.Operator)
                    ? "equals"
                    : filter.Operator.Trim()
            })
            .Take(24)
            .ToArray()
            ?? [];

    private static IReadOnlyList<string> NormalizeColumnIds(IReadOnlyList<string?>? columnIds)
        => columnIds?
            .Where(static columnId => !string.IsNullOrWhiteSpace(columnId))
            .Select(static columnId => columnId!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray()
            ?? [];

    private static IReadOnlyList<FinancialRecordExplorerScopeItemDto> BuildScope(
        StrategyRunSummary run,
        DateTimeOffset asOf,
        string source)
        =>
        [
            new("Run", run.RunId),
            new("Strategy", run.StrategyName),
            new("Mode", run.Mode.ToString()),
            new("As of", asOf.ToString("u", CultureInfo.InvariantCulture)),
            new("Source", source)
        ];

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildLedgerSummary(
        StrategyRunSummary run,
        LedgerSummary ledger,
        int rowCount)
        =>
        [
            new("Accounts", rowCount.ToString(CultureInfo.InvariantCulture), "Retained trial-balance rows."),
            new("Assets", FormatCurrency(ledger.AssetBalance), "Source-backed asset balance."),
            new("Liabilities", FormatCurrency(ledger.LiabilityBalance), "Source-backed liability balance."),
            new("Equity", FormatCurrency(ledger.EquityBalance), "Source-backed equity balance."),
            new("Run", run.Status.ToString(), $"Last updated {run.LastUpdatedAt:u}.", ToneFromRunStatus(run.Status))
        ];

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildPortfolioSummary(
        StrategyRunSummary run,
        PortfolioSummary portfolio,
        int rowCount)
        =>
        [
            new("Positions", rowCount.ToString(CultureInfo.InvariantCulture), "Retained position rows."),
            new("Equity", FormatCurrency(portfolio.TotalEquity), "Source-backed total equity."),
            new("Cash", FormatCurrency(portfolio.Cash), "Source-backed cash."),
            new("Net Exposure", FormatCurrency(portfolio.NetExposure), "Source-backed net exposure."),
            new("Run", run.Status.ToString(), $"Last updated {run.LastUpdatedAt:u}.", ToneFromRunStatus(run.Status))
        ];

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildLedgerFilters(
        StrategyRunSummary run,
        LedgerSummary ledger)
    {
        var filters = new List<FinancialRecordExplorerFilterDto>
        {
            new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info),
            new("ledger", "Ledger", ledger.LedgerReference, Tone: FinancialRecordExplorerTone.Info)
        };

        var dimensions = ledger.TrialBalance
            .Select(static line => line.Dimensions)
            .Append(ledger.Dimensions);

        AddDimensionFilters(filters, dimensions);
        return filters;
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildPortfolioFilters(
        StrategyRunSummary run,
        PortfolioSummary portfolio)
        =>
        [
            new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info),
            new("portfolio", "Portfolio", portfolio.PortfolioId, Tone: FinancialRecordExplorerTone.Info)
        ];

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildSecurityFilters(
        IReadOnlyList<WorkstationSecurityReference> references)
        => references
            .Select(static reference => reference.AssetClass)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .Select(static value => new FinancialRecordExplorerFilterDto($"asset-{Slugify(value)}", "Asset class", value))
            .ToArray();

    private static FinancialRecordExplorerRowDto BuildLedgerRow(
        StrategyRunSummary run,
        LedgerSummary ledger,
        LedgerTrialBalanceLine line,
        int index)
    {
        var recordId = $"ledger:{run.RunId}:{index}";
        var dimensionFields = BuildDimensionFields(line.Dimensions);
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Ledger account",
            line.AccountName,
            $"{line.AccountType} - {ledger.LedgerReference}",
            "Source-backed trial-balance line retained for run accounting review.",
            FinancialRecordExplorerTone.Default,
            Fields:
            [
                new("Account Type", line.AccountType),
                new("Balance", FormatCurrency(line.Balance)),
                new("Entries", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("Symbol", line.Symbol ?? "None"),
                new("Financial Account", line.FinancialAccountId ?? "None"),
                .. dimensionFields
            ],
            ProofActions: BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
            UsedIn:
            [
                new("ledger-run", "Run ledger", $"Trial balance belongs to run {run.RunId}.", UiApiRoutes.WithParam(UiApiRoutes.RunsLedger, "runId", run.RunId)),
                new("accounting", "Accounting workspace", "Available to Accounting close, reconciliation, and audit review.", UiApiRoutes.WorkstationAccounting)
            ],
            Impacts:
            [
                new("balance-sheet", "Balance sheet", $"{line.AccountType} balance contributes {FormatCurrency(line.Balance)}.", Tone: ToneFromBalance(line.Balance))
            ],
            FullRecordHref: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId));

        return new FinancialRecordExplorerRowDto(
            recordId,
            "ledger",
            line.AccountName,
            ledger.LedgerReference,
            line.AccountType,
            ToneFromBalance(line.Balance),
            Cells:
            [
                new("accountName", line.AccountName),
                new("accountType", line.AccountType),
                new("symbol", line.Symbol ?? "-"),
                new("balance", FormatCurrency(line.Balance), line.Balance.ToString(CultureInfo.InvariantCulture), ToneFromBalance(line.Balance)),
                new("entryCount", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("fundId", DisplayDimension(line.Dimensions?.FundId), line.Dimensions?.FundId ?? string.Empty),
                new("entityId", DisplayDimension(line.Dimensions?.EntityId), line.Dimensions?.EntityId ?? string.Empty),
                new("bookId", DisplayDimension(line.Dimensions?.BookId), line.Dimensions?.BookId ?? string.Empty),
                new("accountId", DisplayDimension(line.Dimensions?.AccountId), line.Dimensions?.AccountId ?? string.Empty),
                new("source", ledger.LedgerReference)
            ],
            detail);
    }

    private static void AddDimensionFilters(
        ICollection<FinancialRecordExplorerFilterDto> filters,
        IEnumerable<LedgerDimensionSetDto?> dimensionSets)
    {
        var dimensions = dimensionSets
            .Where(static dimensions => dimensions is not null)
            .Cast<LedgerDimensionSetDto>()
            .ToArray();

        AddDimensionFilter(filters, "fund", "Fund", dimensions.Select(static value => value.FundId));
        AddDimensionFilter(filters, "entity", "Entity", dimensions.Select(static value => value.EntityId));
        AddDimensionFilter(filters, "sleeve", "Sleeve", dimensions.Select(static value => value.SleeveId));
        AddDimensionFilter(filters, "strategy", "Strategy", dimensions.Select(static value => value.StrategyId));
        AddDimensionFilter(filters, "portfolio", "Portfolio", dimensions.Select(static value => value.PortfolioId));
        AddDimensionFilter(filters, "book", "Book", dimensions.Select(static value => value.BookId));
        AddDimensionFilter(filters, "account", "Account Scope", dimensions.Select(static value => value.AccountId));
        AddDimensionFilter(filters, "investor", "Investor", dimensions.Select(static value => value.InvestorId));
        AddDimensionFilter(filters, "capital-account", "Capital Account", dimensions.Select(static value => value.CapitalAccountId));
        AddDimensionFilter(filters, "instrument", "Instrument", dimensions.Select(static value => value.InstrumentId?.ToString("D")));
        AddDimensionFilter(filters, "tax-lot", "Tax Lot", dimensions.Select(static value => value.TaxLotId));
        AddDimensionFilter(filters, "cost-center", "Cost Center", dimensions.Select(static value => value.CostCenterId));
        AddDimensionFilter(filters, "counterparty", "Counterparty", dimensions.Select(static value => value.CounterpartyId));

        var externalGlDimensions = dimensions
            .SelectMany(static value => value.ExternalGlDimensions)
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .GroupBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var group in externalGlDimensions)
        {
            AddDimensionFilter(filters, $"external-gl-{Slugify(group.Key)}", $"External GL: {group.Key}", group.Select(static pair => pair.Value));
        }
    }

    private static void AddDimensionFilter(
        ICollection<FinancialRecordExplorerFilterDto> filters,
        string filterPrefix,
        string label,
        IEnumerable<string?> values)
    {
        foreach (var value in values
            .Select(static value => value?.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            filters.Add(new FinancialRecordExplorerFilterDto(
                $"dimension-{filterPrefix}-{Slugify(value!)}",
                label,
                value!,
                Tone: FinancialRecordExplorerTone.Info));
        }
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildDimensionFields(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return [];
        }

        var fields = new List<FinancialRecordExplorerSummaryItemDto>();
        AddDimensionField(fields, "Fund", dimensions.FundId);
        AddDimensionField(fields, "Entity", dimensions.EntityId);
        AddDimensionField(fields, "Sleeve", dimensions.SleeveId);
        AddDimensionField(fields, "Strategy", dimensions.StrategyId);
        AddDimensionField(fields, "Portfolio", dimensions.PortfolioId);
        AddDimensionField(fields, "Book", dimensions.BookId);
        AddDimensionField(fields, "Account Scope", dimensions.AccountId);
        AddDimensionField(fields, "Investor", dimensions.InvestorId);
        AddDimensionField(fields, "Capital Account", dimensions.CapitalAccountId);
        AddDimensionField(fields, "Instrument", dimensions.InstrumentId?.ToString("D"));
        AddDimensionField(fields, "Position", dimensions.PositionId?.ToString("D"));
        AddDimensionField(fields, "Tax Lot", dimensions.TaxLotId);
        AddDimensionField(fields, "Cost Center", dimensions.CostCenterId);
        AddDimensionField(fields, "Counterparty", dimensions.CounterpartyId);

        foreach (var pair in dimensions.ExternalGlDimensions
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            AddDimensionField(fields, $"External GL: {pair.Key}", pair.Value);
        }

        return fields;
    }

    private static void AddDimensionField(
        ICollection<FinancialRecordExplorerSummaryItemDto> fields,
        string label,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            fields.Add(new FinancialRecordExplorerSummaryItemDto(label, value, Tone: FinancialRecordExplorerTone.Info));
        }
    }

    private static string DisplayDimension(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static FinancialRecordExplorerRowDto BuildPortfolioRow(
        StrategyRunSummary run,
        PortfolioSummary portfolio,
        PortfolioPositionSummary position,
        int index)
    {
        var recordId = $"portfolio:{run.RunId}:{index}:{position.Symbol}";
        var security = position.Security;
        var securityHref = security is null || security.SecurityId == Guid.Empty
            ? string.Empty
            : UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", security.SecurityId.ToString("D"));
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Portfolio position",
            position.Symbol,
            $"{portfolio.PortfolioId} - {portfolio.AsOf:u}",
            "Source-backed portfolio position retained for account and aggregate review.",
            position.IsShort ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default,
            Fields:
            [
                new("Quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("Average Cost", FormatCurrency(position.AverageCostBasis)),
                new("Realized PnL", FormatCurrency(position.RealizedPnl), Tone: ToneFromBalance(position.RealizedPnl)),
                new("Unrealized PnL", FormatCurrency(position.UnrealizedPnl), Tone: ToneFromBalance(position.UnrealizedPnl)),
                new("Security", security?.DisplayName ?? "Unresolved", Tone: security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success)
            ],
            ProofActions: BuildExplorerProofActions(run, UiApiRoutes.WorkstationPortfolio),
            UsedIn:
            [
                new("portfolio-run", "Run portfolio", $"Position belongs to portfolio {portfolio.PortfolioId}.", UiApiRoutes.WorkstationPortfolio),
                new("accounting", "Accounting handoff", "Position can be reconciled against ledger and close records.", UiApiRoutes.WorkstationAccounting)
            ],
            Impacts:
            [
                new("portfolio-equity", "Portfolio equity", $"Unrealized PnL impact is {FormatCurrency(position.UnrealizedPnl)}.", Tone: ToneFromBalance(position.UnrealizedPnl)),
                new("security-master", "Security Master", security is null ? "Security reference is unresolved." : $"Resolved to {security.DisplayName}.", securityHref, security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success)
            ],
            FullRecordHref: securityHref);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "portfolio",
            position.Symbol,
            portfolio.PortfolioId,
            position.IsShort ? "Short" : "Long",
            position.IsShort ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default,
            Cells:
            [
                new("symbol", position.Symbol),
                new("quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("averageCost", FormatCurrency(position.AverageCostBasis), position.AverageCostBasis.ToString(CultureInfo.InvariantCulture)),
                new("realizedPnl", FormatCurrency(position.RealizedPnl), position.RealizedPnl.ToString(CultureInfo.InvariantCulture), ToneFromBalance(position.RealizedPnl)),
                new("unrealizedPnl", FormatCurrency(position.UnrealizedPnl), position.UnrealizedPnl.ToString(CultureInfo.InvariantCulture), ToneFromBalance(position.UnrealizedPnl)),
                new("security", security?.DisplayName ?? "Unresolved", Tone: security is null ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success, LinkHref: securityHref),
                new("coverage", security?.CoverageStatus.ToString() ?? "Missing", Tone: ToneFromCoverage(security?.CoverageStatus))
            ],
            detail);
    }

    private static FinancialRecordExplorerRowDto BuildSecurityRow(
        StrategyRunSummary run,
        StrategyRunDetail detail,
        WorkstationSecurityReference reference,
        int index,
        SecurityInstrumentEnrichment enrichment)
    {
        var recordId = reference.SecurityId == Guid.Empty
            ? $"security:{run.RunId}:{index}"
            : $"security:{reference.SecurityId:D}";
        var href = reference.SecurityId == Guid.Empty
            ? UiApiRoutes.WorkstationSecurityMasterSearch
            : UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", reference.SecurityId.ToString("D"));
        var usedIn = BuildSecurityUsedIn(detail, reference, href, enrichment);
        var fields = BuildSecurityFields(reference, enrichment);
        var proofActions = BuildSecurityProofActions(reference, href, enrichment);
        var impacts = BuildSecurityImpacts(reference, href, enrichment);
        var trustCell = BuildTrustCell(enrichment.Passport);
        var identifierConfidenceCell = BuildIdentifierConfidenceCell(enrichment.Passport);
        var operationsCell = BuildAssetOperationsCell(enrichment.Readiness);
        var directLendingCell = BuildDirectLendingCell(enrichment);
        var cashFlowCell = BuildCashFlowCell(enrichment.Operations);
        var ledgerCell = BuildLedgerCell(enrichment.Operations);
        var termsCell = BuildTermsCell(enrichment.Operations);
        var reportUsageCell = BuildReportUsageCell(reference, enrichment);
        var evidenceCell = BuildEvidenceCell(reference, enrichment);
        var auditTrailCell = BuildAuditTrailCell(reference, enrichment);
        var selected = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Security instrument",
            reference.DisplayName,
            $"{reference.AssetClass} - {reference.Currency}",
            reference.ResolutionReason ?? "Security reference retained by source-backed portfolio or ledger records.",
            ToneFromCoverage(reference.CoverageStatus),
            Fields: fields,
            ProofActions: proofActions,
            UsedIn: usedIn,
            Impacts: impacts,
            FullRecordHref: href);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "security-instrument",
            reference.DisplayName,
            reference.LookupSource ?? "Security Master",
            reference.CoverageStatus.ToString(),
            ToneFromCoverage(reference.CoverageStatus),
            Cells:
            [
                new("security", reference.DisplayName, LinkHref: href),
                new("assetClass", reference.AssetClass),
                new("currency", reference.Currency),
                new("status", reference.Status.ToString()),
                new("coverage", reference.CoverageStatus.ToString(), Tone: ToneFromCoverage(reference.CoverageStatus)),
                trustCell,
                identifierConfidenceCell,
                operationsCell,
                directLendingCell,
                cashFlowCell,
                ledgerCell,
                termsCell,
                reportUsageCell,
                evidenceCell,
                auditTrailCell,
                new("identifier", reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? "-"),
                new("source", reference.LookupSource ?? "Security Master")
            ],
            selected);
    }

    private async Task<SecurityInstrumentEnrichment> BuildSecurityInstrumentEnrichmentAsync(
        WorkstationSecurityReference reference,
        IReadOnlyList<ReportPackWorkflowRecordDto> reportRecords,
        DirectLendingOperationsReadModelDto? directLendingOperations,
        CancellationToken ct)
    {
        var reportLineUsages = CollectSecurityReportLineUsages(reference, reportRecords);
        if (reference.SecurityId == Guid.Empty)
        {
            return new(null, null, null, [], reportLineUsages, []);
        }

        InstrumentPassportDto? passport = null;
        if (_securityMasterWorkbenchQueryService is not null)
        {
            passport = await _securityMasterWorkbenchQueryService
                .GetInstrumentPassportAsync(reference.SecurityId, fundProfileId: null, ct)
                .ConfigureAwait(false);
        }

        AssetOperationsDetailDto? operations = null;
        AssetOperationsReadinessDto? readiness = null;
        if (_assetOperationsQueryService is not null)
        {
            operations = await _assetOperationsQueryService
                .GetOperationsAsync(reference.SecurityId, ct)
                .ConfigureAwait(false);
            if (operations?.Subject.SecurityId != reference.SecurityId)
            {
                operations = null;
            }

            readiness = operations?.Readiness;
            if (readiness is null)
            {
                readiness = await _assetOperationsQueryService
                    .GetReadinessAsync(reference.SecurityId, ct)
                    .ConfigureAwait(false);
            }

            if (readiness?.SecurityId != reference.SecurityId)
            {
                readiness = null;
            }
        }

        var journalProofs = operations is null
            ? []
            : await BuildInstrumentJournalProofsAsync(operations, ct).ConfigureAwait(false);

        var directLendingHealth = directLendingOperations?.LoanHealth
            .Where(health => health.SecurityId == reference.SecurityId)
            .ToArray() ?? [];

        return new(passport, operations, readiness, directLendingHealth, reportLineUsages, journalProofs);
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildSecurityFields(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var fields = new List<FinancialRecordExplorerSummaryItemDto>
        {
            new("Instrument Identity", BuildInstrumentIdentityLabel(reference), BuildInstrumentIdentityDetail(reference), ToneFromCoverage(reference.CoverageStatus)),
            new("Identifier Map", BuildIdentifierMapSummary(reference, enrichment.Passport), BuildIdentifierMapDetail(reference, enrichment.Passport), ToneFromIdentifierMap(reference, enrichment.Passport)),
            new("Asset Class", reference.AssetClass),
            new("Sub Type", reference.SubType ?? "None"),
            new("Currency", reference.Currency),
            new("Status", reference.Status.ToString()),
            new("Primary Identifier", reference.PrimaryIdentifier ?? "None"),
            new("Matched Provider", reference.MatchedProvider ?? "None"),
            new("Accounting Classification", BuildAccountingClassificationSummary(reference, enrichment.Operations), BuildAccountingClassificationDetail(reference, enrichment.Operations), ToneFromCoverage(reference.CoverageStatus)),
            new("Coverage", reference.CoverageStatus.ToString(), Tone: ToneFromCoverage(reference.CoverageStatus))
        };

        if (enrichment.Passport is not null)
        {
            var passport = enrichment.Passport;
            fields.Add(new(
                "Trust Posture",
                BuildTrustPostureLabel(passport.TrustPosture),
                passport.TrustPosture.Summary,
                ToneFromTrustPosture(passport.TrustPosture.Tone)));
            fields.Add(new(
                "Identifier Confidence",
                BuildIdentifierConfidenceLabel(passport),
                BuildIdentifierConfidenceDetail(passport),
                ToneFromIdentifierConfidence(passport)));
            fields.Add(new(
                "Conflict Posture",
                passport.TrustPosture.HasOpenConflicts
                    ? $"{passport.TrustPosture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} open conflict{Plural(passport.TrustPosture.OpenConflictCount)}"
                    : "No open conflicts",
                BuildTrustConflictDetail(passport.TrustPosture),
                passport.TrustPosture.HasOpenConflicts ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Success));
        }

        if (enrichment.Operations is not null)
        {
            fields.Add(new(
                "Terms / Obligations",
                BuildTermsObligationsSummary(enrichment.Operations),
                BuildTermsObligationsDetail(enrichment.Operations),
                ToneFromTerms(enrichment.Operations)));
        }

        if (enrichment.Readiness is not null)
        {
            fields.Add(new(
                "AssetOperations Readiness",
                enrichment.Readiness.Status,
                BuildReadinessDetail(enrichment.Readiness),
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));
        }

        if (enrichment.DirectLendingHealth.Count > 0)
        {
            fields.Add(new(
                "Direct Lending Operations",
                BuildDirectLendingSummary(enrichment),
                BuildDirectLendingDetail(enrichment),
                ToneFromDirectLending(enrichment)));
        }

        if (enrichment.Operations is not null)
        {
            fields.Add(new(
                "Projected Cash Flows",
                BuildCashFlowSummary(enrichment.Operations),
                BuildCashFlowDetail(enrichment.Operations),
                ToneFromCashFlows(enrichment.Operations)));
            fields.Add(new(
                "Accounting Projection",
                BuildLedgerProjectionSummary(enrichment.Operations),
                BuildLedgerProjectionDetail(enrichment.Operations),
                ToneFromLedgerProjections(enrichment.Operations)));
            fields.Add(new(
                "Projected Accounting Effect",
                BuildProjectedAccountingEffectSummary(enrichment.Operations),
                BuildProjectedAccountingEffectDetail(enrichment.Operations),
                ToneFromLedgerProjections(enrichment.Operations)));
            fields.Add(new(
                "Reconciliation",
                BuildReconciliationSummary(enrichment.Operations),
                BuildReconciliationDetail(enrichment.Operations),
                ToneFromReconciliationResults(enrichment.Operations)));
        }

        AppendInstrumentJournalProofFields(fields, enrichment);

        if (enrichment.ReportLineUsages.Count > 0)
        {
            fields.Add(new(
                "Reported",
                $"{enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)} retained line{Plural(enrichment.ReportLineUsages.Count)}",
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                FinancialRecordExplorerTone.Success));
        }

        fields.Add(new(
            "Evidence",
            BuildSecurityEvidenceSummary(enrichment),
            BuildSecurityEvidenceDetail(enrichment),
            ToneFromSecurityEvidence(enrichment)));
        fields.Add(new(
            "Audit Trail",
            BuildSecurityAuditSummary(enrichment),
            BuildSecurityAuditDetail(enrichment),
            ToneFromSecurityAudit(enrichment)));

        return fields;
    }

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildSecurityProofActions(
        WorkstationSecurityReference reference,
        string securityHref,
        SecurityInstrumentEnrichment enrichment)
    {
        var actions = new List<FinancialRecordExplorerProofActionDto>
        {
            new(
                "open-security-master",
                "Open Security Master",
                "Open the retained Security Master record.",
                securityHref,
                reference.SecurityId != Guid.Empty,
                "Security reference is not resolved.",
                ToneFromCoverage(reference.CoverageStatus))
        };

        if (reference.SecurityId != Guid.Empty)
        {
            actions.Add(new(
                "open-instrument-passport",
                "Open instrument passport",
                "Open Security Master passport and trust evidence for this instrument.",
                BuildInstrumentPassportHref(reference.SecurityId),
                enrichment.Passport is not null,
                "Security Master passport evidence is not available.",
                enrichment.Passport is null ? FinancialRecordExplorerTone.Warning : ToneFromTrustPosture(enrichment.Passport.TrustPosture.Tone)));
            actions.Add(new(
                "open-asset-operations",
                "Open AssetOperations",
                "Open operations readiness, projected cash-flow, and ledger-projection evidence.",
                BuildAssetOperationsHref(reference.SecurityId),
                enrichment.Readiness is not null,
                "AssetOperations readiness evidence is not available.",
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));
            actions.Add(new(
                "open-direct-lending-operations",
                "Open direct-lending operations",
                "Open retained direct-lending loan health, collateral, covenant/status, servicing, evidence, journal, reconciliation, and close-blocker posture.",
                BuildDirectLendingHref(enrichment),
                enrichment.DirectLendingHealth.Count > 0,
                "No direct-lending operations posture references this instrument.",
                ToneFromDirectLending(enrichment)));
            actions.Add(new(
                "open-position-transaction",
                "Open position/transaction",
                "Open the retained position or transaction rows that reference this instrument.",
                BuildSecurityPositionTransactionHref(reference, enrichment),
                HasSecurityPositionTransaction(reference, enrichment),
                "No retained position, transaction, or provider-event route references this instrument.",
                HasSecurityPositionTransaction(reference, enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
            actions.Add(new(
                "open-reconciliation",
                "Open reconciliation",
                "Open the reconciliation result or run linked to this instrument.",
                BuildSecurityReconciliationHref(enrichment),
                HasSecurityReconciliation(enrichment),
                "No retained reconciliation result or run references this instrument.",
                HasSecurityReconciliation(enrichment) ? ToneFromSecurityReconciliation(enrichment) : FinancialRecordExplorerTone.Warning));
            if (HasSecurityJournal(enrichment))
            {
                actions.Add(new(
                    "open-journal-impact",
                    "Open posted journal",
                    "Open the durable posted journal resolved by exact ledger-book and source-event scope.",
                    BuildSecurityJournalHref(enrichment),
                    IsEnabled: true,
                    DisabledReason: string.Empty,
                    Tone: FinancialRecordExplorerTone.Success));
            }
        }

        if (enrichment.ReportLineUsages.Count > 0)
        {
            actions.Add(new(
                "open-report-line-provenance",
                "Open report-line provenance",
                "Open retained report-line provenance rows that reference this security.",
                BuildSecurityReportLineHref(reference, enrichment),
                true,
                string.Empty,
                FinancialRecordExplorerTone.Info));
        }

        actions.Add(new(
            "open-evidence",
            "Open evidence",
            "Open the retained evidence packet for this instrument's proof chain.",
            BuildSecurityEvidenceHref(reference, enrichment),
            HasSecurityEvidence(enrichment),
            "No retained evidence packet references this instrument.",
            HasSecurityEvidence(enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        actions.Add(new(
            "open-audit-trail",
            "Open audit trail",
            "Open the retained audit graph for this instrument's proof chain.",
            BuildSecurityAuditHref(reference, enrichment),
            HasSecurityAudit(enrichment),
            "No retained audit event references this instrument.",
            HasSecurityAudit(enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));

        return actions;
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildSecurityImpacts(
        WorkstationSecurityReference reference,
        string securityHref,
        SecurityInstrumentEnrichment enrichment)
    {
        var impacts = new List<FinancialRecordExplorerRelationshipDto>
        {
            new("instrument-coverage", "Instrument coverage", $"Coverage state is {reference.CoverageStatus}.", securityHref, ToneFromCoverage(reference.CoverageStatus))
        };
        var positionTransactionHref = BuildSecurityPositionTransactionHref(reference, enrichment);
        impacts.Add(new(
            "position-transaction",
            "Position / transaction",
            HasSecurityPositionTransaction(reference, enrichment)
                ? "Retained position, transaction, or provider-event evidence references this instrument."
                : "No retained position, transaction, or provider-event evidence references this instrument.",
            positionTransactionHref,
            HasSecurityPositionTransaction(reference, enrichment) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));

        if (enrichment.Passport is not null)
        {
            var passportHref = BuildInstrumentPassportHref(enrichment.Passport.SecurityId);
            impacts.Add(new(
                "instrument-passport",
                "Instrument passport",
                enrichment.Passport.TrustPosture.Summary,
                passportHref,
                ToneFromTrustPosture(enrichment.Passport.TrustPosture.Tone)));

            if (enrichment.Passport.TrustPosture.HasOpenConflicts)
            {
                impacts.Add(new(
                    "security-master-conflicts",
                    "Conflict blockers",
                    $"{enrichment.Passport.TrustPosture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} Security Master conflict{Plural(enrichment.Passport.TrustPosture.OpenConflictCount)} require steward review.",
                    passportHref,
                FinancialRecordExplorerTone.Warning));
            }
        }

        if (enrichment.Readiness is not null)
        {
            var assetHref = BuildAssetOperationsHref(enrichment.Readiness.SecurityId);
            impacts.Add(new(
                "asset-operations-readiness",
                "AssetOperations readiness",
                BuildReadinessDetail(enrichment.Readiness),
                assetHref,
                ToneFromAssetOperationsReadiness(enrichment.Readiness)));

            if (enrichment.Readiness.MissingCapabilities.Count > 0 || enrichment.Readiness.Warnings.Count > 0)
            {
                impacts.Add(new(
                    "asset-operations-validation-blockers",
                    "Validation blockers",
                    BuildReadinessBlockerDetail(enrichment.Readiness),
                    assetHref,
                    FinancialRecordExplorerTone.Warning));
            }
        }

        impacts.Add(new(
            "direct-lending-operations",
            "Direct-lending operations",
            enrichment.DirectLendingHealth.Count > 0
                ? BuildDirectLendingDetail(enrichment)
                : "No direct-lending operations read model references this instrument.",
            BuildDirectLendingHref(enrichment),
            ToneFromDirectLending(enrichment)));

        if (enrichment.Operations is not null)
        {
            var assetHref = BuildAssetOperationsHref(enrichment.Operations.Subject.SecurityId);
            impacts.Add(new(
                "terms-obligations",
                "Terms / obligations",
                BuildTermsObligationsDetail(enrichment.Operations),
                assetHref,
                ToneFromTerms(enrichment.Operations)));
            impacts.Add(new(
                "projected-cash-flows",
                "Projected cash flows",
                BuildCashFlowDetail(enrichment.Operations),
                assetHref,
                ToneFromCashFlows(enrichment.Operations)));
            impacts.Add(new(
                "reconciliation",
                "Reconciliation",
                BuildReconciliationDetail(enrichment.Operations),
                BuildSecurityReconciliationHref(enrichment),
                ToneFromReconciliationResults(enrichment.Operations)));
            impacts.Add(new(
                "ledger-projection",
                "Accounting projection",
                BuildLedgerProjectionDetail(enrichment.Operations),
                assetHref,
                ToneFromLedgerProjections(enrichment.Operations)));
            impacts.Add(new(
                "projected-accounting-effect",
                "Projected accounting effect",
                BuildProjectedAccountingEffectDetail(enrichment.Operations),
                assetHref,
                ToneFromLedgerProjections(enrichment.Operations)));
        }

        AppendInstrumentJournalProofImpacts(impacts, reference, enrichment);

        if (enrichment.ReportLineUsages.Count > 0)
        {
            impacts.Add(new(
                "report-line",
                "Reported line",
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                BuildSecurityReportLineHref(reference, enrichment),
                FinancialRecordExplorerTone.Success));
        }
        impacts.Add(new(
            "evidence",
            "Evidence",
            BuildSecurityEvidenceDetail(enrichment),
            BuildSecurityEvidenceHref(reference, enrichment),
            ToneFromSecurityEvidence(enrichment)));
        impacts.Add(new(
            "audit-event",
            "Audit event",
            BuildSecurityAuditDetail(enrichment),
            BuildSecurityAuditHref(reference, enrichment),
            ToneFromSecurityAudit(enrichment)));

        return impacts;
    }

    private static FinancialRecordExplorerCellDto BuildTrustCell(InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return new("trust", "-");
        }

        return new(
            "trust",
            passport.TrustPosture.Tone.ToString(),
            passport.TrustPosture.TrustScore.ToString(CultureInfo.InvariantCulture),
            ToneFromTrustPosture(passport.TrustPosture.Tone),
            BuildInstrumentPassportHref(passport.SecurityId));
    }

    private static FinancialRecordExplorerCellDto BuildIdentifierConfidenceCell(InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return new("identifierConfidence", "-");
        }

        return new(
            "identifierConfidence",
            BuildIdentifierConfidenceLabel(passport),
            BuildIdentifierConfidenceDetail(passport),
            ToneFromIdentifierConfidence(passport),
            BuildInstrumentPassportHref(passport.SecurityId));
    }

    private static FinancialRecordExplorerCellDto BuildDirectLendingCell(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return new("directLending", "-");
        }

        return new(
            "directLending",
            BuildDirectLendingSummary(enrichment),
            enrichment.DirectLendingHealth.Count.ToString(CultureInfo.InvariantCulture),
            ToneFromDirectLending(enrichment),
            BuildDirectLendingHref(enrichment));
    }

    private static FinancialRecordExplorerCellDto BuildAssetOperationsCell(AssetOperationsReadinessDto? readiness)
        => readiness is null
            ? new("operations", "-")
            : new(
                "operations",
                readiness.Status,
                BuildReadinessDetail(readiness),
                ToneFromAssetOperationsReadiness(readiness),
                BuildAssetOperationsHref(readiness.SecurityId));

    private static FinancialRecordExplorerCellDto BuildCashFlowCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("cashFlow", "-")
            : new(
                "cashFlow",
                BuildCashFlowSummary(operations),
                BuildCashFlowDetail(operations),
                ToneFromCashFlows(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildLedgerCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("ledger", "-")
            : new(
                "ledger",
                BuildLedgerProjectionSummary(operations),
                BuildLedgerProjectionDetail(operations),
                ToneFromLedgerProjections(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildTermsCell(AssetOperationsDetailDto? operations)
        => operations is null
            ? new("terms", "-")
            : new(
                "terms",
                BuildTermsObligationsSummary(operations),
                BuildTermsObligationsDetail(operations),
                ToneFromTerms(operations),
                BuildAssetOperationsHref(operations.Subject.SecurityId));

    private static FinancialRecordExplorerCellDto BuildReportUsageCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => enrichment.ReportLineUsages.Count == 0
            ? new("reportUsage", "-")
            : new(
                "reportUsage",
                BuildReportUsageSummary(enrichment.ReportLineUsages),
                BuildReportedLineUsageDetail(enrichment.ReportLineUsages),
                FinancialRecordExplorerTone.Success,
                BuildSecurityReportLineHref(reference, enrichment));

    private static FinancialRecordExplorerCellDto BuildEvidenceCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => new(
            "evidence",
            BuildSecurityEvidenceSummary(enrichment),
            BuildSecurityEvidenceDetail(enrichment),
            ToneFromSecurityEvidence(enrichment),
            BuildSecurityEvidenceHref(reference, enrichment));

    private static FinancialRecordExplorerCellDto BuildAuditTrailCell(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => new(
            "auditTrail",
            BuildSecurityAuditSummary(enrichment),
            BuildSecurityAuditDetail(enrichment),
            ToneFromSecurityAudit(enrichment),
            BuildSecurityAuditHref(reference, enrichment));

    private static string BuildInstrumentIdentityLabel(WorkstationSecurityReference reference)
    {
        var identifier = reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue;
        return HasText(identifier)
            ? $"{identifier} / {reference.DisplayName}"
            : reference.DisplayName;
    }

    private static string BuildInstrumentIdentityDetail(WorkstationSecurityReference reference)
    {
        var securityId = reference.SecurityId == Guid.Empty ? "unresolved" : reference.SecurityId.ToString("D");
        return $"{reference.AssetClass} {reference.SubType ?? "instrument"} in {reference.Currency}; Security Master id {securityId}.";
    }

    private static string BuildIdentifierMapSummary(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return HasText(reference.PrimaryIdentifier)
                ? $"{reference.PrimaryIdentifier} ({reference.MatchedProvider ?? "source"})"
                : "No identifier map";
        }

        return $"{passport.IdentifierSummary.ActiveIdentifierCount.ToString(CultureInfo.InvariantCulture)} identifier{Plural(passport.IdentifierSummary.ActiveIdentifierCount)} / {passport.IdentifierSummary.ProviderMappingCount.ToString(CultureInfo.InvariantCulture)} provider mapping{Plural(passport.IdentifierSummary.ProviderMappingCount)}";
    }

    private static string BuildIdentifierMapDetail(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is null)
        {
            return reference.ResolutionReason ?? "Identifier map evidence is not available from Security Master.";
        }

        var providers = passport.IdentifierSummary.ProviderMappings
            .Select(static mapping => mapping.Provider ?? mapping.MappingSource)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        return providers.Length == 0
            ? passport.IdentifierSummary.Summary
            : $"{passport.IdentifierSummary.Summary} Providers: {string.Join(", ", providers)}.";
    }

    private static FinancialRecordExplorerTone ToneFromIdentifierMap(
        WorkstationSecurityReference reference,
        InstrumentPassportDto? passport)
    {
        if (passport is not null)
        {
            return passport.IdentifierSummary.HasPrimaryIdentifier && passport.IdentifierSummary.HasProviderMappings
                ? FinancialRecordExplorerTone.Success
                : FinancialRecordExplorerTone.Warning;
        }

        return HasText(reference.PrimaryIdentifier) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning;
    }

    private static string BuildAccountingClassificationSummary(
        WorkstationSecurityReference reference,
        AssetOperationsDetailDto? operations)
    {
        var ledgerBasis = operations?.LedgerProjections
            .Select(static projection => projection.LedgerBasis)
            .FirstOrDefault(HasText);
        return HasText(ledgerBasis)
            ? $"{reference.AssetClass} / {ledgerBasis}"
            : reference.AssetClass;
    }

    private static string BuildAccountingClassificationDetail(
        WorkstationSecurityReference reference,
        AssetOperationsDetailDto? operations)
    {
        var projection = operations?.LedgerProjections
            .OrderByDescending(static item => item.AccountingDate)
            .FirstOrDefault();
        if (projection is null)
        {
            return $"{reference.SubType ?? reference.AssetClass} classification is retained from Security Master coverage.";
        }

        return $"{projection.ProjectionType} is classified on {projection.LedgerBasis} with status {projection.Status}.";
    }

    private static string BuildTrustConflictDetail(SecurityMasterTrustPostureDto posture)
        => posture.HasOpenConflicts
            ? $"{posture.OpenConflictCount.ToString(CultureInfo.InvariantCulture)} open conflict{Plural(posture.OpenConflictCount)}; recommended steward review before accounting close or report publication."
            : $"{posture.Summary} No open Security Master conflicts are retained.";

    private static string BuildTermsObligationsSummary(AssetOperationsDetailDto operations)
    {
        var terms = operations.TermsHistory.Count;
        var obligations = operations.TermsObligationsTimeline?.Events.Count
            ?? operations.ProjectedCashFlows.Count + operations.LifecycleEvents.Count;
        if (terms == 0 && obligations == 0)
        {
            return "No terms";
        }

        return $"{terms.ToString(CultureInfo.InvariantCulture)} term{Plural(terms)} / {obligations.ToString(CultureInfo.InvariantCulture)} obligation{Plural(obligations)}";
    }

    private static string BuildTermsObligationsDetail(AssetOperationsDetailDto operations)
    {
        var latestTerms = operations.TermsHistory
            .OrderByDescending(static terms => terms.EffectiveDate)
            .FirstOrDefault();
        var nextTimelineEvent = operations.TermsObligationsTimeline?.Events
            .FirstOrDefault(static timelineEvent => !string.Equals(timelineEvent.EventLane, "Lifecycle", StringComparison.OrdinalIgnoreCase));
        var nextFlow = nextTimelineEvent is null
            ? operations.ProjectedCashFlows
                .OrderBy(static flow => flow.DueDate)
                .FirstOrDefault()
            : null;

        if (latestTerms is null && nextTimelineEvent is null && nextFlow is null)
        {
            return "No retained terms or obligation rows are available.";
        }

        var parts = new List<string>();
        if (latestTerms is not null)
        {
            parts.Add($"{latestTerms.Summary} Effective {latestTerms.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
        }

        if (nextTimelineEvent is not null)
        {
            var amount = nextTimelineEvent.ExpectedAmount.HasValue
                ? $" for {FormatAmountCurrency(nextTimelineEvent.ExpectedAmount.Value, nextTimelineEvent.Currency)}"
                : string.Empty;
            parts.Add($"Next timeline event is {nextTimelineEvent.EventKind} due {nextTimelineEvent.EffectiveDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{amount}; status {nextTimelineEvent.Status}.");
        }
        else if (nextFlow is not null)
        {
            parts.Add($"Next obligation is {nextFlow.FlowType} due {nextFlow.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(nextFlow.Amount, nextFlow.Currency)}.");
        }

        return string.Join(" ", parts);
    }

    private static FinancialRecordExplorerTone ToneFromTerms(AssetOperationsDetailDto operations)
        => operations.TermsHistory.Count == 0 &&
           operations.ProjectedCashFlows.Count == 0 &&
           (operations.TermsObligationsTimeline?.Events.Count ?? 0) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Success;

    private static string BuildReconciliationSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.ReconciliationResults.Count;
        return count == 0
            ? "No reconciliation"
            : $"{count.ToString(CultureInfo.InvariantCulture)} result{Plural(count)}";
    }

    private static string BuildReconciliationDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.ReconciliationResults
            .OrderByDescending(static result => result.ActualDate ?? result.ExpectedDate ?? DateOnly.MinValue)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained reconciliation result is available.";
        }

        var expected = latest.ExpectedAmount.HasValue ? FormatAmountCurrency(latest.ExpectedAmount.Value, "USD") : "no expected amount";
        var actual = latest.ActualAmount.HasValue ? FormatAmountCurrency(latest.ActualAmount.Value, "USD") : "no actual amount";
        return $"{latest.MatchStatus} reconciliation result compares {expected} expected to {actual} actual.";
    }

    private static string BuildProjectedAccountingEffectSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.LedgerProjections.Count;
        return count == 0
            ? "No projected effect"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projected effect{Plural(count)}";
    }

    private static string BuildProjectedAccountingEffectDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.LedgerProjections
            .OrderByDescending(static projection => projection.AccountingDate)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained projected accounting effect is available.";
        }

        var debit = latest.DebitAmount ?? 0m;
        var credit = latest.CreditAmount ?? 0m;
        return $"{latest.ProjectionType} {latest.Status} on {latest.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {latest.LedgerBasis}; projected debit {FormatAmountCurrency(debit, latest.Currency)} and projected credit {FormatAmountCurrency(credit, latest.Currency)}. This is not a posted journal.";
    }

    private static string BuildReportUsageSummary(IReadOnlyList<SecurityReportLineUsage> usages)
    {
        if (usages.Count == 0)
        {
            return "No report usage";
        }

        var firstLine = usages
            .Select(static usage => usage.Line.LineKey)
            .FirstOrDefault(HasText);
        return usages.Count == 1 && HasText(firstLine)
            ? firstLine!
            : $"{usages.Count.ToString(CultureInfo.InvariantCulture)} report line{Plural(usages.Count)}";
    }

    private static string BuildSecurityEvidenceSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = CountSecurityEvidenceAnchors(enrichment);
        return count == 0
            ? "No evidence"
            : $"{count.ToString(CultureInfo.InvariantCulture)} evidence anchor{Plural(count)}";
    }

    private static string BuildSecurityEvidenceDetail(SecurityInstrumentEnrichment enrichment)
    {
        var reportLineEvidence = enrichment.ReportLineUsages
            .Select(static usage => usage.Line.EvidenceId)
            .FirstOrDefault(HasText);
        if (HasText(reportLineEvidence))
        {
            return $"Report-line evidence {reportLineEvidence} anchors this instrument's retained proof chain.";
        }

        var reconciliationEvidence = enrichment.Operations?.ReconciliationResults
            .Select(static result => result.EvidenceLink)
            .FirstOrDefault(HasText);
        if (HasText(reconciliationEvidence))
        {
            return $"AssetOperations reconciliation evidence is retained at {reconciliationEvidence}.";
        }

        var termsHash = enrichment.Operations?.TermsHistory
            .Select(static terms => terms.TermsHash)
            .FirstOrDefault(HasText);
        return HasText(termsHash)
            ? $"Terms hash {termsHash} is retained as instrument evidence."
            : "No retained evidence anchor is available.";
    }

    private static FinancialRecordExplorerTone ToneFromSecurityEvidence(SecurityInstrumentEnrichment enrichment)
        => CountSecurityEvidenceAnchors(enrichment) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Info;

    private static string BuildSecurityAuditSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = CountSecurityAuditEvents(enrichment);
        return count == 0
            ? "No audit"
            : $"{count.ToString(CultureInfo.InvariantCulture)} audit event{Plural(count)}";
    }

    private static string BuildSecurityAuditDetail(SecurityInstrumentEnrichment enrichment)
    {
        var latestWorkflowAudit = enrichment.Operations?.WorkflowAudit
            .OrderByDescending(static item => item.RecordedAt)
            .FirstOrDefault();
        if (latestWorkflowAudit is not null)
        {
            return $"{latestWorkflowAudit.EventType} audit event retained from {latestWorkflowAudit.SourceDomain}: {latestWorkflowAudit.Summary}";
        }

        if (enrichment.ReportLineUsages.Any(static usage => HasText(usage.Line.EvidenceId)))
        {
            return "Report-line evidence id links this instrument to retained audit-packet history.";
        }

        return enrichment.Passport is null
            ? "No retained audit event is available."
            : $"Instrument passport was retrieved at {enrichment.Passport.RetrievedAtUtc.ToString("u", CultureInfo.InvariantCulture)}.";
    }

    private static FinancialRecordExplorerTone ToneFromSecurityAudit(SecurityInstrumentEnrichment enrichment)
        => CountSecurityAuditEvents(enrichment) == 0
            ? FinancialRecordExplorerTone.Warning
            : FinancialRecordExplorerTone.Info;

    private static int CountSecurityEvidenceAnchors(SecurityInstrumentEnrichment enrichment)
        => enrichment.ReportLineUsages.Count(static usage => HasText(usage.Line.EvidenceId)) +
           enrichment.InstrumentJournalProofs.Sum(static proof => proof.Lineage.TriggerEvent.EvidenceLinks.Count) +
           (enrichment.Operations?.ActualActivity.Count(static activity => HasText(activity.EvidenceLink)) ?? 0) +
           (enrichment.Operations?.ReconciliationResults.Count(static result => HasText(result.EvidenceLink)) ?? 0) +
           (enrichment.Operations?.TermsHistory.Count(static terms => HasText(terms.TermsHash)) ?? 0);

    private static int CountSecurityAuditEvents(SecurityInstrumentEnrichment enrichment)
        => (enrichment.Operations?.WorkflowAudit.Count ?? 0) +
           (enrichment.Passport?.LifecycleEvents.Count ?? 0) +
           enrichment.InstrumentJournalProofs.Count(static proof => proof.Journal is not null) +
           enrichment.ReportLineUsages.Count(static usage => HasText(usage.Line.EvidenceId));

    private static bool HasSecurityPositionTransaction(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
        => reference.SecurityId != Guid.Empty ||
           enrichment.ReportLineUsages.Any(static usage =>
               HasText(usage.Line.ProviderEventId) ||
               HasText(usage.Line.SourceSessionId) ||
               HasText(usage.Line.SourceId));

    private static bool HasSecurityReconciliation(SecurityInstrumentEnrichment enrichment)
        => enrichment.Operations?.ReconciliationResults.Count > 0 ||
           enrichment.ReportLineUsages.Any(static usage =>
               HasText(usage.Line.ReconciliationRunId) ||
               HasText(usage.Line.ReconciliationCaseId));

    private static bool HasSecurityJournal(SecurityInstrumentEnrichment enrichment)
        => enrichment.InstrumentJournalProofs.Any(static proof => proof.Journal is not null);

    private static bool HasSecurityEvidence(SecurityInstrumentEnrichment enrichment)
        => CountSecurityEvidenceAnchors(enrichment) > 0;

    private static bool HasSecurityAudit(SecurityInstrumentEnrichment enrichment)
        => CountSecurityAuditEvents(enrichment) > 0 || enrichment.Passport is not null;

    private static FinancialRecordExplorerTone ToneFromSecurityReconciliation(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.Operations is not null && enrichment.Operations.ReconciliationResults.Count > 0)
        {
            return ToneFromReconciliationResults(enrichment.Operations);
        }

        var outcome = enrichment.ReportLineUsages
            .Select(static usage => usage.Line.ReconciliationOutcome)
            .FirstOrDefault(HasText);
        return HasText(outcome) ? ToneFromReconciliationOutcome(outcome) : FinancialRecordExplorerTone.Warning;
    }

    private static FinancialRecordExplorerTone ToneFromSecurityJournal(SecurityInstrumentEnrichment enrichment)
        => enrichment.InstrumentJournalProofs.Any(static proof => proof.Journal is not null)
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Warning;

    private static string BuildSecurityPositionTransactionHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var usage = enrichment.ReportLineUsages.FirstOrDefault();
        return BuildFinancialRecordExplorerHref(
            PortfolioExplorerId,
            BuildSecurityQueryParameters(reference, usage?.Line));
    }

    private static string BuildSecurityReconciliationHref(SecurityInstrumentEnrichment enrichment)
    {
        var reportLineHref = enrichment.ReportLineUsages
            .Select(static usage => BuildReportLineReconciliationHref(usage.Line))
            .FirstOrDefault(HasText);
        if (HasText(reportLineHref))
        {
            return reportLineHref!;
        }

        var securityId = enrichment.Operations?.Subject.SecurityId ?? Guid.Empty;
        return securityId == Guid.Empty ? string.Empty : BuildAssetOperationsHref(securityId);
    }

    private static string BuildSecurityReportLineHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var usage = enrichment.ReportLineUsages.FirstOrDefault();
        return BuildFinancialRecordExplorerHref(
            ReportLineProvenanceExplorerId,
            BuildSecurityQueryParameters(reference, usage?.Line));
    }

    private static string BuildSecurityEvidenceHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var reportLineHref = enrichment.ReportLineUsages
            .Select(static usage => BuildReportLineAuditHref(usage.Line))
            .FirstOrDefault(HasText);
        if (HasText(reportLineHref))
        {
            return reportLineHref!;
        }

        var reconciliationEvidence = enrichment.Operations?.ReconciliationResults
            .Select(static result => result.EvidenceLink)
            .FirstOrDefault(HasText);
        if (HasText(reconciliationEvidence))
        {
            return reconciliationEvidence!;
        }

        var subjectId = reference.SecurityId != Guid.Empty
            ? reference.SecurityId.ToString("D")
            : reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName;
        return HasText(subjectId)
            ? BuildEvidenceSubjectRoute(UiApiRoutes.WorkstationEvidenceSubjectPacket, "security-instrument", subjectId!)
            : string.Empty;
    }

    private static string BuildSecurityAuditHref(
        WorkstationSecurityReference reference,
        SecurityInstrumentEnrichment enrichment)
    {
        var subjectId = reference.SecurityId != Guid.Empty
            ? reference.SecurityId.ToString("D")
            : reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName;
        if (HasText(subjectId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectGraph,
                "security-instrument",
                subjectId!);
        }

        return BuildSecurityEvidenceHref(reference, enrichment);
    }

    private static string BuildDirectLendingSummary(SecurityInstrumentEnrichment enrichment)
    {
        var count = enrichment.DirectLendingHealth.Count;
        if (count == 0)
        {
            return "No linked loans";
        }

        var blocked = enrichment.DirectLendingHealth.Count(static health => IsNegativeStatus(health.HealthStatus));
        return blocked > 0
            ? $"{blocked.ToString(CultureInfo.InvariantCulture)}/{count.ToString(CultureInfo.InvariantCulture)} loan health blocker{Plural(blocked)}"
            : $"{count.ToString(CultureInfo.InvariantCulture)} linked loan{Plural(count)}";
    }

    private static string BuildDirectLendingDetail(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return "No direct-lending operations read model row references this Security Master instrument.";
        }

        return string.Join(
            "; ",
            enrichment.DirectLendingHealth
                .OrderBy(static health => health.FacilityName, StringComparer.OrdinalIgnoreCase)
                .Select(static health => $"{health.FacilityName}: {health.HealthStatus} - {health.HealthDetail}"));
    }

    private static string BuildDirectLendingHref(SecurityInstrumentEnrichment enrichment)
        => enrichment.DirectLendingHealth.FirstOrDefault()?.Route ?? "/api/loans/operations";

    private static string BuildFinancialRecordExplorerHref(
        string explorerId,
        IReadOnlyList<string> queryParameters)
    {
        var route = UiApiRoutes.WithParam(UiApiRoutes.WorkstationFinancialRecordExplorer, "explorerId", explorerId);
        return queryParameters.Count == 0
            ? route
            : UiApiRoutes.WithQuery(route, string.Join("&", queryParameters));
    }

    private static IReadOnlyList<string> BuildSecurityQueryParameters(
        WorkstationSecurityReference reference,
        ReportPackLineProvenanceDto? line)
    {
        var parameters = new List<string>();
        AddQueryParameter(parameters, "securityId", reference.SecurityId == Guid.Empty ? null : reference.SecurityId.ToString("D"));
        AddQueryParameter(parameters, "sourceId", reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? reference.DisplayName);
        AddQueryParameter(parameters, "lineKey", line?.LineKey);
        AddQueryParameter(parameters, "evidenceId", line?.EvidenceId);
        return parameters;
    }

    private static void AddQueryParameter(ICollection<string> parameters, string key, string? value)
    {
        if (HasText(value))
        {
            parameters.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value!.Trim())}");
        }
    }

    private static string BuildTrustPostureLabel(SecurityMasterTrustPostureDto posture)
        => $"{posture.Tone} ({posture.TrustScore.ToString(CultureInfo.InvariantCulture)})";

    private static string BuildIdentifierConfidenceLabel(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is not null)
        {
            return $"{EmptyFallback(confidence.Provider, "Provider")} {FormatConfidence(confidence.ConfidenceScore)}";
        }

        if (passport.IdentifierSummary.HasPrimaryIdentifier)
        {
            var kind = EmptyFallback(passport.IdentifierSummary.PrimaryIdentifierKind, "Identifier");
            return $"{kind}: {passport.IdentifierSummary.PrimaryIdentifierValue}";
        }

        return "No primary identifier";
    }

    private static string BuildIdentifierConfidenceDetail(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is null)
        {
            return passport.IdentifierSummary.Summary;
        }

        return $"{confidence.ProviderSource} {confidence.MappingKind} mapping for {confidence.Symbol}: {confidence.ConfidenceReason}";
    }

    private static string BuildReadinessDetail(AssetOperationsReadinessDto readiness)
    {
        var total = readiness.Capabilities.Count;
        var ready = readiness.ReadyCapabilities.Count;
        if (readiness.MissingCapabilities.Count == 0 && readiness.Warnings.Count == 0)
        {
            return $"{ready.ToString(CultureInfo.InvariantCulture)}/{total.ToString(CultureInfo.InvariantCulture)} capability{Plural(total)} ready.";
        }

        return BuildReadinessBlockerDetail(readiness);
    }

    private static string BuildReadinessBlockerDetail(AssetOperationsReadinessDto readiness)
    {
        var blockers = readiness.MissingCapabilities
            .Concat(readiness.Warnings)
            .Where(HasText)
            .Take(4)
            .ToArray();
        return blockers.Length == 0
            ? "No validation blockers retained."
            : string.Join("; ", blockers);
    }

    private static string BuildCashFlowSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.ProjectedCashFlows.Count;
        return count == 0
            ? "No projection"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projected";
    }

    private static string BuildCashFlowDetail(AssetOperationsDetailDto operations)
    {
        var next = operations.ProjectedCashFlows
            .OrderBy(static flow => flow.DueDate)
            .FirstOrDefault();
        if (next is null)
        {
            return "No retained projected cash-flow row is available.";
        }

        return $"Next {next.FlowType} due {next.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(next.Amount, next.Currency)} ({next.Status}).";
    }

    private static string BuildLedgerProjectionSummary(AssetOperationsDetailDto operations)
    {
        var count = operations.LedgerProjections.Count;
        return count == 0
            ? "No projection"
            : $"{count.ToString(CultureInfo.InvariantCulture)} projection{Plural(count)}";
    }

    private static string BuildLedgerProjectionDetail(AssetOperationsDetailDto operations)
    {
        var latest = operations.LedgerProjections
            .OrderByDescending(static projection => projection.AccountingDate)
            .FirstOrDefault();
        if (latest is null)
        {
            return "No retained ledger-projection row is available.";
        }

        var amount = latest.DebitAmount ?? latest.CreditAmount ?? 0m;
        return $"{latest.ProjectionType} {latest.Status} on {latest.AccountingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} for {FormatAmountCurrency(amount, latest.Currency)}.";
    }

    private static string BuildReportedLineUsageDetail(IReadOnlyList<SecurityReportLineUsage> usages)
    {
        var lineKeys = usages
            .Select(static usage => usage.Line.LineKey)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToArray();
        var publicationIds = usages
            .Select(static usage => usage.Record.Publication?.ManifestId)
            .Where(HasText)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();
        var lines = lineKeys.Length == 0
            ? "Published or restated report-line provenance references this security."
            : $"Reported lines: {string.Join(", ", lineKeys)}.";
        return publicationIds.Length == 0
            ? lines
            : $"{lines} Publication manifests: {string.Join(", ", publicationIds!)}.";
    }

    private static InstrumentPassportProviderConfidenceDto? SelectBestProviderConfidence(InstrumentPassportDto passport)
        => passport.ProviderConfidence
            .OrderByDescending(static confidence => confidence.ConfidenceScore)
            .FirstOrDefault();

    private static IReadOnlyList<SecurityReportLineUsage> CollectSecurityReportLineUsages(
        WorkstationSecurityReference reference,
        IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        var usages = new List<SecurityReportLineUsage>();
        foreach (var record in records.Where(static record => IsAuthoritativelyReported(record)))
        {
            foreach (var line in record.LineProvenance ?? [])
            {
                if (IsSameSecurityReportLine(reference, line))
                {
                    usages.Add(new(record, line));
                }
            }
        }

        return usages;
    }

    private static bool IsSameSecurityReportLine(
        WorkstationSecurityReference reference,
        ReportPackLineProvenanceDto line)
    {
        if (reference.SecurityId != Guid.Empty && HasText(line.SecurityMasterId))
        {
            if (Guid.TryParse(line.SecurityMasterId, out var lineSecurityId) && lineSecurityId == reference.SecurityId)
            {
                return true;
            }

            return false;
        }

        var candidateIdentifiers = new[]
            {
                reference.PrimaryIdentifier,
                reference.MatchedIdentifierValue,
                reference.DisplayName
            }
            .Where(HasText)
            .Select(static value => value!.Trim())
            .ToArray();

        return candidateIdentifiers.Any(identifier =>
            string.Equals(line.SecurityDefinitionId, identifier, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(line.SourceId, identifier, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildInstrumentPassportHref(Guid securityId)
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterInstrumentPassport, "securityId", securityId.ToString("D"));

    private static string BuildReportLineProvenanceHref()
        => UiApiRoutes.WithParam(UiApiRoutes.WorkstationFinancialRecordExplorer, "explorerId", ReportLineProvenanceExplorerId);

    private static string FormatConfidence(decimal score)
    {
        var percentage = score <= 1m ? score * 100m : score;
        return $"{percentage.ToString("0.#", CultureInfo.InvariantCulture)}%";
    }

    private static string FormatAmountCurrency(decimal amount, string currency)
        => $"{amount.ToString("N2", CultureInfo.InvariantCulture)} {currency}";

    private static IReadOnlyList<FinancialRecordExplorerScopeItemDto> BuildReportLineProvenanceScope(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts)
    {
        if (records.Count == 0)
        {
            return
            [
                new("Source", "No report-line provenance", FinancialRecordExplorerTone.Warning),
                new("Explorer", ReportLineProvenanceExplorerId, FinancialRecordExplorerTone.Info)
            ];
        }

        var latest = records
            .OrderByDescending(static record => record.UpdatedAt)
            .First();
        var reportIds = records.Select(static record => record.ReportId).ToHashSet();
        var scopedDeliveryCount = deliveryAttempts.Count(attempt => reportIds.Contains(attempt.ReportId));
        return
        [
            new("Report packs", records.Count.ToString(CultureInfo.InvariantCulture), FinancialRecordExplorerTone.Info),
            new("Latest period", latest.Period),
            new("Fund", latest.FundProfileId),
            new("Delivery attempts", scopedDeliveryCount.ToString(CultureInfo.InvariantCulture), scopedDeliveryCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning)
        ];
    }

    private static IReadOnlyList<FinancialRecordExplorerSummaryItemDto> BuildReportLineProvenanceSummary(
        IReadOnlyList<ReportPackWorkflowRecordDto> records,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        int rowCount)
    {
        var lines = records.SelectMany(static record => record.LineProvenance ?? []).ToArray();
        var reportIds = records.Select(static record => record.ReportId).ToHashSet();
        var scopedDeliveries = deliveryAttempts.Where(attempt => reportIds.Contains(attempt.ReportId)).ToArray();
        var restatedLineCount = records
            .SelectMany(static record => (record.Restatement?.ChangedLines ?? [])
                .Select(line => $"{record.ReportId:D}:{line.LineKey}"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var approvalCount = DistinctNonEmptyCount(lines.Select(static line => line.ApprovalId))
            + records.Sum(static record => record.AuditTrail.Count(static audit => audit.ToState == ReportPackWorkflowStateDto.Approved));

        return
        [
            new("Reported lines", rowCount.ToString(CultureInfo.InvariantCulture), "Published or restated report lines with retained publication evidence and provenance.", rowCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new("Instruments", lines.Count(static line => HasText(line.SecurityMasterId) || HasText(line.SecurityDefinitionId)).ToString(CultureInfo.InvariantCulture), "Lines linked to retained Security Master or instrument definition evidence."),
            new("Positions / transactions", lines.Count(static line => HasText(line.ProviderEventId) || HasText(line.SourceSessionId) || ContainsToken(line.SourceKind, "position") || ContainsToken(line.SourceKind, "transaction")).ToString(CultureInfo.InvariantCulture), "Lines linked to provider positions, transactions, source sessions, or retained source rows."),
            new("Source records", DistinctNonEmptyCount(lines.Select(static line => line.SourceId)).ToString(CultureInfo.InvariantCulture), "Distinct retained source records linked to report lines."),
            new("Reconciliations", lines.Count(static line => HasText(line.ReconciliationRunId) || HasText(line.ReconciliationCaseId)).ToString(CultureInfo.InvariantCulture), "Lines linked to reconciliation runs or break cases."),
            new("Ledger references", lines.Count(static line => HasText(line.LedgerEntryId)).ToString(CultureInfo.InvariantCulture), "Line-level ledger provenance references; these are not promoted to posted journals without durable resolution."),
            new("Approvals", approvalCount.ToString(CultureInfo.InvariantCulture), "Line-level and workflow approval evidence retained by the report-pack workflow."),
            new("Deliveries", scopedDeliveries.Length.ToString(CultureInfo.InvariantCulture), "Retained report-pack delivery attempts and packages.", scopedDeliveries.Length > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new("Audit links", lines.Count(static line => HasText(line.EvidenceId)).ToString(CultureInfo.InvariantCulture), "Lines with retained evidence ids that route to audit packets."),
            new("Restatements", restatedLineCount.ToString(CultureInfo.InvariantCulture), "Changed report lines with restatement evidence.", restatedLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
        ];
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> BuildReportLineProvenanceFilters(
        IReadOnlyList<ReportPackWorkflowRecordDto> records)
    {
        var filters = new List<FinancialRecordExplorerFilterDto>();
        filters.AddRange(records
            .Select(static record => record.State)
            .Distinct()
            .OrderBy(static state => state.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(state => new FinancialRecordExplorerFilterDto(
                $"state-{Slugify(state.ToString())}",
                "Workflow state",
                state.ToString(),
                Tone: ToneFromReportPackState(state))));
        filters.AddRange(records
            .Select(static record => record.Period)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static period => period, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(static period => new FinancialRecordExplorerFilterDto($"period-{Slugify(period)}", "Period", period)));
        filters.AddRange(records
            .Select(static record => record.FundProfileId)
            .Where(HasText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static fund => fund, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .Select(static fund => new FinancialRecordExplorerFilterDto($"fund-{Slugify(fund)}", "Fund", fund)));

        return filters.ToArray();
    }

    private static FinancialRecordExplorerRowDto BuildReportLineProvenanceRow(
        ReportPackWorkflowRecordDto record,
        ReportPackLineProvenanceDto line,
        IReadOnlyList<ReportPackDeliveryAttemptDto> deliveryAttempts,
        int index)
    {
        var reportHref = UiApiRoutes.WithParam(
            UiApiRoutes.FundReportPackById,
            "reportId",
            record.ReportId.ToString("D", CultureInfo.InvariantCulture));
        var sourceHref = BuildReportLineSourceHref(line);
        var instrumentHref = BuildReportLineInstrumentHref(line);
        var activityHref = BuildReportLineActivityHref(line, sourceHref);
        var auditHref = BuildReportLineAuditHref(line);
        var reconciliationHref = BuildReportLineReconciliationHref(line);
        var approvalHref = BuildReportLineApprovalHref(line);
        var deliveries = deliveryAttempts
            .Where(attempt => attempt.ReportId == record.ReportId)
            .OrderByDescending(static attempt => attempt.AttemptedAtUtc)
            .ThenByDescending(static attempt => attempt.AttemptNumber)
            .ToArray();
        var latestDelivery = deliveries.FirstOrDefault();
        var deliveryGraphHref = latestDelivery is null
            ? string.Empty
            : BuildReportLineDeliveryGraphHref(record.ReportId, latestDelivery.AttemptId);
        // Legacy workflow records do not carry an immutable governed-run identifier. Do not
        // reinterpret the legacy GUID as a canonical run/package id; the latest retained delivery
        // evidence remains navigable while canonical distribution history stays unbound.
        var deliveryHistoryHref = deliveryGraphHref;
        var changedLines = record.Restatement?.ChangedLines
            .Where(changedLine => IsSameLineKey(changedLine.LineKey, line.LineKey))
            .ToArray() ?? [];
        var restatementHref = BuildReportLineRestatementHref(record, changedLines);
        var recordId = $"report-line:{record.ReportId:N}:{Slugify(line.LineKey)}:{index}";
        var reportLabel = $"{record.Period} - {record.TemplateId.Name} v{record.TemplateId.Version}";
        var deliveryLabel = latestDelivery is null
            ? "No canonical distribution binding"
            : $"{latestDelivery.State} #{latestDelivery.AttemptNumber}";
        var deliveryReferenceSummary = latestDelivery is null
            ? "Create a new certified governed run before distribution."
            : ReportingDeliveryReadModelSecurity.IsSafeRetainedEvidenceHref(latestDelivery.DeliveryReference)
                ? latestDelivery.DeliveryReference.Trim()
                : "Legacy delivery reference retained without navigable credentials.";
        var restatementLabel = changedLines.Length == 0
            ? "No change"
            : $"{changedLines.Length} changed line{Plural(changedLines.Length)}";
        var status = changedLines.Length > 0
            ? "Restated"
            : latestDelivery?.State.ToString() ?? record.State.ToString();
        var tone = changedLines.Length > 0
            ? FinancialRecordExplorerTone.Warning
            : latestDelivery?.State == ReportPackDeliveryStateDto.Delivered
                ? FinancialRecordExplorerTone.Success
                : ToneFromReportPackState(record.State);

        var detail = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Report line",
            line.LineKey,
            reportLabel,
            "Published or restated report line with retained instrument, position or transaction, reconciliation, ledger provenance, report, evidence, and audit drill-through links.",
            tone,
            Fields:
            [
                new("Report value", EmptyFallback(line.ReportValue, "Not captured")),
                new("Source record", $"{line.SourceKind}:{line.SourceId}", sourceHref),
                new("Instrument", BuildReportLineInstrumentLabel(line), instrumentHref, HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Position / transaction", BuildReportLineActivityLabel(line), activityHref, HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Evidence id", line.EvidenceId),
                new("Reconciliation", BuildReportLineReconciliationLabel(line), reconciliationHref, HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning),
                new("Ledger provenance", EmptyFallback(line.LedgerEntryId, "No ledger reference"), "A report-line ledger reference is provenance only; it is not a resolved posted journal.", HasText(line.LedgerEntryId) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Default),
                new("Approval", EmptyFallback(line.ApprovalId, "No line approval"), approvalHref, HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Evidence and audit links", line.EvidenceId, auditHref, HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Delivery history", deliveryLabel, deliveryReferenceSummary, latestDelivery is null ? FinancialRecordExplorerTone.Warning : ToneFromDeliveryState(latestDelivery.State)),
                new("Restatement", restatementLabel, record.Restatement?.ReasonCode ?? "No restatement on this line", changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default),
                new("Updated", record.UpdatedAt.ToString("u", CultureInfo.InvariantCulture))
            ],
            ProofActions: BuildReportLineProofActions(
                sourceHref,
                instrumentHref,
                activityHref,
                reconciliationHref,
                approvalHref,
                deliveryHistoryHref,
                auditHref,
                deliveryGraphHref,
                restatementHref,
                changedLines.Length),
            UsedIn: BuildReportLineUsedIn(record, reportHref, deliveryHistoryHref, deliveryGraphHref, restatementHref, latestDelivery, changedLines),
            Impacts: BuildReportLineImpacts(line, sourceHref, instrumentHref, activityHref, reconciliationHref, approvalHref, deliveryHistoryHref, auditHref, deliveryGraphHref, restatementHref, changedLines.Length),
            FullRecordHref: sourceHref);

        return new FinancialRecordExplorerRowDto(
            recordId,
            "report-line",
            line.LineKey,
            $"{line.SourceKind}:{line.SourceId}",
            status,
            tone,
            Cells:
            [
                new("lineKey", line.LineKey, LinkHref: sourceHref),
                new("report", reportLabel, LinkHref: reportHref),
                new("value", EmptyFallback(line.ReportValue, "-")),
                new("source", $"{line.SourceKind}:{line.SourceId}", LinkHref: sourceHref),
                new("instrument", BuildReportLineInstrumentLabel(line), LinkHref: instrumentHref, Tone: HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("activity", BuildReportLineActivityLabel(line), LinkHref: activityHref, Tone: HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("reconciliation", BuildReportLineReconciliationLabel(line), LinkHref: reconciliationHref, Tone: HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning),
                new("journal", EmptyFallback(line.LedgerEntryId, "-"), Tone: HasText(line.LedgerEntryId) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Default),
                new("approval", EmptyFallback(line.ApprovalId, "-"), LinkHref: approvalHref, Tone: HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("delivery", deliveryLabel, LinkHref: deliveryHistoryHref, Tone: latestDelivery is null ? FinancialRecordExplorerTone.Info : ToneFromDeliveryState(latestDelivery.State)),
                new("restatement", restatementLabel, LinkHref: restatementHref, Tone: changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
            ],
            detail);
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildSecurityUsedIn(
        StrategyRunDetail detail,
        WorkstationSecurityReference reference,
        string href,
        SecurityInstrumentEnrichment enrichment)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>();
        if (detail.Portfolio?.Positions.Any(position => IsSameSecurity(position.Security, reference)) == true)
        {
            relationships.Add(new("portfolio-position", "Portfolio position", "Referenced by retained portfolio position rows.", UiApiRoutes.WorkstationPortfolio));
        }

        if (detail.Ledger?.TrialBalance.Any(line => IsSameSecurity(line.Security, reference)) == true)
        {
            relationships.Add(new("ledger-line", "Ledger trial balance", "Referenced by retained ledger trial-balance rows.", UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", detail.Summary.RunId)));
        }

        if (enrichment.ReportLineUsages.Count > 0)
        {
            relationships.Add(new(
                "report-line-provenance",
                "Report-line provenance",
                $"{enrichment.ReportLineUsages.Count.ToString(CultureInfo.InvariantCulture)} published or restated report line{Plural(enrichment.ReportLineUsages.Count)} with publication evidence reference this security.",
                BuildReportLineProvenanceHref(),
                FinancialRecordExplorerTone.Success));
        }

        if (enrichment.Operations?.ReconciliationResults.Count > 0)
        {
            relationships.Add(new(
                "asset-operations-reconciliation",
                "AssetOperations reconciliation",
                $"{enrichment.Operations.ReconciliationResults.Count.ToString(CultureInfo.InvariantCulture)} retained reconciliation result{Plural(enrichment.Operations.ReconciliationResults.Count)} reference projected or actual activity for this security.",
                BuildAssetOperationsHref(enrichment.Operations.Subject.SecurityId),
                ToneFromReconciliationResults(enrichment.Operations)));
        }

        AppendInstrumentJournalProofUsedIn(relationships, reference, enrichment);

        if (relationships.Count == 0)
        {
            relationships.Add(new("security-master", "Security Master", "Reference is retained by Security Master lookup.", href));
        }

        return relationships;
    }

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildReportLineProvenanceProofActions(int rowCount)
        =>
        [
            new(
                "open-reporting-runs",
                "Open governed reporting runs",
                "Open canonical governed reporting run records.",
                UiApiRoutes.ReportingRuns,
                rowCount > 0,
                rowCount > 0 ? string.Empty : "No report-line provenance is available.",
                rowCount > 0 ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-provenance-evidence-graph",
                "Open provenance evidence graph",
                "Open the retained evidence graph for report-line provenance.",
                BuildEvidenceSubjectRoute(
                    UiApiRoutes.WorkstationEvidenceSubjectGraph,
                    "report-line-provenance",
                    "all"),
                rowCount > 0,
                rowCount > 0 ? string.Empty : "No report-line provenance is available.",
                rowCount > 0 ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning)
        ];

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildReportLineProofActions(
        string sourceHref,
        string instrumentHref,
        string activityHref,
        string reconciliationHref,
        string approvalHref,
        string deliveryHistoryHref,
        string auditHref,
        string deliveryGraphHref,
        string restatementHref,
        int restatementLineCount)
        =>
        [
            new(
                "open-source-record",
                "Open source record",
                "Open the retained source record behind this report line.",
                sourceHref,
                HasText(sourceHref),
                HasText(sourceHref) ? string.Empty : "No source record route was retained.",
                HasText(sourceHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-instrument",
                "Open instrument",
                "Open the retained Security Master or instrument definition evidence for this report line.",
                instrumentHref,
                HasText(instrumentHref),
                HasText(instrumentHref) ? string.Empty : "No instrument identifier was retained on this line.",
                HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new(
                "open-position-transaction",
                "Open position/transaction evidence",
                "Open the retained provider position, transaction, source session, or source record feeding this report line.",
                activityHref,
                HasText(activityHref),
                HasText(activityHref) ? string.Empty : "No position, transaction, source session, or source record route was retained.",
                HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-reconciliation",
                "Open reconciliation",
                "Open the reconciliation run or break case linked to this report line.",
                reconciliationHref,
                HasText(reconciliationHref),
                HasText(reconciliationHref) ? string.Empty : "No reconciliation run or break case was retained.",
                HasText(reconciliationHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-approval-evidence",
                "Open approval evidence",
                "Open retained approval evidence for this report line.",
                approvalHref,
                HasText(approvalHref),
                HasText(approvalHref) ? string.Empty : "No approval reference was retained on this line.",
                HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new(
                "open-evidence-audit-links",
                "Open evidence and audit links",
                "Open the retained evidence packet that anchors this report line's audit trail.",
                auditHref,
                HasText(auditHref),
                HasText(auditHref) ? string.Empty : "No report-line evidence id was retained.",
                HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-delivery-history",
                "Open delivery history",
                "Open retained delivery evidence for the latest legacy report-pack attempt.",
                deliveryHistoryHref,
                HasText(deliveryHistoryHref),
                HasText(deliveryHistoryHref) ? string.Empty : "No retained legacy delivery evidence exists; create a certified governed run before distribution.",
                HasText(deliveryHistoryHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-delivery-evidence-graph",
                "Open delivery evidence graph",
                "Open the evidence graph for the latest retained report-pack delivery attempt.",
                deliveryGraphHref,
                HasText(deliveryGraphHref),
                HasText(deliveryGraphHref) ? string.Empty : "No retained delivery attempt has an evidence graph yet.",
                HasText(deliveryGraphHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
            new(
                "open-restatement-evidence",
                "Open restatement evidence",
                "Open retained evidence for restatement changes on this report line.",
                restatementHref,
                restatementLineCount > 0,
                restatementLineCount > 0 ? string.Empty : "This report line has not been restated.",
                restatementLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
        ];

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildReportLineUsedIn(
        ReportPackWorkflowRecordDto record,
        string reportHref,
        string deliveryHistoryHref,
        string deliveryGraphHref,
        string restatementHref,
        ReportPackDeliveryAttemptDto? latestDelivery,
        IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>
        {
            new(
                $"report-pack:{record.ReportId:D}",
                record.State == ReportPackWorkflowStateDto.Restated
                    ? "Restated report pack (read-only)"
                    : "Published report pack (read-only)",
                $"{record.TemplateId.Name} v{record.TemplateId.Version} for {record.Period} is retained with publication manifest {record.Publication!.ManifestId} and evidence hash {record.Publication.EvidenceHash}.",
                reportHref,
                ToneFromReportPackState(record.State))
        };

        if (latestDelivery is not null)
        {
            relationships.Add(new(
                $"report-delivery:{latestDelivery.AttemptId:D}",
                "Delivery history",
                $"{latestDelivery.Recipient} delivery attempt #{latestDelivery.AttemptNumber} is {latestDelivery.State}.",
                deliveryHistoryHref,
                ToneFromDeliveryState(latestDelivery.State)));
            relationships.Add(new(
                $"delivery-graph:{latestDelivery.AttemptId:D}",
                "Delivery evidence graph",
                "Latest retained report-pack delivery evidence packet.",
                deliveryGraphHref,
                FinancialRecordExplorerTone.Info));
        }

        if (changedLines.Count > 0)
        {
            relationships.Add(new(
                $"report-restatement:{record.ReportId:D}",
                "Restatement record",
                $"{record.Restatement?.ReasonCode ?? "Restatement"} changed this report line.",
                restatementHref,
                FinancialRecordExplorerTone.Warning));
        }

        return relationships;
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildReportLineImpacts(
        ReportPackLineProvenanceDto line,
        string sourceHref,
        string instrumentHref,
        string activityHref,
        string reconciliationHref,
        string approvalHref,
        string deliveryHistoryHref,
        string auditHref,
        string deliveryGraphHref,
        string restatementHref,
        int restatementLineCount)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>
        {
            new("source-record", "Source record", $"{line.SourceKind}:{line.SourceId} supports the report line value.", sourceHref, FinancialRecordExplorerTone.Info)
        };

        relationships.Add(new(
            "instrument",
            "Instrument",
            HasText(instrumentHref)
                ? $"Instrument identity {BuildReportLineInstrumentLabel(line)} anchors this report line."
                : "No Security Master or instrument definition identifier was retained.",
            instrumentHref,
            HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "position-transaction",
            "Position / transaction",
            HasText(activityHref)
                ? $"{BuildReportLineActivityLabel(line)} feeds reconciliation and reported-line provenance."
                : "No retained provider position, transaction, source session, or source route was retained.",
            activityHref,
            HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "reconciliation",
            "Reconciliation",
            HasText(reconciliationHref)
                ? $"Reconciliation outcome is {EmptyFallback(line.ReconciliationOutcome, "retained")}."
                : "No reconciliation run or break case was retained.",
            reconciliationHref,
            HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning));
        if (HasText(line.LedgerEntryId))
        {
            relationships.Add(new(
                "ledger-reference",
                "Ledger provenance reference",
                $"Report provenance retains ledger reference {line.LedgerEntryId}; no durable journal association was resolved by this read model.",
                string.Empty,
                FinancialRecordExplorerTone.Info));
        }
        relationships.Add(new(
            "approval",
            "Approval",
            HasText(approvalHref)
                ? $"Approval {line.ApprovalId} is retained as line evidence."
                : "No line approval reference was retained.",
            approvalHref,
            HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "delivery-history",
            "Delivery history",
            "Report-pack deliveries carry this line's publication and evidence packet history.",
            deliveryHistoryHref,
            HasText(deliveryGraphHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "evidence-audit-links",
            "Evidence and audit links",
            $"Evidence id {line.EvidenceId} anchors the retained audit packet for this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "evidence",
            "Evidence",
            HasText(auditHref)
                ? $"Evidence id {line.EvidenceId} anchors the report-line proof packet."
                : "No retained evidence packet is linked to this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "audit-event",
            "Audit event",
            HasText(auditHref)
                ? $"Audit trail retains evidence id {line.EvidenceId} for this report line."
                : "No retained audit event is linked to this report line.",
            auditHref,
            HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
        relationships.Add(new(
            "restatement-evidence",
            "Restatement evidence",
            restatementLineCount > 0
                ? "Restatement evidence exists for this report line."
                : "No restatement evidence is retained for this report line.",
            restatementHref,
            restatementLineCount > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default));

        return relationships;
    }

    private static string BuildReportLineSourceHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.FinancialRecordHref))
        {
            return line.FinancialRecordHref!.Trim();
        }

        if (HasText(line.FinancialRecordExplorerId))
        {
            var route = UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationFinancialRecordExplorer,
                "explorerId",
                line.FinancialRecordExplorerId!);
            var query = $"lineKey={Uri.EscapeDataString(line.LineKey)}&sourceId={Uri.EscapeDataString(line.SourceId)}&evidenceId={Uri.EscapeDataString(line.EvidenceId)}";
            return UiApiRoutes.WithQuery(route, query);
        }

        if (HasText(line.RunId))
        {
            return UiApiRoutes.WithParam(UiApiRoutes.RunsLedger, "runId", line.RunId!);
        }

        return BuildEvidenceSubjectRoute(
            UiApiRoutes.WorkstationEvidenceSubjectPacket,
            "report-line",
            $"{line.SourceKind}:{line.SourceId}:{line.LineKey}");
    }

    private static string BuildReportLineInstrumentHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.SecurityMasterId))
        {
            return UiApiRoutes.WithParam(
                UiApiRoutes.WorkstationSecurityMasterById,
                "securityId",
                line.SecurityMasterId!);
        }

        return HasText(line.SecurityDefinitionId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "security-definition",
                line.SecurityDefinitionId!)
            : string.Empty;
    }

    private static string BuildReportLineActivityHref(ReportPackLineProvenanceDto line, string sourceHref)
    {
        if (HasText(line.ProviderEventId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "provider-event",
                line.ProviderEventId!);
        }

        if (HasText(line.SourceSessionId))
        {
            return BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "source-session",
                line.SourceSessionId!);
        }

        return sourceHref;
    }

    private static string BuildReportLineAuditHref(ReportPackLineProvenanceDto line)
        => HasText(line.EvidenceId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "report-line",
                line.EvidenceId)
            : string.Empty;

    private static string BuildReportLineInstrumentLabel(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.SecurityMasterId))
        {
            return line.SecurityMasterId!.Trim();
        }

        return HasText(line.SecurityDefinitionId)
            ? line.SecurityDefinitionId!.Trim()
            : "No instrument retained";
    }

    private static string BuildReportLineActivityLabel(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.ProviderEventId))
        {
            return line.ProviderEventId!.Trim();
        }

        if (HasText(line.SourceSessionId))
        {
            return line.SourceSessionId!.Trim();
        }

        return $"{line.SourceKind}:{line.SourceId}";
    }

    private static string BuildReportLineReconciliationHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.ReconciliationRunId))
        {
            return UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationRunById,
                "reconciliationRunId",
                line.ReconciliationRunId!);
        }

        return HasText(line.ReconciliationCaseId)
            ? UiApiRoutes.WithParam(
                UiApiRoutes.ReconciliationBreakQueueById,
                "breakId",
                line.ReconciliationCaseId!)
            : string.Empty;
    }

    private static string BuildReportLineApprovalHref(ReportPackLineProvenanceDto line)
        => HasText(line.ApprovalId)
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "approval",
                line.ApprovalId!)
            : string.Empty;

    private static string BuildReportLineDeliveryGraphHref(Guid reportId, Guid attemptId)
        => BuildEvidenceSubjectRoute(
            UiApiRoutes.WorkstationEvidenceSubjectGraph,
            "report-pack-delivery",
            $"{reportId:D}:{attemptId:D}");

    private static string BuildReportLineRestatementHref(
        ReportPackWorkflowRecordDto record,
        IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        var route = changedLines
            .SelectMany(static line => line.EvidenceLinks ?? [])
            .Select(static link => link.Route)
            .FirstOrDefault(ReportingDeliveryReadModelSecurity.IsSafeRetainedEvidenceHref);
        if (HasText(route))
        {
            return route!.Trim();
        }

        return changedLines.Count > 0
            ? BuildEvidenceSubjectRoute(
                UiApiRoutes.WorkstationEvidenceSubjectPacket,
                "report-pack-restatement",
                $"{record.ReportId:D}:{changedLines[0].LineKey}")
            : string.Empty;
    }

    private static string BuildEvidenceSubjectRoute(string route, string subjectKind, string subjectId)
        => UiApiRoutes.WithParam(
            UiApiRoutes.WithParam(route, "subjectKind", subjectKind),
            "subjectId",
            subjectId);

    private static string BuildReportLineReconciliationLabel(ReportPackLineProvenanceDto line)
        => HasText(line.ReconciliationOutcome)
            ? line.ReconciliationOutcome!.Trim()
            : HasText(line.ReconciliationRunId)
                ? line.ReconciliationRunId!.Trim()
                : EmptyFallback(line.ReconciliationCaseId, "No reconciliation");

    private static IReadOnlyList<FinancialRecordExplorerProofActionDto> BuildExplorerProofActions(
        StrategyRunSummary run,
        string primaryHref)
        =>
        [
            new("open-source", "Open source record", "Open the source-backed projection used by this explorer.", primaryHref),
            new("open-evidence", "Evidence packet", "Open the retained evidence packet for the source run.", UiApiRoutes.WithParam(UiApiRoutes.WithParam(UiApiRoutes.WorkstationEvidenceSubjectPacket, "subjectKind", "run"), "subjectId", run.RunId), !string.IsNullOrWhiteSpace(run.AuditReference), "Run does not expose an audit reference.", !string.IsNullOrWhiteSpace(run.AuditReference) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning)
        ];

    private static FinancialRecordExplorerRecordGraphDto BuildGraph(IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = rows
            .Take(25)
            .Select(static row => new FinancialRecordExplorerGraphNodeDto(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref))
            .ToList();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();
        foreach (var row in rows.Take(25))
        {
            foreach (var relationship in row.Detail.UsedIn.Take(2))
            {
                var nodeId = $"rel:{relationship.RelationshipId}";
                if (nodes.All(node => !string.Equals(node.NodeId, nodeId, StringComparison.Ordinal)))
                {
                    nodes.Add(new FinancialRecordExplorerGraphNodeDto(nodeId, relationship.Label, "relationship", relationship.Tone, relationship.Href));
                }

                edges.Add(new FinancialRecordExplorerGraphEdgeDto(row.RecordId, nodeId, "used in", relationship.Tone));
            }
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static FinancialRecordExplorerRecordGraphDto BuildReportLineProvenanceGraph(
        IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = new List<FinancialRecordExplorerGraphNodeDto>();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();

        foreach (var row in rows.Take(12))
        {
            AddGraphNode(nodes, new(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref));

            var instrument = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument");
            var activity = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "position-transaction");
            var reconciliation = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "reconciliation");
            var report = row.Detail.UsedIn.FirstOrDefault(static relationship => relationship.RelationshipId.StartsWith("report-pack:", StringComparison.OrdinalIgnoreCase));
            var evidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence-audit-links");
            var evidencePacket = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence") ?? evidence;
            var auditEvent = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "audit-event");

            var previousNodeId = AddRelationshipGraphNode(nodes, row.RecordId, instrument, "instrument");
            var activityNodeId = AddRelationshipGraphNode(nodes, row.RecordId, activity, "position-transaction");
            AddGraphEdge(edges, previousNodeId, activityNodeId, "feeds", activity?.Tone ?? FinancialRecordExplorerTone.Info);

            var reconciliationNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reconciliation, "reconciliation");
            AddGraphEdge(edges, activityNodeId, reconciliationNodeId, "reconciles", reconciliation?.Tone ?? FinancialRecordExplorerTone.Info);

            AddGraphEdge(edges, reconciliationNodeId, row.RecordId, "reported", row.Tone);

            var reportNodeId = AddRelationshipGraphNode(nodes, row.RecordId, report, "report-pack");
            AddGraphEdge(edges, row.RecordId, reportNodeId, "included in", report?.Tone ?? row.Tone);

            var evidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidence, "evidence-audit-links");
            AddGraphEdge(edges, reportNodeId, evidenceNodeId, "retains audit", evidence?.Tone ?? FinancialRecordExplorerTone.Info);

            var evidencePacketNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidencePacket, "evidence");
            AddGraphEdge(edges, row.RecordId, evidencePacketNodeId, "retains evidence", evidencePacket?.Tone ?? FinancialRecordExplorerTone.Info);

            var auditNodeId = AddRelationshipGraphNode(nodes, row.RecordId, auditEvent, "audit-event");
            AddGraphEdge(edges, evidencePacketNodeId, auditNodeId, "audits", auditEvent?.Tone ?? FinancialRecordExplorerTone.Info);
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static FinancialRecordExplorerRecordGraphDto BuildSecurityInstrumentGraph(
        IReadOnlyList<FinancialRecordExplorerRowDto> rows)
    {
        if (rows.Count == 0)
        {
            return new FinancialRecordExplorerRecordGraphDto([], []);
        }

        var nodes = new List<FinancialRecordExplorerGraphNodeDto>();
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();

        foreach (var row in rows.Take(12))
        {
            AddGraphNode(nodes, new(row.RecordId, row.Label, row.RecordType, row.Tone, row.Detail.FullRecordHref));

            var positionTransaction = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "position-transaction");
            var passport = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "instrument-passport");
            var operations = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "asset-operations-readiness");
            var terms = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "terms-obligations");
            var reconciliation = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "reconciliation");
            var journal = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "journal");
            var reportLine = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "report-line");
            var evidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence");
            var audit = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "audit-event");
            AppendInstrumentJournalProofGraph(nodes, edges, row, journal);

            var positionTransactionNodeId = AddRelationshipGraphNode(nodes, row.RecordId, positionTransaction, "position-transaction");
            AddGraphEdge(edges, row.RecordId, positionTransactionNodeId, "referenced by", positionTransaction?.Tone ?? FinancialRecordExplorerTone.Info);

            var passportNodeId = AddRelationshipGraphNode(nodes, row.RecordId, passport, "instrument-passport");
            AddGraphEdge(edges, row.RecordId, passportNodeId, "trust", passport?.Tone ?? row.Tone);

            var operationsNodeId = AddRelationshipGraphNode(nodes, row.RecordId, operations, "asset-operations-readiness");
            AddGraphEdge(edges, row.RecordId, operationsNodeId, "operations", operations?.Tone ?? FinancialRecordExplorerTone.Info);

            var termsNodeId = AddRelationshipGraphNode(nodes, row.RecordId, terms, "terms-obligations");
            AddGraphEdge(edges, operationsNodeId, termsNodeId, "terms", terms?.Tone ?? FinancialRecordExplorerTone.Info);

            var reconciliationNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reconciliation, "reconciliation");
            AddGraphEdge(edges, operationsNodeId, reconciliationNodeId, "reconciles", reconciliation?.Tone ?? FinancialRecordExplorerTone.Info);

            string? journalNodeId = null;
            if (journal is not null)
            {
                journalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, journal, "journal");
                AddGraphEdge(edges, reconciliationNodeId, journalNodeId, "posts", journal.Tone);
            }

            string? reportLineNodeId = null;
            if (reportLine is not null)
            {
                reportLineNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reportLine, "report-line");
                AddGraphEdge(
                    edges,
                    journalNodeId ?? reconciliationNodeId,
                    reportLineNodeId,
                    "reported",
                    reportLine.Tone);
            }

            var evidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidence, "evidence");
            AddGraphEdge(edges, reportLineNodeId ?? reconciliationNodeId, evidenceNodeId, "retains evidence", evidence?.Tone ?? FinancialRecordExplorerTone.Info);

            var auditNodeId = AddRelationshipGraphNode(nodes, row.RecordId, audit, "audit-event");
            AddGraphEdge(edges, evidenceNodeId, auditNodeId, "audits", audit?.Tone ?? FinancialRecordExplorerTone.Info);
        }

        return new FinancialRecordExplorerRecordGraphDto(nodes, edges);
    }

    private static string AddRelationshipGraphNode(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        string rowRecordId,
        FinancialRecordExplorerRelationshipDto? relationship,
        string fallbackNodeType)
    {
        var nodeId = relationship is null
            ? $"rel:{rowRecordId}:{fallbackNodeType}"
            : $"rel:{rowRecordId}:{relationship.RelationshipId}";
        AddGraphNode(nodes, new(
            nodeId,
            relationship?.Label ?? fallbackNodeType,
            relationship?.RelationshipId ?? fallbackNodeType,
            relationship?.Tone ?? FinancialRecordExplorerTone.Warning,
            relationship?.Href ?? string.Empty));
        return nodeId;
    }

    private static void AddGraphNode(
        List<FinancialRecordExplorerGraphNodeDto> nodes,
        FinancialRecordExplorerGraphNodeDto node)
    {
        if (nodes.Any(existing => string.Equals(existing.NodeId, node.NodeId, StringComparison.Ordinal)))
        {
            return;
        }

        nodes.Add(node);
    }

    private static void AddGraphEdge(
        List<FinancialRecordExplorerGraphEdgeDto> edges,
        string sourceNodeId,
        string targetNodeId,
        string label,
        FinancialRecordExplorerTone tone)
    {
        if (string.IsNullOrWhiteSpace(sourceNodeId) ||
            string.IsNullOrWhiteSpace(targetNodeId) ||
            string.Equals(sourceNodeId, targetNodeId, StringComparison.Ordinal))
        {
            return;
        }

        edges.Add(new(sourceNodeId, targetNodeId, label, tone));
    }

    private static IReadOnlyList<WorkstationSecurityReference> CollectSecurityReferences(StrategyRunDetail detail)
    {
        var portfolioReferences = detail.Portfolio?.Positions.Select(static position => position.Security)
            ?? Enumerable.Empty<WorkstationSecurityReference?>();
        var ledgerReferences = detail.Ledger?.TrialBalance.Select(static line => line.Security)
            ?? Enumerable.Empty<WorkstationSecurityReference?>();
        var references = portfolioReferences
            .Concat(ledgerReferences)
            .Where(static reference => reference is not null)
            .Select(static reference => reference!)
            .GroupBy(static reference => reference.SecurityId == Guid.Empty
                    ? $"{reference.DisplayName}:{reference.PrimaryIdentifier}:{reference.AssetClass}"
                    : reference.SecurityId.ToString("D"),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static reference => reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return references;
    }

    private static bool IsSameSecurity(WorkstationSecurityReference? left, WorkstationSecurityReference right)
    {
        if (left is null)
        {
            return false;
        }

        if (left.SecurityId != Guid.Empty && right.SecurityId != Guid.Empty)
        {
            return left.SecurityId == right.SecurityId;
        }

        return string.Equals(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.PrimaryIdentifier, right.PrimaryIdentifier, StringComparison.OrdinalIgnoreCase);
    }

    private static int DistinctNonEmptyCount(IEnumerable<string?> values)
        => values
            .Where(HasText)
            .Select(static value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    private static bool IsSameLineKey(string? left, string? right)
        => HasText(left) && HasText(right) && string.Equals(left!.Trim(), right!.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasText(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static bool ContainsToken(string? value, string token)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string EmptyFallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string Plural(int count)
        => count == 1 ? string.Empty : "s";

    private static FinancialRecordExplorerTone ToneFromRunStatus(StrategyRunStatus status)
        => status switch
        {
            StrategyRunStatus.Completed => FinancialRecordExplorerTone.Success,
            StrategyRunStatus.Failed or StrategyRunStatus.Cancelled => FinancialRecordExplorerTone.Danger,
            StrategyRunStatus.Running or StrategyRunStatus.Paused => FinancialRecordExplorerTone.Warning,
            _ => FinancialRecordExplorerTone.Default
        };

    private static FinancialRecordExplorerTone ToneFromBalance(decimal value)
        => value > 0 ? FinancialRecordExplorerTone.Success :
            value < 0 ? FinancialRecordExplorerTone.Warning :
            FinancialRecordExplorerTone.Default;

    private static FinancialRecordExplorerTone ToneFromCoverage(WorkstationSecurityCoverageStatus? status)
        => status switch
        {
            WorkstationSecurityCoverageStatus.Resolved => FinancialRecordExplorerTone.Success,
            WorkstationSecurityCoverageStatus.Partial => FinancialRecordExplorerTone.Warning,
            WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Warning
        };

    private static FinancialRecordExplorerTone ToneFromTrustPosture(SecurityMasterTrustTone tone)
        => tone switch
        {
            SecurityMasterTrustTone.Trusted => FinancialRecordExplorerTone.Success,
            SecurityMasterTrustTone.Review => FinancialRecordExplorerTone.Warning,
            SecurityMasterTrustTone.Blocked => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Default
        };

    private static FinancialRecordExplorerTone ToneFromIdentifierConfidence(InstrumentPassportDto passport)
    {
        var confidence = SelectBestProviderConfidence(passport);
        if (confidence is not null)
        {
            return ToneFromConfidence(confidence.ConfidenceScore);
        }

        return passport.IdentifierSummary.HasPrimaryIdentifier
            ? FinancialRecordExplorerTone.Info
            : FinancialRecordExplorerTone.Warning;
    }

    private static FinancialRecordExplorerTone ToneFromConfidence(decimal score)
    {
        var percentage = score <= 1m ? score * 100m : score;
        return percentage >= 80m ? FinancialRecordExplorerTone.Success :
            percentage >= 50m ? FinancialRecordExplorerTone.Warning :
            FinancialRecordExplorerTone.Danger;
    }

    private static FinancialRecordExplorerTone ToneFromAssetOperationsReadiness(AssetOperationsReadinessDto? readiness)
    {
        if (readiness is null)
        {
            return FinancialRecordExplorerTone.Default;
        }

        if (readiness.MissingCapabilities.Count == 0 &&
            readiness.Warnings.Count == 0 &&
            ContainsStatusToken(readiness.Status, "ready"))
        {
            return FinancialRecordExplorerTone.Success;
        }

        return ToneFromStatusText(readiness.Status, FinancialRecordExplorerTone.Warning);
    }

    private static FinancialRecordExplorerTone ToneFromDirectLending(SecurityInstrumentEnrichment enrichment)
    {
        if (enrichment.DirectLendingHealth.Count == 0)
        {
            return FinancialRecordExplorerTone.Default;
        }

        if (enrichment.DirectLendingHealth.Any(static health => IsNegativeStatus(health.HealthStatus)))
        {
            return FinancialRecordExplorerTone.Warning;
        }

        return enrichment.DirectLendingHealth.All(static health => ContainsStatusToken(health.HealthStatus, "ready"))
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Info;
    }

    private static FinancialRecordExplorerTone ToneFromCashFlows(AssetOperationsDetailDto operations)
        => operations.ProjectedCashFlows.Count == 0
            ? FinancialRecordExplorerTone.Warning
            : operations.ProjectedCashFlows.Any(static flow => IsNegativeStatus(flow.Status))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromLedgerProjections(AssetOperationsDetailDto operations)
        => operations.LedgerProjections.Count == 0
            ? FinancialRecordExplorerTone.Warning
            : operations.LedgerProjections.Any(static projection => IsNegativeStatus(projection.Status))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromReconciliationResults(AssetOperationsDetailDto operations)
        => operations.ReconciliationResults.Count == 0
            ? FinancialRecordExplorerTone.Default
            : operations.ReconciliationResults.Any(static result => IsNegativeStatus(result.MatchStatus))
                ? FinancialRecordExplorerTone.Warning
                : FinancialRecordExplorerTone.Success;

    private static FinancialRecordExplorerTone ToneFromStatusText(string? status, FinancialRecordExplorerTone fallback)
    {
        if (!HasText(status))
        {
            return fallback;
        }

        return IsNegativeStatus(status) ? FinancialRecordExplorerTone.Warning :
            ContainsStatusToken(status, "ready") ||
            ContainsStatusToken(status, "matched") ||
            ContainsStatusToken(status, "complete") ||
            ContainsStatusToken(status, "posted") ||
            ContainsStatusToken(status, "projected") ||
            ContainsStatusToken(status, "generated")
                ? FinancialRecordExplorerTone.Success
                : fallback;
    }

    private static bool IsNegativeStatus(string? status)
        => ContainsStatusToken(status, "block") ||
           ContainsStatusToken(status, "missing") ||
           ContainsStatusToken(status, "fail") ||
           ContainsStatusToken(status, "break") ||
           ContainsStatusToken(status, "mismatch") ||
           ContainsStatusToken(status, "error");

    private static bool ContainsStatusToken(string? status, string token)
        => !string.IsNullOrWhiteSpace(status) &&
           status.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static FinancialRecordExplorerTone ToneFromReportPackState(ReportPackWorkflowStateDto state)
        => state switch
        {
            ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Archived => FinancialRecordExplorerTone.Success,
            ReportPackWorkflowStateDto.Restated => FinancialRecordExplorerTone.Warning,
            ReportPackWorkflowStateDto.Rejected => FinancialRecordExplorerTone.Danger,
            ReportPackWorkflowStateDto.Approved or ReportPackWorkflowStateDto.PendingApproval => FinancialRecordExplorerTone.Info,
            ReportPackWorkflowStateDto.InReview or ReportPackWorkflowStateDto.Validated => FinancialRecordExplorerTone.Warning,
            _ => FinancialRecordExplorerTone.Default
        };

    private static bool IsAuthoritativelyReported(ReportPackWorkflowRecordDto record)
    {
        if (record.State is not (ReportPackWorkflowStateDto.Published or ReportPackWorkflowStateDto.Restated) ||
            record.Publication is not { } publication ||
            record.LineProvenance is not { Count: > 0 })
        {
            return false;
        }

        var publicationIsComplete =
            HasText(publication.ManifestId) &&
            HasText(publication.RetainedManifestPath) &&
            HasText(publication.EvidenceHash) &&
            HasText(publication.SignedOffBy) &&
            HasText(publication.SignedOffRole) &&
            HasText(publication.SignOffContext) &&
            publication.SignedOffAt != default &&
            Meridian.Contracts.Operations.OperationsOriginGuard.IsHumanOperator(publication.ActionOrigin) &&
            HasCompleteReportEvidence(publication.EvidenceLinks) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Approved &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.AuditTrail.Any(static audit =>
                audit.ToState == ReportPackWorkflowStateDto.Published &&
                HasText(audit.Actor) &&
                audit.At != default) &&
            record.LineProvenance.All(line => EnumerateReportLineEvidencePointers(line)
                .All(pointer => publication.EvidenceLinks.Any(link =>
                    string.Equals(link.EvidenceId, pointer, StringComparison.OrdinalIgnoreCase))));
        if (!publicationIsComplete)
        {
            return false;
        }

        if (record.State == ReportPackWorkflowStateDto.Published)
        {
            return true;
        }

        return record.Restatement is { } restatement &&
               HasText(restatement.ReasonCode) &&
               HasText(restatement.Approver) &&
               restatement.PriorVersionReportId != Guid.Empty &&
               restatement.ChangedLines.Count > 0 &&
               restatement.ChangedLines.All(static line =>
                   HasText(line.LineKey) &&
                   HasText(line.PreviousValue) &&
                   HasText(line.CurrentValue) &&
                   HasCompleteReportEvidence(line.EvidenceLinks)) &&
               HasCompleteReportEvidence(restatement.EvidenceLinks) &&
               record.AuditTrail.Any(static audit =>
                   audit.ToState == ReportPackWorkflowStateDto.Restated &&
                   HasText(audit.Actor) &&
                   audit.At != default);
    }

    private static bool HasCompleteReportEvidence(IReadOnlyList<ReportPackEvidenceLinkDto>? evidenceLinks)
        => evidenceLinks is { Count: > 0 } &&
           evidenceLinks.All(static evidence =>
               HasText(evidence.EvidenceId) &&
               HasText(evidence.Label) &&
               HasText(evidence.Source));

    private static IEnumerable<string> EnumerateReportLineEvidencePointers(ReportPackLineProvenanceDto line)
    {
        yield return line.EvidenceId;
        yield return line.SourceId;

        foreach (var pointer in new[]
                 {
                     line.RunId,
                     line.SourceSessionId,
                     line.LedgerEntryId,
                     line.ReconciliationCaseId,
                     line.ReconciliationRunId,
                     line.ProviderEventId,
                     line.SecurityMasterId,
                     line.SecurityDefinitionId,
                     line.ApprovalId
                 })
        {
            if (HasText(pointer))
            {
                yield return pointer!;
            }
        }
    }

    private static FinancialRecordExplorerTone ToneFromDeliveryState(ReportPackDeliveryStateDto state)
        => state switch
        {
            ReportPackDeliveryStateDto.Delivered => FinancialRecordExplorerTone.Success,
            ReportPackDeliveryStateDto.Failed => FinancialRecordExplorerTone.Danger,
            _ => FinancialRecordExplorerTone.Warning
        };

    private static FinancialRecordExplorerTone ToneFromReconciliationOutcome(string? outcome)
    {
        if (!HasText(outcome))
        {
            return FinancialRecordExplorerTone.Warning;
        }

        var value = outcome!.Trim();
        return value.Contains("match", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("cleared", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("resolved", StringComparison.OrdinalIgnoreCase)
            ? FinancialRecordExplorerTone.Success
            : FinancialRecordExplorerTone.Warning;
    }

    private static string FormatCurrency(decimal value)
        => value.ToString("C2", CultureInfo.CurrentCulture);

    private static string NormalizeExplorerId(string explorerId)
        => explorerId.Trim().ToLowerInvariant();

    private static string Slugify(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "view" : slug;
    }

    private sealed record SecurityInstrumentEnrichment(
        InstrumentPassportDto? Passport,
        AssetOperationsDetailDto? Operations,
        AssetOperationsReadinessDto? Readiness,
        IReadOnlyList<DirectLendingOperationsLoanHealthDto> DirectLendingHealth,
        IReadOnlyList<SecurityReportLineUsage> ReportLineUsages,
        IReadOnlyList<InstrumentJournalProof> InstrumentJournalProofs);

    private sealed record SecurityReportLineUsage(
        ReportPackWorkflowRecordDto Record,
        ReportPackLineProvenanceDto Line);
}
