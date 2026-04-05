using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading cockpit shell for live and paper execution workflows.
/// Hosts shared run context, capital posture, and embedded legacy panes.
/// </summary>
public partial class TradingWorkspaceShellPage : Page
{
    private const string WorkspaceId = "trading";

    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _runService;
    private readonly FundContextService _fundContextService;
    private readonly CashFinancingReadService _cashFinancingReadService;

    public TradingWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService,
        FundContextService fundContextService,
        CashFinancingReadService cashFinancingReadService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _runService = runService;
        _fundContextService = fundContextService;
        _cashFinancingReadService = cashFinancingReadService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _fundContextService.ActiveFundProfileChanged += OnActiveFundProfileChanged;
        _runService.ActiveRunContextChanged += OnActiveRunContextChanged;

        UpdateActiveFundText();
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
            var summary = await _runService.GetTradingSummaryAsync();

            PaperRunsText.Text = summary.PaperRunCount.ToString();
            LiveRunsText.Text = summary.LiveRunCount.ToString();
            TotalEquityText.Text = summary.TotalEquityFormatted;
            DrawdownText.Text = summary.MaxDrawdownFormatted;
            PositionLimitText.Text = summary.PositionLimitLabel;
            OrderRateText.Text = summary.OrderRateLabel;

            UpdateActiveRun(summary.ActiveRunContext);

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
                CapitalControlsDetailText.Text = "Select a fund to unlock capital, financing, and reconciliation posture.";
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    private void UpdateActiveRun(ActiveRunContext? activeRun)
    {
        if (activeRun is null)
        {
            TradingActiveRunText.Text = "No active trading run";
            TradingActiveRunMetaText.Text = "Use Research to promote a run, or open a live/paper panel below.";
            WatchlistStatusText.Text = "Watchlists and active strategies populate once paper or live runs are started.";
            MarketCoreText.Text = "Live data, order book, and portfolio inspectors are ready to dock below.";
            RiskRailText.Text = "Risk rail becomes specific once an active run is selected.";
            return;
        }

        TradingActiveRunText.Text = activeRun.StrategyName;
        TradingActiveRunMetaText.Text = $"{activeRun.ModeLabel} · {activeRun.StatusLabel} · {activeRun.FundScopeLabel}";
        WatchlistStatusText.Text = activeRun.PortfolioPreview;
        MarketCoreText.Text = activeRun.LedgerPreview;
        RiskRailText.Text = activeRun.RiskSummary;
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

                TradingDockManager.LoadLayout(layoutState.DockLayoutXml);
                return;
            }

            await LoadDefaultDockingAsync();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to restore dock layout: {ex.Message}");
            await LoadDefaultDockingAsync();
        }
    }

    private async Task SaveDockLayoutAsync()
    {
        try
        {
            var fundProfileId = _fundContextService.CurrentFundProfile?.FundProfileId;
            var layout = TradingDockManager.CaptureLayoutState("trading-cockpit", "Trading Cockpit");
            await WorkspaceService.Instance.SaveWorkspaceLayoutStateAsync(WorkspaceId, layout, fundProfileId);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private async Task LoadDefaultDockingAsync()
    {
        OpenWorkspacePage("LiveData", PaneDropAction.Replace);
        OpenWorkspacePage("RunPortfolio", PaneDropAction.SplitLeft);
        OpenWorkspacePage("PositionBlotter", PaneDropAction.SplitRight);
        OpenWorkspacePage("RunRisk", PaneDropAction.SplitBelow);

        var activeRun = await _runService.GetActiveRunContextAsync();
        if (activeRun is not null)
        {
            OpenWorkspacePage("RunLedger", PaneDropAction.OpenTab, activeRun.RunId);
        }
        else
        {
            OpenWorkspacePage("OrderBook", PaneDropAction.OpenTab);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(e.PageTag, e.Action);

    private void OpenWorkspacePage(string pageTag, PaneDropAction action, object? parameter = null)
    {
        try
        {
            var pageContent = _navigationService.CreatePageContent(pageTag, parameter);
            TradingDockManager.LoadPage(BuildPageKey(pageTag, parameter), GetPageTitle(pageTag), pageContent, action);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to open '{pageTag}': {ex.Message}");
            _navigationService.NavigateTo(pageTag, parameter);
        }
    }

    private async Task OpenActiveRunPageAsync(string pageTag, PaneDropAction action)
    {
        var activeRun = await _runService.GetActiveRunContextAsync();
        OpenWorkspacePage(pageTag, action, activeRun?.RunId);
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
        => OpenWorkspacePage("LiveData", PaneDropAction.Replace);

    private void OpenPortfolio_Click(object sender, RoutedEventArgs e)
        => _ = OpenActiveRunPageAsync("RunPortfolio", PaneDropAction.SplitLeft);

    private void OpenBlotter_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("PositionBlotter", PaneDropAction.SplitRight);

    private void OpenOrderBook_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("OrderBook", PaneDropAction.FloatWindow);

    private void OpenRiskRail_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("RunRisk", PaneDropAction.SplitRight);

    private void OpenAlerts_Click(object sender, RoutedEventArgs e)
        => OpenWorkspacePage("NotificationCenter", PaneDropAction.SplitBelow);

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

        UpdateActiveRun(e);
    }

    private void UpdateActiveFundText()
    {
        var profile = _fundContextService.CurrentFundProfile;
        if (profile is null)
        {
            ActiveFundText.Text = "No fund selected";
            ActiveFundDetailText.Text = "Runs and KPIs scope to the active fund profile.";
            return;
        }

        ActiveFundText.Text = profile.DisplayName;
        ActiveFundDetailText.Text = $"{profile.LegalEntityName} · {profile.BaseCurrency}";
    }

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
