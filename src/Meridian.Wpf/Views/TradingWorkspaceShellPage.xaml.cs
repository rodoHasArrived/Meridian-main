using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

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

    public TradingWorkspaceShellPage(
        NavigationService navigationService,
        StrategyRunWorkspaceService runService)
    {
        InitializeComponent();
        _navigationService = navigationService;
        _runService = runService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
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
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    // ── AvalonDock layout persistence ─────────────────────────────────────

    private async System.Threading.Tasks.Task RestoreDockLayoutAsync()
    {
        try
        {
            var xml = await WorkspaceService.Instance.GetDockLayoutAsync(WorkspaceId);
            if (!string.IsNullOrWhiteSpace(xml))
                TradingDockManager.LoadLayout(xml);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
            var xml = TradingDockManager.SaveLayout();
            if (!string.IsNullOrWhiteSpace(xml))
                await WorkspaceService.Instance.SaveDockLayoutAsync(WorkspaceId, xml);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[TradingWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    // ── Drop handler ──────────────────────────────────────────────────────

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
    {
        // For now, dropped page tags navigate in the main navigation service.
        // A future iteration will resolve pages via DI and embed them directly
        // as LayoutDocument content in the dock manager.
        _navigationService.NavigateTo(e.PageTag);
    }

    // ── Quick Action Handlers ─────────────────────────────────────────────

    private void OpenLiveData_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("LiveData");

    private void OpenPortfolio_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("RunPortfolio");

    private void ImportPositions_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("PortfolioImport");

    private void OpenTradingHours_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("TradingHours");
}
