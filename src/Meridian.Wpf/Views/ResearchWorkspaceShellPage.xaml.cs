using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Services;
using Meridian.Ui.Shared.Services;
using Meridian.Ui.Services;
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
    private readonly StrategyRunWorkspaceService _runService;
    private readonly IResearchBriefingWorkspaceService _briefingService;
    private readonly Meridian.Wpf.Services.WatchlistService _watchlistService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private readonly WorkstationWorkflowSummaryService? _workflowSummaryService;
    private bool _canPromoteActiveRun;
    private bool _canOpenTradingCockpit;

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
            UpdateWorkflowHandoff(workflow);
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

    private void UpdateWorkflowHandoff(WorkspaceWorkflowSummary? workflow)
    {
        var effectiveWorkflow = workflow ?? new WorkspaceWorkflowSummary(
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

        ResearchWorkflowStatusText.Text = effectiveWorkflow.StatusLabel;
        ResearchWorkflowDetailText.Text = effectiveWorkflow.StatusDetail;
        ResearchWorkflowBlockerLabelText.Text = effectiveWorkflow.PrimaryBlocker.Label;
        ResearchWorkflowBlockerDetailText.Text = effectiveWorkflow.PrimaryBlocker.Detail;
        ResearchWorkflowTargetText.Text = $"Target page: {effectiveWorkflow.NextAction.TargetPageTag}";
        ResearchWorkflowEvidenceItems.ItemsSource = effectiveWorkflow.Evidence
            .Select(static evidence => $"{evidence.Label}: {evidence.Value}")
            .ToArray();

        StartBacktestButton.Visibility = string.Equals(effectiveWorkflow.NextAction.TargetPageTag, "Backtest", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        ReviewRunButton.Visibility = string.Equals(effectiveWorkflow.NextAction.TargetPageTag, "StrategyRuns", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
        SendToTradingReviewButton.Visibility = string.Equals(effectiveWorkflow.NextAction.TargetPageTag, "TradingShell", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
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
            WorkspaceTitle = "Research Workspace",
            WorkspaceSubtitle = "Market briefing, run studio, and promotion-aware research workflow.",
            PrimaryScopeLabel = "Research",
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
                ApprovalReason: "Research workstation promotion"));

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
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Accounting Impact", Description = "Open trial-balance impact view", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation review", Glyph = "\uE895" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit Trail", Description = "Open governance audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "LeanIntegration", Label = "Lean Integration", Description = "Open Lean integration", Glyph = "\uE943" }
            ]
        };

}
