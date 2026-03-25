using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Research workspace shell — landing page for the Research workspace.
/// Surfaces recent strategy runs, KPIs, quick actions, and the promotion pipeline.
/// All data loading is intentionally lightweight; deep drill-ins navigate to dedicated pages.
/// </summary>
public partial class ResearchWorkspaceShellPage : Page
{
    private readonly NavigationService _navigationService;
    private readonly StrategyRunWorkspaceService _runService;

    public ResearchWorkspaceShellPage(
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
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Refresh failed: {ex.Message}");
        }
    }

    // ── Quick Action Handlers ─────────────────────────────────────────────

    private void NewBacktest_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Backtest");

    private void OpenRunMat_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("RunMat");

    private void OpenCharts_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Charts");

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("StrategyRuns");

    private void OpenLean_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("LeanIntegration");

    private void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            _navigationService.NavigateTo("RunDetail", runId);
        }
    }
}
