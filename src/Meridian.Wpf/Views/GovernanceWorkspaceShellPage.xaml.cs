using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

public partial class GovernanceWorkspaceShellPage : Page
{
    private const string WorkspaceId = "governance";

    private readonly NavigationService _navigationService;
    private readonly FundContextService _fundContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly FundLedgerReadService _fundLedgerReadService;
    private readonly ReconciliationReadService _reconciliationReadService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly NotificationService _notificationService;

    public GovernanceWorkspaceShellPage(
        NavigationService navigationService,
        FundContextService fundContextService,
        WorkspaceShellContextService shellContextService,
        FundLedgerReadService fundLedgerReadService,
        ReconciliationReadService reconciliationReadService,
        CashFinancingReadService cashFinancingReadService,
        NotificationService notificationService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _fundContextService = fundContextService;
        _shellContextService = shellContextService;
        _fundLedgerReadService = fundLedgerReadService;
        _reconciliationReadService = reconciliationReadService;
        _cashFinancingReadService = cashFinancingReadService;
        _notificationService = notificationService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnSignalsChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnSignalsChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        _ = SaveDockLayoutAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var profile = _fundContextService.CurrentFundProfile;
            var unreadAlerts = _shellContextService.GetUnreadAlertCount();
            var notifications = _notificationService.GetHistory().Take(4).ToArray();

            if (profile is null)
            {
                ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
                {
                    WorkspaceTitle = "Governance Workspace",
                    WorkspaceSubtitle = "Fund-first review shell for operations, reconciliation pressure, diagnostics, and audit posture.",
                    PrimaryScopeLabel = "Fund",
                    AsOfValue = "Awaiting fund scope",
                    FreshnessValue = "No active fund",
                    ReviewStateLabel = "Access",
                    ReviewStateValue = "Locked",
                    ReviewStateTone = WorkspaceTone.Warning,
                    CriticalLabel = "Attention",
                    CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : "Switch fund to unlock review queues",
                    CriticalTone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
                });

                CommandBar.CommandGroup = BuildCommandGroup(hasFund: false);
                NoFundEmptyState.Visibility = Visibility.Visible;
                AttentionQueueScrollViewer.Visibility = Visibility.Collapsed;
                QueueScopeBadgeText.Text = "Awaiting fund scope";
                QueueSummaryText.Text = "Governance queues unlock after the active fund is selected.";
                PopulateQueues([], [], [], []);
                PopulateInspector(null, null, null, null, notifications);
                return;
            }

            var ledgerTask = _fundLedgerReadService.GetAsync(new FundLedgerQuery(profile.FundProfileId));
            var reconTask = _reconciliationReadService.GetAsync(profile.FundProfileId);
            var cashTask = _cashFinancingReadService.GetAsync(profile.FundProfileId, profile.BaseCurrency);
            await Task.WhenAll(ledgerTask, reconTask, cashTask);

            var ledger = ledgerTask.Result;
            var reconciliation = reconTask.Result;
            var cash = cashTask.Result;

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Governance Workspace",
                WorkspaceSubtitle = "Review fund operations, reconciliations, diagnostics, and approval gates without leaving the workstation shell.",
                PrimaryScopeLabel = "Fund",
                PrimaryScopeValue = $"{profile.DisplayName} · {profile.BaseCurrency}",
                AsOfValue = ledger?.AsOf.ToLocalTime().ToString("MMM dd yyyy HH:mm") ?? "Awaiting ledger snapshot",
                FreshnessValue = ledger is null ? "Fund data not loaded" : $"Ledger {ledger.JournalEntryCount} journals · {ledger.TrialBalance.Count} lines",
                ReviewStateLabel = "Approval",
                ReviewStateValue = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} break(s) require review" : "Unlocked",
                ReviewStateTone = reconciliation.OpenBreakCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success,
                CriticalLabel = "Critical",
                CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} coverage issue(s)" : "Queue stable",
                CriticalTone = unreadAlerts > 0 || reconciliation.SecurityCoverageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
            });

            CommandBar.CommandGroup = BuildCommandGroup(hasFund: true);
            NoFundEmptyState.Visibility = Visibility.Collapsed;
            AttentionQueueScrollViewer.Visibility = Visibility.Visible;
            QueueScopeBadgeText.Text = profile.DisplayName;
            QueueSummaryText.Text = $"Prioritize fund ops, reconciliation, diagnostics, and alerts for {profile.DisplayName}.";

            PopulateQueues(
                BuildFundOpsQueue(profile, ledger),
                BuildReconciliationQueue(reconciliation, ledger),
                BuildDiagnosticsQueue(reconciliation, unreadAlerts),
                BuildAlertsQueue(notifications, unreadAlerts));
            PopulateInspector(profile, ledger, reconciliation, cash, notifications);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private async Task RestoreDockLayoutAsync()
    {
        try
        {
            var layout = await WorkspaceService.Instance.GetWorkspaceLayoutStateAsync(WorkspaceId, _fundContextService.CurrentFundProfile?.FundProfileId);
            if (layout?.Panes.Count > 0)
            {
                foreach (var pane in layout.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(pane.PageTag, MapDockAction(pane.DockZone));
                }

                if (!string.IsNullOrWhiteSpace(layout.DockLayoutXml))
                {
                    GovernanceDockManager.LoadLayout(layout.DockLayoutXml);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }

        if (_fundContextService.CurrentFundProfile is null)
        {
            OpenWorkspacePage("Diagnostics", PaneDropAction.Replace);
            OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitRight);
            OpenWorkspacePage("SystemHealth", PaneDropAction.SplitBelow);
        }
        else
        {
            OpenWorkspacePage("FundLedger", PaneDropAction.Replace);
            OpenWorkspacePage("FundReconciliation", PaneDropAction.SplitRight);
            OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitBelow);
            OpenWorkspacePage("FundAuditTrail", PaneDropAction.OpenTab);
        }
    }

    private async Task SaveDockLayoutAsync()
    {
        try
        {
            var layout = GovernanceDockManager.CaptureLayoutState("governance-workspace", "Governance Workspace");
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateAsync(WorkspaceId, layout, _fundContextService.CurrentFundProfile?.FundProfileId);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e) => OpenWorkspacePage(e.PageTag, e.Action);
    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e) => ExecuteAction(e.Command.Id, navigate: true);
    private void SwitchFund_Click(object sender, RoutedEventArgs e) => _fundContextService.RequestSwitchFund();
    private void OpenDiagnosticsFromEmptyState_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("Diagnostics");
    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("FundAuditTrail");
    private void OpenProviderHealth_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("ProviderHealth");
    private void OpenSystemHealth_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("SystemHealth");
    private void OpenNotifications_Click(object sender, RoutedEventArgs e) => _navigationService.NavigateTo("NotificationCenter");

    private void OnQueuePrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkspaceQueueItem item })
        {
            ExecuteAction(item.PrimaryActionId, navigate: false);
        }
    }

    private void OnQueueSecondaryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WorkspaceQueueItem item })
        {
            ExecuteAction(item.SecondaryActionId, navigate: false);
        }
    }

    private void OnRecentActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            ExecuteAction(actionId, navigate: false);
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

    private void ExecuteAction(string actionId, bool navigate)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (actionId == "SwitchFund")
        {
            _fundContextService.RequestSwitchFund();
            return;
        }

        if (navigate)
        {
            _navigationService.NavigateTo(actionId);
            return;
        }

        var dockAction = actionId switch
        {
            "FundLedger" => PaneDropAction.Replace,
            "FundAccounts" or "FundReconciliation" => PaneDropAction.SplitRight,
            "NotificationCenter" or "Diagnostics" => PaneDropAction.SplitBelow,
            _ => PaneDropAction.OpenTab
        };

        OpenWorkspacePage(actionId, dockAction);
    }

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            GovernanceDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, action);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private static WorkspaceCommandGroup BuildCommandGroup(bool hasFund) => hasFund
        ? new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundLedger", Label = "Fund Overview", Description = "Open fund overview", ShortcutHint = "Ctrl+1", Glyph = "\uEE94", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "FundAccounts", Label = "Accounts", Description = "Open fund accounts", ShortcutHint = "Ctrl+2", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation", ShortcutHint = "Ctrl+3", Glyph = "\uE895" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundBanking", Label = "Banking", Description = "Open banking", Glyph = "\uE8C7" },
                new WorkspaceCommandItem { Id = "FundPortfolio", Label = "Portfolio", Description = "Open portfolio", Glyph = "\uE8B5" },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Trial Balance", Description = "Open trial balance", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        }
        : new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "SwitchFund", Label = "Switch Fund", Description = "Choose an active fund", ShortcutHint = "Required", Glyph = "\uE777", Tone = WorkspaceTone.Primary }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        };

    private static IReadOnlyList<WorkspaceQueueItem> BuildFundOpsQueue(FundProfileDetail profile, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Fund operations posture", Detail = ledger is null ? "Awaiting the first ledger snapshot for the selected fund." : $"{ledger.EntityCount} entities, {ledger.SleeveCount} sleeves, {ledger.VehicleCount} vehicles across {ledger.JournalEntryCount} journals and {ledger.TrialBalance.Count} trial-balance lines.", StatusLabel = ledger?.JournalEntryCount > 0 ? "Live review" : "Needs setup", CountLabel = ledger?.JournalEntryCount > 0 ? $"{ledger!.JournalEntryCount} journals" : "No journals", Tone = ledger?.JournalEntryCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundLedger", PrimaryActionLabel = "Open Overview", SecondaryActionId = "FundAccounts", SecondaryActionLabel = "Accounts" },
        new WorkspaceQueueItem { Title = "Cash, banking, and linked accounts", Detail = $"{profile.DisplayName} is ready for account and banking drill-ins from the governance shell.", StatusLabel = "Operator review", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundAccounts", PrimaryActionLabel = "Accounts", SecondaryActionId = "FundBanking", SecondaryActionLabel = "Banking" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildReconciliationQueue(ReconciliationSummary reconciliation, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Reconciliation review queue", Detail = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open break(s) across {reconciliation.RunCount} recent run(s) with {reconciliation.BreakAmountTotal:C0} at risk." : $"{reconciliation.RunCount} reconciliation run(s) are currently matched and ready for sign-off.", StatusLabel = reconciliation.OpenBreakCount > 0 ? "Approval hold" : "Matched", CountLabel = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open" : $"{reconciliation.RunCount} reviewed", Tone = reconciliation.OpenBreakCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Review Breaks", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" },
        new WorkspaceQueueItem { Title = "Security coverage posture", Detail = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} coverage issue(s) need review before approvals are released." : $"Security coverage is aligned for the current reconciliation scope{(ledger is null ? string.Empty : $" with {ledger.TrialBalance.Count} ledger lines available for validation")}.", StatusLabel = reconciliation.SecurityCoverageIssueCount > 0 ? "Coverage open" : "Aligned", CountLabel = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} issue(s)" : "0 issues", Tone = reconciliation.SecurityCoverageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Open Review", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit Trail" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildDiagnosticsQueue(ReconciliationSummary reconciliation, int unreadAlerts) =>
    [
        new WorkspaceQueueItem { Title = "Diagnostic and provider trust checks", Detail = "Keep diagnostics and provider health one action away when reconciliation pressure or alert load starts to rise.", StatusLabel = unreadAlerts > 0 ? "Escalated" : "Available", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} alert-linked" : "Diagnostics ready", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Diagnostics", PrimaryActionLabel = "Diagnostics", SecondaryActionId = "ProviderHealth", SecondaryActionLabel = "Provider Health" },
        new WorkspaceQueueItem { Title = "System readiness and host posture", Detail = reconciliation.OpenBreakCount > 0 ? "Use host and dependency checks before releasing governance approvals with open reconciliation pressure." : "System health remains available as a quick trust check before operator handoff.", StatusLabel = "Trust check", CountLabel = "Host posture", Tone = WorkspaceTone.Neutral, PrimaryActionId = "SystemHealth", PrimaryActionLabel = "System Health", SecondaryActionId = "Diagnostics", SecondaryActionLabel = "Run Checks" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildAlertsQueue(IReadOnlyList<NotificationHistoryItem> notifications, int unreadAlerts) =>
    [
        new WorkspaceQueueItem { Title = "Operator alerts and approvals", Detail = notifications.FirstOrDefault() is { } latest ? $"Latest notification: {latest.Title} at {latest.Timestamp:t}. Open grouped alerts and acknowledgement history from the workspace." : "No recent notifications. Keep the notification center docked when approval gates change.", StatusLabel = unreadAlerts > 0 ? "Unread alerts" : "Quiet", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} unread" : $"{notifications.Count} recent", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "NotificationCenter", PrimaryActionLabel = "Open Alerts", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit Trail" }
    ];

    private void PopulateQueues(IReadOnlyList<WorkspaceQueueItem> fundOps, IReadOnlyList<WorkspaceQueueItem> reconciliation, IReadOnlyList<WorkspaceQueueItem> diagnostics, IReadOnlyList<WorkspaceQueueItem> alerts)
    {
        FundOpsQueueList.ItemsSource = fundOps;
        ReconciliationQueueList.ItemsSource = reconciliation;
        DiagnosticsQueueList.ItemsSource = diagnostics;
        AlertsQueueList.ItemsSource = alerts;
    }

    private void PopulateInspector(FundProfileDetail? profile, FundLedgerSummary? ledger, ReconciliationSummary? reconciliation, CashFinancingSummary? cash, IReadOnlyList<NotificationHistoryItem> notifications)
    {
        FundSummaryTitleText.Text = profile?.DisplayName ?? "No fund selected";
        FundSummaryDetailText.Text = profile is null ? "Switch fund to unlock fund operations, reconciliation, and audit-first review." : $"{profile.LegalEntityName} · {profile.BaseCurrency} · default {profile.DefaultLedgerScope}";
        FundSummaryMetaText.Text = ledger is null ? "No current ledger snapshot." : $"As of {ledger.AsOf:MMM dd yyyy HH:mm} · {ledger.EntityCount} entities · {ledger.VehicleCount} vehicles";
        SummaryCashText.Text = cash is null || cash.TotalCash == 0m ? "—" : cash.TotalCash.ToString("C0");
        SummaryBreaksText.Text = reconciliation?.OpenBreakCount.ToString() ?? "—";
        SummaryJournalText.Text = ledger?.JournalEntryCount.ToString() ?? "—";

        RecentWorkList.ItemsSource = notifications.Count > 0
            ? notifications.Take(3).Select(notification => new WorkspaceRecentItem { Title = notification.Title, Detail = notification.Message, Meta = $"{notification.Timestamp:g} · {notification.Type}", Tone = notification.IsRead ? WorkspaceTone.Neutral : WorkspaceTone.Warning, ActionId = "NotificationCenter", ActionLabel = "Open Alerts" }).ToArray()
            : new[] { new WorkspaceRecentItem { Title = profile is null ? "Select the active fund" : "Audit trail ready", Detail = profile is null ? "Fund context is the main trust signal for governance review. Choose the fund before working breaks or approvals." : "Open the audit trail to inspect recent governance activity and sign-off context.", Meta = profile is null ? "Locked shell" : "No recent notifications", Tone = profile is null ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = profile is null ? "SwitchFund" : "FundAuditTrail", ActionLabel = profile is null ? "Switch Fund" : "Open Audit Trail" } };
    }

    private static string BuildPageKey(string pageTag, object? parameter) => parameter is null ? pageTag : $"{pageTag}:{parameter}";
    private static string GetPageTitle(string pageTag) => pageTag switch { "FundLedger" => "Fund Overview", "FundAccounts" => "Accounts", "FundReconciliation" => "Reconciliation", "FundTrialBalance" => "Trial Balance", "FundAuditTrail" => "Audit Trail", "NotificationCenter" => "Alerts", "Diagnostics" => "Diagnostics", "ProviderHealth" => "Provider Health", "SystemHealth" => "System Health", _ => pageTag };
    private static PaneDropAction MapDockAction(string dockZone) => dockZone switch { "left" => PaneDropAction.SplitLeft, "right" => PaneDropAction.SplitRight, "bottom" => PaneDropAction.SplitBelow, "floating" => PaneDropAction.FloatWindow, _ => PaneDropAction.Replace };
}
