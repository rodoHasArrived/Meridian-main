using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.Api;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Infrastructure.Adapters.Polygon;
using Meridian.Ui.Services;
using ISmQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;
using ISmService = Meridian.Contracts.SecurityMaster.ISecurityMasterService;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Security Master workstation page.
/// Wraps <see cref="SecurityMasterWorkstationDto"/>-backed search and detail
/// surfaced by the <c>/api/workstation/security-master</c> endpoints.
/// </summary>
public sealed class SecurityMasterViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.LoggingService _loggingService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly ITradingParametersBackfillService _backfillService;
    private readonly ISecurityMasterImportService _importService;
    private readonly ISecurityMasterRuntimeStatus _securityMasterRuntimeStatus;
    private readonly WpfServices.ISecurityMasterOperatorWorkflowClient _workflowClient;
    private readonly WpfServices.NavigationService _navigationService;
    private readonly ISmQueryService _queryService;
    private readonly ISmService _service;
    private readonly bool _hasPolygonApiKey;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _workflowCts;
    private Task? _workflowPollingTask;

    // ── Public collections ──────────────────────────────────────────────────
    public ObservableCollection<SecurityMasterWorkstationDto> Results { get; } = new();
    public ObservableCollection<CorporateActionDto> CorporateActions { get; } = new();
    public ObservableCollection<SecurityMasterConflict> OpenConflicts { get; } = new();

    /// <summary>
    /// Static list of corporate action types available for recording.
    /// </summary>
    public IReadOnlyList<string> CorpActTypes => new[] { "Dividend", "StockSplit" };

    // ── Bindable properties ─────────────────────────────────────────────────
    private string _searchQuery = string.Empty;
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    private bool _activeOnly = true;
    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
            {
                RaiseSearchDerivedStateChanged();
            }
        }
    }

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    private string _statusText = "Enter a query and press Search.";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    private SecurityMasterWorkstationDto? _selectedSecurity;
    public SecurityMasterWorkstationDto? SelectedSecurity
    {
        get => _selectedSecurity;
        set
        {
            if (SetProperty(ref _selectedSecurity, value))
            {
                RaiseSelectionDerivedStateChanged();
            }
        }
    }

    private string _historyText = string.Empty;
    public string HistoryText
    {
        get => _historyText;
        private set
        {
            if (SetProperty(ref _historyText, value))
            {
                RaisePropertyChanged(nameof(LatestHistoryEventText));
            }
        }
    }

    private bool _isEditPanelVisible;
    public bool IsEditPanelVisible
    {
        get => _isEditPanelVisible;
        set => SetProperty(ref _isEditPanelVisible, value);
    }

    private bool _isDeactivatePanelVisible;
    public bool IsDeactivatePanelVisible
    {
        get => _isDeactivatePanelVisible;
        set => SetProperty(ref _isDeactivatePanelVisible, value);
    }

    private SecurityMasterEditViewModel? _editVm;
    public SecurityMasterEditViewModel? EditVm
    {
        get => _editVm;
        private set => SetProperty(ref _editVm, value);
    }

    private SecurityMasterDeactivateViewModel? _deactivateVm;
    public SecurityMasterDeactivateViewModel? DeactivateVm
    {
        get => _deactivateVm;
        private set => SetProperty(ref _deactivateVm, value);
    }

    private int _selectedDetailTab;
    public int SelectedDetailTab
    {
        get => _selectedDetailTab;
        set => SetProperty(ref _selectedDetailTab, value);
    }

    private bool _isRecordCorpActionVisible;
    public bool IsRecordCorpActionVisible
    {
        get => _isRecordCorpActionVisible;
        set => SetProperty(ref _isRecordCorpActionVisible, value);
    }

    private string _corpActType = "Dividend";
    public string CorpActType
    {
        get => _corpActType;
        set => SetProperty(ref _corpActType, value);
    }

    private string _corpActExDate = string.Empty;
    public string CorpActExDate
    {
        get => _corpActExDate;
        set => SetProperty(ref _corpActExDate, value);
    }

    private decimal _corpActAmount;
    public decimal CorpActAmount
    {
        get => _corpActAmount;
        set => SetProperty(ref _corpActAmount, value);
    }

    private string _corpActCurrency = "USD";
    public string CorpActCurrency
    {
        get => _corpActCurrency;
        set => SetProperty(ref _corpActCurrency, value);
    }

    private bool _isBackfillingTradingParams;
    public bool IsBackfillingTradingParams
    {
        get => _isBackfillingTradingParams;
        private set
        {
            if (SetProperty(ref _isBackfillingTradingParams, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    private string _backfillStatus = string.Empty;
    public string BackfillStatus
    {
        get => _backfillStatus;
        private set
        {
            if (SetProperty(ref _backfillStatus, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    // ── Import properties ───────────────────────────────────────────────────────
    private int _importTotal;
    public int ImportTotal
    {
        get => _importTotal;
        private set
        {
            if (SetProperty(ref _importTotal, value))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importProcessed;
    public int ImportProcessed
    {
        get => _importProcessed;
        private set
        {
            if (SetProperty(ref _importProcessed, value))
            {
                RaisePropertyChanged(nameof(ImportStatus));
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importImported;
    public int ImportImported
    {
        get => _importImported;
        private set
        {
            if (SetProperty(ref _importImported, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private int _importFailed;
    public int ImportFailed
    {
        get => _importFailed;
        private set
        {
            if (SetProperty(ref _importFailed, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private bool _isImporting;
    public bool IsImporting
    {
        get => _isImporting;
        private set
        {
            if (SetProperty(ref _isImporting, value))
            {
                ImportFromFileCommand.NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    public string ImportStatus
    {
        get
        {
            if (ImportTotal == 0)
                return string.Empty;
            return $"Importing {ImportProcessed}/{ImportTotal} ({ImportFailed} failed)";
        }
    }

    private bool _isImportResultVisible;
    public bool IsImportResultVisible
    {
        get => _isImportResultVisible;
        private set
        {
            if (SetProperty(ref _isImportResultVisible, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _importResultSummary = string.Empty;
    public string ImportResultSummary
    {
        get => _importResultSummary;
        private set
        {
            if (SetProperty(ref _importResultSummary, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _importSessionSummary = "No import activity recorded by the workstation service.";
    public string ImportSessionSummary
    {
        get => _importSessionSummary;
        private set
        {
            if (SetProperty(ref _importSessionSummary, value))
            {
                RaisePropertyChanged(nameof(ImportSessionText));
            }
        }
    }

    private string _workflowStatusText = "Polling Security Master ingest and conflict posture.";
    public string WorkflowStatusText
    {
        get => _workflowStatusText;
        private set
        {
            if (SetProperty(ref _workflowStatusText, value))
            {
                RaisePropertyChanged(nameof(RuntimeStatusDetail));
            }
        }
    }

    private string _workflowRetrievedAtText = "-";
    public string WorkflowRetrievedAtText
    {
        get => _workflowRetrievedAtText;
        private set => SetProperty(ref _workflowRetrievedAtText, value);
    }

    private string _conflictOperatorText = "desktop-user";
    public string ConflictOperatorText
    {
        get => _conflictOperatorText;
        set
        {
            if (SetProperty(ref _conflictOperatorText, value))
            {
                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    private string _conflictNoteText = string.Empty;
    public string ConflictNoteText
    {
        get => _conflictNoteText;
        set => SetProperty(ref _conflictNoteText, value);
    }

    private SecurityMasterConflict? _selectedConflict;
    public SecurityMasterConflict? SelectedConflict
    {
        get => _selectedConflict;
        set
        {
            if (SetProperty(ref _selectedConflict, value))
            {
                NotifyConflictWorkflowCommandsChanged();
            }
        }
    }

    // ── Derived display helpers ─────────────────────────────────────────────
    public bool HasSelectedSecurity => SelectedSecurity is not null;

    public int ResultCount => Results.Count;

    public string SearchScopeText => string.IsNullOrWhiteSpace(SearchQuery)
        ? ActiveOnly
            ? "Active-only scope ready. Enter a symbol, name, or identifier."
            : "All-status scope ready. Enter a symbol, name, or identifier."
        : $"{(ActiveOnly ? "Active-only" : "All-status")} scope • query \"{SearchQuery.Trim()}\"";

    public string SelectedAssetClass =>
        SelectedSecurity?.Classification.AssetClass ?? string.Empty;

    public string SelectedCurrency =>
        SelectedSecurity?.EconomicDefinition.Currency ?? string.Empty;

    public string SelectedStatusBadge =>
        SelectedSecurity?.Status.ToString() ?? string.Empty;

    public string SelectedIdentifier =>
        SelectedSecurity?.Classification.PrimaryIdentifierValue is { } v
            ? $"{SelectedSecurity!.Classification.PrimaryIdentifierKind}: {v}"
            : string.Empty;

    public string RuntimeStatusLabel => _hasPolygonApiKey
        ? "Polygon enrichment available"
        : "Manual enrichment only";

    public string RuntimeStatusDetail => IsBackfillingTradingParams
        ? BackfillStatus
        : $"{_securityMasterRuntimeStatus.AvailabilityDescription} {WorkflowStatusText}".Trim();

    public string ConflictSummaryText => HasOpenConflicts
        ? $"{OpenConflictCount} identifier conflict{(OpenConflictCount == 1 ? string.Empty : "s")} require review."
        : "No open identifier conflicts detected.";

    public string SelectionSummaryText => SelectedSecurity is null
        ? "Select a security to inspect identifiers, runtime state, history, and corporate actions."
        : $"{SelectedSecurity.DisplayName} • {SelectedAssetClass} • {SelectedCurrency}";

    public string SelectionLifecycleText => SelectedSecurity is null
        ? "No security selected."
        : $"Status {SelectedStatusBadge} • version {SelectedSecurity.EconomicDefinition?.Version ?? 0}";

    public string CorporateActionSummaryText => SelectedSecurity is null
        ? "Corporate action timeline appears after a security is selected."
        : CorporateActions.Count == 0
            ? "No corporate actions recorded for the selected security."
            : $"{CorporateActions.Count} corporate action(s) loaded for the selected security.";

    public string LatestHistoryEventText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(HistoryText) || HistoryText == "(no history)")
            {
                return "No audit history loaded.";
            }

            return HistoryText
                       .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault()
                   ?? "No audit history loaded.";
        }
    }

    public string ImportSessionText => IsImporting
        ? ImportStatus
        : ImportSessionSummary;

    public string SelectedConflictSummaryText => SelectedConflict is null
        ? "Select a conflict to review the ingest-time mismatch and choose a resolution."
        : $"{SelectedConflict.FieldPath} • {SelectedConflict.ProviderA} vs {SelectedConflict.ProviderB}";

    // ── Commands ────────────────────────────────────────────────────────────
    public IRelayCommand CreateNewCommand { get; }
    public IRelayCommand EditSelectedCommand { get; }
    public IRelayCommand DeactivateSelectedCommand { get; }
    public IRelayCommand LoadCorporateActionsCommand { get; }
    public IRelayCommand ShowRecordCorpActionCommand { get; }
    public IRelayCommand CancelRecordCorpActionCommand { get; }
    public IRelayCommand RecordCorpActionCommand { get; }
    public IAsyncRelayCommand BackfillTradingParamsCommand { get; }
    public IAsyncRelayCommand ImportFromFileCommand { get; }
    public IRelayCommand CloseImportResultCommand { get; }
    public IAsyncRelayCommand RefreshConflictCountCommand { get; }
    public IAsyncRelayCommand RefreshWorkflowCommand { get; }
    public IAsyncRelayCommand AcceptPrimaryConflictCommand { get; }
    public IAsyncRelayCommand AcceptSecondaryConflictCommand { get; }
    public IAsyncRelayCommand DismissConflictCommand { get; }
    public IRelayCommand OpenFundPortfolioCommand { get; }
    public IRelayCommand OpenFundLedgerCommand { get; }
    public IRelayCommand OpenFundReconciliationCommand { get; }
    public IRelayCommand OpenFundCashFlowCommand { get; }
    public IRelayCommand OpenFundReportPackCommand { get; }

    // ── Conflict badge ───────────────────────────────────────────────────────
    private int _openConflictCount;
    /// <summary>Number of open identifier conflicts detected in Security Master. Drives the badge.</summary>
    public int OpenConflictCount
    {
        get => _openConflictCount;
        private set
        {
            if (SetProperty(ref _openConflictCount, value))
            {
                RaisePropertyChanged(nameof(HasOpenConflicts));
                RaisePropertyChanged(nameof(ConflictSummaryText));
            }
        }
    }

    /// <summary>True when at least one open conflict exists. Drives badge visibility.</summary>
    public bool HasOpenConflicts => _openConflictCount > 0;

    public bool CanResolveSelectedConflict =>
        SelectedConflict is not null &&
        !string.IsNullOrWhiteSpace(ConflictOperatorText);

    // ── Constructor ─────────────────────────────────────────────────────────
    public SecurityMasterViewModel(
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService,
        ITradingParametersBackfillService backfillService,
        ISecurityMasterImportService importService,
        ISecurityMasterRuntimeStatus securityMasterRuntimeStatus,
        WpfServices.ISecurityMasterOperatorWorkflowClient workflowClient,
        WpfServices.NavigationService navigationService,
        ISmQueryService queryService,
        ISmService service)
    {
        _loggingService = loggingService;
        _notificationService = notificationService;
        _backfillService = backfillService;
        _importService = importService;
        _securityMasterRuntimeStatus = securityMasterRuntimeStatus ?? throw new ArgumentNullException(nameof(securityMasterRuntimeStatus));
        _workflowClient = workflowClient ?? throw new ArgumentNullException(nameof(workflowClient));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _queryService = queryService;
        _service = service;
        _hasPolygonApiKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("POLYGON_API_KEY"));

        CreateNewCommand = new RelayCommand(OnCreateNew);
        EditSelectedCommand = new RelayCommand(OnEditSelected, () => HasSelectedSecurity);
        DeactivateSelectedCommand = new RelayCommand(OnDeactivateSelected, () => HasSelectedSecurity && IsSelectedSecurityActive());
        LoadCorporateActionsCommand = new AsyncRelayCommand(OnLoadCorporateActions, () => HasSelectedSecurity);
        ShowRecordCorpActionCommand = new RelayCommand(OnShowRecordCorpAction, () => HasSelectedSecurity);
        CancelRecordCorpActionCommand = new RelayCommand(OnCancelRecordCorpAction);
        RecordCorpActionCommand = new AsyncRelayCommand(OnRecordCorpAction);
        BackfillTradingParamsCommand = new AsyncRelayCommand(OnBackfillTradingParams);
        ImportFromFileCommand = new AsyncRelayCommand(OnImportFromFile, () => !IsImporting);
        CloseImportResultCommand = new RelayCommand(OnCloseImportResult);
        RefreshConflictCountCommand = new AsyncRelayCommand(RefreshConflictCountAsync);
        RefreshWorkflowCommand = new AsyncRelayCommand(RefreshOperatorWorkflowAsync);
        AcceptPrimaryConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("AcceptA", ct), () => CanResolveSelectedConflict);
        AcceptSecondaryConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("AcceptB", ct), () => CanResolveSelectedConflict);
        DismissConflictCommand = new AsyncRelayCommand(ct => ResolveSelectedConflictAsync("Dismiss", ct), () => CanResolveSelectedConflict);
        OpenFundPortfolioCommand = new RelayCommand(() => _navigationService.NavigateTo("FundPortfolio"));
        OpenFundLedgerCommand = new RelayCommand(() => _navigationService.NavigateTo("FundLedger"));
        OpenFundReconciliationCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReconciliation"));
        OpenFundCashFlowCommand = new RelayCommand(() => _navigationService.NavigateTo("FundCashFinancing"));
        OpenFundReportPackCommand = new RelayCommand(() => _navigationService.NavigateTo("FundReportPack"));

        Results.CollectionChanged += (_, _) => RaiseSearchDerivedStateChanged();
        CorporateActions.CollectionChanged += (_, _) => RaiseSelectionDerivedStateChanged();
        OpenConflicts.CollectionChanged += (_, _) =>
        {
            RaisePropertyChanged(nameof(ConflictSummaryText));
            RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        };

        StartWorkflowPolling();
    }

    private void OnCreateNew()
    {
        EditVm = SecurityMasterEditViewModel.CreateNew(_loggingService, _notificationService, _service);
        WireEditVmEvents();
        IsEditPanelVisible = true;
    }

    private void OnEditSelected()
    {
        if (SelectedSecurity is null)
            return;

        // Fetch the full detail so we have all the required information
        _ = LoadAndEditAsync();
    }

    private async Task LoadAndEditAsync()
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var detail = await _queryService.GetByIdAsync(id, CancellationToken.None)
                .ConfigureAwait(false);

            if (detail is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    EditVm = new SecurityMasterEditViewModel(_loggingService, _notificationService, _service);
                    EditVm.LoadForEdit(detail);
                    WireEditVmEvents();
                    IsEditPanelVisible = true;
                });
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Failed to load security {id} for edit", ex);
            StatusText = "Failed to load security for editing.";
            _notificationService.ShowNotification("Security Master", "Failed to load security.", NotificationType.Error);
        }
    }

    private void OnDeactivateSelected()
    {
        if (SelectedSecurity is null)
            return;

        DeactivateVm = new SecurityMasterDeactivateViewModel(_loggingService, _notificationService, _service)
        {
            SecurityName = SelectedSecurity.DisplayName,
            SecurityId = SelectedSecurity.SecurityId,
            Version = SelectedSecurity.EconomicDefinition?.Version ?? 0
        };
        WireDeactivateVmEvents();
        IsDeactivatePanelVisible = true;
    }

    private bool IsSelectedSecurityActive()
    {
        return SelectedSecurity?.Status == SecurityStatusDto.Active;
    }

    private void RaiseSearchDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ResultCount));
        RaisePropertyChanged(nameof(SearchScopeText));
    }

    private void RaiseSelectionDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(HasSelectedSecurity));
        RaisePropertyChanged(nameof(SelectedAssetClass));
        RaisePropertyChanged(nameof(SelectedCurrency));
        RaisePropertyChanged(nameof(SelectedStatusBadge));
        RaisePropertyChanged(nameof(SelectedIdentifier));
        RaisePropertyChanged(nameof(SelectionSummaryText));
        RaisePropertyChanged(nameof(SelectionLifecycleText));
        RaisePropertyChanged(nameof(CorporateActionSummaryText));
        RaisePropertyChanged(nameof(LatestHistoryEventText));
        NotifySelectionCommandsChanged();
    }

    private void NotifySelectionCommandsChanged()
    {
        EditSelectedCommand.NotifyCanExecuteChanged();
        DeactivateSelectedCommand.NotifyCanExecuteChanged();
        LoadCorporateActionsCommand.NotifyCanExecuteChanged();
        ShowRecordCorpActionCommand.NotifyCanExecuteChanged();
    }

    private void NotifyConflictWorkflowCommandsChanged()
    {
        RaisePropertyChanged(nameof(SelectedConflictSummaryText));
        RaisePropertyChanged(nameof(CanResolveSelectedConflict));
        AcceptPrimaryConflictCommand.NotifyCanExecuteChanged();
        AcceptSecondaryConflictCommand.NotifyCanExecuteChanged();
        DismissConflictCommand.NotifyCanExecuteChanged();
    }

    private void WireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested += OnEditCancelled;
        EditVm.SaveCompleted += OnEditSaveCompleted;
    }

    private void UnwireEditVmEvents()
    {
        if (EditVm is null)
            return;

        EditVm.CancelRequested -= OnEditCancelled;
        EditVm.SaveCompleted -= OnEditSaveCompleted;
    }

    private void OnEditCancelled()
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
    }

    private void OnEditSaveCompleted(SecurityDetailDto result)
    {
        UnwireEditVmEvents();
        IsEditPanelVisible = false;
        StatusText = "Security saved successfully.";
        
        // Refresh search results
        _ = SearchAsync();
    }

    private void WireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested += OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted += OnDeactivateCompleted;
    }

    private void UnwireDeactivateVmEvents()
    {
        if (DeactivateVm is null)
            return;

        DeactivateVm.CancelRequested -= OnDeactivateCancelled;
        DeactivateVm.DeactivateCompleted -= OnDeactivateCompleted;
    }

    private void OnDeactivateCancelled()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
    }

    private void OnDeactivateCompleted()
    {
        UnwireDeactivateVmEvents();
        IsDeactivatePanelVisible = false;
        StatusText = "Security deactivated successfully.";
        
        // Refresh search results
        _ = SearchAsync();
    }

    private async Task OnBackfillTradingParams()
    {
        try
        {
            IsBackfillingTradingParams = true;
            BackfillStatus = "Starting trading parameters backfill…";

            await _backfillService.BackfillAllAsync().ConfigureAwait(false);

            BackfillStatus = "Trading parameters backfill completed successfully.";
            _notificationService.ShowNotification("Security Master", 
                "Trading parameters backfilled successfully.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Trading parameters backfill failed", ex);
            BackfillStatus = "Backfill failed. Check logs for details.";
            _notificationService.ShowNotification("Security Master", 
                "Trading parameters backfill failed.", NotificationType.Error);
        }
        finally
        {
            IsBackfillingTradingParams = false;
        }
    }

    // ── Lifecycle ───────────────────────────────────────────────────────────
    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = null;
        _workflowPollingTask = null;
    }

    // ── Search ──────────────────────────────────────────────────────────────
    public async Task SearchAsync(CancellationToken ct = default)
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText = "Enter a query and press Search.";
            return;
        }

        if (!_securityMasterRuntimeStatus.IsAvailable)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Results.Clear();
            SelectedSecurity = null;
            HistoryText = string.Empty;
            StatusText = _securityMasterRuntimeStatus.AvailabilityDescription;
            return;
        }

        _cts?.Cancel();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var linked = _cts.Token;

        IsLoading = true;
        Results.Clear();
        SelectedSecurity = null;
        HistoryText = string.Empty;
        StatusText = "Searching…";

        try
        {
            var endpoint = $"/api/workstation/security-master/securities" +
                           $"?query={Uri.EscapeDataString(query)}&take=50&activeOnly={ActiveOnly}";

            var results = await ApiClientService.Instance
                .GetAsync<SecurityMasterWorkstationDto[]>(endpoint, linked)
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Results.Clear();
                if (results is { Length: > 0 })
                {
                    foreach (var r in results)
                        Results.Add(r);
                    StatusText = $"{results.Length} result{(results.Length == 1 ? "" : "s")} found.";
                }
                else
                {
                    StatusText = "No securities matched the query.";
                }
            });
        }
        catch (OperationCanceledException)
        {
            StatusText = "Search cancelled.";
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master search failed", ex);
            StatusText = "Search failed. Check connection to backend.";
            _notificationService.ShowNotification("Security Master", "Search failed.", NotificationType.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ── Detail + history ─────────────────────────────────────────────────────
    public async Task LoadDetailAsync(Guid securityId, CancellationToken ct = default)
    {
        IsLoading = true;
        HistoryText = string.Empty;
        CorporateActions.Clear();
        try
        {
            var detailTask = ApiClientService.Instance
                .GetAsync<SecurityMasterWorkstationDto>($"/api/workstation/security-master/securities/{securityId}", ct);
            var historyTask = ApiClientService.Instance
                .GetAsync<SecurityMasterEventEnvelope[]>($"/api/workstation/security-master/securities/{securityId}/history?take=20", ct);

            await Task.WhenAll(detailTask, historyTask).ConfigureAwait(false);

            var detail = await detailTask;
            var history = await historyTask;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (detail is not null)
                    SelectedSecurity = detail;

                HistoryText = history is { Length: > 0 }
                    ? string.Join(Environment.NewLine, history.Select(e =>
                        $"[{e.EventTimestamp:yyyy-MM-dd HH:mm}] {e.EventType}  v{e.StreamVersion}"))
                    : "(no history)";
            });

            // Load corporate actions
            await OnLoadCorporateActions(ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError($"Security Master detail load failed for {securityId}", ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void Dispose() => Stop();

    // ── Navigation parameter handling ───────────────────────────────────────
    private object? _parameter;
    public object? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                OnNavigationParameterReceived(value);
            }
        }
    }

    private async void OnNavigationParameterReceived(object? parameter)
    {
        try
        {
            if (parameter is string ticker)
            {
                // Pre-fill search with the ticker and execute search
                SearchQuery = ticker;
                await SearchAsync();
            }
            else if (parameter is Guid securityId)
            {
                // Load the specific security detail
                await LoadDetailAsync(securityId);
            }
        }
        catch (Exception ex)
        {
            _loggingService.LogError($"Navigation parameter handling failed: {parameter}", ex);
        }
    }

    // ── Corporate Actions ───────────────────────────────────────────────────
    private async Task OnLoadCorporateActions(CancellationToken ct = default)
    {
        if (SelectedSecurity?.SecurityId is not { } id)
            return;

        try
        {
            var actions = await _queryService.GetCorporateActionsAsync(id, ct)
                .ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                CorporateActions.Clear();
                foreach (var action in actions.OrderByDescending(a => a.ExDate))
                    CorporateActions.Add(action);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to load corporate actions", ex);
        }
    }

    private void OnShowRecordCorpAction()
    {
        IsRecordCorpActionVisible = true;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private void OnCancelRecordCorpAction()
    {
        IsRecordCorpActionVisible = false;
        CorpActType = "Dividend";
        CorpActExDate = string.Empty;
        CorpActAmount = 0m;
        CorpActCurrency = "USD";
    }

    private async Task OnRecordCorpAction(CancellationToken ct = default)
    {
        if (SelectedSecurity?.SecurityId is not { } securityId)
            return;

        if (string.IsNullOrWhiteSpace(CorpActExDate))
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter an ex-date.", NotificationType.Warning);
            return;
        }

        if (CorpActAmount <= 0)
        {
            _notificationService.ShowNotification("Corporate Actions", "Please enter a valid amount/ratio.", NotificationType.Warning);
            return;
        }

        try
        {
            // Parse the ex-date
            if (!DateOnly.TryParse(CorpActExDate, out var exDate))
            {
                _notificationService.ShowNotification("Corporate Actions", "Invalid date format. Use yyyy-MM-dd.", NotificationType.Warning);
                return;
            }

            // Build the CorporateActionDto
            var dto = new CorporateActionDto(
                CorpActId: Guid.NewGuid(),
                SecurityId: securityId,
                EventType: CorpActType,
                ExDate: exDate,
                PayDate: null,
                DividendPerShare: CorpActType == "Dividend" ? CorpActAmount : null,
                Currency: CorpActType == "Dividend" ? CorpActCurrency : null,
                SplitRatio: CorpActType == "StockSplit" ? CorpActAmount : null,
                NewSecurityId: null,
                DistributionRatio: null,
                AcquirerSecurityId: null,
                ExchangeRatio: null,
                SubscriptionPricePerShare: null,
                RightsPerShare: null);

            var result = await ApiClientService.Instance
                .PostAsync<CorporateActionDto>($"/api/workstation/security-master/securities/{securityId}/corporate-actions", dto, ct)
                .ConfigureAwait(false);

            if (result is not null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsRecordCorpActionVisible = false;
                    CorpActType = "Dividend";
                    CorpActExDate = string.Empty;
                    CorpActAmount = 0m;
                    CorpActCurrency = "USD";
                    _notificationService.ShowNotification("Corporate Actions", "Corporate action recorded successfully.", NotificationType.Success);
                });

                // Reload the list
                await OnLoadCorporateActions(ct);
            }
            else
            {
                _notificationService.ShowNotification("Corporate Actions", "Failed to record corporate action.", NotificationType.Error);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to record corporate action", ex);
            _notificationService.ShowNotification("Corporate Actions", "An error occurred while recording the corporate action.", NotificationType.Error);
        }
    }

    // ── Conflict badge ───────────────────────────────────────────────────────
    private async Task RefreshConflictCountAsync(CancellationToken ct = default)
    {
        await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
    }

    private void StartWorkflowPolling()
    {
        _workflowCts?.Cancel();
        _workflowCts?.Dispose();
        _workflowCts = new CancellationTokenSource();
        var token = _workflowCts.Token;

        _workflowPollingTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                await RefreshOperatorWorkflowAsync(token).ConfigureAwait(false);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    private async Task RefreshOperatorWorkflowAsync(CancellationToken ct = default)
    {
        try
        {
            var statusTask = _workflowClient.GetIngestStatusAsync(ct);
            var conflictsTask = _workflowClient.GetOpenConflictsAsync(ct);
            await Task.WhenAll(statusTask, conflictsTask).ConfigureAwait(false);

            var status = await statusTask.ConfigureAwait(false);
            var conflicts = await conflictsTask.ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OpenConflictCount = conflicts.Count;
                OpenConflicts.Clear();
                foreach (var conflict in conflicts.OrderBy(conflict => conflict.DetectedAt))
                {
                    OpenConflicts.Add(conflict);
                }

                SelectedConflict ??= OpenConflicts.FirstOrDefault();
                if (SelectedConflict is not null)
                {
                    SelectedConflict = OpenConflicts.FirstOrDefault(conflict => conflict.ConflictId == SelectedConflict.ConflictId)
                        ?? OpenConflicts.FirstOrDefault();
                }

                ApplyIngestStatus(status);
                NotifyConflictWorkflowCommandsChanged();
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to refresh Security Master operator workflow status", ex);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                WorkflowStatusText = "Operator workflow polling surface is unavailable.";
                WorkflowRetrievedAtText = "-";
            });
        }
    }

    private void ApplyIngestStatus(SecurityMasterIngestStatusResponse? status)
    {
        if (status is null)
        {
            WorkflowStatusText = "Ingest polling surface unavailable.";
            WorkflowRetrievedAtText = "-";
            ImportSessionSummary = "Security Master ingest status is unavailable from the workstation service.";
            return;
        }

        WorkflowRetrievedAtText = status.RetrievedAtUtc.LocalDateTime.ToString("g");
        if (status.IsImportActive && status.ActiveImport is not null)
        {
            WorkflowStatusText = $"Active ingest {status.ActiveImport.Processed}/{status.ActiveImport.Total} via {status.ActiveImport.FileExtension}.";
            ImportSessionSummary =
                $"Active ingest: {status.ActiveImport.Processed}/{status.ActiveImport.Total} processed • {status.ActiveImport.Imported} imported • {status.ActiveImport.Skipped} skipped • {status.ActiveImport.Failed} failed.";
            return;
        }

        if (status.LastCompleted is not null)
        {
            WorkflowStatusText = $"Last ingest completed {status.LastCompleted.CompletedAtUtc.LocalDateTime:g}.";
            ImportSessionSummary =
                $"Last ingest: {status.LastCompleted.Imported} imported • {status.LastCompleted.Skipped} skipped • {status.LastCompleted.Failed} failed • {status.LastCompleted.ConflictsDetected} conflicts.";
            return;
        }

        WorkflowStatusText = "No ingest activity has been recorded yet.";
        ImportSessionSummary = "No Security Master ingest has completed yet.";
    }

    private async Task ResolveSelectedConflictAsync(string resolution, CancellationToken ct = default)
    {
        if (SelectedConflict is null || string.IsNullOrWhiteSpace(ConflictOperatorText))
        {
            return;
        }

        try
        {
            var updated = await _workflowClient
                .ResolveConflictAsync(
                    SelectedConflict.ConflictId,
                    resolution,
                    ConflictOperatorText.Trim(),
                    string.IsNullOrWhiteSpace(ConflictNoteText) ? null : ConflictNoteText.Trim(),
                    ct)
                .ConfigureAwait(false);

            if (updated is null)
            {
                _notificationService.ShowNotification("Security Master", "Conflict no longer exists.", NotificationType.Warning);
                return;
            }

            ConflictNoteText = string.Empty;
            await RefreshOperatorWorkflowAsync(ct).ConfigureAwait(false);
            _notificationService.ShowNotification(
                "Security Master",
                resolution.Equals("Dismiss", StringComparison.OrdinalIgnoreCase)
                    ? "Conflict dismissed."
                    : "Conflict marked resolved.",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Failed to resolve Security Master conflict", ex);
            _notificationService.ShowNotification("Security Master", "Conflict resolution failed.", NotificationType.Error);
        }
    }

    // ── Bulk Import ──────────────────────────────────────────────────────────
    private async Task OnImportFromFile(CancellationToken ct = default)
    {
        var openDialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "CSV/JSON Files|*.csv;*.json",
            DefaultExt = ".csv",
            Title = "Import Securities"
        };

        if (openDialog.ShowDialog() != true)
            return;

        try
        {
            IsImporting = true;
            ImportTotal = 0;
            ImportProcessed = 0;
            ImportImported = 0;
            ImportFailed = 0;
            IsImportResultVisible = false;

            var fileContent = await System.IO.File.ReadAllTextAsync(openDialog.FileName, ct).ConfigureAwait(false);
            var fileExtension = System.IO.Path.GetExtension(openDialog.FileName);

            var progress = new Progress<SecurityMasterImportProgress>(p =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ImportTotal = p.Total;
                    ImportProcessed = p.Processed;
                    ImportImported = p.Imported;
                    ImportFailed = p.Failed;
                    RaisePropertyChanged(nameof(ImportStatus));
                });
            });

            var result = await _importService.ImportAsync(fileContent, fileExtension, progress, ct).ConfigureAwait(false);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ImportTotal = result.Imported + result.Skipped + result.Failed;
                ImportImported = result.Imported;
                ImportFailed = result.Failed;

                var summary = $"Imported {result.Imported} securities, Skipped {result.Skipped}, Failed {result.Failed}.";
                if (result.Errors.Any())
                {
                    summary += $"\r\nErrors:\r\n{string.Join("\r\n", result.Errors.Take(10))}";
                    if (result.Errors.Count > 10)
                        summary += $"\r\n... and {result.Errors.Count - 10} more errors.";
                }

                ImportResultSummary = summary;
                IsImportResultVisible = true;
                RaisePropertyChanged(nameof(ImportStatus));

                _notificationService.ShowNotification(
                    "Security Master Import",
                    $"Import completed: {result.Imported} imported, {result.Failed} failed.",
                    result.Failed == 0 ? NotificationType.Success : NotificationType.Warning);
            });

            // Refresh search results
            _ = SearchAsync();
            _ = RefreshOperatorWorkflowAsync();
        }
        catch (OperationCanceledException)
        {
            _notificationService.ShowNotification("Security Master Import", "Import cancelled.", NotificationType.Info);
        }
        catch (Exception ex)
        {
            _loggingService.LogError("Security Master import failed", ex);
            _notificationService.ShowNotification("Security Master Import", $"Import failed: {ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsImporting = false;
        }
    }

    private void OnCloseImportResult()
    {
        IsImportResultVisible = false;
        ImportResultSummary = string.Empty;
    }
}
