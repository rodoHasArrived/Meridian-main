using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

public partial class GovernanceWorkspaceShellPage : GovernanceWorkspaceShellPageBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly FundOperationsWorkspaceReadService _fundOperationsWorkspaceReadService;
    private readonly Meridian.Wpf.Services.NotificationService _notificationService;
    private GovernanceSubarea _selectedSubarea = GovernanceSubarea.Operations;

    public GovernanceWorkspaceShellPage(
        NavigationService navigationService,
        GovernanceWorkspaceShellStateProvider stateProvider,
        GovernanceWorkspaceShellViewModel viewModel,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService,
        FundOperationsWorkspaceReadService fundOperationsWorkspaceReadService,
        Meridian.Wpf.Services.NotificationService notificationService)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _shellContextService = shellContextService;
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService;
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
        await RestoreDockLayoutAsync(GovernanceDockManager);
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

        _ = SaveDockLayoutAsync(GovernanceDockManager);
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
                    WorkspaceTitle = WorkspaceCopyCatalog.Governance.ShellTitle,
                    WorkspaceSubtitle = WorkspaceCopyCatalog.Governance.ShellSubtitleNoFund,
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

                ViewModel.CommandGroup = BuildCommandGroup(hasFund: false);
                CommandBar.CommandGroup = ViewModel.CommandGroup;
                NoFundEmptyState.Visibility = Visibility.Visible;
                AttentionQueueScrollViewer.Visibility = Visibility.Collapsed;
                QueueScopeBadgeText.Text = operatingContext?.DisplayName ?? "Awaiting fund-linked scope";
                QueueSummaryText.Text = "Governance queues unlock after a fund-linked operating context is selected.";
                PopulateQueues([], [], [], [], []);
                PopulateInspector(operatingContext, null, null, null, null, notifications);
                return;
            }

            var workspace = await _fundOperationsWorkspaceReadService
                .GetWorkspaceAsync(
                    new FundOperationsWorkspaceQuery(
                        FundProfileId: profile.FundProfileId,
                        Currency: profile.BaseCurrency))
                .ConfigureAwait(false);
            var ledger = workspace.Ledger;
            var reconciliation = workspace.Reconciliation;
            var cash = workspace.CashFinancing;

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = WorkspaceCopyCatalog.Governance.ShellTitle,
                WorkspaceSubtitle = WorkspaceCopyCatalog.Governance.ShellSubtitleFund,
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

            ViewModel.CommandGroup = BuildCommandGroup(hasFund: true);
            CommandBar.CommandGroup = ViewModel.CommandGroup;
            NoFundEmptyState.Visibility = Visibility.Collapsed;
            AttentionQueueScrollViewer.Visibility = Visibility.Visible;
            QueueScopeBadgeText.Text = operatingContext?.DisplayName ?? profile.DisplayName;
            QueueSummaryText.Text = $"Prioritize operations, accounting, reconciliation, reporting, and audit review for {(operatingContext?.DisplayName ?? profile.DisplayName)}.";

            PopulateQueues(
                BuildOperationsQueue(profile, workspace),
                BuildAccountingQueue(profile, workspace),
                BuildReconciliationQueue(reconciliation, ledger),
                BuildReportingQueue(profile, workspace),
                BuildAuditQueue(reconciliation, notifications, unreadAlerts));
            PopulateInspector(operatingContext, profile, ledger, reconciliation, cash, notifications);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[GovernanceWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(GovernanceDockManager, e.PageTag, e.Action);

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
        => DispatchRefresh(RefreshAsync);

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
            NavigationService.NavigateTo(actionId);
            return;
        }

        var dockAction = actionId switch
        {
            "FundLedger" => PaneDropAction.Replace,
            "FundAccounts" or "FundReconciliation" or "FundTrialBalance" => PaneDropAction.SplitRight,
            "FundCashFinancing" or "NotificationCenter" or "Diagnostics" => PaneDropAction.SplitBelow,
            _ => PaneDropAction.OpenTab
        };

        OpenWorkspacePage(GovernanceDockManager, actionId, dockAction);
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
                new WorkspaceCommandItem { Id = "FundReportPack", Label = "Report Pack", Description = "Open governance report-pack preview", Glyph = "\uE8A5" },
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

    private static IReadOnlyList<WorkspaceQueueItem> BuildOperationsQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var ledger = workspace.Ledger;
        var summary = workspace.Workspace;

        return
        [
            new WorkspaceQueueItem { Title = "Fund operations posture", Detail = $"{summary.TotalAccounts} linked account(s), {ledger.EntityCount} entities, {ledger.SleeveCount} sleeves, and {ledger.VehicleCount} vehicles feed {ledger.JournalEntryCount} journals and {ledger.TrialBalance.Count} trial-balance lines through the shared governance workspace.", StatusLabel = ledger.JournalEntryCount > 0 ? "Live review" : "Needs setup", CountLabel = ledger.JournalEntryCount > 0 ? $"{ledger.JournalEntryCount} journals" : "No journals", Tone = ledger.JournalEntryCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundLedger", PrimaryActionLabel = "Open Operations", SecondaryActionId = "FundAccounts", SecondaryActionLabel = "Accounts" },
            new WorkspaceQueueItem { Title = "Accounts and banking coordination", Detail = $"{profile.DisplayName} now reuses the shared fund-operations projection for account, banking, and entity drill-ins from the governance shell.", StatusLabel = "Operator review", CountLabel = workspace.BankSnapshots.Count > 0 ? $"{workspace.BankSnapshots.Count} bank views" : profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundAccounts", PrimaryActionLabel = "Accounts", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Operations" }
        ];
    }

    private static IReadOnlyList<WorkspaceQueueItem> BuildAccountingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var ledger = workspace.Ledger;
        var cash = workspace.CashFinancing;

        return
        [
            new WorkspaceQueueItem { Title = "Trial balance and journals", Detail = $"{ledger.TrialBalance.Count} trial-balance line(s) and {ledger.JournalEntryCount} journal(s) are ready for accounting review from the shared fund-operations query path.", StatusLabel = ledger.TrialBalance.Count > 0 ? "Accounting ready" : "Awaiting snapshot", CountLabel = ledger.TrialBalance.Count > 0 ? $"{ledger.TrialBalance.Count} lines" : "No lines", Tone = ledger.TrialBalance.Count > 0 ? WorkspaceTone.Info : WorkspaceTone.Warning, PrimaryActionId = "FundTrialBalance", PrimaryActionLabel = "Open Accounting", SecondaryActionId = "FundLedger", SecondaryActionLabel = "Ledger" },
            new WorkspaceQueueItem { Title = "Cash and financing posture", Detail = $"Total cash {cash.TotalCash:C0}, financing cost {cash.FinancingCost:C0}, and pending settlement {cash.PendingSettlement:C0} are synchronized for reporting and sign-off.", StatusLabel = "Ready", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Success, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" }
        ];
    }

    private static IReadOnlyList<WorkspaceQueueItem> BuildReconciliationQueue(ReconciliationSummary reconciliation, FundLedgerSummary? ledger) =>
    [
        new WorkspaceQueueItem { Title = "Reconciliation review queue", Detail = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open break(s) across {reconciliation.RunCount} recent run(s) with {reconciliation.BreakAmountTotal:C0} at risk." : $"{reconciliation.RunCount} reconciliation run(s) are currently matched and ready for sign-off.", StatusLabel = reconciliation.OpenBreakCount > 0 ? "Approval hold" : "Matched", CountLabel = reconciliation.OpenBreakCount > 0 ? $"{reconciliation.OpenBreakCount} open" : $"{reconciliation.RunCount} reviewed", Tone = reconciliation.OpenBreakCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Review Breaks", SecondaryActionId = "FundTrialBalance", SecondaryActionLabel = "Trial Balance" },
        new WorkspaceQueueItem { Title = "Security coverage posture", Detail = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} coverage issue(s) need review before approvals are released." : $"Security coverage is aligned for the current reconciliation scope{(ledger is null ? string.Empty : $" with {ledger.TrialBalance.Count} ledger lines available for validation")}.", StatusLabel = reconciliation.SecurityCoverageIssueCount > 0 ? "Coverage open" : "Aligned", CountLabel = reconciliation.SecurityCoverageIssueCount > 0 ? $"{reconciliation.SecurityCoverageIssueCount} issue(s)" : "0 issues", Tone = reconciliation.SecurityCoverageIssueCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success, PrimaryActionId = "FundReconciliation", PrimaryActionLabel = "Open Review", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit Trail" }
    ];

    private static IReadOnlyList<WorkspaceQueueItem> BuildReportingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
    {
        var cash = workspace.CashFinancing;
        var reporting = workspace.Reporting;

        return
        [
            new WorkspaceQueueItem { Title = "Portfolio and cash reporting", Detail = $"Cash, financing, NAV, and portfolio-linked reporting can be reviewed without leaving governance. {reporting.ProfileCount} reporting/export profile(s) are already available through the shared workspace summary.", StatusLabel = "Ready", CountLabel = $"{cash.TotalCash:C0}", Tone = WorkspaceTone.Info, PrimaryActionId = "FundCashFinancing", PrimaryActionLabel = "Open Reporting", SecondaryActionId = "FundPortfolio", SecondaryActionLabel = "Portfolio" },
            new WorkspaceQueueItem { Title = "Board and operator handoff", Detail = $"Keep reporting, trial-balance, audit references, and {string.Join(", ", reporting.ReportPackTargets)} pack targets together before approvals or exports leave the workstation.", StatusLabel = "Review", CountLabel = profile.BaseCurrency, Tone = WorkspaceTone.Neutral, PrimaryActionId = "FundReportPack", PrimaryActionLabel = "Open Report Pack", SecondaryActionId = "FundAuditTrail", SecondaryActionLabel = "Audit" }
        ];
    }

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
        SummaryCashText.Text = cash is null || cash.TotalCash == 0m ? "-" : cash.TotalCash.ToString("C0");
        SummaryBreaksText.Text = reconciliation?.OpenBreakCount.ToString() ?? "-";
        SummaryJournalText.Text = ledger?.JournalEntryCount.ToString() ?? "-";

        RecentWorkList.ItemsSource = notifications.Count > 0
            ? notifications.Take(3).Select(notification => new WorkspaceRecentItem { Title = notification.Title, Detail = notification.Message, Meta = $"{notification.Timestamp:g} · {notification.Type}", Tone = notification.IsRead ? WorkspaceTone.Neutral : WorkspaceTone.Warning, ActionId = "NotificationCenter", ActionLabel = "Open Alerts" }).ToArray()
            : new[] { new WorkspaceRecentItem { Title = profile is null ? "Select the active context" : "Audit trail ready", Detail = profile is null ? "A fund-linked operating context is the main trust signal for governance review. Choose the context before working breaks or approvals." : "Open the audit trail to inspect recent governance activity and sign-off context.", Meta = profile is null ? "Locked shell" : "No recent notifications", Tone = profile is null ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = profile is null ? "SwitchContext" : "FundAuditTrail", ActionLabel = profile is null ? "Switch Context" : "Open Audit Trail" } };
    }

    private void RequestContextSelection()
        => RequestContextSelection(_fundContextService, _operatingContextService);

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
}
