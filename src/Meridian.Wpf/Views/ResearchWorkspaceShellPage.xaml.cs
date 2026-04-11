using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Research workspace shell — landing page for the Research workspace.
/// Surfaces recent strategy runs, KPIs, quick actions, and the promotion pipeline.
/// Embeds a <see cref="MeridianDockingManager"/> for IDE-style floating panes.
/// </summary>
public partial class ResearchWorkspaceShellPage : Page
{
    private const string WorkspaceId = "research";

    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _runService;
<<<<<<< HEAD
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly WorkspaceShellContextService _shellContextService;
    private bool _canPromoteActiveRun;
    private bool _canOpenTradingCockpit;

    public ResearchWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        WorkspaceShellContextService shellContextService)
=======

    public ResearchWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService)
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
    {
        InitializeComponent();
        _navigationService = navigationService;
        _runService = runService;
<<<<<<< HEAD
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _shellContextService = shellContextService;
=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
<<<<<<< HEAD
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
        }

=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
<<<<<<< HEAD
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
        }
=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
        _ = SaveDockLayoutAsync();
    }

    // ── Data ─────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            var summary = await _runService.GetResearchSummaryAsync();

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
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

<<<<<<< HEAD
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

    private async Task RestoreDockLayoutAsync()
    {
        try
        {
            var operatingContextKey = GetLayoutScopeKey();
            var layoutState = await WorkspaceService.Instance.GetWorkspaceLayoutStateForContextAsync(WorkspaceId, operatingContextKey);

            if (layoutState?.Panes.Count > 0)
            {
                foreach (var pane in layoutState.Panes.OrderBy(static pane => pane.Order))
                {
                    OpenWorkspacePage(pane.PageTag, NormalizeDockAction(MapDockAction(pane.DockZone)));
                }

                if (ShouldRestoreSerializedLayout(layoutState))
                {
                    ResearchDockManager.LoadLayout(layoutState.DockLayoutXml);
                }

                return;
            }

            await LoadDefaultDockingAsync();
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to restore dock layout: {ex.Message}");
            await LoadDefaultDockingAsync();
=======
    // ── AvalonDock layout persistence ─────────────────────────────────────

    private async System.Threading.Tasks.Task RestoreDockLayoutAsync()
    {
        try
        {
            var xml = await WorkspaceService.Instance.GetDockLayoutAsync(WorkspaceId);
            if (!string.IsNullOrWhiteSpace(xml))
                ResearchDockManager.LoadLayout(xml);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to restore dock layout: {ex.Message}");
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
<<<<<<< HEAD
            var layout = ResearchDockManager.CaptureLayoutState("research-backtest-studio", "Backtest Studio");
            layout.OperatingContextKey = GetLayoutScopeKey();
            layout.WindowMode = GetWindowMode();
            layout.LayoutPresetId = _operatingContextService?.CurrentLayoutPresetId;
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateForContextAsync(WorkspaceId, layout, layout.OperatingContextKey);
=======
            var xml = ResearchDockManager.SaveLayout();
            if (!string.IsNullOrWhiteSpace(xml))
                await WorkspaceService.Instance.SaveDockLayoutAsync(WorkspaceId, xml);
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

<<<<<<< HEAD
    private async Task LoadDefaultDockingAsync()
    {
        if (GetWindowMode() == BoundedWindowMode.WorkbenchPreset &&
            string.Equals(_operatingContextService?.CurrentLayoutPresetId, "research-compare", StringComparison.OrdinalIgnoreCase))
        {
            OpenWorkspacePage("Backtest", PaneDropAction.Replace);
            OpenWorkspacePage("StrategyRuns", PaneDropAction.SplitLeft);
            await OpenActiveRunPageAsync("RunDetail", PaneDropAction.SplitRight);
            await OpenActiveRunPageAsync("RunPortfolio", PaneDropAction.SplitBelow);
            await OpenActiveRunPageAsync("RunLedger", PaneDropAction.OpenTab);
            return;
        }

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
=======
    // ── Drop handler ──────────────────────────────────────────────────────
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
    {
<<<<<<< HEAD
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            ResearchDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
=======
        // For now, dropped page tags navigate in the main navigation service.
        // A future iteration will resolve pages via DI and embed them directly
        // as LayoutDocument content in the dock manager.
        _navigationService.NavigateTo(e.PageTag);
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
    }

    // ── Quick Action Handlers ─────────────────────────────────────────────

<<<<<<< HEAD
    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
    {
        switch (e.Command.Id)
        {
            case "ResetStudio":
                _ = LoadDefaultDockingAsync();
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
            case "LeanIntegration":
                OpenLean_Click(sender, new RoutedEventArgs());
                break;
        }
    }
=======
    private void NewBacktest_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Backtest");
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

    private void OpenRunMat_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("RunMat");

    private void OpenCharts_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Charts");

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("StrategyRuns");

    private void OpenAccountingImpact_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundTrialBalance", PaneDropAction.OpenTab);

    private void OpenReconciliationPreview_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundReconciliation", PaneDropAction.SplitBelow);

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundAuditTrail", PaneDropAction.OpenTab);

    private void OpenLean_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("LeanIntegration");

    private void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            _navigationService.NavigateTo("RunDetail", runId);
        }
    }
<<<<<<< HEAD

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

    private void OnSignalsChanged(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(async () => await RefreshAsync());
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

    private string? GetLayoutScopeKey()
        => _operatingContextService?.GetActiveScopeKey() ?? _fundContextService.CurrentFundProfile?.FundProfileId;

    private BoundedWindowMode GetWindowMode()
        => _operatingContextService?.CurrentWindowMode ?? BoundedWindowMode.DockFloat;

    private static bool ShouldRestoreSerializedLayout(WorkstationLayoutState layoutState)
        => layoutState.WindowMode != BoundedWindowMode.Focused && !string.IsNullOrWhiteSpace(layoutState.DockLayoutXml);

    private PaneDropAction NormalizeDockAction(PaneDropAction action)
        => GetWindowMode() == BoundedWindowMode.Focused && action == PaneDropAction.FloatWindow
            ? PaneDropAction.OpenTab
            : action;

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(() => OnActiveRunContextChanged(sender, e));
            return;
        }

        _ = RefreshAsync();
    }

    private static string BuildPageKey(string pageTag, object? parameter)
        => parameter is null ? pageTag : $"{pageTag}:{parameter}";

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

    private static string GetPageTitle(string pageTag) => pageTag switch
    {
        "Backtest" => "Backtest Studio",
        "StrategyRuns" => "Run Browser",
        "RunDetail" => "Run Detail",
        "RunPortfolio" => "Portfolio Inspector",
        "RunLedger" => "Ledger Inspector",
        "FundTrialBalance" => "Accounting Impact",
        "FundReconciliation" => "Reconciliation",
        "FundAuditTrail" => "Audit Trail",
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
=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
}
