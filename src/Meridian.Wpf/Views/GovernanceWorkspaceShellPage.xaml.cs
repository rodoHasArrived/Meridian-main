using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

public partial class GovernanceWorkspaceShellPage : Page
{
    private const string WorkspaceId = "governance";

    private readonly NavigationService _navigationService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly FundLedgerReadService _fundLedgerReadService;
    private readonly ReconciliationReadService _reconciliationReadService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly Meridian.Wpf.Services.NotificationService _notificationService;
    private GovernanceSubarea _selectedSubarea = GovernanceSubarea.Operations;

    public GovernanceWorkspaceShellPage(
        NavigationService navigationService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService,
        FundLedgerReadService fundLedgerReadService,
        ReconciliationReadService reconciliationReadService,
        CashFinancingReadService cashFinancingReadService,
        Meridian.Wpf.Services.NotificationService notificationService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
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
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
        }

        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnSignalsChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
        }

        _ = SaveDockLayoutAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var profile = _fundContextService.CurrentFundProfile;
            var operatingContext = _operatingContextService?.CurrentContext;
            var unreadAlerts = _shellContextService.GetUnreadAlertCount();
            var notifications = _notificationService.GetHistory().Take(4).ToArray();
            UpdateSubareaButtons();

            if (profile is null)
            {
                ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
                {
                    WorkspaceTitle = "Governance Workspace",
                    WorkspaceSubtitle = "Organization-aware review shell for operations, accounting, reconciliation, reporting, and audit posture.",
                    PrimaryScopeLabel = "Context",
                    PrimaryScopeValue = operatingContext?.DisplayName ?? "Awaiting fund-linked scope",
                    AsOfValue = "Awaiting fund-linked scope",
                    FreshnessValue = operatingContext is null ? "No active operating context" : $"{operatingContext.ScopeKind.ToDisplayName()} selected",
                    ReviewStateLabel = "Access",
                    ReviewStateValue = "Locked",
                    ReviewStateTone = WorkspaceTone.Warning,
                    CriticalLabel = "Attention",
                    CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : "Switch context to unlock governance queues",
                    CriticalTone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
                });

                CommandBar.CommandGroup = BuildCommandGroup(hasFund: false);
                NoFundEmptyState.Visibility = Visibility.Visible;
                AttentionQueueScrollViewer.Visibility = Visibility.Collapsed;
                QueueScopeBadgeText.Text = operatingContext?.DisplayName ?? "Awaiting fund-linked scope";
                QueueSummaryText.Text = "Governance queues unlock after a fund-linked operating context is selected.";
                PopulateQueues([], [], [], [], []);
                PopulateInspector(operatingContext, null, null, null, null, notifications);
                return;
            }

            var ledgerTask = _fundLedgerReadService.GetAsync(new FundLedgerQuery(profile.FundProfileId));
            var reconTask = _reconciliationReadService.GetAsync(profile.FundProfileId);
            var cashTask = _cashFinancingReadService.GetAsync(profile.FundProfileId, profile.BaseCurrency);
            await Task.WhenAll(ledgerTask, reconTask, cashTask);

            var ledger = await ledgerTask.ConfigureAwait(false);
            var reconciliation = await reconTask.ConfigureAwait(false);
            var cash = await cashTask.ConfigureAwait(false);

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Governance Workspace",
                WorkspaceSubtitle = "Review operations, accounting, reconciliations, reporting, and approval gates without leaving the workstation shell.",
                PrimaryScopeLabel = "Governance Scope",
                PrimaryScopeValue = operatingContext?.DisplayName ?? $"{profile.DisplayName} · {profile.BaseCurrency}",
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
            QueueScopeBadgeText.Text = operatingContext?.DisplayName ?? profile.DisplayName;
            QueueSummaryText.Text = $"Prioritize operations, accounting, reconciliation, reporting, and audit review for {(operatingContext?.DisplayName ?? profile.DisplayName)}.";

            PopulateQueues(
                BuildOperationsQueue(profile, ledger),
                BuildAccountingQueue(profile, ledger, cash),
                BuildReconciliationQueue(reconciliation, ledger),
                BuildReportingQueue(profile, cash),
                BuildAuditQueue(reconciliation, notifications, unreadAlerts));
            PopulateInspector(operatingContext, profile, ledger, reconciliation, cash, notifications);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private async Task RestoreDockLayoutAsync()
    {
        try
        {
            var layout = await WorkspaceService.Instance.GetWorkspaceLayoutStateForContextAsync(WorkspaceId, GetLayoutScopeKey());
            if (layout?.Panes.Count > 0)
            {
                foreach (var pane in layout.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(pane.PageTag, NormalizeDockAction(MapDockAction(pane.DockZone)));
                }

                if (ShouldRestoreSerializedLayout(layout))
                {
                    GovernanceDockManager.LoadLayout(layout.DockLayoutXml);
                }

                return;
            }
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }

        if (_fundContextService.CurrentFundProfile is null)
        {
            OpenWorkspacePage("Diagnostics", PaneDropAction.Replace);
            OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitRight);
            OpenWorkspacePage("SystemHealth", PaneDropAction.SplitBelow);
        }
        else if (GetWindowMode() == BoundedWindowMode.WorkbenchPreset &&
                 string.Equals(_operatingContextService?.CurrentLayoutPresetId, "reconciliation-workbench", StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkspacePage("FundReconciliation", PaneDropAction.Replace);
            OpenWorkspacePage("FundTrialBalance", PaneDropAction.SplitLeft);
            OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitBelow);
            OpenWorkspacePage("FundAuditTrail", PaneDropAction.OpenTab);
        }
        else if (GetWindowMode() == BoundedWindowMode.WorkbenchPreset &&
                 string.Equals(_operatingContextService?.CurrentLayoutPresetId, "accounting-review", StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkspacePage("FundLedger", PaneDropAction.Replace);
            OpenWorkspacePage("FundTrialBalance", PaneDropAction.SplitRight);
            OpenWorkspacePage("FundCashFinancing", PaneDropAction.SplitBelow);
            OpenWorkspacePage("FundAuditTrail", PaneDropAction.OpenTab);
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
            layout.OperatingContextKey = GetLayoutScopeKey();
            layout.WindowMode = GetWindowMode();
            layout.LayoutPresetId = _operatingContextService?.CurrentLayoutPresetId;
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateForContextAsync(WorkspaceId, layout, layout.OperatingContextKey);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e) => OpenWorkspacePage(e.PageTag, e.Action);
    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e) => ExecuteAction(e.Command.Id, navigate: false);
    private void SwitchFund_Click(object sender, RoutedEventArgs e) => RequestContextSelection();
    private void SwitchContext_Click(object sender, RoutedEventArgs e) => RequestContextSelection();
    private void OpenDiagnosticsFromEmptyState_Click(object sender, RoutedEventArgs e) => ExecuteAction("Diagnostics", navigate: false);
    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e) => ExecuteAction("FundAuditTrail", navigate: false);
    private void OpenProviderHealth_Click(object sender, RoutedEventArgs e) => ExecuteAction("ProviderHealth", navigate: false);
    private void OpenSystemHealth_Click(object sender, RoutedEventArgs e) => ExecuteAction("SystemHealth", navigate: false);
    private void OpenNotifications_Click(object sender, RoutedEventArgs e) => ExecuteAction("NotificationCenter", navigate: false);
    private void OpenOperationsLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(GovernanceSubarea.Operations);
    private void OpenAccountingLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(GovernanceSubarea.Accounting);
    private void OpenReconciliationLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(GovernanceSubarea.Reconciliation);
    private void OpenReportingLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(GovernanceSubarea.Reporting);
    private void OpenAuditLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(GovernanceSubarea.Audit);

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

    private void OnOperatingContextChanged(object? sender, WorkstationOperatingContextChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnOperatingContextChanged(sender, e));
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

        if (actionId == "SwitchContext")
        {
            RequestContextSelection();
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
            "FundAccounts" or "FundReconciliation" or "FundTrialBalance" => PaneDropAction.SplitRight,
            "FundCashFinancing" or "NotificationCenter" or "Diagnostics" => PaneDropAction.SplitBelow,
            _ => PaneDropAction.OpenTab
        };

        OpenWorkspacePage(actionId, dockAction);
    }

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            GovernanceDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private static WorkspaceCommandGroup BuildCommandGroup(bool hasFund) => hasFund
        ? new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundLedger", Label = "Operations", Description = "Open operations lane", ShortcutHint = "Ctrl+1", Glyph = "\uEE94", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Accounting", Description = "Open accounting lane", ShortcutHint = "Ctrl+2", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation lane", ShortcutHint = "Ctrl+3", Glyph = "\uE895" }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "FundAccounts", Label = "Accounts", Description = "Open account surfaces", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "FundCashFinancing", Label = "Reporting", Description = "Open cash and reporting view", Glyph = "\uE8C7" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit", Description = "Open audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        }
        : new WorkspaceCommandGroup
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "SwitchContext", Label = "Switch Context", Description = "Choose an active operating context", ShortcutHint = "Required", Glyph = "\uE777", Tone = WorkspaceTone.Primary }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Diagnostics", Label = "Diagnostics", Description = "Open diagnostics", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Notifications", Description = "Open notifications", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "Settings", Label = "Settings", Description = "Open settings", Glyph = "\uE713" }
            ]
        };

    private static IReadOnlyList<WorkspaceQueueItem> BuildOperationsQueue(FundProfileDetail profile, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Fund operations posture", Detail = ledger is null ? "Awaiting the first ledger snapshot for the selected fund." : $"{ledger.EntityCount} entities, {ledger.SleeveCount} sleeves, {ledger.VehicleCount} vehicles across {ledger.JournalEntryCount} journals and {ledger.TrialBalance.Count} trial-balance lines.", StatusLabel = ledger?.JournalEntryCount > 0 ? "Live review" : "Needs setup", CountLabel = ledger?.JournalEntryCount > 0 ? $"{ledger!.JournalEntryCount} journals" : "No journals", Tone = ledger?.JournalEntryCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundLedger", PrimaryActionLabel = "Open Operations", SecondaryActionId = "FundAccounts", SecondaryActionLabel = "Accounts" },
        new WorkspaceQueueItem { Title = "Accounts and banking coordination", Detail = $"{profile.DisplayName} is ready for account, banking, and entity drill-ins from the governance shell.", StatusLabel = "Operator review", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundAccounts", PrimaryActionLabel = "Accounts", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Operations" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildAccountingQueue(FundProfileDetail profile, FundLedgerSummary? ledger, CashFinancingSummary? cash) =>
    [
        new WorkspaceQueueItem { Title = "Trial balance and journals", Detail = ledger is null ? "Trial-balance, journal, and ledger detail becomes available after the first governance snapshot." : $"{ledger.TrialBalance.Count} trial-balance line(s) and {ledger.JournalEntryCount} journal(s) are ready for accounting review.", StatusLabel = ledger?.TrialBalance.Count > 0 ? "Accounting ready" : "Awaiting snapshot", CountLabel = ledger?.TrialBalance.Count > 0 ? $"{ledger!.TrialBalance.Count} lines" : "No lines", Tone = ledger?.TrialBalance.Count > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundTrialBalance", PrimaryActionLabel = "Open Accounting", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Ledger" },
        new WorkspaceQueueItem { Title = "Cash and financing posture", Detail = cash is null ? $"Accounting review for {profile.DisplayName} is waiting on capital and financing metrics." : $"Total cash {cash.TotalCash:C0}, financing cost {cash.FinancingCost:C0}, and bank posture are available for reporting and sign-off.", StatusLabel = cash is null ? "Pending" : "Ready", CountLabel = profile.BaseCurrency, Tone = cash is null ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildReconciliationQueue(ReconciliationSummary reconciliation, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Reconciliation review queue", Detail = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open break(s) across {reconciliation.RunCount} recent run(s) with {reconciliation.BreakAmountTotal:C0} at risk." : $"{reconciliation.RunCount} reconciliation run(s) are currently matched and ready for sign-off.", StatusLabel = reconciliation.OpenBreakCount > 0 ? "Approval hold" : "Matched", CountLabel = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open" : $"{reconciliation.RunCount} reviewed", Tone = reconciliation.OpenBreakCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Review Breaks", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" },
        new WorkspaceQueueItem { Title = "Security coverage posture", Detail = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} coverage issue(s) need review before approvals are released." : $"Security coverage is aligned for the current reconciliation scope{(ledger is null ? string.Empty : $" with {ledger.TrialBalance.Count} ledger lines available for validation")}.", StatusLabel = reconciliation.SecurityCoverageIssueCount > 0 ? "Coverage open" : "Aligned", CountLabel = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} issue(s)" : "0 issues", Tone = reconciliation.SecurityCoverageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Open Review", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit Trail" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildReportingQueue(FundProfileDetail profile, CashFinancingSummary? cash) =>
    [
        new WorkspaceQueueItem { Title = "Portfolio and cash reporting", Detail = cash is null ? $"Reporting for {profile.DisplayName} is waiting on cash and financing data." : "Cash, financing, and portfolio-linked reporting can be reviewed without leaving governance.", StatusLabel = cash is null ? "Pending" : "Ready", CountLabel = cash is null ? "Awaiting data" : $"{cash.TotalCash:C0}", Tone = cash is null ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundPortfolio", SecondaryActionLabel = "Portfolio" },
        new WorkspaceQueueItem { Title = "Board and operator handoff", Detail = "Keep reporting, trial-balance, and audit references together before approvals or exports leave the workstation.", StatusLabel = "Review", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundTrialBalance", PrimaryActionLabel = "Accounting", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildAuditQueue(ReconciliationSummary reconciliation, IReadOnlyList<NotificationHistoryItem> notifications, int unreadAlerts) =>
    [
        new WorkspaceQueueItem { Title = "Audit trail and approvals", Detail = notifications.FirstOrDefault() is { } latest ? $"Latest notification: {latest.Title} at {latest.Timestamp:t}. Open grouped alerts and acknowledgement history from the workspace." : "No recent notifications. Keep the audit trail docked when approval gates change.", StatusLabel = unreadAlerts > 0 ? "Unread alerts" : "Quiet", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} unread" : $"{notifications.Count} recent", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "FundAuditTrail", PrimaryActionLabel = "Open Audit", SecondaryActionId = "NotificationCenter", SecondaryActionLabel = "Alerts" },
        new WorkspaceQueueItem { Title = "Diagnostics and provider trust checks", Detail = reconciliation.OpenBreakCount > 0 ? "Use diagnostics and provider health before releasing approvals with open reconciliation pressure." : "Diagnostics and provider health remain available as quick trust checks before operator handoff.", StatusLabel = unreadAlerts > 0 ? "Escalated" : "Available", CountLabel = unreadAlerts > 0 ? $"{unreadAlerts} alert-linked" : "Diagnostics ready", Tone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info, PrimaryActionId = "Diagnostics", PrimaryActionLabel = "Diagnostics", SecondaryActionId = "ProviderHealth", SecondaryActionLabel = "Provider Health" }
    ];

    private void PopulateQueues(IReadOnlyList<WorkspaceQueueItem> operations, IReadOnlyList<WorkspaceQueueItem> accounting, IReadOnlyList<WorkspaceQueueItem> reconciliation, IReadOnlyList<WorkspaceQueueItem> reporting, IReadOnlyList<WorkspaceQueueItem> audit)
    {
        OperationsQueueList.ItemsSource = operations;
        AccountingQueueList.ItemsSource = accounting;
        ReconciliationQueueList.ItemsSource = reconciliation;
        ReportingQueueList.ItemsSource = reporting;
        AuditQueueList.ItemsSource = audit;
    }

    private void PopulateInspector(WorkstationOperatingContext? operatingContext, FundProfileDetail? profile, FundLedgerSummary? ledger, ReconciliationSummary? reconciliation, CashFinancingSummary? cash, IReadOnlyList<NotificationHistoryItem> notifications)
    {
        FundSummaryTitleText.Text = operatingContext?.DisplayName ?? profile?.DisplayName ?? "No operating context selected";
        FundSummaryDetailText.Text = profile is null ? "Switch to a fund-linked operating context to unlock operations, accounting, reconciliation, reporting, and audit review." : $"{profile.LegalEntityName} · {profile.BaseCurrency} · default {profile.DefaultLedgerScope}";
        FundSummaryMetaText.Text = ledger is null ? "No current ledger snapshot." : $"As of {ledger.AsOf:MMM dd yyyy HH:mm} · {ledger.EntityCount} entities · {ledger.VehicleCount} vehicles";
        SummaryCashText.Text = cash is null || cash.TotalCash == 0m ? "—" : cash.TotalCash.ToString("C0");
        SummaryBreaksText.Text = reconciliation?.OpenBreakCount.ToString() ?? "—";
        SummaryJournalText.Text = ledger?.JournalEntryCount.ToString() ?? "—";

        RecentWorkList.ItemsSource = notifications.Count > 0
            ? notifications.Take(3).Select(notification => new WorkspaceRecentItem { Title = notification.Title, Detail = notification.Message, Meta = $"{notification.Timestamp:g} · {notification.Type}", Tone = notification.IsRead ? WorkspaceTone.Neutral : WorkspaceTone.Warning, ActionId = "NotificationCenter", ActionLabel = "Open Alerts" }).ToArray()
            : new[] { new WorkspaceRecentItem { Title = profile is null ? "Select the active context" : "Audit trail ready", Detail = profile is null ? "A fund-linked operating context is the main trust signal for governance review. Choose the context before working breaks or approvals." : "Open the audit trail to inspect recent governance activity and sign-off context.", Meta = profile is null ? "Locked shell" : "No recent notifications", Tone = profile is null ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = profile is null ? "SwitchContext" : "FundAuditTrail", ActionLabel = profile is null ? "Switch Context" : "Open Audit Trail" } };
    }

    private void RequestContextSelection()
    {
        if (_operatingContextService is not null)
        {
            _operatingContextService.RequestSwitchContext();
            return;
        }

        _fundContextService.RequestSwitchFund();
    }

    private string? GetLayoutScopeKey()
        => _operatingContextService?.GetActiveScopeKey() ?? _fundContextService.CurrentFundProfile?.FundProfileId;

    private BoundedWindowMode GetWindowMode()
        => _operatingContextService?.CurrentWindowMode ?? BoundedWindowMode.DockFloat;

    private static bool ShouldRestoreSerializedLayout(WorkstationLayoutState layout)
        => layout.WindowMode != BoundedWindowMode.Focused && !string.IsNullOrWhiteSpace(layout.DockLayoutXml);

    private PaneDropAction NormalizeDockAction(PaneDropAction action)
        => GetWindowMode() == BoundedWindowMode.Focused && action == PaneDropAction.FloatWindow
            ? PaneDropAction.OpenTab
            : action;

    private void SelectSubarea(GovernanceSubarea subarea)
    {
        _selectedSubarea = subarea;
        UpdateSubareaButtons();

        if (_fundContextService.CurrentFundProfile is null)
        {
            return;
        }

        var actionId = subarea switch
        {
            GovernanceSubarea.Operations => "FundLedger",
            GovernanceSubarea.Accounting => "FundTrialBalance",
            GovernanceSubarea.Reconciliation => "FundReconciliation",
            GovernanceSubarea.Reporting => "FundCashFinancing",
            GovernanceSubarea.Audit => "FundAuditTrail",
            _ => "FundLedger"
        };

        ExecuteAction(actionId, navigate: false);
    }

    private void UpdateSubareaButtons()
    {
        ApplySubareaStyle(OperationsLaneButton, _selectedSubarea == GovernanceSubarea.Operations);
        ApplySubareaStyle(AccountingLaneButton, _selectedSubarea == GovernanceSubarea.Accounting);
        ApplySubareaStyle(ReconciliationLaneButton, _selectedSubarea == GovernanceSubarea.Reconciliation);
        ApplySubareaStyle(ReportingLaneButton, _selectedSubarea == GovernanceSubarea.Reporting);
        ApplySubareaStyle(AuditLaneButton, _selectedSubarea == GovernanceSubarea.Audit);
    }

    private static void ApplySubareaStyle(Button button, bool isSelected)
    {
        var resourceKey = isSelected ? "SecondaryButtonStyle" : "GhostButtonStyle";
        button.Style = (Style)System.Windows.Application.Current.FindResource(resourceKey);
    }

    private static string BuildPageKey(string pageTag, object? parameter) => parameter is null ? pageTag : $"{pageTag}:{parameter}";
    private static string GetPageTitle(string pageTag) => pageTag switch { "FundLedger" => "Operations", "FundAccounts" => "Accounts", "FundReconciliation" => "Reconciliation", "FundTrialBalance" => "Accounting", "FundCashFinancing" => "Reporting", "FundPortfolio" => "Portfolio", "FundAuditTrail" => "Audit Trail", "NotificationCenter" => "Alerts", "Diagnostics" => "Diagnostics", "ProviderHealth" => "Provider Health", "SystemHealth" => "System Health", _ => pageTag };
    private static PaneDropAction MapDockAction(string dockZone) => dockZone switch { "left" => PaneDropAction.SplitLeft, "right" => PaneDropAction.SplitRight, "bottom" => PaneDropAction.SplitBelow, "floating" => PaneDropAction.FloatWindow, _ => PaneDropAction.Replace };
}
