using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using WpfLoggingService = Meridian.Wpf.Services.LoggingService;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading workspace shell — landing page for the Trading workspace.
/// Surfaces active paper/live run counts, total equity, open positions, and the risk rail.
/// Embeds a <see cref="MeridianDockingManager"/> for IDE-style floating panes.
/// </summary>
public partial class TradingWorkspaceShellPage : Page
{
    private const string WorkspaceId = "trading";

    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;
    private readonly CashFinancingReadService _cashFinancingReadService;
    private readonly WorkspaceShellContextService _shellContextService;

    public TradingWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService,
        CashFinancingReadService cashFinancingReadService,
        WorkspaceShellContextService shellContextService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _runService = runService;
        _fundContextService = fundContextService;
        _operatingContextService = operatingContextService;
        _cashFinancingReadService = cashFinancingReadService;
        _shellContextService = shellContextService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;
        _shellContextService.SignalsChanged += OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged += OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged += OnSignalsChanged;
        }

        UpdateActiveFundText();
        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged -= OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged -= OnActiveRunContextChanged;
        _shellContextService.SignalsChanged -= OnSignalsChanged;
        if (_operatingContextService is not null)
        {
            _operatingContextService.ActiveContextChanged -= OnOperatingContextChanged;
            _operatingContextService.WindowModeChanged -= OnSignalsChanged;
        }
        _ = SaveDockLayoutAsync();
    }

    // ── Data ─────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        try
        {
            var summary = await _runService.GetTradingSummaryAsync();

            PaperRunsText.Text = summary.PaperRunCount.ToString();
            LiveRunsText.Text = summary.LiveRunCount.ToString();
            TotalEquityText.Text = summary.TotalEquityFormatted;

            DrawdownText.Text = summary.MaxDrawdownFormatted;
            PositionLimitText.Text = summary.PositionLimitLabel;
            OrderRateText.Text = summary.OrderRateLabel;

            if (summary.ActivePositions.Count > 0)
            {
                ActivePositionsList.ItemsSource = summary.ActivePositions;
                NoPositionsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ActivePositionsList.ItemsSource = null;
                NoPositionsText.Visibility = Visibility.Visible;
            }

            var profile = _fundContextService.CurrentFundProfile;
            if (profile is not null)
            {
                var capitalSummary = await _cashFinancingReadService.GetAsync(profile.FundProfileId, profile.BaseCurrency);
                CapitalCashText.Text = capitalSummary.TotalCash.ToString("C0");
                CapitalGrossExposureText.Text = capitalSummary.GrossExposure.ToString("C0");
                CapitalNetExposureText.Text = capitalSummary.NetExposure.ToString("C0");
                CapitalFinancingText.Text = capitalSummary.FinancingCost.ToString("C0");
                CapitalControlsDetailText.Text = capitalSummary.Highlights.FirstOrDefault()
                    ?? "Capital and financing posture is available for the active fund.";
            }
            else
            {
                CapitalCashText.Text = "—";
                CapitalGrossExposureText.Text = "—";
                CapitalNetExposureText.Text = "—";
                CapitalFinancingText.Text = "—";
                CapitalControlsDetailText.Text = _operatingContextService?.CurrentContext is { } operatingContext
                    ? $"Switch to a fund-linked accounting view to unlock capital and reconciliation posture for {operatingContext.DisplayName}."
                    : "Select an operating context to unlock capital, financing, and reconciliation posture.";
            }

            ContextStrip.ShellContext = await _shellContextService.CreateAsync(new WorkspaceShellContextInput
            {
                WorkspaceTitle = "Trading Workspace",
                WorkspaceSubtitle = "Risk-aware trading shell for live posture, blotter review, safe staging, and docked execution detail.",
                PrimaryScopeLabel = "Desk",
                PrimaryScopeValue = summary.ActiveRunContext?.StrategyName ?? (_fundContextService.CurrentFundProfile?.DisplayName ?? "No active trading run"),
                AsOfValue = DateTimeOffset.Now.ToString("MMM dd yyyy HH:mm"),
                FreshnessValue = summary.ActiveRunContext is null ? "Awaiting active run" : $"{summary.ActiveRunContext.ModeLabel} · {summary.ActiveRunContext.StatusLabel}",
                ReviewStateLabel = "Risk",
                ReviewStateValue = summary.ActivePositions.Count > 0 ? $"{summary.ActivePositions.Count} active position(s)" : "No live positions",
                ReviewStateTone = summary.ActivePositions.Count > 0 ? WorkspaceTone.Warning : WorkspaceTone.Success,
                CriticalLabel = "Critical",
                CriticalValue = summary.LiveRunCount > 0 ? $"{summary.LiveRunCount} live run(s)" : "No live runs",
                CriticalTone = summary.LiveRunCount > 0 ? WorkspaceTone.Warning : WorkspaceTone.Info,
                AdditionalBadges =
                [
                    new WorkspaceShellBadge
                    {
                        Label = "Equity",
                        Value = summary.TotalEquityFormatted,
                        Glyph = "\uE9F5",
                        Tone = summary.LiveRunCount > 0 ? WorkspaceTone.Info : WorkspaceTone.Neutral
                    }
                ]
            });

            UpdateActiveRun(summary.ActiveRunContext);
            CommandBar.CommandGroup = BuildCommandGroup();
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private void UpdateActiveRun(ActiveRunContext? activeRun)
    {
        if (activeRun is null)
        {
            TradingActiveRunText.Text = "No active trading run";
            TradingActiveRunMetaText.Text = "Use Research to promote a run, or open a live/paper panel below.";
            WatchlistStatusText.Text = "Watchlists and active strategies populate once paper or live runs are started.";
            MarketCoreText.Text = "Live data, order book, portfolio, and accounting consequences are ready to dock below.";
            RiskRailText.Text = "Risk, reconciliation, and audit surfaces become specific once an active run is selected.";
            return;
        }

        TradingActiveRunText.Text = activeRun.StrategyName;
        TradingActiveRunMetaText.Text = $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}";
        WatchlistStatusText.Text = activeRun.PortfolioPreview;
        MarketCoreText.Text = $"{activeRun.LedgerPreview} Accounting consequences stay available from the same cockpit.";
        RiskRailText.Text = $"{activeRun.RiskSummary} Audit references and reconciliation review remain one action away.";
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
                    TradingDockManager.LoadLayout(layoutState.DockLayoutXml);
                }

                return;
            }

            await LoadDefaultDockingAsync();
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to restore dock layout: {ex.Message}");
            await LoadDefaultDockingAsync();
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
            var layout = TradingDockManager.CaptureLayoutState("trading-cockpit", "Trading Cockpit");
            layout.OperatingContextKey = GetLayoutScopeKey();
            layout.WindowMode = GetWindowMode();
            layout.LayoutPresetId = _operatingContextService?.CurrentLayoutPresetId;
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateForContextAsync(WorkspaceId, layout, layout.OperatingContextKey);
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private async Task LoadDefaultDockingAsync()
    {
        OpenWorkspacePage("LiveData", PaneDropAction.Replace);
        OpenWorkspacePage("RunPortfolio", PaneDropAction.SplitLeft);
        OpenWorkspacePage("PositionBlotter", PaneDropAction.SplitRight);
        OpenWorkspacePage("RunRisk", PaneDropAction.SplitBelow);

        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is not null || GetWindowMode() == BoundedWindowMode.WorkbenchPreset)
        {
            OpenWorkspacePage("RunLedger", PaneDropAction.OpenTab, activeRun?.RunId);
            OpenWorkspacePage("FundTrialBalance", PaneDropAction.OpenTab);
        }
        else
        {
            OpenWorkspacePage("OrderBook", PaneDropAction.OpenTab);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
    {
        var pageTag = e.PageTag;
        object? parameter = null;
        var action = e.Action;

        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            TradingDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            TradingDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, NormalizeDockAction(action));
        }
        catch (Exception ex)
        {
            WpfLoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private async Task OpenActiveRunPageAsync(string pageTag, PaneDropAction action)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        OpenWorkspacePage(pageTag, action, activeRun?.RunId);
    }

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
    {
        switch (e.Command.Id)
        {
            case "Pause":
                PauseTrading_Click(sender, new RoutedEventArgs());
                break;
            case "Stop":
                StopTrading_Click(sender, new RoutedEventArgs());
                break;
            case "Flatten":
                FlattenPositions_Click(sender, new RoutedEventArgs());
                break;
            case "CancelAll":
                CancelAll_Click(sender, new RoutedEventArgs());
                break;
            case "AcknowledgeRisk":
                AcknowledgeRisk_Click(sender, new RoutedEventArgs());
                break;
            case "LiveData":
                OpenLiveData_Click(sender, new RoutedEventArgs());
                break;
            case "PositionBlotter":
                OpenBlotter_Click(sender, new RoutedEventArgs());
                break;
            case "RunRisk":
                OpenRiskRail_Click(sender, new RoutedEventArgs());
                break;
            case "NotificationCenter":
                OpenAlerts_Click(sender, new RoutedEventArgs());
                break;
            case "FundTrialBalance":
                OpenAccountingConsequences_Click(sender, new RoutedEventArgs());
                break;
            case "FundReconciliation":
                OpenReconciliationReview_Click(sender, new RoutedEventArgs());
                break;
            case "FundAuditTrail":
                OpenAuditTrail_Click(sender, new RoutedEventArgs());
                break;
            case "TradingHours":
                _navigationService.NavigateTo("TradingHours");
                break;
        }
    }

    private void PauseTrading_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Pause queued. Review blotter and risk rail before resuming.";
        OpenWorkspacePage("PositionBlotter", PaneDropAction.SplitRight);
    }

    private void StopTrading_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Stop requested. Existing positions remain visible for review.";
        OpenWorkspacePage("RunRisk", PaneDropAction.SplitBelow);
    }

    private void FlattenPositions_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Flatten review opened. Use the blotter and order book to verify exit posture.";
        OpenWorkspacePage("OrderBook", PaneDropAction.FloatWindow);
    }

    private void CancelAll_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Cancel-all review opened. Confirm open orders in the blotter.";
        OpenWorkspacePage("PositionBlotter", PaneDropAction.FloatWindow);
    }

    private void AcknowledgeRisk_Click(object sender, RoutedEventArgs e)
    {
        DeskActionStatusText.Text = "Risk acknowledgement captured locally for this workstation session.";
        OpenWorkspacePage("RunRisk", PaneDropAction.SplitRight);
    }

    private void OpenLiveData_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("LiveData");

    private void OpenBlotter_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("PositionBlotter", PaneDropAction.SplitRight);

    private void OpenPortfolio_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("RunPortfolio");

    private void ImportPositions_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("PortfolioImport");

    private void OpenOrderBook_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("OrderBook", PaneDropAction.FloatWindow);

    private void OpenRiskRail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("RunRisk", PaneDropAction.SplitRight);

    private void OpenAlerts_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitBelow);

    private void OpenAccountingConsequences_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundTrialBalance", PaneDropAction.OpenTab);

    private void OpenReconciliationReview_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundReconciliation", PaneDropAction.SplitBelow);

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("FundAuditTrail", PaneDropAction.OpenTab);

    private async void OnActiveFundProfileChanged(object? sender, FundProfileChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnActiveFundProfileChanged(sender, e));
            return;
        }

        UpdateActiveFundText();
        await RefreshAsync();
    }

    private void OnActiveRunContextChanged(object? sender, ActiveRunContext? e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnActiveRunContextChanged(sender, e));
            return;
        }

        _ = RefreshAsync();
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

        UpdateActiveFundText();
        _ = RefreshAsync();
    }

    private void UpdateActiveFundText()
    {
        if (_operatingContextService?.CurrentContext is { } operatingContext)
        {
            ActiveFundText.Text = operatingContext.DisplayName;
            ActiveFundDetailText.Text = operatingContext.Subtitle;
            return;
        }

        var profile = _fundContextService.CurrentFundProfile;
        if (profile is null)
        {
            ActiveFundText.Text = "No operating context selected";
            ActiveFundDetailText.Text = "Runs, allocations, and accounting posture scope to the active operating context.";
            return;
        }

        ActiveFundText.Text = profile.DisplayName;
        ActiveFundDetailText.Text = $"{profile.LegalEntityName} · {profile.BaseCurrency}";
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

    private static WorkspaceCommandGroup BuildCommandGroup() =>
        new()
        {
            PrimaryCommands =
            [
                new WorkspaceCommandItem { Id = "Pause", Label = "Pause", Description = "Pause trading", ShortcutHint = "Desk", Glyph = "\uE769", Tone = WorkspaceTone.Primary },
                new WorkspaceCommandItem { Id = "Stop", Label = "Stop", Description = "Stop trading", ShortcutHint = "Desk", Glyph = "\uE71A", Tone = WorkspaceTone.Secondary },
                new WorkspaceCommandItem { Id = "Flatten", Label = "Flatten", Description = "Flatten positions", ShortcutHint = "Risk", Glyph = "\uE9F5", Tone = WorkspaceTone.Danger }
            ],
            SecondaryCommands =
            [
                new WorkspaceCommandItem { Id = "CancelAll", Label = "Cancel All", Description = "Cancel staged orders", Glyph = "\uE711" },
                new WorkspaceCommandItem { Id = "AcknowledgeRisk", Label = "Acknowledge Risk", Description = "Acknowledge current risk posture", Glyph = "\uE73E" },
                new WorkspaceCommandItem { Id = "LiveData", Label = "Live Data", Description = "Open live data", Glyph = "\uE9D2" },
                new WorkspaceCommandItem { Id = "PositionBlotter", Label = "Blotter", Description = "Open position blotter", Glyph = "\uE8A5" },
                new WorkspaceCommandItem { Id = "RunRisk", Label = "Risk Rail", Description = "Open risk rail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "FundTrialBalance", Label = "Accounting", Description = "Open accounting consequences", Glyph = "\uE9D9" },
                new WorkspaceCommandItem { Id = "FundReconciliation", Label = "Reconciliation", Description = "Open reconciliation review", Glyph = "\uE895" },
                new WorkspaceCommandItem { Id = "FundAuditTrail", Label = "Audit", Description = "Open audit trail", Glyph = "\uE7BA" },
                new WorkspaceCommandItem { Id = "NotificationCenter", Label = "Alerts", Description = "Open alerts", Glyph = "\uE7F4" },
                new WorkspaceCommandItem { Id = "TradingHours", Label = "Trading Hours", Description = "Open trading hours", Glyph = "\uE823" }
            ]
        };

    private static string BuildPageKey(string pageTag, object? parameter)
        => parameter is null ? pageTag : $"{pageTag}:{parameter}";

    private static string GetPageTitle(string pageTag) => pageTag switch
    {
        "LiveData" => "Live Market View",
        "RunPortfolio" => "Portfolio",
        "PositionBlotter" => "Blotter",
        "OrderBook" => "Order Book",
        "RunRisk" => "Risk Rail",
        "RunLedger" => "Ledger Activity",
        "FundTrialBalance" => "Accounting Consequences",
        "FundReconciliation" => "Reconciliation",
        "FundAuditTrail" => "Audit Trail",
        "NotificationCenter" => "Alerts",
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
