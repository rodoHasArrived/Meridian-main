using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Trading workspace shell — landing page for the Trading workspace.
/// Surfaces active paper/live run counts, total equity, open positions, and the risk rail.
/// All data loading is intentionally lightweight; deep drill-ins navigate to dedicated pages.
/// </summary>
public partial class TradingWorkspaceShellPage : Page
{
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
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        // No subscriptions to clean up — shell page uses one-shot load.
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
