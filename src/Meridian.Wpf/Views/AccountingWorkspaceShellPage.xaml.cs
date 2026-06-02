using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

public partial class AccountingWorkspaceShellPage : AccountingWorkspaceShellPageBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly FundOperationsWorkspaceReadService _fundOperationsWorkspaceReadService;
    private readonly Meridian.Wpf.Services.NotificationService _notificationService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private readonly IAccountingConfigurationService? _accountingConfigurationService;
    private AccountingSubarea _selectedSubarea = AccountingSubarea.Operations;
    private FundProfileDetail? _lastProfile;
    private FundOperationsWorkspaceDto? _lastWorkspace;
    private WorkstationOperatingContext? _lastOperatingContext;
    private WorkspaceWorkflowSummary? _lastWorkflow;
    private IReadOnlyList<NotificationHistoryItem> _lastNotifications = Array.Empty<NotificationHistoryItem>();
    private int _lastUnreadAlerts;
    private string _heroPrimaryActionId = "SwitchContext";
    private string _heroSecondaryActionId = "Diagnostics";

    public AccountingWorkspaceShellPage(
        NavigationService navigationService,
        AccountingWorkspaceShellStateProvider stateProvider,
        AccountingWorkspaceShellViewModel viewModel,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService,
        FundOperationsWorkspaceReadService fundOperationsWorkspaceReadService,
        Meridian.Wpf.Services.NotificationService notificationService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null,
        IAccountingConfigurationService? accountingConfigurationService = null)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _shellContextService = shellContextService;
        _fundOperationsWorkspaceReadService = fundOperationsWorkspaceReadService;
        _notificationService = notificationService;
        _workflowSummaryService = workflowSummaryService;
        _accountingConfigurationService = accountingConfigurationService;
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
        await RestoreDockLayoutAsync(AccountingDockManager);
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

        _ = SaveDockLayoutAsync(AccountingDockManager);
    }

    private async Task RefreshAsync()
    {
        try
        {
            var profile = _fundContextService.CurrentFundProfile;
            var operatingContext = _operatingContextService?.CurrentContext;
            var unreadAlerts = _shellContextService.GetUnreadAlertCount();
            var notifications = _notificationService.GetHistory().Take(4).ToArray();
            var workflowSummaryTask = GetAccountingWorkflowSummaryAsync();
            UpdateSubareaButtons();

            if (profile is null)
            {
                ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
                {
                    WorkspaceTitle = "Accounting Workspace",
                    WorkspaceSubtitle = "Organization-aware review shell for operations, accounting, reconciliation, reporting, and audit posture.",
                    PrimaryScopeLabel = "Context",
                    PrimaryScopeValue = operatingContext?.DisplayName ?? "Awaiting fund-linked scope",
                    AsOfValue = "Awaiting fund-linked scope",
                    FreshnessValue = operatingContext is null ? "No active operating context" : $"{operatingContext.ScopeKind.ToDisplayName()} selected",
                    ReviewStateLabel = "Access",
                    ReviewStateValue = "Locked",
                    ReviewStateTone = WorkspaceTone.Warning,
                    CriticalLabel = "Attention",
                    CriticalValue = unreadAlerts > 0 ? $"{unreadAlerts} unread alert(s)" : "Switch context to unlock accounting queues",
                    CriticalTone = unreadAlerts > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info
                });

                ViewModel.CommandGroup = BuildCommandGroup(hasFund: false);
                CommandBar.CommandGroup = ViewModel.CommandGroup;
                NoFundEmptyState.Visibility = Visibility.Visible;
                AttentionQueueScrollViewer.Visibility = Visibility.Collapsed;
                QueueScopeBadgeText.Text = operatingContext?.DisplayName ?? "Awaiting fund-linked scope";
                QueueSummaryText.Text = "Accounting queues unlock after a fund-linked operating context is selected.";
                var workflow = await workflowSummaryTask.ConfigureAwait(true);
                FinancialOperationsWorkflowStepsList.ItemsSource = AccountingWorkspacePresentationService.BuildFinancialOperationsWorkflowSteps(
                    profile: null,
                    workspace: null,
                    workflow,
                    notifications,
                    unreadAlerts);
                ApplyAccountingLaneSummaries(profile: null, workspace: null, workflow, notifications, unreadAlerts);
                if (workflow is not null)
                {
                    QueueSummaryText.Text = workflow.StatusDetail;
                }

                _lastProfile = null;
                _lastWorkspace = null;
                _lastOperatingContext = operatingContext;
                _lastWorkflow = workflow;
                _lastNotifications = notifications;
                _lastUnreadAlerts = unreadAlerts;
                UpdateAccountingHero();

                PopulateQueues([], [], [], [], []);
                PopulateInspector(operatingContext, null, null, null, null, notifications);
                ApplyAccountingConfigurationWorkspace(null, "Select a fund-linked context before configuration readiness can be loaded.");
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
            var accountingWorkflow = await workflowSummaryTask.ConfigureAwait(true);

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                    WorkspaceTitle = "Accounting Workspace",
                WorkspaceSubtitle = "Review operations, accounting, reconciliations, reporting, and approval gates without leaving the workstation shell.",
                PrimaryScopeLabel = "Accounting Scope",
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
            QueueSummaryText.Text = accountingWorkflow?.StatusDetail
                ?? $"Prioritize operations, accounting, reconciliation, reporting, and audit review for {(operatingContext?.DisplayName ?? profile.DisplayName)}.";

            PopulateQueues(
                BuildOperationsQueue(profile, workspace),
                BuildAccountingQueue(profile, workspace),
                BuildReconciliationQueue(reconciliation, ledger),
                BuildReportingQueue(profile, workspace),
                BuildAuditQueue(reconciliation, notifications, unreadAlerts));
            FinancialOperationsWorkflowStepsList.ItemsSource = AccountingWorkspacePresentationService.BuildFinancialOperationsWorkflowSteps(
                profile,
                workspace,
                accountingWorkflow,
                notifications,
                unreadAlerts);
            ApplyAccountingLaneSummaries(profile, workspace, accountingWorkflow, notifications, unreadAlerts);

            _lastProfile = profile;
            _lastWorkspace = workspace;
            _lastOperatingContext = operatingContext;
            _lastWorkflow = accountingWorkflow;
            _lastNotifications = notifications;
            _lastUnreadAlerts = unreadAlerts;
            UpdateAccountingHero();

            PopulateInspector(operatingContext, profile, ledger, reconciliation, cash, notifications);
            await RefreshAccountingConfigurationAsync(profile).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[AccountingWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private async Task<WorkspaceWorkflowSummary?> GetAccountingWorkflowSummaryAsync()
    {
        if (_workflowSummaryService is null)
        {
            return null;
        }

        try
        {
            var summary = await _workflowSummaryService
                .GetAsync(
                    hasOperatingContext: _operatingContextService?.CurrentContext is not null || _fundContextService.CurrentFundProfile is not null,
                    operatingContextDisplayName: _operatingContextService?.CurrentContext?.DisplayName,
                    fundProfileId: _fundContextService.CurrentFundProfile?.FundProfileId,
                    fundDisplayName: _fundContextService.CurrentFundProfile?.DisplayName)
                .ConfigureAwait(true);

            return summary.Workspaces.FirstOrDefault(static workspace =>
                       string.Equals(workspace.WorkspaceId, "accounting", StringComparison.OrdinalIgnoreCase))
                   ?? summary.Workspaces.FirstOrDefault(static workspace =>
                       string.Equals(workspace.WorkspaceId, "governance", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(AccountingDockManager, e.PageTag, e.Action);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e) => ExecuteAction(e.Command.Id, navigate: false);
    private void SwitchFund_Click(object sender, RoutedEventArgs e) => RequestContextSelection();
    private void SwitchContext_Click(object sender, RoutedEventArgs e) => RequestContextSelection();
    private void OpenDiagnosticsFromEmptyState_Click(object sender, RoutedEventArgs e) => ExecuteAction("Diagnostics", navigate: false);
    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e) => ExecuteAction("FundAuditTrail", navigate: false);
    private void OpenDataQuality_Click(object sender, RoutedEventArgs e) => ExecuteAction("DataQuality", navigate: false);
    private void OpenSystemHealth_Click(object sender, RoutedEventArgs e) => ExecuteAction("SystemHealth", navigate: false);
    private void OpenNotifications_Click(object sender, RoutedEventArgs e) => ExecuteAction("NotificationCenter", navigate: false);
    private void OnInspectorActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            ExecuteAction(actionId, navigate: false);
        }
    }
    private void OnAccountingHeroPrimaryActionClick(object sender, RoutedEventArgs e) => ExecuteAction(_heroPrimaryActionId, navigate: false);
    private void OnAccountingHeroSecondaryActionClick(object sender, RoutedEventArgs e) => ExecuteAction(_heroSecondaryActionId, navigate: false);
    private void OpenOperationsLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(AccountingSubarea.Operations);
    private void OpenAccountingLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(AccountingSubarea.Accounting);
    private void OpenReconciliationLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(AccountingSubarea.Reconciliation);
    private void OpenReportingLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(AccountingSubarea.Reporting);
    private void OpenAuditLane_Click(object sender, RoutedEventArgs e) => SelectSubarea(AccountingSubarea.Audit);

    private void OnAccountingDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => ExecuteAction(e.ActionId, navigate: false);

    private void OnFinancialOperationsWorkflowStepClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            ExecuteAction(actionId, navigate: false);
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

        OpenWorkspacePage(
            AccountingDockManager,
            actionId,
            AccountingWorkspacePresentationService.ResolveDockAction(actionId));
    }

    private static WorkspaceCommandGroup BuildCommandGroup(bool hasFund)
        => AccountingWorkspacePresentationService.BuildCommandGroup(hasFund);

    private static IReadOnlyList<WorkspaceQueueItem> BuildOperationsQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
        => AccountingWorkspacePresentationService.BuildOperationsQueue(profile, workspace);

    private static IReadOnlyList<WorkspaceQueueItem> BuildAccountingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
        => AccountingWorkspacePresentationService.BuildAccountingQueue(profile, workspace);

    private static IReadOnlyList<WorkspaceQueueItem> BuildReconciliationQueue(ReconciliationSummary reconciliation, FundLedgerSummary? ledger)
        => AccountingWorkspacePresentationService.BuildReconciliationQueue(reconciliation, ledger);

    private static IReadOnlyList<WorkspaceQueueItem> BuildReportingQueue(
        FundProfileDetail profile,
        FundOperationsWorkspaceDto workspace)
        => AccountingWorkspacePresentationService.BuildReportingQueue(profile, workspace);

    private static IReadOnlyList<WorkspaceQueueItem> BuildAuditQueue(
        ReconciliationSummary reconciliation,
        IReadOnlyList<NotificationHistoryItem> notifications,
        int unreadAlerts)
        => AccountingWorkspacePresentationService.BuildAuditQueue(reconciliation, notifications, unreadAlerts);

    private void ApplyAccountingLaneSummaries(
        FundProfileDetail? profile,
        FundOperationsWorkspaceDto? workspace,
        WorkspaceWorkflowSummary? workflow,
        IReadOnlyList<NotificationHistoryItem> notifications,
        int unreadAlerts)
    {
        var summaries = AccountingWorkspacePresentationService.BuildLaneSummaries(profile, workspace, workflow, notifications, unreadAlerts);
        SetLaneSummary(AccountingLaneSummaryText, AccountingLaneDetailText, summaries.Accounting.Summary, summaries.Accounting.Detail);
        SetLaneSummary(ReconciliationLaneSummaryText, ReconciliationLaneDetailText, summaries.Reconciliation.Summary, summaries.Reconciliation.Detail);
        SetLaneSummary(ReportingLaneSummaryText, ReportingLaneDetailText, summaries.Reporting.Summary, summaries.Reporting.Detail);
        SetLaneSummary(AuditLaneSummaryText, AuditLaneDetailText, summaries.Audit.Summary, summaries.Audit.Detail);
    }

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
            : new[] { new WorkspaceRecentItem { Title = profile is null ? "Select the active context" : "Audit trail ready", Detail = profile is null ? "A fund-linked operating context is the main trust signal for accounting review. Choose the context before working breaks or approvals." : "Open the audit trail to inspect recent accounting activity and sign-off context.", Meta = profile is null ? "Locked shell" : "No recent notifications", Tone = profile is null ? WorkspaceTone.Warning : WorkspaceTone.Info, ActionId = profile is null ? "SwitchContext" : "FundAuditTrail", ActionLabel = profile is null ? "Switch Context" : "Open Audit Trail" } };
    }

    private async Task RefreshAccountingConfigurationAsync(FundProfileDetail profile)
    {
        if (_accountingConfigurationService is null)
        {
            ApplyAccountingConfigurationWorkspace(null, "Accounting configuration service is not registered for this desktop session.");
            return;
        }

        try
        {
            var workspace = await _accountingConfigurationService
                .GetWorkspaceAsync(profile.FundProfileId)
                .ConfigureAwait(true);
            ApplyAccountingConfigurationWorkspace(workspace, null);
        }
        catch (Exception ex)
        {
            ApplyAccountingConfigurationWorkspace(null, $"Accounting configuration could not be loaded: {ex.Message}");
        }
    }

    private void ApplyAccountingConfigurationWorkspace(AccountingConfigurationWorkspaceDto? workspace, string? fallbackDetail)
    {
        if (workspace is null)
        {
            AccountingConfigurationStatusText.Text = "Not available";
            AccountingConfigurationDetailText.Text = fallbackDetail ?? "Accounting configuration readiness has not loaded.";
            AccountingConfigurationChartCountText.Text = "-";
            AccountingConfigurationTemplateCountText.Text = "-";
            AccountingConfigurationRuleCountText.Text = "-";
            AccountingConfigurationAuditText.Text = "No configuration audit events loaded.";
            return;
        }

        var criticalIssues = workspace.ValidationIssues.Count(issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        var activeChart = workspace.ChartOfAccounts.Count(node => !node.IsArchived);
        var activeTemplates = workspace.JournalTemplates.Count(template => !template.IsArchived);
        var activeRules = workspace.PostingRules.Count(rule => !rule.IsArchived);
        AccountingConfigurationStatusText.Text = $"{workspace.Status} {workspace.ConfigurationVersion}";
        AccountingConfigurationDetailText.Text = criticalIssues > 0
            ? $"{criticalIssues} critical validation issue(s) block activation. Review the browser Configure workflow for remediation."
            : "Configuration readiness is sourced from shared DTOs; WPF does not fork accounting rules or validation logic.";
        AccountingConfigurationChartCountText.Text = activeChart.ToString();
        AccountingConfigurationTemplateCountText.Text = activeTemplates.ToString();
        AccountingConfigurationRuleCountText.Text = activeRules.ToString();
        AccountingConfigurationAuditText.Text = workspace.AuditTrail.Count == 0
            ? "No configuration audit events recorded yet."
            : $"{workspace.AuditTrail.Count} audit event(s); latest {workspace.AuditTrail[0].Action} by {workspace.AuditTrail[0].Actor}.";
    }

    private void UpdateAccountingHero()
    {
        var hero = AccountingWorkspacePresentationService.BuildLaneHeroState(
            _selectedSubarea,
            _lastOperatingContext,
            _lastProfile,
            _lastWorkspace,
            _lastWorkflow,
            _lastNotifications,
            _lastUnreadAlerts);

        AccountingHeroLaneText.Text = hero.LaneLabel;
        AccountingHeroSummaryText.Text = hero.Summary;
        AccountingHeroDetailText.Text = hero.Detail;
        AccountingHeroActionTitleText.Text = hero.HandoffTitle;
        AccountingHeroActionDetailText.Text = hero.HandoffDetail;
        AccountingHeroTargetText.Text = hero.TargetLabel;
        AccountingHeroPrimaryActionButton.Content = hero.PrimaryActionLabel;
        AccountingHeroSecondaryActionButton.Content = hero.SecondaryActionLabel;
        AccountingHeroSecondaryActionButton.Visibility = string.IsNullOrWhiteSpace(hero.SecondaryActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _heroPrimaryActionId = hero.PrimaryActionId;
        _heroSecondaryActionId = hero.SecondaryActionId;
    }

    private void RequestContextSelection()
        => RequestContextSelection(_fundContextService, _operatingContextService);

    private void SelectSubarea(AccountingSubarea subarea)
    {
        _selectedSubarea = subarea;
        UpdateSubareaButtons();
        UpdateAccountingHero();

        if (_fundContextService.CurrentFundProfile is null)
        {
            return;
        }

        ExecuteAction(AccountingWorkspacePresentationService.ResolveLanePrimaryActionId(subarea), navigate: false);
    }

    private void UpdateSubareaButtons()
    {
        ApplySubareaStyle(OperationsLaneButton, _selectedSubarea == AccountingSubarea.Operations);
        ApplySubareaStyle(AccountingLaneButton, _selectedSubarea == AccountingSubarea.Accounting);
        ApplySubareaStyle(ReconciliationLaneButton, _selectedSubarea == AccountingSubarea.Reconciliation);
        ApplySubareaStyle(ReportingLaneButton, _selectedSubarea == AccountingSubarea.Reporting);
        ApplySubareaStyle(AuditLaneButton, _selectedSubarea == AccountingSubarea.Audit);
    }

    private static void ApplySubareaStyle(Button button, bool isSelected)
    {
        var resourceKey = isSelected ? "SecondaryButtonStyle" : "GhostButtonStyle";
        button.Style = (Style)System.Windows.Application.Current.FindResource(resourceKey);
    }

    private static void SetLaneSummary(TextBlock summaryText, TextBlock detailText, string summary, string detail)
    {
        summaryText.Text = summary;
        detailText.Text = detail;
    }

}
