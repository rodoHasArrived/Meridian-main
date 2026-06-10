using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Api;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.ViewModels;

public sealed class FinancialRecordExplorerViewModel : BindableBase, IDisposable
{
    private readonly ApiClientService _apiClient;
    private readonly CancellationTokenSource _cts = new();
    private bool _isDisposed;
    private bool _hasLoaded;
    private string _pageTag = "LedgerExplorer";
    private string _explorerId = "ledger";
    private string _title = "Financial Record Explorer";
    private string _description = "Review retained source-backed financial records.";
    private string _sourceState = "Waiting for source-backed records.";
    private bool _isBlocked;
    private string _blockedReason = string.Empty;
    private bool _isRefreshing;
    private string _emptyStateTitle = "No source-backed records loaded";
    private string _emptyStateDetail = "Refresh after the workstation API returns a retained financial record explorer projection.";
    private FinancialRecordExplorerRowModel? _selectedRecord;
    private InspectorPanelModel _selectedRecordInspector = BuildEmptyInspector("No financial record selected", "Select a source-backed row to inspect fields, proof actions, and relationships.");
    private WorkstationTableModel<FinancialRecordExplorerRowModel> _recordsTable = BuildEmptyTable();

    public FinancialRecordExplorerViewModel(ApiClientService apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsRefreshing);
        OpenProofActionCommand = new RelayCommand<FinancialRecordExplorerActionModel>(
            OpenProofAction,
            static action => action?.IsEnabled == true);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<FinancialRecordExplorerActionModel> OpenProofActionCommand { get; }

    public ObservableCollection<FinancialRecordExplorerRowModel> Records { get; } = [];

    public ObservableCollection<FinancialRecordExplorerFactModel> SummaryItems { get; } = [];

    public ObservableCollection<FinancialRecordExplorerFactModel> ScopeItems { get; } = [];

    public ObservableCollection<FinancialRecordExplorerFactModel> FilterItems { get; } = [];

    public ObservableCollection<FinancialRecordExplorerSavedViewModel> SavedViews { get; } = [];

    public ObservableCollection<FinancialRecordExplorerActionModel> ExplorerActions { get; } = [];

    public ObservableCollection<FinancialRecordExplorerActionModel> SelectedRecordActions { get; } = [];

    public ObservableCollection<FinancialRecordExplorerRelationshipModel> UsedInRelationships { get; } = [];

    public ObservableCollection<FinancialRecordExplorerRelationshipModel> ImpactRelationships { get; } = [];

    public string PageTag
    {
        get => _pageTag;
        private set => SetProperty(ref _pageTag, value);
    }

    public string ExplorerId
    {
        get => _explorerId;
        private set => SetProperty(ref _explorerId, value);
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        private set => SetProperty(ref _description, value);
    }

    public string SourceState
    {
        get => _sourceState;
        private set => SetProperty(ref _sourceState, value);
    }

    public bool IsBlocked
    {
        get => _isBlocked;
        private set => SetProperty(ref _isBlocked, value);
    }

    public string BlockedReason
    {
        get => _blockedReason;
        private set => SetProperty(ref _blockedReason, value);
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (SetProperty(ref _isRefreshing, value))
            {
                OnPropertyChanged(nameof(RefreshTooltip));
                RefreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public WorkstationTableModel<FinancialRecordExplorerRowModel> RecordsTable
    {
        get => _recordsTable;
        private set => SetProperty(ref _recordsTable, value);
    }

    public FinancialRecordExplorerRowModel? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (SetProperty(ref _selectedRecord, value))
            {
                ApplySelectedRecord(value);
            }
        }
    }

    public InspectorPanelModel SelectedRecordInspector
    {
        get => _selectedRecordInspector;
        private set => SetProperty(ref _selectedRecordInspector, value);
    }

    public string EmptyStateTitle
    {
        get => _emptyStateTitle;
        private set => SetProperty(ref _emptyStateTitle, value);
    }

    public string EmptyStateDetail
    {
        get => _emptyStateDetail;
        private set => SetProperty(ref _emptyStateDetail, value);
    }

    public bool HasRows => Records.Count > 0;

    public bool IsEmptyStateVisible => !HasRows;

    public bool HasSelectedRecord => SelectedRecord is not null;

    public string RefreshTooltip => IsRefreshing
        ? "Financial record explorer refresh is already running."
        : $"Refresh the source-backed {Title}.";

    public void ApplyPageTag(string pageTag)
    {
        var canonicalPageTag = string.IsNullOrWhiteSpace(pageTag)
            ? "LedgerExplorer"
            : ShellNavigationCatalog.GetCanonicalPageTag(pageTag);
        var explorerId = ResolveExplorerId(canonicalPageTag);

        if (string.Equals(PageTag, canonicalPageTag, StringComparison.Ordinal) &&
            string.Equals(ExplorerId, explorerId, StringComparison.Ordinal))
        {
            return;
        }

        PageTag = canonicalPageTag;
        ExplorerId = explorerId;
        _hasLoaded = false;

        if (!_isDisposed)
        {
            _ = RefreshAsync(_cts.Token);
        }
    }

    public void Activate()
    {
        if (!_hasLoaded && !IsRefreshing && !_isDisposed)
        {
            _ = RefreshAsync(_cts.Token);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _cts.Dispose();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (_isDisposed)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            var route = UiApiRoutes.WithParam(UiApiRoutes.WorkstationFinancialRecordExplorer, "explorerId", ExplorerId);
            var dto = await _apiClient.GetAsync<FinancialRecordExplorerDto>(route, ct).ConfigureAwait(true);
            ApplyExplorer(dto ?? CreateUnavailableExplorer(ExplorerId, Title));
            _hasLoaded = true;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    internal void ApplyExplorer(FinancialRecordExplorerDto explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);

        var presentation = BuildPresentation(explorer);

        Title = presentation.Title;
        Description = presentation.Description;
        SourceState = presentation.SourceState;
        IsBlocked = presentation.IsBlocked;
        BlockedReason = presentation.BlockedReason;
        EmptyStateTitle = presentation.EmptyStateTitle;
        EmptyStateDetail = presentation.EmptyStateDetail;
        RecordsTable = presentation.Table;

        Replace(Records, presentation.Rows);
        Replace(SummaryItems, presentation.SummaryItems);
        Replace(ScopeItems, presentation.ScopeItems);
        Replace(FilterItems, presentation.FilterItems);
        Replace(SavedViews, presentation.SavedViews);
        Replace(ExplorerActions, presentation.ExplorerActions);

        SelectedRecord = presentation.SelectedRecord
            ?? Records.FirstOrDefault();

        RaiseRecordStateChanged();
    }

    internal static string ResolveExplorerId(string? pageTag)
        => (pageTag ?? string.Empty).Trim() switch
        {
            "PortfolioExplorer" => "portfolio",
            "SecurityInstrumentExplorer" => "security-instrument",
            "ReportLineProvenanceExplorer" => "report-line-provenance",
            "LedgerExplorer" => "ledger",
            "portfolio" => "portfolio",
            "security-instrument" => "security-instrument",
            "report-line-provenance" => "report-line-provenance",
            _ => "ledger"
        };

    internal static FinancialRecordExplorerPresentation BuildPresentation(FinancialRecordExplorerDto explorer)
    {
        ArgumentNullException.ThrowIfNull(explorer);

        var rows = explorer.Rows
            .Select(row => FinancialRecordExplorerRowModel.FromDto(row, explorer.Columns))
            .ToArray();
        var selectedRow = rows.FirstOrDefault(row => string.Equals(row.RecordId, explorer.SelectedRecord?.RecordId, StringComparison.OrdinalIgnoreCase));
        var columns = explorer.Columns.Count == 0
            ? BuildFallbackColumns()
            : explorer.Columns
                .Select(static column => new WorkstationTableColumnModel(
                    column.Header,
                    $"Cells[{column.ColumnId}]",
                    Math.Clamp(column.Width, 80, 260)))
                .ToArray();

        var emptyState = BuildEmptyState(explorer);
        return new FinancialRecordExplorerPresentation(
            explorer.Title,
            explorer.Description,
            explorer.SourceState,
            explorer.IsBlocked,
            explorer.BlockedReason,
            new WorkstationTableModel<FinancialRecordExplorerRowModel>(
                rows,
                columns,
                explorer.Title,
                emptyState.Title,
                emptyState.Detail),
            rows,
            explorer.SummaryItems.Select(FinancialRecordExplorerFactModel.FromSummary).ToArray(),
            explorer.ScopeItems.Select(FinancialRecordExplorerFactModel.FromScope).ToArray(),
            explorer.Filters.Select(FinancialRecordExplorerFactModel.FromFilter).ToArray(),
            explorer.SavedViews.Select(FinancialRecordExplorerSavedViewModel.FromDto).ToArray(),
            explorer.ProofActions.Select(FinancialRecordExplorerActionModel.FromDto).ToArray(),
            selectedRow,
            emptyState.Title,
            emptyState.Detail);
    }

    internal static InspectorPanelModel BuildRecordInspector(FinancialRecordExplorerSelectedRecordDto selected)
        => new()
        {
            Title = selected.Title,
            Subtitle = selected.Subtitle,
            Detail = selected.Description,
            Badge = new WorkstationBadgeModel(
                selected.RecordType,
                selected.Tone.ToString(),
                "\uE8D7",
                ToneToWorkspaceTone(selected.Tone)),
            Facts = selected.Fields
                .Select(static field => new KeyValueFactModel(
                    field.Label,
                    field.Value,
                    field.Detail,
                    ToneToWorkspaceTone(field.Tone)))
                .ToArray()
        };

    private static WorkstationTableModel<FinancialRecordExplorerRowModel> BuildEmptyTable()
        => new(
            Array.Empty<FinancialRecordExplorerRowModel>(),
            BuildFallbackColumns(),
            "Financial records",
            "No source-backed records loaded",
            "Refresh after the workstation API returns retained financial record rows.");

    private static IReadOnlyList<WorkstationTableColumnModel> BuildFallbackColumns()
        =>
        [
            new("Record", nameof(FinancialRecordExplorerRowModel.Label), 220),
            new("Type", nameof(FinancialRecordExplorerRowModel.RecordType), 140),
            new("Source", nameof(FinancialRecordExplorerRowModel.Source), 160),
            new("Status", nameof(FinancialRecordExplorerRowModel.Status), 120)
        ];

    private static FinancialRecordExplorerEmptyState BuildEmptyState(FinancialRecordExplorerDto explorer)
    {
        if (explorer.IsBlocked)
        {
            var reason = string.IsNullOrWhiteSpace(explorer.BlockedReason)
                ? "The shared financial record explorer service returned a blocked source state."
                : explorer.BlockedReason;
            return new FinancialRecordExplorerEmptyState(
                "Financial record source blocked",
                reason);
        }

        if (explorer.Rows.Count == 0)
        {
            var detail = string.IsNullOrWhiteSpace(explorer.SourceState)
                ? "No retained source-backed records are available for this explorer."
                : explorer.SourceState;
            return new FinancialRecordExplorerEmptyState(
                "No source-backed records available",
                detail);
        }

        return new FinancialRecordExplorerEmptyState(string.Empty, string.Empty);
    }

    private static InspectorPanelModel BuildEmptyInspector(string title, string detail)
        => new()
        {
            Title = title,
            Subtitle = "Financial record inspector",
            Detail = detail
        };

    private static FinancialRecordExplorerDto CreateUnavailableExplorer(string explorerId, string title)
        => new(
            explorerId,
            string.IsNullOrWhiteSpace(title) ? "Financial Record Explorer" : title,
            "Review retained source-backed financial records.",
            "The workstation API did not return a financial record explorer projection.",
            IsBlocked: true,
            BlockedReason: "The shared financial record explorer endpoint is unavailable.",
            ScopeItems: [new("Source", "Unavailable", FinancialRecordExplorerTone.Danger)],
            SavedViews: [],
            SummaryItems: [new("State", "Unavailable", "No source-backed DTO was returned.", FinancialRecordExplorerTone.Danger)],
            Filters: [],
            Columns: [],
            Rows: [],
            SelectedRecord: null,
            ProofActions:
            [
                new(
                    "source-unavailable",
                    "Source unavailable",
                    "Disabled until the shared explorer endpoint returns source-backed records.",
                    string.Empty,
                    IsEnabled: false,
                    DisabledReason: "No source-backed financial record explorer DTO was returned.",
                    FinancialRecordExplorerTone.Danger)
            ],
            RecordGraph: new FinancialRecordExplorerRecordGraphDto([], []));

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private void ApplySelectedRecord(FinancialRecordExplorerRowModel? selected)
    {
        if (selected is null)
        {
            SelectedRecordInspector = BuildEmptyInspector(
                IsEmptyStateVisible ? EmptyStateTitle : "No financial record selected",
                IsEmptyStateVisible ? EmptyStateDetail : "Select a source-backed record row to inspect fields, proof actions, and relationships.");
            Replace(SelectedRecordActions, Array.Empty<FinancialRecordExplorerActionModel>());
            Replace(UsedInRelationships, Array.Empty<FinancialRecordExplorerRelationshipModel>());
            Replace(ImpactRelationships, Array.Empty<FinancialRecordExplorerRelationshipModel>());
        }
        else
        {
            SelectedRecordInspector = BuildRecordInspector(selected.Detail);
            Replace(SelectedRecordActions, selected.Detail.ProofActions.Select(FinancialRecordExplorerActionModel.FromDto));
            Replace(UsedInRelationships, selected.Detail.UsedIn.Select(FinancialRecordExplorerRelationshipModel.FromDto));
            Replace(ImpactRelationships, selected.Detail.Impacts.Select(FinancialRecordExplorerRelationshipModel.FromDto));
        }

        RaiseRecordStateChanged();
    }

    private void OpenProofAction(FinancialRecordExplorerActionModel? action)
    {
        if (action?.IsEnabled != true || string.IsNullOrWhiteSpace(action.TargetPageTag))
        {
            return;
        }

        NavigationService.Instance.NavigateTo(action.TargetPageTag);
    }

    private void RaiseRecordStateChanged()
    {
        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(IsEmptyStateVisible));
        OnPropertyChanged(nameof(HasSelectedRecord));
        OpenProofActionCommand.NotifyCanExecuteChanged();
    }

    private static string ResolveNavigationTarget(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return string.Empty;
        }

        var normalized = href.Trim();
        if (normalized.Contains("/financial-record-explorers/report-line-provenance", StringComparison.OrdinalIgnoreCase))
        {
            return "ReportLineProvenanceExplorer";
        }

        if (normalized.Contains("/financial-record-explorers/security-instrument", StringComparison.OrdinalIgnoreCase))
        {
            return "SecurityInstrumentExplorer";
        }

        if (normalized.Contains("/financial-record-explorers/portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return "PortfolioExplorer";
        }

        if (normalized.Contains("/financial-record-explorers/ledger", StringComparison.OrdinalIgnoreCase))
        {
            return "LedgerExplorer";
        }

        if (normalized.Contains("/security-master", StringComparison.OrdinalIgnoreCase))
        {
            return "SecurityMaster";
        }

        if (normalized.Contains("/portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return "PortfolioExplorer";
        }

        if (normalized.Contains("/fund-structure/reporting/packs", StringComparison.OrdinalIgnoreCase))
        {
            return "FundReportPack";
        }

        if (normalized.Contains("/reconciliation/", StringComparison.OrdinalIgnoreCase))
        {
            return "FundReconciliation";
        }

        if (normalized.Contains("/journal-entry", StringComparison.OrdinalIgnoreCase))
        {
            return "FundAccountingConfigure";
        }

        if (normalized.Contains("/approval", StringComparison.OrdinalIgnoreCase))
        {
            return "FundAuditTrail";
        }

        if (normalized.Contains("/evidence/", StringComparison.OrdinalIgnoreCase))
        {
            return "FundAuditTrail";
        }

        if (normalized.Contains("/ledger", StringComparison.OrdinalIgnoreCase))
        {
            return "LedgerExplorer";
        }

        if (string.Equals(normalized, UiApiRoutes.WorkstationAccounting, StringComparison.OrdinalIgnoreCase))
        {
            return "AccountingShell";
        }

        return ShellNavigationCatalog.GetPage(normalized) is null
            ? string.Empty
            : normalized;
    }

    private static string ToneToWorkspaceTone(FinancialRecordExplorerTone tone)
        => tone switch
        {
            FinancialRecordExplorerTone.Success => WorkspaceTone.Success,
            FinancialRecordExplorerTone.Warning => WorkspaceTone.Warning,
            FinancialRecordExplorerTone.Danger => WorkspaceTone.Danger,
            FinancialRecordExplorerTone.Info => WorkspaceTone.Info,
            _ => WorkspaceTone.Neutral
        };

    public sealed record FinancialRecordExplorerPresentation(
        string Title,
        string Description,
        string SourceState,
        bool IsBlocked,
        string BlockedReason,
        WorkstationTableModel<FinancialRecordExplorerRowModel> Table,
        IReadOnlyList<FinancialRecordExplorerRowModel> Rows,
        IReadOnlyList<FinancialRecordExplorerFactModel> SummaryItems,
        IReadOnlyList<FinancialRecordExplorerFactModel> ScopeItems,
        IReadOnlyList<FinancialRecordExplorerFactModel> FilterItems,
        IReadOnlyList<FinancialRecordExplorerSavedViewModel> SavedViews,
        IReadOnlyList<FinancialRecordExplorerActionModel> ExplorerActions,
        FinancialRecordExplorerRowModel? SelectedRecord,
        string EmptyStateTitle,
        string EmptyStateDetail);

    private sealed record FinancialRecordExplorerEmptyState(string Title, string Detail);

    public sealed class FinancialRecordExplorerRowModel
    {
        private FinancialRecordExplorerRowModel(
            string recordId,
            string recordType,
            string label,
            string source,
            string status,
            string tone,
            IReadOnlyDictionary<string, string> cells,
            FinancialRecordExplorerSelectedRecordDto detail)
        {
            RecordId = recordId;
            RecordType = recordType;
            Label = label;
            Source = source;
            Status = status;
            Tone = tone;
            Cells = cells;
            Detail = detail;
        }

        public string RecordId { get; }

        public string RecordType { get; }

        public string Label { get; }

        public string Source { get; }

        public string Status { get; }

        public string Tone { get; }

        public IReadOnlyDictionary<string, string> Cells { get; }

        public FinancialRecordExplorerSelectedRecordDto Detail { get; }

        public static FinancialRecordExplorerRowModel FromDto(
            FinancialRecordExplorerRowDto row,
            IReadOnlyList<FinancialRecordExplorerColumnDto> columns)
        {
            ArgumentNullException.ThrowIfNull(row);
            ArgumentNullException.ThrowIfNull(columns);

            var cells = row.Cells.ToDictionary(
                static cell => cell.ColumnId,
                static cell => cell.DisplayValue,
                StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                if (!cells.ContainsKey(column.ColumnId))
                {
                    cells[column.ColumnId] = string.Empty;
                }
            }

            return new FinancialRecordExplorerRowModel(
                row.RecordId,
                row.RecordType,
                row.Label,
                row.Source,
                row.Status,
                row.Tone.ToString(),
                cells,
                row.Detail);
        }
    }

    public sealed record FinancialRecordExplorerFactModel(
        string Label,
        string Value,
        string Detail,
        string Tone)
    {
        public static FinancialRecordExplorerFactModel FromSummary(FinancialRecordExplorerSummaryItemDto item)
            => new(item.Label, item.Value, item.Detail, ToneToWorkspaceTone(item.Tone));

        public static FinancialRecordExplorerFactModel FromScope(FinancialRecordExplorerScopeItemDto item)
            => new(item.Label, item.Value, string.Empty, ToneToWorkspaceTone(item.Tone));

        public static FinancialRecordExplorerFactModel FromFilter(FinancialRecordExplorerFilterDto item)
            => new(item.Label, item.Value, item.Operator, ToneToWorkspaceTone(item.Tone));
    }

    public sealed record FinancialRecordExplorerSavedViewModel(
        string ViewId,
        string Label,
        string Description,
        bool IsSystem,
        bool IsActive,
        string SearchText)
    {
        public static FinancialRecordExplorerSavedViewModel FromDto(FinancialRecordExplorerSavedViewDto view)
            => new(view.ViewId, view.Label, view.Description, view.IsSystem, view.IsActive, view.SearchText);
    }

    public sealed record FinancialRecordExplorerActionModel(
        string ActionId,
        string Label,
        string Description,
        string Href,
        bool IsEnabled,
        string DisabledReason,
        string TargetPageTag,
        string Tone)
    {
        public string Tooltip => IsEnabled
            ? Description
            : string.IsNullOrWhiteSpace(DisabledReason)
                ? Description
                : DisabledReason;

        public static FinancialRecordExplorerActionModel FromDto(FinancialRecordExplorerProofActionDto action)
        {
            var target = ResolveNavigationTarget(action.Href);
            var enabled = action.IsEnabled && !string.IsNullOrWhiteSpace(target);
            var disabledReason = action.IsEnabled && string.IsNullOrWhiteSpace(target)
                ? "No WPF route is available for this proof action."
                : action.DisabledReason;

            return new FinancialRecordExplorerActionModel(
                action.ActionId,
                action.Label,
                action.Description,
                action.Href,
                enabled,
                disabledReason,
                target,
                ToneToWorkspaceTone(action.Tone));
        }
    }

    public sealed record FinancialRecordExplorerRelationshipModel(
        string RelationshipId,
        string Label,
        string Description,
        string Href,
        string TargetPageTag,
        string Tone)
    {
        public static FinancialRecordExplorerRelationshipModel FromDto(FinancialRecordExplorerRelationshipDto relationship)
            => new(
                relationship.RelationshipId,
                relationship.Label,
                relationship.Description,
                relationship.Href,
                ResolveNavigationTarget(relationship.Href),
                ToneToWorkspaceTone(relationship.Tone));
    }
}
