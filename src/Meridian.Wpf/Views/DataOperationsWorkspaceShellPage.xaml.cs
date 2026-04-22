using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Api;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using ProviderInfoModel = Meridian.Ui.Services.Services.ProviderInfo;
using StatusProviderInfoModel = Meridian.Ui.Services.Services.StatusProviderInfo;

namespace Meridian.Wpf.Views;

public partial class DataOperationsWorkspaceShellPage : DataOperationsWorkspaceShellPageBase
{
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly Meridian.Wpf.Services.NotificationService _notificationService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly StatusService _statusService;
    private readonly BackfillApiService _backfillApiService;
    private readonly BackfillCheckpointService _backfillCheckpointService;
    private readonly StorageService _storageService;
    private readonly CollectionSessionService _collectionSessionService;
    private readonly ScheduleManagerService _scheduleManagerService;
    private readonly BatchExportSchedulerService _exportSchedulerService;

    public DataOperationsWorkspaceShellPage(
        NavigationService navigationService,
        DataOperationsWorkspaceShellStateProvider stateProvider,
        DataOperationsWorkspaceShellViewModel viewModel,
        WorkspaceShellContextService shellContextService,
        WorkstationOperatingContextService? operatingContextService,
        Meridian.Wpf.Services.NotificationService notificationService,
        StatusService statusService,
        BackfillApiService backfillApiService,
        BackfillCheckpointService backfillCheckpointService,
        StorageService storageService,
        CollectionSessionService collectionSessionService,
        ScheduleManagerService scheduleManagerService,
        BatchExportSchedulerService exportSchedulerService)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _shellContextService = shellContextService;
        _operatingContextService = operatingContextService;
        _notificationService = notificationService;
        _statusService = statusService;
        _backfillApiService = backfillApiService;
        _backfillCheckpointService = backfillCheckpointService;
        _storageService = storageService;
        _collectionSessionService = collectionSessionService;
        _scheduleManagerService = scheduleManagerService;
        _exportSchedulerService = exportSchedulerService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _shellContextService.SignalsChanged += OnSignalsChanged;
        await RefreshAsync();
        await RestoreDockLayoutAsync(DataOperationsDockManager);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        _ = SaveDockLayoutAsync(DataOperationsDockManager);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var presentation = DataOperationsWorkspacePresentationBuilder.Build(await LoadWorkspaceDataAsync());
            ContextStrip.ShellContext = await _shellContextService.CreateAsync(presentation.Context);
            ViewModel.CommandGroup = presentation.CommandGroup;
            CommandBar.CommandGroup = presentation.CommandGroup;
            ApplyPresentation(presentation);
        }
        catch (Exception ex)
        {
            Meridian.Wpf.Services.LoggingService.Instance.LogError("[DataOperationsWorkspaceShell] Refresh failed", ex);
        }
    }

    private async Task<DataOperationsWorkspaceData> LoadWorkspaceDataAsync()
    {
        var unreadAlerts = _shellContextService.GetUnreadAlertCount();
        var notifications = _notificationService.GetHistory().Take(4).ToArray();
        var operatingContext = _operatingContextService?.CurrentContext;
        var scopeLabel = operatingContext is null
            ? "Provider and storage posture"
            : $"{operatingContext.ScopeKind.ToDisplayName()} · {operatingContext.DisplayName}";
        var scopeSummary = operatingContext is null
            ? "Provider posture, backfill priority, storage follow-up, and export delivery stay in one fixed shell."
            : $"Route providers, backfills, storage, and export jobs for {operatingContext.DisplayName} without leaving the shell.";

        var providersTask = LoadSafeAsync("provider catalog", async () => (await _statusService.GetAvailableProvidersAsync()).ToArray(), Array.Empty<ProviderInfoModel>());
        var providerStatusTask = LoadSafeAsync("provider status", () => _statusService.GetProviderStatusAsync(), default(StatusProviderInfoModel));
        var backfillHealthTask = LoadSafeAsync("backfill health", () => _backfillApiService.CheckProviderHealthAsync(), default(BackfillHealthResponse));
        var lastBackfillTask = LoadSafeAsync("backfill status", () => _backfillApiService.GetLastStatusAsync(), default(BackfillResultDto));
        var backfillExecutionsTask = LoadSafeAsync("backfill executions", async () => (await _backfillApiService.GetExecutionHistoryAsync(limit: 6)).ToArray(), Array.Empty<BackfillExecution>());
        var resumableJobsTask = LoadSafeAsync("backfill checkpoints", async () => (await _backfillCheckpointService.GetResumableJobsAsync()).ToArray(), Array.Empty<BackfillCheckpoint>());
        var backfillSchedulesTask = LoadSafeAsync("backfill schedules", async () => (await _scheduleManagerService.GetBackfillSchedulesAsync())?.ToArray() ?? Array.Empty<BackfillSchedule>(), Array.Empty<BackfillSchedule>());
        var storageStatsTask = LoadSafeAsync("storage stats", () => _storageService.GetStorageStatsAsync(), default(StorageStatsSummary));
        var storageHealthTask = LoadSafeAsync("storage health", () => _storageService.GetStorageHealthAsync(), default(StorageHealthReport));
        var activeSessionTask = LoadSafeAsync("active session", () => _collectionSessionService.GetActiveSessionAsync(), default(Meridian.Contracts.Session.CollectionSession));
        var sessionsTask = LoadSafeAsync("session history", async () => (await _collectionSessionService.GetSessionsAsync()).ToArray(), Array.Empty<Meridian.Contracts.Session.CollectionSession>());
        var exportJobsTask = LoadSafeAsync("export jobs", async () => (await _exportSchedulerService.ReadPersistedJobsAsync()).ToArray(), Array.Empty<ExportJob>());

        await Task.WhenAll(
            providersTask,
            providerStatusTask,
            backfillHealthTask,
            lastBackfillTask,
            backfillExecutionsTask,
            resumableJobsTask,
            backfillSchedulesTask,
            storageStatsTask,
            storageHealthTask,
            activeSessionTask,
            sessionsTask,
            exportJobsTask);

        return new DataOperationsWorkspaceData
        {
            ScopeLabel = scopeLabel,
            ScopeSummary = scopeSummary,
            RetrievedAt = DateTimeOffset.Now,
            UnreadAlerts = unreadAlerts,
            Notifications = notifications,
            Providers = await providersTask,
            ProviderStatus = await providerStatusTask,
            BackfillHealth = await backfillHealthTask,
            LastBackfillStatus = await lastBackfillTask,
            BackfillExecutions = await backfillExecutionsTask,
            ResumableJobs = await resumableJobsTask,
            BackfillSchedules = await backfillSchedulesTask,
            StorageStats = await storageStatsTask,
            StorageHealth = await storageHealthTask,
            ActiveSession = await activeSessionTask,
            Sessions = await sessionsTask,
            ExportJobs = await exportJobsTask
        };
    }

    private void ApplyPresentation(DataOperationsWorkspacePresentation presentation)
    {
        QueueScopeBadgeText.Text = presentation.QueueScopeBadgeText;
        QueueSummaryText.Text = presentation.QueueSummaryText;
        ProviderQueueList.ItemsSource = presentation.ProviderQueueItems;
        BackfillQueueList.ItemsSource = presentation.BackfillQueueItems;
        StorageQueueList.ItemsSource = presentation.StorageQueueItems;

        OperationsSummaryTitleText.Text = presentation.OperationsSummaryTitleText;
        OperationsSummaryDetailText.Text = presentation.OperationsSummaryDetailText;
        SummaryProvidersText.Text = NormalizeSummaryText(presentation.SummaryProvidersText, DataOperationsWorkspacePresentationBuilder.ProvidersUnavailableSummary);
        SummaryProvidersText.Foreground = ResolveToneBrush(presentation.SummaryProvidersTone);
        SummaryBackfillText.Text = NormalizeSummaryText(presentation.SummaryBackfillText, DataOperationsWorkspacePresentationBuilder.BackfillUnavailableSummary);
        SummaryBackfillText.Foreground = ResolveToneBrush(presentation.SummaryBackfillTone);
        SummaryStorageText.Text = NormalizeSummaryText(presentation.SummaryStorageText, DataOperationsWorkspacePresentationBuilder.StorageUnavailableSummary);
        SummaryStorageText.Foreground = ResolveToneBrush(presentation.SummaryStorageTone);
        RecentOperationsList.ItemsSource = presentation.RecentOperations;
    }

    private static string NormalizeSummaryText(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var normalized = value.Trim();
        return normalized is "Loading..." or "Loading…" or "—" ? fallback : normalized;
    }

    private static async Task<T> LoadSafeAsync<T>(string operationName, Func<Task<T>> loader, T fallback)
    {
        try
        {
            var result = await loader();
            return result is null ? fallback : result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Meridian.Wpf.Services.LoggingService.Instance.LogWarning($"[DataOperationsWorkspaceShell] {operationName} unavailable: {ex.Message}");
            return fallback;
        }
    }

    private Brush ResolveToneBrush(string tone)
    {
        var resourceKey = tone switch
        {
            WorkspaceTone.Success => "SuccessColorBrush",
            WorkspaceTone.Warning => "WarningColorBrush",
            WorkspaceTone.Danger => "ErrorColorBrush",
            WorkspaceTone.Info => "InfoColorBrush",
            _ => "ConsoleTextPrimaryBrush"
        };

        return TryFindResource(resourceKey) as Brush ?? Brushes.White;
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(DataOperationsDockManager, e.PageTag, e.Action);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e) => ExecuteAction(e.Command.Id, navigate: true);
    private void OnQueuePrimaryActionClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string actionId }) ExecuteAction(actionId, navigate: false); }
    private void OnQueueSecondaryActionClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string actionId }) ExecuteAction(actionId, navigate: false); }
    private void OnRecentActionClick(object sender, RoutedEventArgs e) { if (sender is Button { Tag: string actionId }) ExecuteAction(actionId, navigate: false); }

    private void ExecuteAction(string actionId, bool navigate)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (navigate)
        {
            NavigationService.NavigateTo(actionId);
            return;
        }

        var dockAction = actionId switch
        {
            "Provider" or "ProviderHealth" => PaneDropAction.Replace,
            "Backfill" or "Schedules" => PaneDropAction.SplitRight,
            "Storage" or "CollectionSessions" or "DataExport" => PaneDropAction.SplitBelow,
            _ => PaneDropAction.OpenTab
        };

        OpenWorkspacePage(DataOperationsDockManager, actionId, dockAction);
    }

    private void OnSignalsChanged(object? sender, EventArgs e)
        => DispatchRefresh(RefreshAsync);

    private void OpenProviders_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Provider");
    private void OpenBackfill_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Backfill");
    private void OpenSymbols_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Symbols");
    private void OpenStorage_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Storage");
    private void OpenCollectionSessions_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("CollectionSessions");
    private void OpenDataExport_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("DataExport");
    private void OpenSchedules_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Schedules");
    private void OpenPackageManager_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("PackageManager");
}
