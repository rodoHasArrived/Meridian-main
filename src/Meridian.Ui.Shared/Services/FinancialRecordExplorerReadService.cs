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

    private static readonly string[] KnownExplorerIds =
    [
        LedgerExplorerId,
        PortfolioExplorerId,
        SecurityInstrumentExplorerId
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
    {
        var normalized = NormalizeExplorerId(explorerId);
        return normalized switch
        {
            LedgerExplorerId => await BuildLedgerExplorerAsync(ct).ConfigureAwait(false),
            PortfolioExplorerId => await BuildPortfolioExplorerAsync(ct).ConfigureAwait(false),
            SecurityInstrumentExplorerId => await BuildSecurityInstrumentExplorerAsync(ct).ConfigureAwait(false),
            _ => null
        };
    }

    public async Task<FinancialRecordExplorerSelectedRecordDto?> GetRecordAsync(
        string explorerId,
        string recordId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordId);

        var explorer = await GetExplorerAsync(explorerId, ct).ConfigureAwait(false);
        return explorer?.Rows
            .FirstOrDefault(row => string.Equals(row.RecordId, recordId, StringComparison.OrdinalIgnoreCase))
            ?.Detail;
    }

    public async Task<FinancialRecordExplorerSavedViewDto?> SaveViewAsync(
        string explorerId,
        FinancialRecordExplorerSavedViewSaveRequestDto request,
        CancellationToken ct = default)
    {
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

        return await _savedViewStore.SaveAsync(normalized, view, ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildLedgerExplorerAsync(CancellationToken ct)
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
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildPortfolioExplorerAsync(CancellationToken ct)
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
            ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildSecurityInstrumentExplorerAsync(CancellationToken ct)
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
            ct).ConfigureAwait(false);
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
        CancellationToken ct)
    {
        var savedViews = await LoadSavedViewsAsync(explorerId, systemViews, ct).ConfigureAwait(false);
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
            await LoadSavedViewsAsync(explorerId, systemViews, ct).ConfigureAwait(false),
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
        string explorerId,
        IReadOnlyList<FinancialRecordExplorerSavedViewDto> systemViews,
        CancellationToken ct)
    {
        var operatorViews = await _savedViewStore.LoadAsync(explorerId, ct).ConfigureAwait(false);
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

    private static IReadOnlyList<WorkstationSecurityReference> CollectSecurityReferences(StrategyRunDetail detail)
    {
        var references = detail.Portfolio?.Positions
            .Select(static position => position.Security)
            .Concat(detail.Ledger?.TrialBalance.Select(static line => line.Security) ?? [])
            .Where(static reference => reference is not null)
            .Select(static reference => reference!)
            .GroupBy(static reference => reference.SecurityId == Guid.Empty
                    ? $"{reference.DisplayName}:{reference.PrimaryIdentifier}:{reference.AssetClass}"
                    : reference.SecurityId.ToString("D"),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .OrderBy(static reference => reference.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return references ?? [];
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
