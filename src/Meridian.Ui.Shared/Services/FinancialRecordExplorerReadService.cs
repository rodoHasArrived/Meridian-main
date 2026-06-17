using System.Globalization;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Ui.Shared.Services;

public sealed class FinancialRecordExplorerReadService
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

    private readonly IServiceProvider _services;
    private readonly IFinancialRecordExplorerSavedViewStore _savedViewStore;

    public FinancialRecordExplorerReadService(
        IServiceProvider services,
        IFinancialRecordExplorerSavedViewStore savedViewStore)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _savedViewStore = savedViewStore ?? throw new ArgumentNullException(nameof(savedViewStore));
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
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        var normalized = NormalizeExplorerId(explorerId);
        return normalized switch
        {
            LedgerExplorerId => await BuildLedgerExplorerAsync(tenantId, ct).ConfigureAwait(false),
            PortfolioExplorerId => await BuildPortfolioExplorerAsync(tenantId, ct).ConfigureAwait(false),
            SecurityInstrumentExplorerId => await BuildSecurityInstrumentExplorerAsync(tenantId, ct).ConfigureAwait(false),
            ReportLineProvenanceExplorerId => await BuildReportLineProvenanceExplorerAsync(tenantId, ct).ConfigureAwait(false),
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

        var label = request.Label.Trim();
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new InvalidOperationException("Saved view label is required.");
        }

        if (request.Filters.Count == 0 && string.IsNullOrWhiteSpace(request.SearchText))
        {
            throw new InvalidOperationException("Saved view requires a search term or at least one filter.");
        }

        var view = new FinancialRecordExplorerSavedViewDto(
            ViewId: $"operator-{Slugify(label)}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            Label: label,
            Description: request.Description.Trim(),
            IsSystem: false,
            IsActive: false,
            Filters: request.Filters,
            SearchText: request.SearchText.Trim());

        return await _savedViewStore.SaveAsync(tenantId, normalized, view, ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildLedgerExplorerAsync(string tenantId, CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
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
                new("source", "Source", Width: 140)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
            tenantId,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildPortfolioExplorerAsync(string tenantId, CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
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
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildSecurityInstrumentExplorerAsync(string tenantId, CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
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
        var rows = references
            .Select((reference, index) => BuildSecurityRow(run, source, reference, index))
            .ToArray();

        return await CreateExplorerAsync(
            SecurityInstrumentExplorerId,
            "Security & Instrument Explorer",
            "Explore Security Master references used by retained accounting and portfolio records.",
            $"Source-backed Security Master references from run {run.RunId}.",
            BuildScope(run, source.Portfolio?.AsOf ?? source.Ledger?.AsOf ?? run.LastUpdatedAt, "Security Master"),
            [
                new("Securities", rows.Length.ToString(CultureInfo.InvariantCulture), "Distinct resolved or referenced instruments."),
                new("Resolved", references.Count(reference => reference.CoverageStatus == WorkstationSecurityCoverageStatus.Resolved).ToString(CultureInfo.InvariantCulture), "Security Master references with resolved coverage.", FinancialRecordExplorerTone.Success),
                new("Missing", references.Count(reference => reference.CoverageStatus is WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable).ToString(CultureInfo.InvariantCulture), "References requiring operator follow-up.", FinancialRecordExplorerTone.Warning)
            ],
            BuildSystemViews(SecurityInstrumentExplorerId, "Security references", "Instrument references used by portfolio and ledger records."),
            BuildSecurityFilters(references),
            [
                new("security", "Security", Width: 220),
                new("assetClass", "Asset Class", Width: 120),
                new("currency", "Currency", Width: 90),
                new("status", "Status", Width: 110),
                new("coverage", "Coverage", Width: 120),
                new("identifier", "Identifier", Width: 160),
                new("source", "Source", Width: 120)
            ],
            rows,
            BuildExplorerProofActions(run, UiApiRoutes.WorkstationSecurityMasterSearch),
            tenantId,
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildReportLineProvenanceExplorerAsync(string tenantId, CancellationToken ct)
    {
        var workflowService = _services.GetService<ReportPackWorkflowService>();
        if (workflowService is null)
        {
            return CreateBlockedExplorer(
                ReportLineProvenanceExplorerId,
                "Report-Line Provenance Explorer",
                "Drill from governed report lines into retained source records, reconciliations, journals, approvals, delivery history, and restatement evidence.",
                "Report pack workflow service is not registered.");
        }

        var systemViews = BuildSystemViews(
            ReportLineProvenanceExplorerId,
            "Report lines",
            "Governed report lines with retained source provenance.");
        var savedViews = await LoadSavedViewsAsync(tenantId, ReportLineProvenanceExplorerId, systemViews, ct).ConfigureAwait(false);
        var deliveryService = _services.GetService<ReportPackDeliveryService>();
        return BuildReportLineProvenanceExplorer(
            workflowService.ListRecords(200),
            deliveryService?.ListAttempts(500),
            savedViews);
    }

    public static FinancialRecordExplorerDto BuildReportLineProvenanceExplorer(
        IReadOnlyList<ReportPackWorkflowRecordDto> workflowRecords,
        IReadOnlyList<ReportPackDeliveryAttemptDto>? deliveryAttempts = null,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto>? savedViews = null)
    {
        ArgumentNullException.ThrowIfNull(workflowRecords);

        var records = workflowRecords
            .Where(static record => record.LineProvenance is { Count: > 0 })
            .OrderByDescending(static record => record.UpdatedAt)
            .ThenBy(static record => record.ReportId)
            .ToArray();
        var attempts = deliveryAttempts ?? [];
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
            ? "No governed report-line provenance is available from the report-pack workflow service."
            : $"{rows.Length} governed report line(s) across {records.Length} report pack(s) retain instrument, position or transaction, reconciliation, journal, report, evidence, and audit drill-through links.";

        return new FinancialRecordExplorerDto(
            ReportLineProvenanceExplorerId,
            "Report-Line Provenance Explorer",
            "Drill from governed report lines into retained instruments, positions or transactions, reconciliations, journals, reports, evidence, and audit links.",
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
                new("journal", "Journal", Width: 120),
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
        CancellationToken ct)
    {
        var savedViews = await LoadSavedViewsAsync(tenantId, explorerId, systemViews, ct).ConfigureAwait(false);
        return new FinancialRecordExplorerDto(
            explorerId,
            title,
            description,
            sourceState,
            IsBlocked: false,
            BlockedReason: string.Empty,
            scopeItems,
            savedViews,
            summaryItems,
            filters,
            columns,
            rows,
            rows.FirstOrDefault()?.Detail,
            proofActions,
            BuildGraph(rows));
    }

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
                SearchText: string.Empty)
        ];

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
        =>
        [
            new("run", "Run", run.RunId, Tone: FinancialRecordExplorerTone.Info),
            new("ledger", "Ledger", ledger.LedgerReference, Tone: FinancialRecordExplorerTone.Info)
        ];

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
                new("Financial Account", line.FinancialAccountId ?? "None")
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
                new("source", ledger.LedgerReference)
            ],
            detail);
    }

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
        int index)
    {
        var recordId = reference.SecurityId == Guid.Empty
            ? $"security:{run.RunId}:{index}"
            : $"security:{reference.SecurityId:D}";
        var href = reference.SecurityId == Guid.Empty
            ? UiApiRoutes.WorkstationSecurityMasterSearch
            : UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", reference.SecurityId.ToString("D"));
        var usedIn = BuildSecurityUsedIn(detail, reference, href);
        var selected = new FinancialRecordExplorerSelectedRecordDto(
            recordId,
            "Security instrument",
            reference.DisplayName,
            $"{reference.AssetClass} - {reference.Currency}",
            reference.ResolutionReason ?? "Security reference retained by source-backed portfolio or ledger records.",
            ToneFromCoverage(reference.CoverageStatus),
            Fields:
            [
                new("Asset Class", reference.AssetClass),
                new("Sub Type", reference.SubType ?? "None"),
                new("Currency", reference.Currency),
                new("Status", reference.Status.ToString()),
                new("Primary Identifier", reference.PrimaryIdentifier ?? "None"),
                new("Matched Provider", reference.MatchedProvider ?? "None"),
                new("Coverage", reference.CoverageStatus.ToString(), Tone: ToneFromCoverage(reference.CoverageStatus))
            ],
            ProofActions:
            [
                new("open-security-master", "Open Security Master", "Open the retained Security Master record.", href, reference.SecurityId != Guid.Empty, "Security reference is not resolved.", ToneFromCoverage(reference.CoverageStatus))
            ],
            UsedIn: usedIn,
            Impacts:
            [
                new("instrument-coverage", "Instrument coverage", $"Coverage state is {reference.CoverageStatus}.", href, ToneFromCoverage(reference.CoverageStatus))
            ],
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
                new("identifier", reference.PrimaryIdentifier ?? reference.MatchedIdentifierValue ?? "-"),
                new("source", reference.LookupSource ?? "Security Master")
            ],
            selected);
    }

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
            new("Report lines", rowCount.ToString(CultureInfo.InvariantCulture), "Governed report lines with retained provenance.", rowCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
            new("Instruments", lines.Count(static line => HasText(line.SecurityMasterId) || HasText(line.SecurityDefinitionId)).ToString(CultureInfo.InvariantCulture), "Lines linked to retained Security Master or instrument definition evidence."),
            new("Positions / transactions", lines.Count(static line => HasText(line.ProviderEventId) || HasText(line.SourceSessionId) || ContainsToken(line.SourceKind, "position") || ContainsToken(line.SourceKind, "transaction")).ToString(CultureInfo.InvariantCulture), "Lines linked to provider positions, transactions, source sessions, or retained source rows."),
            new("Source records", DistinctNonEmptyCount(lines.Select(static line => line.SourceId)).ToString(CultureInfo.InvariantCulture), "Distinct retained source records linked to report lines."),
            new("Reconciliations", lines.Count(static line => HasText(line.ReconciliationRunId) || HasText(line.ReconciliationCaseId)).ToString(CultureInfo.InvariantCulture), "Lines linked to reconciliation runs or break cases."),
            new("Journals", lines.Count(static line => HasText(line.LedgerEntryId) || HasText(line.RunId)).ToString(CultureInfo.InvariantCulture), "Lines with ledger or journal drill-through evidence."),
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
            UiApiRoutes.ReportingPackWorkflowDeliveries,
            "reportId",
            record.ReportId.ToString("D"));
        var sourceHref = BuildReportLineSourceHref(line);
        var instrumentHref = BuildReportLineInstrumentHref(line);
        var activityHref = BuildReportLineActivityHref(line, sourceHref);
        var auditHref = BuildReportLineAuditHref(line);
        var reconciliationHref = BuildReportLineReconciliationHref(line);
        var journalHref = BuildReportLineJournalHref(line);
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
        var changedLines = record.Restatement?.ChangedLines
            .Where(changedLine => IsSameLineKey(changedLine.LineKey, line.LineKey))
            .ToArray() ?? [];
        var restatementHref = BuildReportLineRestatementHref(record, changedLines);
        var recordId = $"report-line:{record.ReportId:N}:{Slugify(line.LineKey)}:{index}";
        var reportLabel = $"{record.Period} - {record.TemplateId.Name} v{record.TemplateId.Version}";
        var deliveryLabel = latestDelivery is null
            ? "Not delivered"
            : $"{latestDelivery.State} #{latestDelivery.AttemptNumber}";
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
            "Governed report line with retained instrument, position or transaction, reconciliation, journal, report, evidence, and audit drill-through links.",
            tone,
            Fields:
            [
                new("Report value", EmptyFallback(line.ReportValue, "Not captured")),
                new("Source record", $"{line.SourceKind}:{line.SourceId}", sourceHref),
                new("Instrument", BuildReportLineInstrumentLabel(line), instrumentHref, HasText(instrumentHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Position / transaction", BuildReportLineActivityLabel(line), activityHref, HasText(activityHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Evidence id", line.EvidenceId),
                new("Reconciliation", BuildReportLineReconciliationLabel(line), reconciliationHref, HasText(reconciliationHref) ? ToneFromReconciliationOutcome(line.ReconciliationOutcome) : FinancialRecordExplorerTone.Warning),
                new("Journal", EmptyFallback(line.LedgerEntryId, "No journal link"), journalHref, HasText(journalHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Approval", EmptyFallback(line.ApprovalId, "No line approval"), approvalHref, HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("Evidence and audit links", line.EvidenceId, auditHref, HasText(auditHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("Delivery history", deliveryLabel, latestDelivery?.DeliveryReference ?? "No retained delivery attempt", latestDelivery is null ? FinancialRecordExplorerTone.Warning : ToneFromDeliveryState(latestDelivery.State)),
                new("Restatement", restatementLabel, record.Restatement?.ReasonCode ?? "No restatement on this line", changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default),
                new("Updated", record.UpdatedAt.ToString("u", CultureInfo.InvariantCulture))
            ],
            ProofActions: BuildReportLineProofActions(
                sourceHref,
                instrumentHref,
                activityHref,
                reconciliationHref,
                journalHref,
                approvalHref,
                reportHref,
                auditHref,
                deliveryGraphHref,
                restatementHref,
                deliveries.Length,
                changedLines.Length),
            UsedIn: BuildReportLineUsedIn(record, reportHref, deliveryGraphHref, restatementHref, latestDelivery, changedLines),
            Impacts: BuildReportLineImpacts(line, sourceHref, instrumentHref, activityHref, reconciliationHref, journalHref, approvalHref, reportHref, auditHref, deliveryGraphHref, restatementHref, changedLines.Length),
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
                new("journal", EmptyFallback(line.LedgerEntryId, "-"), LinkHref: journalHref, Tone: HasText(journalHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
                new("approval", EmptyFallback(line.ApprovalId, "-"), LinkHref: approvalHref, Tone: HasText(approvalHref) ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
                new("delivery", deliveryLabel, LinkHref: reportHref, Tone: latestDelivery is null ? FinancialRecordExplorerTone.Warning : ToneFromDeliveryState(latestDelivery.State)),
                new("restatement", restatementLabel, LinkHref: restatementHref, Tone: changedLines.Length > 0 ? FinancialRecordExplorerTone.Warning : FinancialRecordExplorerTone.Default)
            ],
            detail);
    }

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildSecurityUsedIn(
        StrategyRunDetail detail,
        WorkstationSecurityReference reference,
        string href)
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
                "open-report-pack-workflows",
                "Open report-pack workflows",
                "Open governed report-pack workflow records.",
                UiApiRoutes.ReportingPackWorkflows,
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
        string journalHref,
        string approvalHref,
        string deliveryHistoryHref,
        string auditHref,
        string deliveryGraphHref,
        string restatementHref,
        int deliveryCount,
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
                "open-journal",
                "Open journal",
                "Open the ledger journal or manual journal workbench evidence for this report line.",
                journalHref,
                HasText(journalHref),
                HasText(journalHref) ? string.Empty : "No journal or ledger entry was retained.",
                HasText(journalHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning),
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
                "Open retained report-pack delivery history for this line's report.",
                deliveryHistoryHref,
                deliveryCount > 0,
                deliveryCount > 0 ? string.Empty : "No retained delivery attempts exist for this report pack.",
                deliveryCount > 0 ? FinancialRecordExplorerTone.Success : FinancialRecordExplorerTone.Warning),
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
        string deliveryGraphHref,
        string restatementHref,
        ReportPackDeliveryAttemptDto? latestDelivery,
        IReadOnlyList<ReportPackChangedLineDto> changedLines)
    {
        var relationships = new List<FinancialRecordExplorerRelationshipDto>
        {
            new(
                $"report-pack:{record.ReportId:D}",
                record.Publication is null ? "Report pack workflow" : "Published report pack",
                $"{record.TemplateId.Name} v{record.TemplateId.Version} for {record.Period} is {record.State}.",
                reportHref,
                ToneFromReportPackState(record.State))
        };

        if (latestDelivery is not null)
        {
            relationships.Add(new(
                $"report-delivery:{latestDelivery.AttemptId:D}",
                "Delivery history",
                $"{latestDelivery.Recipient} delivery attempt #{latestDelivery.AttemptNumber} is {latestDelivery.State}.",
                reportHref,
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
        string journalHref,
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
                ? $"{BuildReportLineActivityLabel(line)} feeds reconciliation before journal support."
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
        relationships.Add(new(
            "journal",
            "Journal",
            HasText(journalHref)
                ? $"Ledger entry {EmptyFallback(line.LedgerEntryId, line.RunId ?? "retained")} supports this line."
                : "No ledger journal route was retained.",
            journalHref,
            HasText(journalHref) ? FinancialRecordExplorerTone.Info : FinancialRecordExplorerTone.Warning));
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

        if (HasText(line.RunId) && HasText(line.LedgerEntryId))
        {
            return UiApiRoutes.WithQuery(
                UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerJournal, "runId", line.RunId!),
                $"ledgerEntryId={Uri.EscapeDataString(line.LedgerEntryId!)}");
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

    private static string BuildReportLineJournalHref(ReportPackLineProvenanceDto line)
    {
        if (HasText(line.RunId))
        {
            var route = UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerJournal, "runId", line.RunId!);
            return HasText(line.LedgerEntryId)
                ? UiApiRoutes.WithQuery(route, $"ledgerEntryId={Uri.EscapeDataString(line.LedgerEntryId!)}")
                : route;
        }

        return HasText(line.LedgerEntryId)
            ? UiApiRoutes.WithQuery(
                UiApiRoutes.LedgerManualJournalEntryWorkbench,
                $"ledgerEntryId={Uri.EscapeDataString(line.LedgerEntryId!)}")
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
            .FirstOrDefault(HasText);
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
            var journal = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "journal");
            var report = row.Detail.UsedIn.FirstOrDefault(static relationship => relationship.RelationshipId.StartsWith("report-pack:", StringComparison.OrdinalIgnoreCase));
            var evidence = row.Detail.Impacts.FirstOrDefault(static relationship => relationship.RelationshipId == "evidence-audit-links");

            var previousNodeId = AddRelationshipGraphNode(nodes, row.RecordId, instrument, "instrument");
            var activityNodeId = AddRelationshipGraphNode(nodes, row.RecordId, activity, "position-transaction");
            AddGraphEdge(edges, previousNodeId, activityNodeId, "feeds", activity?.Tone ?? FinancialRecordExplorerTone.Info);

            var reconciliationNodeId = AddRelationshipGraphNode(nodes, row.RecordId, reconciliation, "reconciliation");
            AddGraphEdge(edges, activityNodeId, reconciliationNodeId, "reconciles", reconciliation?.Tone ?? FinancialRecordExplorerTone.Info);

            var journalNodeId = AddRelationshipGraphNode(nodes, row.RecordId, journal, "journal");
            AddGraphEdge(edges, reconciliationNodeId, journalNodeId, "posts", journal?.Tone ?? FinancialRecordExplorerTone.Info);
            AddGraphEdge(edges, journalNodeId, row.RecordId, "reports", row.Tone);

            var reportNodeId = AddRelationshipGraphNode(nodes, row.RecordId, report, "report-pack");
            AddGraphEdge(edges, row.RecordId, reportNodeId, "included in", report?.Tone ?? row.Tone);

            var evidenceNodeId = AddRelationshipGraphNode(nodes, row.RecordId, evidence, "evidence-audit-links");
            AddGraphEdge(edges, reportNodeId, evidenceNodeId, "retains audit", evidence?.Tone ?? FinancialRecordExplorerTone.Info);
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
}
