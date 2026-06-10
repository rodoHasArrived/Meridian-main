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

    private static readonly FinancialRecordExplorerRecordGraphDto EmptyGraph = new([], []);

    private readonly IServiceProvider _services;
    private readonly IFinancialRecordExplorerSavedViewStore _savedViewStore;

    public FinancialRecordExplorerReadService(
        IServiceProvider services,
        IFinancialRecordExplorerSavedViewStore savedViewStore)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _savedViewStore = savedViewStore ?? throw new ArgumentNullException(nameof(savedViewStore));
    }

    public async Task<FinancialRecordExplorerDto?> GetExplorerAsync(string explorerId, CancellationToken ct = default)
    {
        var normalizedExplorerId = NormalizeExplorerId(explorerId);
        return normalizedExplorerId switch
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
        var normalizedExplorerId = NormalizeExplorerId(explorerId);
        if (!IsKnownExplorer(normalizedExplorerId))
        {
            return null;
        }

        var name = NormalizeRequiredText(request.Name, "Saved view name", maxLength: 96);
        var description = NormalizeOptionalText(request.Description, maxLength: 256);
        var savedViewId = NormalizeSavedViewId(request.SavedViewId, name);
        var now = DateTimeOffset.UtcNow;
        var existing = (await _savedViewStore.LoadAsync(normalizedExplorerId, ct).ConfigureAwait(false))
            .FirstOrDefault(view => string.Equals(view.SavedViewId, savedViewId, StringComparison.OrdinalIgnoreCase));

        var view = new FinancialRecordExplorerSavedViewDto(
            SavedViewId: savedViewId,
            Name: name,
            Description: description,
            Filters: NormalizeFilters(request.Filters),
            ColumnIds: NormalizeColumnIds(request.ColumnIds),
            IsSystem: false,
            IsActive: false,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now);

        return await _savedViewStore.UpsertAsync(normalizedExplorerId, view, ct).ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildLedgerExplorerAsync(CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return await BuildBlockedExplorerAsync(
                    LedgerExplorerId,
                    "Accounting",
                    "Ledger Explorer",
                    "Accounting",
                    "Journal entries and ledger detail",
                    "Strategy run read service is not registered; ledger records cannot be sourced.",
                    LedgerColumns(),
                    ct)
                .ConfigureAwait(false);
        }

        var runs = await readService.GetRunsAsync(new StrategyRunHistoryQuery(Limit: 24), ct).ConfigureAwait(false);
        var details = await LoadDetailsAsync(readService, runs, ct).ConfigureAwait(false);
        var rows = new List<FinancialRecordExplorerRowDto>();

        foreach (var detail in details)
        {
            if (detail?.Ledger is null)
            {
                continue;
            }

            var rowIndex = 0;
            foreach (var line in detail.Ledger.TrialBalance)
            {
                rows.Add(BuildLedgerRow(detail.Summary, detail.Ledger, line, rowIndex++));
            }
        }

        return await BuildExplorerAsync(
                explorerId: LedgerExplorerId,
                explorerLabel: "Financial Record Explorer",
                title: "Ledger Explorer",
                description: "Source-backed accounting ledger records with journal, reconciliation, report, and audit proof paths.",
                workspaceId: "Accounting",
                recordSetLabel: "Journal entries and ledger detail",
                emptyTitle: "No ledger records",
                emptyDetail: "No source-backed ledger rows were found in the retained strategy-run read model.",
                columns: LedgerColumns(),
                rows: rows,
                scopeItems:
                [
                    new("workspace", "Workspace", "Accounting"),
                    new("record-set", "Record set", "Journal entries and ledger detail"),
                    new("runs", "Runs inspected", runs.Count.ToString(CultureInfo.InvariantCulture)),
                    new("source", "Source", "Strategy run ledger read model")
                ],
                summaryItems:
                [
                    new("rows", "Rows", rows.Count.ToString(CultureInfo.InvariantCulture), rows.Count > 0 ? "success" : "warning"),
                    new("runs", "Runs", runs.Count.ToString(CultureInfo.InvariantCulture)),
                    new("journals", "Journal entries", details.Sum(detail => detail?.Ledger?.JournalEntryCount ?? 0).ToString(CultureInfo.InvariantCulture)),
                    new("missing-security", "Security gaps", details.Sum(detail => detail?.Ledger?.SecurityMissingCount ?? 0).ToString(CultureInfo.InvariantCulture), details.Any(detail => (detail?.Ledger?.SecurityMissingCount ?? 0) > 0) ? "warning" : "success")
                ],
                filters: [new("source", "Source", "Strategy run ledger read model")],
                ct)
            .ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildPortfolioExplorerAsync(CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return await BuildBlockedExplorerAsync(
                    PortfolioExplorerId,
                    "Portfolio",
                    "Portfolio Explorer",
                    "Portfolio",
                    "Positions and run evidence",
                    "Strategy run read service is not registered; portfolio records cannot be sourced.",
                    PortfolioColumns(),
                    ct)
                .ConfigureAwait(false);
        }

        var runs = await readService.GetRunsAsync(new StrategyRunHistoryQuery(Limit: 24), ct).ConfigureAwait(false);
        var details = await LoadDetailsAsync(readService, runs, ct).ConfigureAwait(false);
        var rows = new List<FinancialRecordExplorerRowDto>();

        foreach (var detail in details)
        {
            if (detail?.Portfolio is null)
            {
                continue;
            }

            var rowIndex = 0;
            foreach (var position in detail.Portfolio.Positions)
            {
                rows.Add(BuildPortfolioRow(detail.Summary, detail.Portfolio, position, rowIndex++));
            }
        }

        return await BuildExplorerAsync(
                explorerId: PortfolioExplorerId,
                explorerLabel: "Financial Record Explorer",
                title: "Portfolio Explorer",
                description: "Source-backed holdings, run evidence, brokerage posture, and retained proof links.",
                workspaceId: "Portfolio",
                recordSetLabel: "Positions and run evidence",
                emptyTitle: "No portfolio records",
                emptyDetail: "No source-backed portfolio position rows were found in the retained strategy-run read model.",
                columns: PortfolioColumns(),
                rows: rows,
                scopeItems:
                [
                    new("workspace", "Workspace", "Portfolio"),
                    new("record-set", "Record set", "Positions and run evidence"),
                    new("runs", "Runs inspected", runs.Count.ToString(CultureInfo.InvariantCulture)),
                    new("source", "Source", "Strategy run portfolio read model")
                ],
                summaryItems:
                [
                    new("positions", "Positions", rows.Count.ToString(CultureInfo.InvariantCulture), rows.Count > 0 ? "success" : "warning"),
                    new("runs", "Runs", runs.Count.ToString(CultureInfo.InvariantCulture)),
                    new("gross", "Gross exposure", FormatCurrency(details.Sum(detail => detail?.Portfolio?.GrossExposure ?? 0m))),
                    new("missing-security", "Security gaps", details.Sum(detail => detail?.Portfolio?.SecurityMissingCount ?? 0).ToString(CultureInfo.InvariantCulture), details.Any(detail => (detail?.Portfolio?.SecurityMissingCount ?? 0) > 0) ? "warning" : "success")
                ],
                filters: [new("source", "Source", "Strategy run portfolio read model")],
                ct)
            .ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildSecurityInstrumentExplorerAsync(CancellationToken ct)
    {
        var readService = _services.GetService<StrategyRunReadService>();
        if (readService is null)
        {
            return await BuildBlockedExplorerAsync(
                    SecurityInstrumentExplorerId,
                    "Accounting",
                    "Security & Instrument Explorer",
                    "Security Master",
                    "Security Master references and instrument usage",
                    "Strategy run read service is not registered; Security Master references cannot be sourced.",
                    SecurityColumns(),
                    ct)
                .ConfigureAwait(false);
        }

        var runs = await readService.GetRunsAsync(new StrategyRunHistoryQuery(Limit: 24), ct).ConfigureAwait(false);
        var details = await LoadDetailsAsync(readService, runs, ct).ConfigureAwait(false);
        var records = new Dictionary<string, SecurityRecordAccumulator>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in details)
        {
            if (detail?.Portfolio is not null)
            {
                foreach (var position in detail.Portfolio.Positions)
                {
                    AccumulateSecurity(records, detail.Summary, position.Symbol, "Portfolio position", position.Security);
                }
            }

            if (detail?.Ledger is not null)
            {
                foreach (var line in detail.Ledger.TrialBalance)
                {
                    AccumulateSecurity(records, detail.Summary, line.Symbol ?? line.AccountName, "Ledger account", line.Security);
                }
            }
        }

        var rows = records.Values
            .OrderBy(static record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static record => record.RecordId, StringComparer.OrdinalIgnoreCase)
            .Select(BuildSecurityRow)
            .ToArray();

        return await BuildExplorerAsync(
                explorerId: SecurityInstrumentExplorerId,
                explorerLabel: "Financial Record Explorer",
                title: "Security & Instrument Explorer",
                description: "Security Master references observed in ledger and portfolio records, with source usage and impact paths.",
                workspaceId: "Accounting",
                recordSetLabel: "Security Master references and instrument usage",
                emptyTitle: "No Security Master references",
                emptyDetail: "No source-backed portfolio or ledger records currently reference a Security Master instrument.",
                columns: SecurityColumns(),
                rows: rows,
                scopeItems:
                [
                    new("workspace", "Workspace", "Accounting"),
                    new("record-set", "Record set", "Security Master references"),
                    new("runs", "Runs inspected", runs.Count.ToString(CultureInfo.InvariantCulture)),
                    new("source", "Source", "Ledger and portfolio security coverage")
                ],
                summaryItems:
                [
                    new("instruments", "Instruments", rows.Length.ToString(CultureInfo.InvariantCulture), rows.Length > 0 ? "success" : "warning"),
                    new("resolved", "Resolved", records.Values.Count(static record => record.IsResolved).ToString(CultureInfo.InvariantCulture), "success"),
                    new("unresolved", "Unresolved", records.Values.Count(static record => !record.IsResolved).ToString(CultureInfo.InvariantCulture), records.Values.Any(static record => !record.IsResolved) ? "warning" : "success"),
                    new("runs", "Runs", runs.Count.ToString(CultureInfo.InvariantCulture))
                ],
                filters: [new("source", "Source", "Ledger and portfolio security coverage")],
                ct)
            .ConfigureAwait(false);
    }

    private async Task<FinancialRecordExplorerDto> BuildExplorerAsync(
        string explorerId,
        string explorerLabel,
        string title,
        string description,
        string workspaceId,
        string recordSetLabel,
        string emptyTitle,
        string emptyDetail,
        IReadOnlyList<FinancialRecordExplorerColumnDto> columns,
        IReadOnlyList<FinancialRecordExplorerRowDto> rows,
        IReadOnlyList<FinancialRecordExplorerScopeItemDto> scopeItems,
        IReadOnlyList<FinancialRecordExplorerSummaryItemDto> summaryItems,
        IReadOnlyList<FinancialRecordExplorerFilterDto> filters,
        CancellationToken ct)
    {
        var savedViews = await BuildSavedViewsAsync(explorerId, columns, ct).ConfigureAwait(false);
        var selected = rows.FirstOrDefault()?.Detail;

        return new FinancialRecordExplorerDto(
            ExplorerId: explorerId,
            ExplorerLabel: explorerLabel,
            Title: title,
            Description: description,
            WorkspaceId: workspaceId,
            RecordSetLabel: recordSetLabel,
            IsBlocked: false,
            BlockedReason: null,
            EmptyTitle: emptyTitle,
            EmptyDetail: emptyDetail,
            ScopeItems: scopeItems,
            SavedViews: savedViews,
            SummaryItems: summaryItems,
            Filters: filters,
            Columns: columns,
            Rows: rows,
            SelectedRecord: selected,
            RecordGraph: selected?.RecordGraph ?? EmptyGraph,
            UsedIn: selected?.UsedIn ?? [],
            Impacts: selected?.Impacts ?? []);
    }

    private async Task<FinancialRecordExplorerDto> BuildBlockedExplorerAsync(
        string explorerId,
        string workspaceId,
        string title,
        string scopeWorkspace,
        string recordSetLabel,
        string blockedReason,
        IReadOnlyList<FinancialRecordExplorerColumnDto> columns,
        CancellationToken ct)
    {
        var savedViews = await BuildSavedViewsAsync(explorerId, columns, ct).ConfigureAwait(false);
        return new FinancialRecordExplorerDto(
            ExplorerId: explorerId,
            ExplorerLabel: "Financial Record Explorer",
            Title: title,
            Description: "Source-backed financial records are unavailable until the required read service is registered.",
            WorkspaceId: workspaceId,
            RecordSetLabel: recordSetLabel,
            IsBlocked: true,
            BlockedReason: blockedReason,
            EmptyTitle: "Explorer blocked",
            EmptyDetail: blockedReason,
            ScopeItems:
            [
                new("workspace", "Workspace", scopeWorkspace),
                new("record-set", "Record set", recordSetLabel),
                new("source", "Source", "Unavailable")
            ],
            SavedViews: savedViews,
            SummaryItems:
            [
                new("rows", "Rows", "0", "warning"),
                new("state", "State", "Blocked", "danger")
            ],
            Filters: [new("blocked", "Blocked", blockedReason)],
            Columns: columns,
            Rows: [],
            SelectedRecord: null,
            RecordGraph: EmptyGraph,
            UsedIn: [],
            Impacts: []);
    }

    private async Task<IReadOnlyList<FinancialRecordExplorerSavedViewDto>> BuildSavedViewsAsync(
        string explorerId,
        IReadOnlyList<FinancialRecordExplorerColumnDto> columns,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UnixEpoch;
        var systemView = new FinancialRecordExplorerSavedViewDto(
            SavedViewId: $"{explorerId}-system-default",
            Name: "Default proof view",
            Description: "System view seeded from the shared financial record explorer read model.",
            Filters: [],
            ColumnIds: columns.Select(static column => column.ColumnId).ToArray(),
            IsSystem: true,
            IsActive: true,
            CreatedAt: now,
            UpdatedAt: now);

        var userViews = await _savedViewStore.LoadAsync(explorerId, ct).ConfigureAwait(false);
        return new[] { systemView }
            .Concat(userViews.Select(static view => view with { IsSystem = false, IsActive = false }))
            .ToArray();
    }

    private static async Task<IReadOnlyList<StrategyRunDetail?>> LoadDetailsAsync(
        StrategyRunReadService readService,
        IReadOnlyList<StrategyRunSummary> runs,
        CancellationToken ct)
    {
        if (runs.Count == 0)
        {
            return [];
        }

        return await Task.WhenAll(runs.Select(run => readService.GetRunDetailAsync(run.RunId, ct))).ConfigureAwait(false);
    }

    private static FinancialRecordExplorerRowDto BuildLedgerRow(
        StrategyRunSummary run,
        LedgerSummary ledger,
        LedgerTrialBalanceLine line,
        int rowIndex)
    {
        var recordId = $"ledger:{run.RunId}:{rowIndex.ToString(CultureInfo.InvariantCulture)}";
        var status = line.Security?.CoverageStatus.ToString() ?? "Ledger";
        var tone = line.Security?.CoverageStatus is WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable
            ? "warning"
            : "default";
        var fullRecordRoute = UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId);
        var relationships = BuildRunRelationships(run);
        var impacts = BuildLedgerImpacts(line, run);
        var graph = BuildRecordGraph(recordId, line.AccountName, "Ledger", relationships, impacts);
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            RecordId: recordId,
            RecordType: "Ledger",
            Title: line.AccountName,
            Subtitle: $"{line.AccountType} / {run.StrategyName}",
            Description: $"Trial-balance line from run {run.RunId} with {line.EntryCount.ToString(CultureInfo.InvariantCulture)} ledger entries.",
            Status: status,
            Tone: tone,
            FullRecordRoute: fullRecordRoute,
            Fields:
            [
                new("Run", run.RunId, run.StrategyName),
                new("Ledger reference", ledger.LedgerReference),
                new("Account type", line.AccountType),
                new("Balance", FormatCurrency(line.Balance)),
                new("Entries", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("Security", line.Security?.DisplayName ?? line.Symbol ?? "Not linked", line.Security?.CoverageStatus.ToString(), tone)
            ],
            ProofActions:
            [
                new("open-ledger", "Open ledger", fullRecordRoute),
                new("evidence-packet", "Evidence packet", UiApiRoutes.WithParam(UiApiRoutes.WorkstationEvidenceSubjectPacket, "subjectKind", "run").Replace("{subjectId}", Uri.EscapeDataString(run.RunId), StringComparison.Ordinal)),
                new("approval", "Approval evidence", run.AuditReference, IsEnabled: !string.IsNullOrWhiteSpace(run.AuditReference), DisabledReason: string.IsNullOrWhiteSpace(run.AuditReference) ? "No audit reference is retained for this run." : null)
            ],
            RecordGraph: graph,
            UsedIn: relationships,
            Impacts: impacts);

        return new FinancialRecordExplorerRowDto(
            RecordId: recordId,
            RecordType: "Ledger",
            Label: line.AccountName,
            Status: status,
            Tone: tone,
            Cells:
            [
                new("account", line.AccountName),
                new("type", line.AccountType),
                new("symbol", line.Symbol ?? "—"),
                new("balance", line.Balance.ToString(CultureInfo.InvariantCulture), FormatCurrency(line.Balance), tone),
                new("entries", line.EntryCount.ToString(CultureInfo.InvariantCulture)),
                new("run", run.RunId, run.StrategyName, Href: fullRecordRoute)
            ],
            Detail: detail);
    }

    private static FinancialRecordExplorerRowDto BuildPortfolioRow(
        StrategyRunSummary run,
        PortfolioSummary portfolio,
        PortfolioPositionSummary position,
        int rowIndex)
    {
        var recordId = $"portfolio:{run.RunId}:{position.Symbol}:{rowIndex.ToString(CultureInfo.InvariantCulture)}";
        var status = position.Security?.CoverageStatus.ToString() ?? "Position";
        var tone = position.Security?.CoverageStatus is WorkstationSecurityCoverageStatus.Missing or WorkstationSecurityCoverageStatus.Unavailable
            ? "warning"
            : "default";
        var fullRecordRoute = $"/portfolio?runId={Uri.EscapeDataString(run.RunId)}&symbol={Uri.EscapeDataString(position.Symbol)}";
        var relationships = BuildRunRelationships(run);
        var impacts = BuildPortfolioImpacts(position, run);
        var graph = BuildRecordGraph(recordId, position.Symbol, "Portfolio", relationships, impacts);
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            RecordId: recordId,
            RecordType: "Portfolio",
            Title: position.Symbol,
            Subtitle: $"{portfolio.PortfolioId} / {run.StrategyName}",
            Description: $"Position retained in portfolio {portfolio.PortfolioId} for run {run.RunId}.",
            Status: status,
            Tone: tone,
            FullRecordRoute: fullRecordRoute,
            Fields:
            [
                new("Run", run.RunId, run.StrategyName),
                new("Portfolio", portfolio.PortfolioId),
                new("Quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("Average cost", FormatCurrency(position.AverageCostBasis)),
                new("Unrealized P&L", FormatCurrency(position.UnrealizedPnl)),
                new("Security", position.Security?.DisplayName ?? "Not linked", position.Security?.CoverageStatus.ToString(), tone)
            ],
            ProofActions:
            [
                new("open-portfolio", "Open portfolio", fullRecordRoute),
                new("run-review", "Run review packet", UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.RunId)),
                new("security-master", "Security Master", BuildSecurityHref(position.Security, position.Symbol), IsEnabled: position.Security is not null || !string.IsNullOrWhiteSpace(position.Symbol), DisabledReason: string.IsNullOrWhiteSpace(position.Symbol) ? "No symbol is retained for this position." : null)
            ],
            RecordGraph: graph,
            UsedIn: relationships,
            Impacts: impacts);

        return new FinancialRecordExplorerRowDto(
            RecordId: recordId,
            RecordType: "Portfolio",
            Label: position.Symbol,
            Status: status,
            Tone: tone,
            Cells:
            [
                new("symbol", position.Symbol),
                new("quantity", position.Quantity.ToString(CultureInfo.InvariantCulture)),
                new("averageCost", position.AverageCostBasis.ToString(CultureInfo.InvariantCulture), FormatCurrency(position.AverageCostBasis)),
                new("unrealized", position.UnrealizedPnl.ToString(CultureInfo.InvariantCulture), FormatCurrency(position.UnrealizedPnl), position.UnrealizedPnl < 0m ? "warning" : "success"),
                new("security", position.Security?.DisplayName ?? "Not linked", Tone: tone, Href: BuildSecurityHref(position.Security, position.Symbol)),
                new("run", run.RunId, run.StrategyName, Href: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.RunId))
            ],
            Detail: detail);
    }

    private static FinancialRecordExplorerRowDto BuildSecurityRow(SecurityRecordAccumulator record)
    {
        var tone = record.IsResolved ? "success" : "warning";
        var status = record.IsResolved ? "Resolved" : "Unresolved";
        var fullRecordRoute = record.SecurityId is { } securityId
            ? UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", securityId.ToString("D"))
            : UiApiRoutes.WithQuery(UiApiRoutes.WorkstationSecurityMasterSearch, $"query={Uri.EscapeDataString(record.DisplayName)}");
        var relationships = record.SourceRuns
            .Select(run => new FinancialRecordExplorerRelationshipDto(
                RelationshipId: $"run:{run.RunId}",
                Label: run.StrategyName,
                TargetType: "Run",
                TargetId: run.RunId,
                Detail: $"{run.Mode} / {run.Status}",
                Href: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.RunId)))
            .DistinctBy(static relationship => relationship.RelationshipId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var impacts = record.Usages
            .Select((usage, index) => new FinancialRecordExplorerRelationshipDto(
                RelationshipId: $"usage:{record.RecordId}:{index.ToString(CultureInfo.InvariantCulture)}",
                Label: usage,
                TargetType: "FinancialRecord",
                TargetId: record.RecordId,
                Detail: "Security reference used by a source-backed financial record.",
                Href: fullRecordRoute,
                Tone: tone))
            .ToArray();
        var graph = BuildRecordGraph(record.RecordId, record.DisplayName, "Security", relationships, impacts);
        var detail = new FinancialRecordExplorerSelectedRecordDto(
            RecordId: record.RecordId,
            RecordType: "SecurityInstrument",
            Title: record.DisplayName,
            Subtitle: $"{record.AssetClass} / {record.Currency}",
            Description: record.IsResolved
                ? "Security Master reference observed in source-backed ledger or portfolio rows."
                : "Unresolved security reference observed in source-backed ledger or portfolio rows.",
            Status: status,
            Tone: tone,
            FullRecordRoute: fullRecordRoute,
            Fields:
            [
                new("Status", status, record.CoverageStatus, tone),
                new("Asset class", record.AssetClass),
                new("Currency", record.Currency),
                new("Primary identifier", record.PrimaryIdentifier),
                new("Usages", record.UsageCount.ToString(CultureInfo.InvariantCulture)),
                new("Runs", record.SourceRuns.Count.ToString(CultureInfo.InvariantCulture))
            ],
            ProofActions:
            [
                new("open-search", record.IsResolved ? "Open Security Master" : "Open search", fullRecordRoute),
                new("instrument-passport", "Instrument passport", record.SecurityId is { } securityIdForPassport ? UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterInstrumentPassport, "securityId", securityIdForPassport.ToString("D")) : null, IsEnabled: record.SecurityId is not null, DisabledReason: record.SecurityId is null ? "No resolved Security Master identifier is retained for this reference." : null)
            ],
            RecordGraph: graph,
            UsedIn: relationships,
            Impacts: impacts);

        return new FinancialRecordExplorerRowDto(
            RecordId: record.RecordId,
            RecordType: "SecurityInstrument",
            Label: record.DisplayName,
            Status: status,
            Tone: tone,
            Cells:
            [
                new("instrument", record.DisplayName, Href: fullRecordRoute),
                new("assetClass", record.AssetClass),
                new("currency", record.Currency),
                new("coverage", status, record.CoverageStatus, tone),
                new("source", record.Usages.FirstOrDefault() ?? "Source record"),
                new("run", record.SourceRuns.FirstOrDefault()?.RunId ?? "—", record.SourceRuns.FirstOrDefault()?.StrategyName)
            ],
            Detail: detail);
    }

    private static IReadOnlyList<FinancialRecordExplorerColumnDto> LedgerColumns() =>
    [
        new("account", "Account", IsKey: true, Width: 220),
        new("type", "Type", Width: 120),
        new("symbol", "Symbol", Width: 120),
        new("balance", "Balance", "currency", Width: 130),
        new("entries", "Entries", "number", Width: 90),
        new("run", "Run", Width: 180)
    ];

    private static IReadOnlyList<FinancialRecordExplorerColumnDto> PortfolioColumns() =>
    [
        new("symbol", "Symbol", IsKey: true, Width: 120),
        new("quantity", "Quantity", "number", Width: 110),
        new("averageCost", "Average cost", "currency", Width: 130),
        new("unrealized", "Unrealized P&L", "currency", Width: 140),
        new("security", "Security", Width: 220),
        new("run", "Run", Width: 180)
    ];

    private static IReadOnlyList<FinancialRecordExplorerColumnDto> SecurityColumns() =>
    [
        new("instrument", "Instrument", IsKey: true, Width: 240),
        new("assetClass", "Asset class", Width: 130),
        new("currency", "Currency", Width: 90),
        new("coverage", "Coverage", Width: 120),
        new("source", "Source", Width: 160),
        new("run", "Run", Width: 180)
    ];

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildRunRelationships(StrategyRunSummary run) =>
    [
        new(
            RelationshipId: $"run:{run.RunId}",
            Label: run.StrategyName,
            TargetType: "Run",
            TargetId: run.RunId,
            Detail: $"{run.Mode} / {run.Status}",
            Href: UiApiRoutes.WithParam(UiApiRoutes.RunsReviewPacket, "runId", run.RunId)),
        new(
            RelationshipId: $"dataset:{run.RunId}",
            Label: run.DatasetReference ?? "Dataset evidence unavailable",
            TargetType: "Dataset",
            TargetId: run.DatasetReference ?? run.RunId,
            Detail: string.IsNullOrWhiteSpace(run.DatasetReference) ? "No dataset reference retained." : "Dataset reference retained by run.",
            Tone: string.IsNullOrWhiteSpace(run.DatasetReference) ? "warning" : "default")
    ];

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildLedgerImpacts(
        LedgerTrialBalanceLine line,
        StrategyRunSummary run) =>
    [
        new(
            RelationshipId: $"ledger-report:{run.RunId}:{line.AccountName}",
            Label: "Trial balance report",
            TargetType: "Report",
            TargetId: run.RunId,
            Detail: "Ledger balance can flow into trial-balance reporting when report generation is requested.",
            Href: UiApiRoutes.WithParam(UiApiRoutes.RunsLedgerTrialBalance, "runId", run.RunId)),
        new(
            RelationshipId: $"reconciliation:{run.RunId}:{line.AccountName}",
            Label: "Reconciliation",
            TargetType: "Reconciliation",
            TargetId: run.RunId,
            Detail: "Ledger line participates in run reconciliation checks.",
            Href: UiApiRoutes.WithParam(UiApiRoutes.RunsReconciliation, "runId", run.RunId))
    ];

    private static IReadOnlyList<FinancialRecordExplorerRelationshipDto> BuildPortfolioImpacts(
        PortfolioPositionSummary position,
        StrategyRunSummary run) =>
    [
        new(
            RelationshipId: $"risk:{run.RunId}:{position.Symbol}",
            Label: "Portfolio exposure",
            TargetType: "Risk",
            TargetId: position.Symbol,
            Detail: "Position contributes to portfolio exposure and risk posture."),
        new(
            RelationshipId: $"security:{run.RunId}:{position.Symbol}",
            Label: "Security Master coverage",
            TargetType: "SecurityMaster",
            TargetId: position.Security?.SecurityId.ToString("D") ?? position.Symbol,
            Detail: position.Security is null ? "Position is not linked to a retained Security Master identifier." : "Position is linked to a retained Security Master identifier.",
            Href: BuildSecurityHref(position.Security, position.Symbol),
            Tone: position.Security is null ? "warning" : "success")
    ];

    private static FinancialRecordExplorerRecordGraphDto BuildRecordGraph(
        string recordId,
        string label,
        string nodeType,
        IReadOnlyList<FinancialRecordExplorerRelationshipDto> usedIn,
        IReadOnlyList<FinancialRecordExplorerRelationshipDto> impacts)
    {
        var nodes = new List<FinancialRecordExplorerGraphNodeDto>
        {
            new(recordId, label, nodeType, "default")
        };
        var edges = new List<FinancialRecordExplorerGraphEdgeDto>();

        foreach (var relationship in usedIn.Concat(impacts))
        {
            var nodeId = $"{relationship.TargetType}:{relationship.TargetId}";
            nodes.Add(new FinancialRecordExplorerGraphNodeDto(nodeId, relationship.Label, relationship.TargetType, relationship.Tone, relationship.Href));
            edges.Add(new FinancialRecordExplorerGraphEdgeDto(relationship.RelationshipId, recordId, nodeId, relationship.Label));
        }

        return new FinancialRecordExplorerRecordGraphDto(
            nodes.DistinctBy(static node => node.NodeId, StringComparer.OrdinalIgnoreCase).ToArray(),
            edges);
    }

    private static void AccumulateSecurity(
        IDictionary<string, SecurityRecordAccumulator> records,
        StrategyRunSummary run,
        string? fallbackLabel,
        string usage,
        WorkstationSecurityReference? security)
    {
        var key = security?.SecurityId.ToString("D")
            ?? NormalizeRecordKey(fallbackLabel ?? "unresolved-security");
        var recordId = security is not null ? $"security:{key}" : $"security-unresolved:{key}";
        if (!records.TryGetValue(recordId, out var record))
        {
            record = new SecurityRecordAccumulator(
                RecordId: recordId,
                SecurityId: security?.SecurityId,
                DisplayName: security?.DisplayName ?? fallbackLabel ?? "Unresolved security",
                AssetClass: security?.AssetClass ?? "Unknown",
                Currency: security?.Currency ?? "—",
                PrimaryIdentifier: security?.PrimaryIdentifier ?? fallbackLabel ?? "—",
                CoverageStatus: security?.CoverageStatus.ToString() ?? "Missing");
            records[recordId] = record;
        }

        record.SourceRuns.Add(run);
        record.Usages.Add(usage);
    }

    private static string BuildSecurityHref(WorkstationSecurityReference? security, string fallbackQuery)
        => security is not null
            ? UiApiRoutes.WithParam(UiApiRoutes.WorkstationSecurityMasterById, "securityId", security.SecurityId.ToString("D"))
            : UiApiRoutes.WithQuery(UiApiRoutes.WorkstationSecurityMasterSearch, $"query={Uri.EscapeDataString(fallbackQuery)}");

    private static string FormatCurrency(decimal value)
    {
        var sign = value < 0m ? "-" : value > 0m ? "+" : string.Empty;
        var absolute = Math.Abs(value);
        return $"{sign}${absolute.ToString("#,0.##", CultureInfo.InvariantCulture)}";
    }

    private static bool IsKnownExplorer(string explorerId)
        => explorerId is LedgerExplorerId or PortfolioExplorerId or SecurityInstrumentExplorerId;

    private static string NormalizeExplorerId(string explorerId)
        => explorerId?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeRequiredText(string value, string label, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"{label} cannot exceed {maxLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return trimmed;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        if (trimmed.Length > maxLength)
        {
            throw new InvalidOperationException($"Description cannot exceed {maxLength.ToString(CultureInfo.InvariantCulture)} characters.");
        }

        return trimmed;
    }

    private static IReadOnlyList<FinancialRecordExplorerFilterDto> NormalizeFilters(
        IReadOnlyList<FinancialRecordExplorerFilterDto>? filters)
        => filters?
            .Where(static filter => !string.IsNullOrWhiteSpace(filter.Id) && !string.IsNullOrWhiteSpace(filter.Label))
            .Select(static filter => filter with
            {
                Id = filter.Id.Trim(),
                Label = filter.Label.Trim(),
                Value = filter.Value?.Trim() ?? string.Empty,
                Operator = string.IsNullOrWhiteSpace(filter.Operator) ? "equals" : filter.Operator.Trim()
            })
            .Take(24)
            .ToArray()
            ?? [];

    private static IReadOnlyList<string> NormalizeColumnIds(IReadOnlyList<string>? columnIds)
        => columnIds?
            .Select(static columnId => columnId.Trim())
            .Where(static columnId => !string.IsNullOrWhiteSpace(columnId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToArray()
            ?? [];

    private static string NormalizeSavedViewId(string? requestedId, string name)
    {
        var candidate = string.IsNullOrWhiteSpace(requestedId)
            ? NormalizeRecordKey(name)
            : NormalizeRecordKey(requestedId);
        return string.IsNullOrWhiteSpace(candidate) ? Guid.NewGuid().ToString("N") : candidate;
    }

    private static string NormalizeRecordKey(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray());
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    private sealed record SecurityRecordAccumulator(
        string RecordId,
        Guid? SecurityId,
        string DisplayName,
        string AssetClass,
        string Currency,
        string PrimaryIdentifier,
        string CoverageStatus)
    {
        public List<StrategyRunSummary> SourceRuns { get; } = [];

        public List<string> Usages { get; } = [];

        public bool IsResolved => SecurityId.HasValue;

        public int UsageCount => Usages.Count;
    }
}
