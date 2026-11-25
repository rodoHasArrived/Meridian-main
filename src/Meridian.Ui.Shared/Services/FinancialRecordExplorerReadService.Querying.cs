using System.Globalization;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Contracts.Workstation;
using Meridian.FinancialOperations.Ledger;
using Meridian.Storage.Ledger;
using Meridian.Strategies.Services;

namespace Meridian.Ui.Shared.Services;

public sealed partial class FinancialRecordExplorerReadService
{
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

    internal static FinancialRecordExplorerDto ApplyExplorerQuery(
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

        if (selectedView is null && string.IsNullOrWhiteSpace(requestedViewId) &&
            string.IsNullOrWhiteSpace(searchText) && filters.Length == 0)
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

}
