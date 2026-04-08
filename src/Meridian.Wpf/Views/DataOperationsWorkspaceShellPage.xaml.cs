using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class DataOperationsWorkspaceShellPage : Page
{
    private const string WorkspaceId = "data-operations";

    private readonly NavigationService _navigationService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly NotificationService _notificationService;

    public DataOperationsWorkspaceShellPage(
        NavigationService navigationService,
        WorkspaceShellContextService shellContextService,
        NotificationService notificationService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _shellContextService = shellContextService;
        _notificationService = notificationService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _shellContextService.SignalsChanged += OnSignalsChanged;
        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        _ = SaveDockLayoutAsync();
    }

    private async Task RefreshAsync()
    {
        var unreadAlerts = _shellContextService.GetUnreadAlertCount();
        var notifications = _notificationService.GetHistory().Take(4).ToArray();

        ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
        {
            WorkspaceTitle = "Data Operations Workspace",
            WorkspaceSubtitle = "Provider freshness, backfill pressure, symbol coverage, and storage posture in one fixed operator shell.",
            PrimaryScopeLabel = "Queue",
            PrimaryScopeValue = "Provider and storage posture",
            AsOfValue = DateTimeOffset.Now.ToString("MMM dd yyyy HH:mm"),
            FreshnessValue = unreadAlerts > 0 ? "Recent alerts present" : "Ready for operator review",
            ReviewStateLabel = "Backfill",
            ReviewStateValue = "Queue staged",
            ReviewStateTone = WorkspaceTone.Warning,
            CriticalLabel = "Critical",
            CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : "No urgent blockers",
            CriticalTone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
        });

        CommandBar.CommandGroup = new WorkspaceCommandGroup
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

        QueueScopeBadgeText.Text = unreadAlerts > 0 ? $"{unreadAlerts} alert-linked" : "Provider posture";
        QueueSummaryText.Text = "Supervise providers, backfill coverage, and storage posture without leaving the shell.";

        ProviderQueueList.ItemsSource = new[]
        {
            new WorkspaceQueueItem { Title = "Provider posture", Detail = "Validate provider readiness, fallback ordering, and health before starting backfills or storage handoff.", StatusLabel = "Ready", CountLabel = "3 routes", Tone = WorkspaceTone.Info, PrimaryActionId = "Provider", PrimaryActionLabel = "Open Providers", SecondaryActionId = "PackageManager", SecondaryActionLabel = "Packages" },
            new WorkspaceQueueItem { Title = "Symbol coverage and mapping", Detail = "Curate symbol lists and mapping quality before new historical coverage requests are staged.", StatusLabel = "Review", CountLabel = "Symbol ops", Tone = WorkspaceTone.Neutral, PrimaryActionId = "Symbols", PrimaryActionLabel = "Open Symbols", SecondaryActionId = "Provider", SecondaryActionLabel = "Providers" }
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
            ? $"Latest operational signal: {latest.Title}. Keep providers, sessions, and storage review in one operator shell."
            : "Provider readiness, historical coverage, and storage posture live in the same operator shell.";
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

    private async Task RestoreDockLayoutAsync()
    {
        try
        {
            var layout = await WorkspaceService.Instance.GetWorkspaceLayoutStateAsync(WorkspaceId);
            if (layout?.Panes.Count > 0)
            {
                foreach (var pane in layout.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(pane.PageTag, MapDockAction(pane.DockZone));
                }

                if (!string.IsNullOrWhiteSpace(layout.DockLayoutXml))
                {
                    DataOperationsDockManager.LoadLayout(layout.DockLayoutXml);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[DataOperationsWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }

        OpenWorkspacePage("Provider", PaneDropAction.Replace);
        OpenWorkspacePage("Backfill", PaneDropAction.SplitRight);
        OpenWorkspacePage("Storage", PaneDropAction.SplitBelow);
        OpenWorkspacePage("CollectionSessions", PaneDropAction.OpenTab);
    }

    private async Task SaveDockLayoutAsync()
    {
        try
        {
            var layout = DataOperationsDockManager.CaptureLayoutState("data-operations-workspace", "Data Operations Workspace");
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateAsync(WorkspaceId, layout);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[DataOperationsWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e) => OpenWorkspacePage(e.PageTag, e.Action);

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
            _navigationService.NavigateTo(actionId);
            return;
        }

        var dockAction = actionId switch
        {
            "Provider" => PaneDropAction.Replace,
            "Backfill" => PaneDropAction.SplitRight,
            "Storage" or "CollectionSessions" => PaneDropAction.SplitBelow,
            _ => PaneDropAction.OpenTab
        };

        OpenWorkspacePage(actionId, dockAction);
    }

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            DataOperationsDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, action);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[DataOperationsWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private void OnSignalsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(async () => await RefreshAsync());
            return;
        }

        _ = RefreshAsync();
    }

    private void OpenProviders_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("Provider");
    private void OpenBackfill_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("Backfill");
    private void OpenSymbols_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("Symbols");
    private void OpenStorage_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("Storage");
    private void OpenCollectionSessions_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("CollectionSessions");
    private void OpenPackageManager_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("PackageManager");

    private static string BuildPageKey(string pageTag, object? parameter) => parameter is null ? pageTag : $"{pageTag}:{parameter}";
    private static string GetPageTitle(string pageTag) => pageTag switch { "Provider" => "Providers", "Backfill" => "Backfill", "Storage" => "Storage", "CollectionSessions" => "Collection Sessions", "PackageManager" => "Package Manager", "DataExport" => "Data Export", _ => pageTag };
    private static PaneDropAction MapDockAction(string dockZone) => dockZone switch { "left" => PaneDropAction.SplitLeft, "right" => PaneDropAction.SplitRight, "bottom" => PaneDropAction.SplitBelow, "floating" => PaneDropAction.FloatWindow, _ => PaneDropAction.Replace };
}
