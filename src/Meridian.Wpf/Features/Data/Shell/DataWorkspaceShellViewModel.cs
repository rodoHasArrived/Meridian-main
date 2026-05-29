using System.Windows;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Features.Data.Shell;

public enum DataWorkspaceShellActionKind
{
    Navigate,
    OpenPane,
    SwitchContext
}

public sealed class DataWorkspaceShellActionRequestedEventArgs : EventArgs
{
    public DataWorkspaceShellActionRequestedEventArgs(
        DataWorkspaceShellActionKind kind,
        string actionId,
        PaneDropAction dockAction = PaneDropAction.OpenTab)
    {
        Kind = kind;
        ActionId = actionId;
        DockAction = dockAction;
    }

    public DataWorkspaceShellActionKind Kind { get; }
    public string ActionId { get; }
    public PaneDropAction DockAction { get; }
}

public sealed class DataWorkspaceShellViewModel : WorkspaceShellViewModelBase
{
    private readonly IDataWorkspaceShellSnapshotService? _snapshotService;
    private readonly IDataWorkspaceShellPresentationService _presentationService;
    private readonly WorkspaceShellContextService? _shellContextService;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private bool _isMonitoringSignals;
    private WorkspaceShellContext _shellContext = new();
    private DataOperationsHeroState _heroState = DataOperationsHeroState.Loading();
    private IReadOnlyList<DataOperationsHeroMetric> _heroMetrics = DataOperationsHeroMetric.LoadingMetrics();
    private string _heroScopeText = "Provider and storage health";
    private string _heroSummaryText = "Provider health, backfill priority, storage follow-up, and export delivery stay in one fixed shell.";
    private string _queueScopeBadgeText = "Provider posture";
    private string _queueSummaryText = "Provider posture, backfill priority, and storage follow-up stay in one fixed shell.";
    private IReadOnlyList<WorkspaceQueueItem> _providerQueueItems = Array.Empty<WorkspaceQueueItem>();
    private IReadOnlyList<WorkspaceQueueItem> _backfillQueueItems = Array.Empty<WorkspaceQueueItem>();
    private IReadOnlyList<WorkspaceQueueItem> _storageQueueItems = Array.Empty<WorkspaceQueueItem>();
    private WorkspaceQueueRegionState _providerQueueState = WorkspaceQueueRegionState.Loading("Loading provider queue", "Refreshing provider readiness and connectivity telemetry.");
    private WorkspaceQueueRegionState _backfillQueueState = WorkspaceQueueRegionState.Loading("Loading backfill queue", "Refreshing backfill checkpoints and execution posture.");
    private WorkspaceQueueRegionState _storageQueueState = WorkspaceQueueRegionState.Loading("Loading storage queue", "Refreshing storage health, export, and capacity signals.");
    private string _operationsSummaryTitleText = "Data";
    private string _operationsSummaryDetailText = "Provider readiness, historical coverage, and storage posture live in the same operator shell.";
    private string _summaryProvidersText = DataOperationsWorkspacePresentationBuilder.ProvidersUnavailableSummary;
    private string _summaryProvidersTone = WorkspaceTone.Warning;
    private string _summaryBackfillText = DataOperationsWorkspacePresentationBuilder.BackfillUnavailableSummary;
    private string _summaryBackfillTone = WorkspaceTone.Neutral;
    private string _summaryStorageText = DataOperationsWorkspacePresentationBuilder.StorageUnavailableSummary;
    private string _summaryStorageTone = WorkspaceTone.Success;
    private IReadOnlyList<WorkspaceRecentItem> _recentOperations = Array.Empty<WorkspaceRecentItem>();

    public DataWorkspaceShellViewModel(
        IDataWorkspaceShellSnapshotService? snapshotService = null,
        IDataWorkspaceShellPresentationService? presentationService = null,
        WorkspaceShellContextService? shellContextService = null)
        : base(ShellNavigationCatalog.GetWorkspaceShell("data")!)
    {
        _snapshotService = snapshotService;
        _presentationService = presentationService ?? new DataWorkspaceShellPresentationService();
        _shellContextService = shellContextService;
    }

    public event EventHandler<DataWorkspaceShellActionRequestedEventArgs>? ActionRequested;
    public event EventHandler? RefreshRequested;

    public WorkspaceShellContext ShellContext
    {
        get => _shellContext;
        private set => SetProperty(ref _shellContext, value);
    }

    public DataOperationsHeroState HeroState
    {
        get => _heroState;
        private set
        {
            if (SetProperty(ref _heroState, value))
            {
                RaisePropertyChanged(nameof(HeroPrimaryActionVisibility));
                RaisePropertyChanged(nameof(HeroSecondaryActionVisibility));
            }
        }
    }

    public Visibility HeroPrimaryActionVisibility => HasHeroAction(HeroState.PrimaryActionLabel, HeroState.PrimaryActionId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility HeroSecondaryActionVisibility => HasHeroAction(HeroState.SecondaryActionLabel, HeroState.SecondaryActionId)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public IReadOnlyList<DataOperationsHeroMetric> HeroMetrics
    {
        get => _heroMetrics;
        private set => SetProperty(ref _heroMetrics, value);
    }

    public string HeroScopeText
    {
        get => _heroScopeText;
        private set => SetProperty(ref _heroScopeText, value);
    }

    public string HeroSummaryText
    {
        get => _heroSummaryText;
        private set => SetProperty(ref _heroSummaryText, value);
    }

    public string QueueScopeBadgeText
    {
        get => _queueScopeBadgeText;
        private set => SetProperty(ref _queueScopeBadgeText, value);
    }

    public string QueueSummaryText
    {
        get => _queueSummaryText;
        private set => SetProperty(ref _queueSummaryText, value);
    }

    public IReadOnlyList<WorkspaceQueueItem> ProviderQueueItems
    {
        get => _providerQueueItems;
        private set => SetProperty(ref _providerQueueItems, value);
    }

    public IReadOnlyList<WorkspaceQueueItem> BackfillQueueItems
    {
        get => _backfillQueueItems;
        private set => SetProperty(ref _backfillQueueItems, value);
    }

    public IReadOnlyList<WorkspaceQueueItem> StorageQueueItems
    {
        get => _storageQueueItems;
        private set => SetProperty(ref _storageQueueItems, value);
    }

    public WorkspaceQueueRegionState ProviderQueueState
    {
        get => _providerQueueState;
        private set
        {
            if (SetProperty(ref _providerQueueState, value))
            {
                RaisePropertyChanged(nameof(ProviderQueueListVisibility));
                RaisePropertyChanged(nameof(ProviderQueueStateVisibility));
            }
        }
    }

    public Visibility ProviderQueueListVisibility => ProviderQueueState.IsVisible
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ProviderQueueStateVisibility => ProviderQueueState.IsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public WorkspaceQueueRegionState BackfillQueueState
    {
        get => _backfillQueueState;
        private set
        {
            if (SetProperty(ref _backfillQueueState, value))
            {
                RaisePropertyChanged(nameof(BackfillQueueListVisibility));
                RaisePropertyChanged(nameof(BackfillQueueStateVisibility));
            }
        }
    }

    public Visibility BackfillQueueListVisibility => BackfillQueueState.IsVisible
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility BackfillQueueStateVisibility => BackfillQueueState.IsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public WorkspaceQueueRegionState StorageQueueState
    {
        get => _storageQueueState;
        private set
        {
            if (SetProperty(ref _storageQueueState, value))
            {
                RaisePropertyChanged(nameof(StorageQueueListVisibility));
                RaisePropertyChanged(nameof(StorageQueueStateVisibility));
            }
        }
    }

    public Visibility StorageQueueListVisibility => StorageQueueState.IsVisible
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility StorageQueueStateVisibility => StorageQueueState.IsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string OperationsSummaryTitleText
    {
        get => _operationsSummaryTitleText;
        private set => SetProperty(ref _operationsSummaryTitleText, value);
    }

    public string OperationsSummaryDetailText
    {
        get => _operationsSummaryDetailText;
        private set => SetProperty(ref _operationsSummaryDetailText, value);
    }

    public string SummaryProvidersText
    {
        get => _summaryProvidersText;
        private set => SetProperty(ref _summaryProvidersText, value);
    }

    public string SummaryProvidersTone
    {
        get => _summaryProvidersTone;
        private set => SetProperty(ref _summaryProvidersTone, value);
    }

    public string SummaryBackfillText
    {
        get => _summaryBackfillText;
        private set => SetProperty(ref _summaryBackfillText, value);
    }

    public string SummaryBackfillTone
    {
        get => _summaryBackfillTone;
        private set => SetProperty(ref _summaryBackfillTone, value);
    }

    public string SummaryStorageText
    {
        get => _summaryStorageText;
        private set => SetProperty(ref _summaryStorageText, value);
    }

    public string SummaryStorageTone
    {
        get => _summaryStorageTone;
        private set => SetProperty(ref _summaryStorageTone, value);
    }

    public IReadOnlyList<WorkspaceRecentItem> RecentOperations
    {
        get => _recentOperations;
        private set => SetProperty(ref _recentOperations, value);
    }

    public void Start()
    {
        if (_shellContextService is null || _isMonitoringSignals)
        {
            return;
        }

        _shellContextService.SignalsChanged += OnSignalsChanged;
        _isMonitoringSignals = true;
    }

    public void Stop()
    {
        if (_shellContextService is null || !_isMonitoringSignals)
        {
            return;
        }

        _shellContextService.SignalsChanged -= OnSignalsChanged;
        _isMonitoringSignals = false;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_snapshotService is null)
        {
            ApplyErrorState();
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            ApplyLoadingState();
            var data = await _snapshotService.LoadAsync(cancellationToken);
            var presentation = _presentationService.Build(data);
            var shellContext = _shellContextService is null
                ? ShellContext
                : await _shellContextService.CreateAsync(presentation.Context, cancellationToken);

            ApplyPresentation(presentation, shellContext);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError("[DataWorkspaceShell] Refresh failed", ex);
            ApplyErrorState();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task ExecuteActionAsync(string actionId, bool navigate)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (string.Equals(actionId, "Retry", StringComparison.Ordinal))
        {
            await RefreshAsync();
            return;
        }

        if (string.Equals(actionId, "SwitchContext", StringComparison.Ordinal))
        {
            ActionRequested?.Invoke(
                this,
                new DataWorkspaceShellActionRequestedEventArgs(DataWorkspaceShellActionKind.SwitchContext, actionId));
            return;
        }

        if (navigate)
        {
            ActionRequested?.Invoke(
                this,
                new DataWorkspaceShellActionRequestedEventArgs(DataWorkspaceShellActionKind.Navigate, actionId));
            return;
        }

        ActionRequested?.Invoke(
            this,
            new DataWorkspaceShellActionRequestedEventArgs(
                DataWorkspaceShellActionKind.OpenPane,
                actionId,
                ResolveDockAction(actionId)));
    }

    private void ApplyLoadingState()
    {
        ProviderQueueItems = Array.Empty<WorkspaceQueueItem>();
        BackfillQueueItems = Array.Empty<WorkspaceQueueItem>();
        StorageQueueItems = Array.Empty<WorkspaceQueueItem>();
        ProviderQueueState = WorkspaceQueueRegionState.Loading("Loading provider queue", "Refreshing provider readiness and connectivity telemetry.");
        BackfillQueueState = WorkspaceQueueRegionState.Loading("Loading backfill queue", "Refreshing backfill checkpoints and execution posture.");
        StorageQueueState = WorkspaceQueueRegionState.Loading("Loading storage queue", "Refreshing storage health, export, and capacity signals.");
        HeroState = DataOperationsHeroState.Loading();
        HeroMetrics = DataOperationsHeroMetric.LoadingMetrics();
    }

    private void ApplyPresentation(DataOperationsWorkspacePresentation presentation, WorkspaceShellContext shellContext)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        ShellContext = shellContext ?? new WorkspaceShellContext();
        CommandGroup = presentation.CommandGroup;
        HeroScopeText = presentation.Context.PrimaryScopeValue;
        HeroSummaryText = presentation.QueueSummaryText;
        HeroState = presentation.HeroState;
        HeroMetrics = presentation.HeroMetrics;
        QueueScopeBadgeText = presentation.QueueScopeBadgeText;
        QueueSummaryText = presentation.QueueSummaryText;
        ProviderQueueItems = presentation.ProviderQueueItems;
        BackfillQueueItems = presentation.BackfillQueueItems;
        StorageQueueItems = presentation.StorageQueueItems;
        ProviderQueueState = presentation.ProviderQueueState;
        BackfillQueueState = presentation.BackfillQueueState;
        StorageQueueState = presentation.StorageQueueState;
        OperationsSummaryTitleText = presentation.OperationsSummaryTitleText;
        OperationsSummaryDetailText = presentation.OperationsSummaryDetailText;
        SummaryProvidersText = NormalizeSummaryText(presentation.SummaryProvidersText, DataOperationsWorkspacePresentationBuilder.ProvidersUnavailableSummary);
        SummaryProvidersTone = presentation.SummaryProvidersTone;
        SummaryBackfillText = NormalizeSummaryText(presentation.SummaryBackfillText, DataOperationsWorkspacePresentationBuilder.BackfillUnavailableSummary);
        SummaryBackfillTone = presentation.SummaryBackfillTone;
        SummaryStorageText = NormalizeSummaryText(presentation.SummaryStorageText, DataOperationsWorkspacePresentationBuilder.StorageUnavailableSummary);
        SummaryStorageTone = presentation.SummaryStorageTone;
        RecentOperations = presentation.RecentOperations;
    }

    private void ApplyErrorState()
    {
        ProviderQueueItems = Array.Empty<WorkspaceQueueItem>();
        BackfillQueueItems = Array.Empty<WorkspaceQueueItem>();
        StorageQueueItems = Array.Empty<WorkspaceQueueItem>();
        ProviderQueueState = WorkspaceQueueRegionState.Error("Provider queue degraded", "Provider telemetry is unavailable right now.", "Retry", "Retry", "Open Diagnostics", "Diagnostics");
        BackfillQueueState = WorkspaceQueueRegionState.Error("Backfill queue degraded", "Backfill telemetry could not be refreshed.", "Retry", "Retry", "Open Diagnostics", "Diagnostics");
        StorageQueueState = WorkspaceQueueRegionState.Error("Storage queue degraded", "Storage and export telemetry could not be refreshed.", "Retry", "Retry", "Open Diagnostics", "Diagnostics");
        HeroState = DataOperationsHeroState.Error();
        HeroMetrics = DataOperationsHeroMetric.ErrorMetrics();
    }

    private static string NormalizeSummaryText(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized is "Loading" or "Loading..." or "-" ? fallback : normalized;
    }

    private static bool HasHeroAction(string label, string actionId)
        => !string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(actionId);

    private static PaneDropAction ResolveDockAction(string actionId) => actionId switch
    {
        "Provider" or "ProviderHealth" => PaneDropAction.Replace,
        "Backfill" or "Schedules" => PaneDropAction.SplitRight,
        "Storage" or "CollectionSessions" or "DataExport" => PaneDropAction.SplitBelow,
        _ => PaneDropAction.OpenTab
    };

    private void OnSignalsChanged(object? sender, EventArgs e)
        => RefreshRequested?.Invoke(this, EventArgs.Empty);
}
