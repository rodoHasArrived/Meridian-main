using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Meridian.Strategies.Promotions;
using Meridian.Strategies.Services;
using Meridian.Ui.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Wpf.Copy;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Research workspace shell — landing page for the Research workspace.
/// Surfaces recent strategy runs, KPIs, quick actions, and the promotion pipeline.
/// Embeds a <see cref="MeridianDockingManager"/> for IDE-style floating panes.
/// </summary>
public partial class ResearchWorkspaceShellPage : ResearchWorkspaceShellPageBase
{
    internal enum ResearchDeskHeroTone : byte
    {
        Info,
        Success,
        Warning
    }

    internal readonly record struct ResearchDeskHeroState(
        string FocusLabel,
        string Summary,
        string Detail,
        string BadgeText,
        ResearchDeskHeroTone BadgeTone,
        string HandoffTitle,
        string HandoffDetail,
        string PrimaryActionId,
        string PrimaryActionLabel,
        string SecondaryActionId,
        string SecondaryActionLabel,
        string TargetLabel);

    private readonly StrategyRunWorkspaceService _runService;
    private readonly IResearchBriefingWorkspaceService _briefingService;
    private readonly Meridian.Wpf.Services.WatchlistService _watchlistService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private bool _canPromoteActiveRun;
    private bool _canOpenTradingCockpit;
    private string _heroPrimaryActionId = "Backtest";
    private string _heroSecondaryActionId = "Watchlist";

    public ResearchWorkspaceShellPage(
        NavigationService navigationService,
        ResearchWorkspaceShellStateProvider stateProvider,
        ResearchWorkspaceShellViewModel viewModel,
        StrategyRunWorkspaceService runService,
        IResearchBriefingWorkspaceService briefingService,
        Meridian.Wpf.Services.WatchlistService watchlistService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService,
        WorkstationWorkflowSummaryService? workflowSummaryService = null)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _runService = runService;
        _briefingService = briefingService;
        _watchlistService = watchlistService;
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _shellContextService = shellContextService;
        _workflowSummaryService = workflowSummaryService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;
        _watchlistService.WatchlistsChanged += OnWatchlistsChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
        }

        await RefreshAsync();
        await RestoreDockLayoutAsync(ResearchDockManager);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _watchlistService.WatchlistsChanged -= OnWatchlistsChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
        }
        _ = SaveDockLayoutAsync(ResearchDockManager);
    }

    // ── Data ─────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            var summaryTask = _runService.GetResearchSummaryAsync();
            var activeRunTask = _runService.GetActiveRunContextAsync();
            var briefingTask = _briefingService.GetBriefingAsync();
            var workflowTask = GetResearchWorkflowSummaryAsync();
            await System.Threading.Tasks.Task.WhenAll(summaryTask, activeRunTask, briefingTask, workflowTask);

            var summary = await summaryTask;
            var activeRun = await activeRunTask;
            var briefing = await briefingTask;
            var workflow = await workflowTask;

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(
                BuildShellContextInput(summary, activeRun, briefing));
            TotalRunsText.Text = summary.TotalRuns.ToString();
            PromotedText.Text = summary.PromotedCount.ToString();
            PendingReviewText.Text = summary.PendingReviewCount.ToString();
            PromotionCountBadge.Text = summary.PendingReviewCount.ToString();

            if (summary.RecentRuns.Count > 0)
            {
                RecentRunsList.ItemsSource = summary.RecentRuns;
                NoRunsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                RecentRunsList.ItemsSource = null;
                NoRunsText.Visibility = Visibility.Visible;
            }

            if (summary.PromotionCandidates.Count > 0)
            {
                PromotionCandidatesList.ItemsSource = summary.PromotionCandidates;
                NoPromotionsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                PromotionCandidatesList.ItemsSource = null;
                NoPromotionsText.Visibility = Visibility.Visible;
            }

            UpdateBriefing(briefing);
            UpdateActiveRunContext(activeRun);
            UpdateWorkflowHandoff(summary, activeRun, workflow);
            ViewModel.CommandGroup = BuildCommandGroup();
            CommandBar.CommandGroup = ViewModel.CommandGroup;
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private async Task<WorkspaceWorkflowSummary?> GetResearchWorkflowSummaryAsync()
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
                string.Equals(workspace.WorkspaceId, "research", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static WorkspaceWorkflowSummary CreateFallbackWorkflowSummary()
        => new(
            WorkspaceId: "research",
            WorkspaceTitle: "Research",
            StatusLabel: "Ready for a new research cycle",
            StatusDetail: "No live workflow summary is available, so the shell is using deterministic fallback guidance.",
            StatusTone: "Info",
            NextAction: new WorkflowNextAction(
                Label: "Start Backtest",
                Detail: "Launch a new simulation from the research workspace.",
                TargetPageTag: "Backtest",
                Tone: "Primary"),
            PrimaryBlocker: new WorkflowBlockerSummary(
                Code: "fallback",
                Label: "Workflow summary unavailable",
                Detail: "Fallback guidance keeps one stable next action visible while shared workflow data refreshes.",
                Tone: "Info",
                IsBlocking: false),
            Evidence: []);

    private void UpdateWorkflowHandoff(
        ResearchWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary? workflow)
    {
        var effectiveWorkflow = workflow ?? CreateFallbackWorkflowSummary();
        var hero = BuildDeskHeroState(summary, activeRun, effectiveWorkflow);

        ResearchHeroFocusText.Text = hero.FocusLabel;
        ResearchHeroBadgeText.Text = hero.BadgeText;
        ApplyHeroTone(ResearchHeroBadgeBorder, ResearchHeroBadgeText, hero.BadgeTone);
        ResearchWorkflowStatusText.Text = hero.Summary;
        ResearchWorkflowDetailText.Text = hero.Detail;
        ResearchHeroActionTitleText.Text = hero.HandoffTitle;
        ResearchHeroActionDetailText.Text = hero.HandoffDetail;
        ResearchWorkflowBlockerLabelText.Text = effectiveWorkflow.PrimaryBlocker.Label;
        ResearchWorkflowBlockerDetailText.Text = effectiveWorkflow.PrimaryBlocker.Detail;
        ResearchWorkflowTargetText.Text = hero.TargetLabel;
        ResearchWorkflowEvidenceItems.ItemsSource = effectiveWorkflow.Evidence
            .Select(static evidence => $"{evidence.Label}: {evidence.Value}")
            .ToArray();
        ResearchHeroPrimaryActionButton.Content = hero.PrimaryActionLabel;
        ResearchHeroSecondaryActionButton.Content = hero.SecondaryActionLabel;
        ResearchHeroSecondaryActionButton.Visibility = string.IsNullOrWhiteSpace(hero.SecondaryActionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _heroPrimaryActionId = hero.PrimaryActionId;
        _heroSecondaryActionId = hero.SecondaryActionId;
    }

    private void UpdateActiveRunContext(ActiveRunContext? activeContext)
    {
        if (activeContext is null)
        {
            ActiveRunNameText.Text = "No selected run";
            ActiveRunMetaText.Text = "Start a backtest or choose a run from history.";
            ScenarioStrategyText.Text = "No strategy selected";
            ScenarioCoverageText.Text = "No research session restored.";
            RunStatusText.Text = "Awaiting run selection";
            RunPerformanceText.Text = "Compare runs, equity, and fills from a selected strategy run.";
            RunCompareText.Text = "Use the bottom history rail to select a run and load detail panels.";
            PortfolioPreviewText.Text = "Portfolio inspector opens here once a run is selected.";
            LedgerPreviewText.Text = "Accounting impact preview opens here once a run is selected.";
            RiskPreviewText.Text = "Risk and audit preview becomes available after a completed run is selected.";
            _canPromoteActiveRun = false;
            _canOpenTradingCockpit = false;
            return;
        }

        ActiveRunNameText.Text = activeContext.StrategyName;
        ActiveRunMetaText.Text = $"{activeContext.ModeLabel} · {activeContext.StatusLabel} · {activeContext.FundScopeLabel}";
        ScenarioStrategyText.Text = $"{activeContext.StrategyName} ({activeContext.RunId})";
        ScenarioCoverageText.Text = $"Session scope: {activeContext.FundScopeLabel}";
        RunStatusText.Text = $"{activeContext.ModeLabel} run selected";
        RunPerformanceText.Text = activeContext.PortfolioPreview;
        RunCompareText.Text = activeContext.RiskSummary;
        PortfolioPreviewText.Text = activeContext.PortfolioPreview;
        LedgerPreviewText.Text = $"{activeContext.LedgerPreview} Open accounting impact to verify trial-balance continuity before promotion.";
        RiskPreviewText.Text = $"{activeContext.RiskSummary} Audit and reconciliation drill-ins stay one action away from the same shell.";
        _canPromoteActiveRun = activeContext.CanPromoteToPaper;
        _canOpenTradingCockpit = true;
    }

    internal static ResearchDeskHeroState BuildDeskHeroState(
        ResearchWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary workflow)
    {
        if (activeRun is not null && activeRun.CanPromoteToPaper)
        {
            return new ResearchDeskHeroState(
                FocusLabel: "Promotion review",
                Summary: $"{activeRun.StrategyName} is ready for paper handoff.",
                Detail: BuildActiveRunHeroDetail(activeRun),
                BadgeText: "Ready",
                BadgeTone: ResearchDeskHeroTone.Success,
                HandoffTitle: "Carry the run into trading review",
                HandoffDetail: "Open the trading shell with the selected run still attached, then keep portfolio, ledger, and audit evidence visible before approving the next mode.",
                PrimaryActionId: "TradingShell",
                PrimaryActionLabel: "Open Trading Review",
                SecondaryActionId: "PromoteToPaper",
                SecondaryActionLabel: "Promote to Paper",
                TargetLabel: "Target page: TradingShell");
        }

        if (activeRun is not null)
        {
            return new ResearchDeskHeroState(
                FocusLabel: "Selected run",
                Summary: $"{activeRun.StrategyName} is the active research run.",
                Detail: BuildActiveRunHeroDetail(activeRun),
                BadgeText: workflow.PrimaryBlocker.IsBlocking ? "Attention" : "In review",
                BadgeTone: workflow.PrimaryBlocker.IsBlocking ? ResearchDeskHeroTone.Warning : ResearchDeskHeroTone.Info,
                HandoffTitle: "Continue run review",
                HandoffDetail: "Keep run detail, portfolio, and ledger inspectors docked beside the active run before handing it forward.",
                PrimaryActionId: "RunDetail",
                PrimaryActionLabel: "Open Run Detail",
                SecondaryActionId: "RunPortfolio",
                SecondaryActionLabel: "Open Portfolio",
                TargetLabel: "Target page: RunDetail");
        }

        if (summary.PendingReviewCount > 0 || string.Equals(workflow.NextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase))
        {
            var queueCountLabel = summary.PendingReviewCount == 1
                ? "1 run is waiting for trading review."
                : $"{summary.PendingReviewCount} run(s) are waiting for trading review.";

            return new ResearchDeskHeroState(
                FocusLabel: "Promotion queue",
                Summary: summary.PendingReviewCount > 0 ? queueCountLabel : workflow.StatusLabel,
                Detail: workflow.StatusDetail,
                BadgeText: "Attention",
                BadgeTone: ResearchDeskHeroTone.Warning,
                HandoffTitle: "Stage the next promotion candidate",
                HandoffDetail: "Open the run browser first, choose the candidate with complete evidence attached, then carry it into the trading shell.",
                PrimaryActionId: "StrategyRuns",
                PrimaryActionLabel: "Run Browser",
                SecondaryActionId: "Watchlist",
                SecondaryActionLabel: "Open Watchlists",
                TargetLabel: "Target page: StrategyRuns");
        }

        if (summary.TotalRuns == 0)
        {
            return new ResearchDeskHeroState(
                FocusLabel: "New cycle",
                Summary: "Research queue is empty.",
                Detail: "Start a backtest and stage a watchlist to seed comparisons, alerts, and the promotion pipeline.",
                BadgeText: "Setup",
                BadgeTone: ResearchDeskHeroTone.Info,
                HandoffTitle: "Launch the first run",
                HandoffDetail: "Use Backtest to record the first scenario, then keep symbols staged in Watchlists for follow-on analysis.",
                PrimaryActionId: "Backtest",
                PrimaryActionLabel: "Start Backtest",
                SecondaryActionId: "Watchlist",
                SecondaryActionLabel: "Open Watchlists",
                TargetLabel: "Target page: Backtest");
        }

        var primaryActionId = ResolveHeroActionId(workflow.NextAction, hasActiveRun: false);
        var secondaryActionId = primaryActionId == "StrategyRuns" ? "RunMat" : "StrategyRuns";

        return new ResearchDeskHeroState(
            FocusLabel: "Research cycle",
            Summary: workflow.StatusLabel,
            Detail: workflow.StatusDetail,
            BadgeText: ParseHeroTone(workflow.StatusTone) switch
            {
                ResearchDeskHeroTone.Success => "Ready",
                ResearchDeskHeroTone.Warning => "Attention",
                _ => "Focus"
            },
            BadgeTone: ParseHeroTone(workflow.StatusTone),
            HandoffTitle: "Keep the next action docked",
            HandoffDetail: workflow.NextAction.Detail,
            PrimaryActionId: primaryActionId,
            PrimaryActionLabel: ResolveHeroActionLabel(primaryActionId, workflow.NextAction.Label),
            SecondaryActionId: secondaryActionId,
            SecondaryActionLabel: ResolveHeroActionLabel(secondaryActionId, secondaryActionId),
            TargetLabel: $"Target page: {ResolveHeroTargetLabel(primaryActionId, workflow.NextAction.TargetPageTag)}");
    }

    private static string BuildActiveRunHeroDetail(ActiveRunContext activeRun)
    {
        var validationDetail = string.IsNullOrWhiteSpace(activeRun.ValidationStatus.Detail)
            || string.Equals(activeRun.ValidationStatus.Detail, "Status detail unavailable.", StringComparison.Ordinal)
                ? activeRun.RiskSummary
                : activeRun.ValidationStatus.Detail;
        return $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}. {validationDetail}";
    }

    private void UpdateBriefing(ResearchBriefingDto briefing)
    {
        BriefingSummaryText.Text = briefing.Workspace.Summary;
        BriefingGeneratedText.Text = $"Updated {FormatBriefingTimestamp(briefing.InsightFeed.GeneratedAt)}";

        BindItems(BriefingInsightsList, NoBriefingInsightsText, briefing.InsightFeed.Widgets);
        BindItems(BriefingWatchlistsList, NoBriefingWatchlistsText, briefing.Watchlists);
        BindItems(BriefingWhatChangedList, NoBriefingWhatChangedText, briefing.WhatChanged);
        BindItems(BriefingAlertsList, NoBriefingAlertsText, briefing.Alerts);
        BindItems(BriefingComparisonsList, NoBriefingComparisonsText, briefing.SavedComparisons);
    }

    private static WorkspaceShellContextInput BuildShellContextInput(
        ResearchWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        ResearchBriefingDto briefing)
    {
        var promotionCandidates = briefing.Workspace.PromotionCandidates;
        var alertCount = briefing.Alerts.Count;
        var activeSessions = briefing.Workspace.ActiveRuns;

        return new WorkspaceShellContextInput
        {
            WorkspaceTitle = WorkspaceCopyCatalog.Research.ShellTitle,
            WorkspaceSubtitle = WorkspaceCopyCatalog.Research.ShellSubtitle,
            PrimaryScopeLabel = WorkspaceCopyCatalog.Research.PrimaryScopeLabel,
            PrimaryScopeValue = activeRun?.StrategyName
                ?? briefing.Workspace.LatestStrategyName
                ?? "No active research run",
            AsOfValue = briefing.InsightFeed.GeneratedAt.ToString("MMM dd yyyy HH:mm"),
            FreshnessValue = activeRun is null
                ? $"{activeSessions} active session(s)"
                : $"{activeRun.ModeLabel} · {activeRun.StatusLabel}",
            ReviewStateLabel = "Promotion",
            ReviewStateValue = promotionCandidates > 0
                ? $"{promotionCandidates} candidate(s)"
                : "No promotion queue",
            ReviewStateTone = promotionCandidates > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success,
            CriticalLabel = "Alerts",
            CriticalValue = alertCount > 0 ? $"{alertCount} alert(s)" : "No blocking alerts",
            CriticalTone = alertCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info,
            AdditionalBadges =
            [
                new WorkspaceShellBadge
                {
                    Label = "Watchlists",
                    Value = briefing.Watchlists.Count > 0 ? $"{briefing.Watchlists.Count} staged" : string.Empty,
                    Glyph = "\uE8D4",
                    Tone = WorkspaceTone.Info
                },
                new WorkspaceShellBadge
                {
                    Label = "Comparisons",
                    Value = briefing.SavedComparisons.Count > 0 ? $"{briefing.SavedComparisons.Count} saved" : string.Empty,
                    Glyph = "\uE9D9",
                    Tone = WorkspaceTone.Neutral
                },
                new WorkspaceShellBadge
                {
                    Label = "Runs",
                    Value = summary.TotalRuns > 0 ? $"{summary.TotalRuns} tracked" : string.Empty,
                    Glyph = "\uE8FD",
                    Tone = WorkspaceTone.Neutral
                }
            ]
        };
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
    {
        var pageTag = e.PageTag;
        object? parameter = null;
        var action = e.Action;

        try
        {
            var pageContent = NavigationService.CreatePageContent(pageTag, parameter);
            ResearchDockManager.LoadPage(BuildPageKey(pageTag, parameter), ShellNavigationCatalog.GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            ResearchDockManager.LoadPage(
                BuildPageKey(pageTag, parameter),
                ShellNavigationCatalog.GetPageTitle(pageTag),
                WorkspaceShellFallbackContentFactory.CreateDockFailureContent(ShellNavigationCatalog.GetPageTitle(pageTag), ex),
                NormalizeDockAction(action));
        }
    }

    private async Task OpenActiveRunPageAsync(string pageTag, PaneDropAction action)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        OpenWorkspacePage(ResearchDockManager, pageTag, action, activeRun?.RunId);
    }

    // ── Quick Action Handlers ─────────────────────────────────────────────

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
    {
        switch (e.Command.Id)
        {
            case "ResetStudio":
                _ = LoadDefaultDockLayoutAsync(ResearchDockManager);
                break;
            case "PromoteToPaper":
                if (_canPromoteActiveRun)
                {
                    PromoteActiveRun_Click(sender, new RoutedEventArgs());
                }
                break;
            case "OpenTradingCockpit":
                if (_canOpenTradingCockpit)
                {
                    OpenTradingCockpit_Click(sender, new RoutedEventArgs());
                }
                break;
            case "StrategyRuns":
                OpenStrategyRuns_Click(sender, new RoutedEventArgs());
                break;
            case "RunDetail":
                OpenRunDetailDocked_Click(sender, new RoutedEventArgs());
                break;
            case "RunPortfolio":
                OpenPortfolioInspector_Click(sender, new RoutedEventArgs());
                break;
            case "RunLedger":
                OpenLedgerInspector_Click(sender, new RoutedEventArgs());
                break;
            case "FundTrialBalance":
                OpenAccountingImpact_Click(sender, new RoutedEventArgs());
                break;
            case "FundReconciliation":
                OpenReconciliationPreview_Click(sender, new RoutedEventArgs());
                break;
            case "FundAuditTrail":
                OpenAuditTrail_Click(sender, new RoutedEventArgs());
                break;
            case "Watchlist":
                OpenWatchlists_Click(sender, new RoutedEventArgs());
                break;
            case "LeanIntegration":
                OpenLean_Click(sender, new RoutedEventArgs());
                break;
        }
    }

    private void OnResearchHeroPrimaryActionClick(object sender, RoutedEventArgs e)
        => ExecuteHeroAction(_heroPrimaryActionId, sender, e);

    private void OnResearchHeroSecondaryActionClick(object sender, RoutedEventArgs e)
        => ExecuteHeroAction(_heroSecondaryActionId, sender, e);

    private void ExecuteHeroAction(string actionId, object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        switch (actionId)
        {
            case "Backtest":
                OpenBacktest_Click(sender, e);
                return;
            case "RunMat":
                OpenRunMat_Click(sender, e);
                return;
            case "StrategyRuns":
                OpenStrategyRuns_Click(sender, e);
                return;
            case "RunDetail":
                OpenRunDetailDocked_Click(sender, e);
                return;
            case "RunPortfolio":
                OpenPortfolioInspector_Click(sender, e);
                return;
            case "Watchlist":
                OpenWatchlists_Click(sender, e);
                return;
            case "TradingShell":
                OpenTradingCockpit_Click(sender, e);
                return;
            case "PromoteToPaper":
                PromoteActiveRun_Click(sender, e);
                return;
            default:
                NavigationService.NavigateTo(actionId);
                return;
        }
    }

    private void OpenRunMat_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("RunMat");

    private void OpenBacktest_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("Backtest");

    private void OpenCharts_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("Charts");

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("StrategyRuns");

    private void OpenWatchlists_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("Watchlist");

    private async void PromoteActiveRun_Click(object sender, RoutedEventArgs e)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is null)
        {
            return;
        }

        try
        {
            var promotionService = App.Services.GetService(typeof(PromotionService)) as PromotionService;
            if (promotionService is null)
            {
                OpenWorkspacePage(ResearchDockManager, "RunDetail", PaneDropAction.SplitRight, activeRun.RunId);
                return;
            }

            var result = await promotionService.ApproveAsync(new PromotionApprovalRequest(
                RunId: activeRun.RunId,
                ReviewNotes: "Promoted from research workspace shell.",
                ApprovedBy: Environment.UserName,
                ApprovalReason: "Research workstation promotion",
                ApprovalChecklist: PromotionApprovalChecklist.CreateRequiredFor(RunType.Paper)));

            if (result.Success && !string.IsNullOrWhiteSpace(result.NewRunId))
            {
                await _runService.SetActiveRunContextAsync(result.NewRunId);
                NavigationService.NavigateTo("TradingShell", result.NewRunId);
            }
            else
            {
                OpenWorkspacePage(ResearchDockManager, "RunDetail", PaneDropAction.SplitRight, activeRun.RunId);
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to promote active run: {ex.Message}");
            OpenWorkspacePage(ResearchDockManager, "RunDetail", PaneDropAction.SplitRight, activeRun.RunId);
        }
    }

    private async void OpenTradingCockpit_Click(object sender, RoutedEventArgs e)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is null)
        {
            return;
        }

        await _runService.SetActiveRunContextAsync(activeRun.RunId);
        NavigationService.NavigateTo("TradingShell", activeRun.RunId);
    }

    private async void OpenRunDetailDocked_Click(object sender, RoutedEventArgs e)
        => await OpenActiveRunPageAsync("RunDetail", PaneDropAction.SplitRight);

    private async void OpenPortfolioInspector_Click(object sender, RoutedEventArgs e)
        => await OpenActiveRunPageAsync("RunPortfolio", PaneDropAction.SplitBelow);

    private async void OpenLedgerInspector_Click(object sender, RoutedEventArgs e)
        => await OpenActiveRunPageAsync("RunLedger", PaneDropAction.OpenTab);

    private void OpenAccountingImpact_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(ResearchDockManager, "FundTrialBalance", PaneDropAction.OpenTab);

    private void OpenReconciliationPreview_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(ResearchDockManager, "FundReconciliation", PaneDropAction.SplitBelow);

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage(ResearchDockManager, "FundAuditTrail", PaneDropAction.OpenTab);

    private void OpenLean_Click(object sender, RoutedEventArgs e)
        => NavigationService.NavigateTo("LeanIntegration");

    private void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            NavigationService.NavigateTo("RunDetail", runId);
        }
    }

    private async void OpenRunFromHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            await OpenRunInStudioAsync(runId);
        }
    }

    private async void OpenBriefingRun_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId } && !string.IsNullOrWhiteSpace(runId))
        {
            await OpenRunInStudioAsync(runId);
        }
    }

    private async void OpenBriefingAlert_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId } && !string.IsNullOrWhiteSpace(runId))
        {
            await OpenRunInStudioAsync(runId);
            return;
        }

        NavigationService.NavigateTo("StrategyRuns");
    }

    private async void OpenBriefingComparison_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId } && !string.IsNullOrWhiteSpace(runId))
        {
            await _runService.SetActiveRunContextAsync(runId);
            NavigationService.NavigateTo("StrategyRuns", runId);
            return;
        }

        NavigationService.NavigateTo("StrategyRuns");
    }

    private void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnActiveFundProfileChanged(sender, e));
            return;
        }

        _ = RefreshAsync();
    }

    private void OnSignalsChanged(object? sender, EventArgs e)
        => DispatchRefresh(RefreshAsync);

    private void OnWatchlistsChanged(object? sender, WatchlistsChangedEventArgs e)
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

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
        => DispatchRefresh(RefreshAsync);

    private async System.Threading.Tasks.Task OpenRunInStudioAsync(string runId)
    {
        await _runService.SetActiveRunContextAsync(runId);
        OpenWorkspacePage(ResearchDockManager, "RunDetail", PaneDropAction.SplitRight, runId);
        OpenWorkspacePage(ResearchDockManager, "RunPortfolio", PaneDropAction.SplitBelow, runId);
        await RefreshAsync();
    }

    private static void BindItems<T>(ItemsControl control, TextBlock emptyState, IReadOnlyList<T> items)
    {
        if (items.Count > 0)
        {
            control.ItemsSource = items;
            emptyState.Visibility = Visibility.Collapsed;
            return;
        }

        control.ItemsSource = null;
        emptyState.Visibility = Visibility.Visible;
    }

    private static string FormatBriefingTimestamp(DateTimeOffset timestamp)
    {
        var span = DateTimeOffset.UtcNow - timestamp;
        if (span.TotalMinutes < 1)
        {
            return "just now";
        }

        if (span.TotalHours < 1)
        {
            return $"{Math.Round(span.TotalMinutes)}m ago";
        }

        if (span.TotalDays < 1)
        {
            return $"{Math.Round(span.TotalHours)}h ago";
        }

        return $"{Math.Round(span.TotalDays)}d ago";
    }

    private void ApplyHeroTone(Border border, TextBlock textBlock, ResearchDeskHeroTone tone)
    {
        var (backgroundKey, borderKey) = tone switch
        {
            ResearchDeskHeroTone.Success => ("ConsoleAccentGreenAlpha10Brush", "SuccessColorBrush"),
            ResearchDeskHeroTone.Warning => ("ConsoleAccentOrangeAlpha10Brush", "WarningColorBrush"),
            _ => ("ConsoleAccentBlueAlpha10Brush", "InfoColorBrush")
        };

        border.Background = GetBrush(backgroundKey);
        border.BorderBrush = GetBrush(borderKey);
        textBlock.Foreground = GetBrush(borderKey);
    }

    private Brush GetBrush(string resourceKey)
        => TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;

    private static ResearchDeskHeroTone ParseHeroTone(string? tone) => tone?.ToLowerInvariant() switch
    {
        "success" => ResearchDeskHeroTone.Success,
        "warning" => ResearchDeskHeroTone.Warning,
        "danger" => ResearchDeskHeroTone.Warning,
        _ => ResearchDeskHeroTone.Info
    };

    private static string ResolveHeroActionId(WorkflowNextAction nextAction, bool hasActiveRun)
    {
        if (string.Equals(nextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase))
        {
            return hasActiveRun ? "TradingShell" : "StrategyRuns";
        }

        return nextAction.TargetPageTag;
    }

    private static string ResolveHeroActionLabel(string actionId, string fallbackLabel) => actionId switch
    {
        "Backtest" => "Start Backtest",
        "RunMat" => "Open RunMat",
        "StrategyRuns" => "Run Browser",
        "RunDetail" => "Open Run Detail",
        "RunPortfolio" => "Open Portfolio",
        "Watchlist" => "Open Watchlists",
        "TradingShell" => "Open Trading Review",
        "PromoteToPaper" => "Promote to Paper",
        _ => string.IsNullOrWhiteSpace(fallbackLabel) ? "Open" : fallbackLabel
    };

    private static string ResolveHeroTargetLabel(string actionId, string fallbackTargetPageTag) => actionId switch
    {
        "Backtest" => "Backtest",
        "RunMat" => "RunMat",
        "StrategyRuns" => "StrategyRuns",
        "RunDetail" => "RunDetail",
        "RunPortfolio" => "RunPortfolio",
        "Watchlist" => "Watchlist",
        "TradingShell" => "TradingShell",
        "PromoteToPaper" => "Promotion approval",
        _ => string.IsNullOrWhiteSpace(fallbackTargetPageTag) ? "Research" : fallbackTargetPageTag
    };

    private WorkspaceCommandGroup BuildCommandGroup() =>
        new()
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem
                {
                    Id = "ResetStudio",
                    Label = "Reset Studio",
                    Description = "Reset the research studio layout",
                    ShortcutHint = "Ctrl+R",
                    Glyph = "\uE9D9",
                    Tone = WorkspaceTone.Primary
                },
                new WorkspaceCommandItem
                {
                    Id = "PromoteToPaper",
                    Label = "Promote to Paper",
                    Description = "Promote the selected run",
                    ShortcutHint = "Review",
                    Glyph = "\uE8FB",
                    IsEnabled = _canPromoteActiveRun
                },
                new WorkspaceCommandItem
                {
                    Id = "OpenTradingCockpit",
                    Label = "Open Trading Cockpit",
                    Description = "Open the selected run in trading",
                    ShortcutHint = "Handoff",
                    Glyph = "\uE9F5",
                    IsEnabled = _canOpenTradingCockpit
                }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "Watchlist", Label = "Watchlists", Description = "Open symbol staging watchlists", Glyph = "\uE8D4" },
                new WorkspaceCommandItem { Id = "StrategyRuns", Label = "Run Browser", Description = "Open run browser", Glyph = "\uE8FD" },
                new WorkspaceCommandItem { Id = "RunDetail", Label = "Run Detail", Description = "Open run detail", Glyph = "\uE7C3" },
                new WorkspaceCommandItem { Id = "RunPortfolio", Label = "Portfolio Inspector", Description = "Open portfolio inspector", Glyph = "\uE8B5" },
                new WorkspaceCommandItem { Id = "RunLedger", Label = "Ledger Inspector", Description = "Open ledger inspector", Glyph = "\uEE94" },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Open Accounting Impact", Description = "Open trial-balance impact view", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Review Recon Breaks", Description = "Review reconciliation breaks", Glyph = "\uE895" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Open Audit Trail", Description = "Open governance audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "LeanIntegration", Label = "Lean Integration", Description = "Open Lean integration", Glyph = "\uE943" }
            ]
        };

}
