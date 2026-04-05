using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Backtest studio shell for the Research workspace.
/// Hosts dense run context, promotion actions, and embedded dockable legacy pages.
/// </summary>
public partial class ResearchWorkspaceShellPage : Page
{
    private const string WorkspaceId = "research";

    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;

    public ResearchWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _runService = runService;
        _fundContextService = fundContextService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;

        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _ = SaveDockLayoutAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var summary = await _runService.GetResearchSummaryAsync();
            var activeContext = summary.ActiveRunContext;

            TotalRunsText.Text = summary.TotalRuns.ToString();
            PromotedText.Text = summary.PromotedCount.ToString();
            PendingReviewText.Text = summary.PendingReviewCount.ToString();
            PromotionCountBadge.Text = summary.PendingReviewCount.ToString();

            UpdateActiveRunContext(activeContext);

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
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Refresh failed: {ex.Message}");
        }
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
            LedgerPreviewText.Text = "Ledger inspector opens here once a run is selected.";
            RiskPreviewText.Text = "Risk preview becomes available after a completed run is selected.";
            PromoteActiveRunButton.IsEnabled = false;
            OpenTradingCockpitButton.IsEnabled = false;
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
        LedgerPreviewText.Text = activeContext.LedgerPreview;
        RiskPreviewText.Text = activeContext.RiskSummary;
        PromoteActiveRunButton.IsEnabled = activeContext.CanPromoteToPaper;
        OpenTradingCockpitButton.IsEnabled = true;
    }

    private async Task RestoreDockLayoutAsync()
    {
        try
        {
            var fundProfileId = _fundContextService.CurrentFundProfile?.FundProfileId;
            var layoutState = await WorkspaceService.Instance.GetWorkspaceLayoutStateAsync(WorkspaceId, fundProfileId);

            if (layoutState?.Panes.Count > 0)
            {
                foreach (var pane in layoutState.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(pane.PageTag, MapDockAction(pane.DockZone));
                }

                ResearchDockManager.LoadLayout(layoutState.DockLayoutXml);
                return;
            }

            await LoadDefaultDockingAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to restore dock layout: {ex.Message}");
            await LoadDefaultDockingAsync();
        }
    }

    private async Task SaveDockLayoutAsync()
    {
        try
        {
            var fundProfileId = _fundContextService.CurrentFundProfile?.FundProfileId;
            var layout = ResearchDockManager.CaptureLayoutState("research-backtest-studio", "Backtest Studio");
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateAsync(WorkspaceId, layout, fundProfileId);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private async Task LoadDefaultDockingAsync()
    {
        OpenWorkspacePage("Backtest", PaneDropAction.Replace);
        OpenWorkspacePage("StrategyRuns", PaneDropAction.SplitLeft);

        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is not null)
        {
            OpenWorkspacePage("RunDetail", PaneDropAction.SplitRight, activeRun.RunId);
            OpenWorkspacePage("RunPortfolio", PaneDropAction.SplitBelow, activeRun.RunId);
        }
        else
        {
            OpenWorkspacePage("Charts", PaneDropAction.SplitRight);
            OpenWorkspacePage("LeanIntegration", PaneDropAction.SplitBelow);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(e.PageTag, e.Action);

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            ResearchDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, action);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private async Task OpenActiveRunPageAsync(string pageTag, PaneDropAction action)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        OpenWorkspacePage(pageTag, action, activeRun?.RunId);
    }

    private async void NewBacktest_Click(object sender, RoutedEventArgs e)
        => await LoadDefaultDockingAsync();

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("StrategyRuns", PaneDropAction.SplitLeft);

    private void OpenRunDetailDocked_Click(object sender, RoutedEventArgs e)
        => _ = OpenActiveRunPageAsync("RunDetail", PaneDropAction.SplitRight);

    private void OpenPortfolioInspector_Click(object sender, RoutedEventArgs e)
        => _ = OpenActiveRunPageAsync("RunPortfolio", PaneDropAction.SplitRight);

    private void OpenLedgerInspector_Click(object sender, RoutedEventArgs e)
        => _ = OpenActiveRunPageAsync("RunLedger", PaneDropAction.SplitBelow);

    private void OpenLean_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("LeanIntegration", PaneDropAction.OpenTab);

    private async void PromoteActiveRun_Click(object sender, RoutedEventArgs e)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is null || !activeRun.CanPromoteToPaper)
        {
            return;
        }

        var promotedContext = await _runService.PromoteToPaperAsync(activeRun.RunId);
        UpdateActiveRunContext(promotedContext);
        OpenWorkspacePage("RunDetail", PaneDropAction.SplitRight, promotedContext?.RunId);
        await RefreshAsync();
    }

    private async void OpenTradingCockpit_Click(object sender, RoutedEventArgs e)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is null)
        {
            return;
        }

        _navigationService.NavigateTo("TradingShell", activeRun.RunId);
    }

    private async void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            await _runService.SetActiveRunContextAsync(runId);
            OpenWorkspacePage("RunDetail", PaneDropAction.SplitRight, runId);
            await RefreshAsync();
        }
    }

    private async void OpenRunFromHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            await _runService.SetActiveRunContextAsync(runId);
            OpenWorkspacePage("RunDetail", PaneDropAction.SplitRight, runId);
            OpenWorkspacePage("RunPortfolio", PaneDropAction.SplitBelow, runId);
            await RefreshAsync();
        }
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

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnActiveRunContextChanged(sender, e));
            return;
        }

        UpdateActiveRunContext(e);
    }

    private static string BuildPageKey(string pageTag, object? parameter)
        => parameter is null ? pageTag : $"{pageTag}:{parameter}";

    private static string GetPageTitle(string pageTag) => pageTag switch
    {
        "Backtest" => "Backtest Studio",
        "StrategyRuns" => "Run Browser",
        "RunDetail" => "Run Detail",
        "RunPortfolio" => "Portfolio Inspector",
        "RunLedger" => "Ledger Inspector",
        "Charts" => "Charts",
        "LeanIntegration" => "Lean Integration",
        _ => pageTag
    };

    private static PaneDropAction MapDockAction(string dockZone) => dockZone switch
    {
        "left" => PaneDropAction.SplitLeft,
        "right" => PaneDropAction.SplitRight,
        "bottom" => PaneDropAction.SplitBelow,
        "floating" => PaneDropAction.FloatWindow,
        _ => PaneDropAction.Replace
    };
}
