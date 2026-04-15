using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

public partial class DataOperationsWorkspaceShellPage : DataOperationsWorkspaceShellPageBase
{
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly Meridian.Wpf.Services.NotificationService _notificationService;
    private readonly WorkstationOperatingContextService? _operatingContextService;

    public DataOperationsWorkspaceShellPage(
        NavigationService navigationService,
        WorkspaceService workspaceService,
        DataOperationsWorkspaceShellStateProvider stateProvider,
        DataOperationsWorkspaceShellViewModel viewModel,
        WorkspaceShellContextService shellContextService,
        WorkstationOperatingContextService? operatingContextService,
        Meridian.Wpf.Services.NotificationService notificationService)
        : base(navigationService, workspaceService, stateProvider, viewModel)
    {
        InitializeComponent();
        _shellContextService = shellContextService;
        _operatingContextService = operatingContextService;
        _notificationService = notificationService;
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
        var unreadAlerts = _shellContextService.GetUnreadAlertCount();
        var notifications = _notificationService.GetHistory().Take(4).ToArray();
        var operatingContext = _operatingContextService?.CurrentContext;
        var scopeLabel = operatingContext is null
            ? "Provider and storage posture"
            : $"{operatingContext.ScopeKind.ToDisplayName()} · {operatingContext.DisplayName}";
        var scopeSummary = operatingContext is null
            ? "Provider posture, backfill priority, and storage follow-up stay in one fixed shell."
            : $"Route providers, backfills, and storage work for {operatingContext.DisplayName} without leaving the shell.";

        ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
        {
            WorkspaceTitle = "Data Operations Workspace",
            WorkspaceSubtitle = "Provider freshness, backfill pressure, symbol coverage, and storage posture in one fixed operator shell.",
            PrimaryScopeLabel = "Queue",
            PrimaryScopeValue = scopeLabel,
            AsOfValue = DateTimeOffset.Now.ToString("MMM dd yyyy HH:mm"),
            FreshnessValue = unreadAlerts > 0 ? "Recent alerts present" : "Ready for operator review",
            ReviewStateLabel = "Backfill",
            ReviewStateValue = "Queue staged",
            ReviewStateTone = WorkspaceTone.Warning,
            CriticalLabel = "Critical",
            CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : "No urgent blockers",
            CriticalTone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
        });

        ViewModel.CommandGroup = new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "Provider", Label = "Providers", Description = "Open providers", ShortcutHint = "Ctrl+1", Glyph = "\uE8B9", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "Backfill", Label = "Run Backfill", Description = "Open backfill queue", ShortcutHint = "Ctrl+2", Glyph = "\uE896" },
                new WorkspaceCommandItem { Id = "Symbols", Label = "Manage Symbols", Description = "Open symbols", ShortcutHint = "Ctrl+3", Glyph = "\uE8EF" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Storage", Label = "Review Storage", Description = "Open storage", Glyph = "\uE8B7" },
                new WorkspaceCommandItem { Id = "CollectionSessions", Label = "Collection Sessions", Description = "Open sessions", Glyph = "\uE823" },
                new WorkspaceCommandItem { Id = "PackageManager", Label = "Package Manager", Description = "Open package manager", Glyph = "\uE8A5" },
                new WorkspaceCommandItem { Id = "DataExport", Label = "Data Export", Description = "Open data export", Glyph = "\uE8FE" },
                new WorkspaceCommandItem { Id = "Schedules", Label = "Schedules", Description = "Open schedules", Glyph = "\uE823" }
            ]
        };
        CommandBar.CommandGroup = ViewModel.CommandGroup;

        QueueScopeBadgeText.Text = unreadAlerts > 0 ? $"{scopeLabel} · {unreadAlerts} alert-linked" : scopeLabel;
        QueueSummaryText.Text = scopeSummary;

        ProviderQueueList.ItemsSource = new[]
        {
            new WorkspaceQueueItem { Title = "Provider posture", Detail = $"Validate provider readiness, fallback ordering, and health before starting backfills or storage handoff for {scopeLabel}.", StatusLabel = "Ready", CountLabel = "3 routes", Tone = WorkspaceTone.Info, PrimaryActionId = "Provider", PrimaryActionLabel = "Open Providers", SecondaryActionId = "PackageManager", SecondaryActionLabel = "Packages" },
            new WorkspaceQueueItem { Title = "Symbol coverage and mapping", Detail = $"Curate symbol lists and mapping quality before new historical coverage requests are staged for {scopeLabel}.", StatusLabel = "Review", CountLabel = "Symbol ops", Tone = WorkspaceTone.Neutral, PrimaryActionId = "Symbols", PrimaryActionLabel = "Open Symbols", SecondaryActionId = "Provider", SecondaryActionLabel = "Providers" }
        };

        BackfillQueueList.ItemsSource = new[]
        {
            new WorkspaceQueueItem { Title = "Backfill queue", Detail = "Inspect resumable jobs, provider priority, and historical gap-fill work before starting the next ingest run.", StatusLabel = "Queued", CountLabel = "Historical", Tone = WorkspaceTone.Warning, PrimaryActionId = "Backfill", PrimaryActionLabel = "Open Backfill", SecondaryActionId = "CollectionSessions", SecondaryActionLabel = "Sessions" }
        };

        StorageQueueList.ItemsSource = new[]
        {
            new WorkspaceQueueItem { Title = "Storage and export posture", Detail = "Review storage layout, package state, and export readiness before operational handoff.", StatusLabel = unreadAlerts > 0 ? "Watch" : "Stable", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} alerts" : "Storage ready", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Storage", PrimaryActionLabel = "Open Storage", SecondaryActionId = "DataExport", SecondaryActionLabel = "Data Export" }
        };

        OperationsSummaryTitleText.Text = "Data Operations";
        OperationsSummaryDetailText.Text = notifications.FirstOrDefault() is { } latest
            ? $"Latest operational signal: {latest.Title}. Keep providers, sessions, and storage review aligned to {scopeLabel}."
            : $"Provider readiness, historical coverage, and storage posture live in the same operator shell for {scopeLabel}.";
        SummaryProvidersText.Text = "3";
        SummaryBackfillText.Text = "Queue";
        SummaryStorageText.Text = unreadAlerts > 0 ? "Watch" : "Stable";

        RecentOperationsList.ItemsSource = notifications.Length > 0
            ? notifications.Take(3).Select(notification => new WorkspaceRecentItem
            {
                Title = notification.Title,
                Detail = notification.Message,
                Meta = $"{notification.Timestamp:g} · {notification.Type}",
                Tone = notification.IsRead ? WorkspaceTone.Neutral : WorkspaceTone.Warning,
                ActionId = "CollectionSessions",
                ActionLabel = "Open Sessions"
            }).ToArray()
            : new[]
            {
                new WorkspaceRecentItem { Title = "Collection sessions", Detail = "Inspect recent ingest session history and resumable work from the shell.", Meta = "No recent notifications", Tone = WorkspaceTone.Info, ActionId = "CollectionSessions", ActionLabel = "Open Sessions" },
                new WorkspaceRecentItem { Title = "Package handoff", Detail = "Move prepared datasets into package and export workflows when operational handoff is required.", Meta = "Support path", Tone = WorkspaceTone.Neutral, ActionId = "PackageManager", ActionLabel = "Open Packages" }
            };
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
            "Provider" => PaneDropAction.Replace,
            "Backfill" => PaneDropAction.SplitRight,
            "Storage" or "CollectionSessions" => PaneDropAction.SplitBelow,
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
    private void OpenPackageManager_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("PackageManager");
}
