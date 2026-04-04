using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

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
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
            var xml = ResearchDockManager.SaveLayout();
            if (!string.IsNullOrWhiteSpace(xml))
                await WorkspaceService.Instance.SaveDockLayoutAsync(WorkspaceId, xml);
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[ResearchWorkspaceShell] Failed to save dock layout: {ex.Message}");
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

    private void OpenFundContext_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("FundPortfolio");

    private void OpenLatestPortfolio_Click(object sender, RoutedEventArgs e)
    {
        _ = OpenLatestRunArtifactAsync("RunPortfolio");
    }

    private void OpenLatestLedger_Click(object sender, RoutedEventArgs e)
    {
        _ = OpenLatestRunArtifactAsync("RunLedger");
    }

    private void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            _navigationService.NavigateTo("RunDetail", runId);
        }
    }

    private async Task OpenLatestRunArtifactAsync(string pageTag)
    {
        var latestRun = await _runService.GetLatestRunAsync();
        if (latestRun is null)
        {
            _navigationService.NavigateTo(pageTag);
            return;
        }

        _navigationService.NavigateTo(pageTag, latestRun.RunId);
    }
}
