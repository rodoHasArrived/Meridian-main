using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Strategy workspace shell - compatibility page for the canonical Strategy workspace.
/// Hosts the Strategy shell view model and keeps code-behind limited to WPF
/// lifecycle, visual-resource tone application, and dock/navigation forwarding.
/// </summary>
public partial class StrategyWorkspaceShellPage : StrategyWorkspaceShellPageBase
{
    private bool _viewModelEventsAttached;

    public StrategyWorkspaceShellPage(
        NavigationService navigationService,
        StrategyWorkspaceShellStateProvider stateProvider,
        StrategyWorkspaceShellViewModel viewModel)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModelEvents();
        ApplyToneBindings();
        await ViewModel.StartAsync().ConfigureAwait(true);
        ApplyToneBindings();
        await RestoreShellDockLayoutAsync(StrategyDockManager).ConfigureAwait(true);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DetachViewModelEvents();
        ViewModel.Stop();
        SaveShellDockLayout(StrategyDockManager);
    }

    private void AttachViewModelEvents()
    {
        if (_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = true;
        ViewModel.ActionRequested += OnViewModelActionRequested;
        ViewModel.RefreshRequested += OnViewModelRefreshRequested;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void DetachViewModelEvents()
    {
        if (!_viewModelEventsAttached)
        {
            return;
        }

        _viewModelEventsAttached = false;
        ViewModel.ActionRequested -= OnViewModelActionRequested;
        ViewModel.RefreshRequested -= OnViewModelRefreshRequested;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private void OnViewModelRefreshRequested(object? sender, EventArgs e)
        => DispatchRefresh(ViewModel.RefreshAsync);

    private void OnViewModelActionRequested(
        object? sender,
        StrategyWorkspaceShellActionRequest request)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(() => OnViewModelActionRequested(sender, request));
            return;
        }

        switch (request.Kind)
        {
            case StrategyWorkspaceShellActionKind.ResetLayout:
                _ = LoadDefaultDockLayoutAsync(StrategyDockManager);
                return;
            case StrategyWorkspaceShellActionKind.Navigate when !string.IsNullOrWhiteSpace(request.PageTag):
                NavigationService.NavigateTo(request.PageTag, request.Parameter);
                return;
            case StrategyWorkspaceShellActionKind.Dock when !string.IsNullOrWhiteSpace(request.PageTag):
                OpenWorkspacePage(StrategyDockManager, request.PageTag, request.Action, request.Parameter);
                return;
            case StrategyWorkspaceShellActionKind.OpenRunStudio when request.Parameter is string runId:
                OpenWorkspacePage(StrategyDockManager, "RunDetail", PaneDropAction.SplitRight, runId);
                OpenWorkspacePage(StrategyDockManager, "RunPortfolio", PaneDropAction.SplitBelow, runId);
                return;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StrategyWorkspaceShellViewModel.StrategyHeroBadgeTone))
        {
            ApplyTone(StrategyHeroBadgeBorder, StrategyHeroBadgeText, ViewModel.StrategyHeroBadgeTone);
        }
    }

    private void ApplyToneBindings()
        => ApplyTone(StrategyHeroBadgeBorder, StrategyHeroBadgeText, ViewModel.StrategyHeroBadgeTone);

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenDroppedPane(StrategyDockManager, e);

    private void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => ViewModel.ExecuteCommandAction(e.Command.Id);

    private void OnStrategyHeroPrimaryActionClick(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteHeroPrimaryAction();

    private void OnStrategyHeroSecondaryActionClick(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteHeroSecondaryAction();

    private void OpenRunMat_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("RunMat");

    private void OpenBacktest_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("Backtest");

    private void OpenCharts_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("Charts");

    private void OpenStrategyRuns_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("StrategyRuns");

    private void OpenWatchlists_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("Watchlist");

    private void OpenAccountingImpact_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundTrialBalance");

    private void OpenReconciliationPreview_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundReconciliation");

    private void OpenAuditTrail_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("FundAuditTrail");

    private void OpenLean_Click(object sender, RoutedEventArgs e)
        => ViewModel.ExecuteCommandAction("LeanIntegration");

    private void ReviewPromotion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            ViewModel.ReviewPromotion(runId);
        }
    }

    private void OpenRunFromHistory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            ViewModel.OpenRunStudio(runId);
        }
    }

    private void OpenBriefingRun_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string runId })
        {
            ViewModel.OpenRunStudio(runId);
        }
    }

    private void OpenBriefingAlert_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenBriefingAlert((sender as Button)?.Tag as string);

    private void OpenBriefingComparison_Click(object sender, RoutedEventArgs e)
        => ViewModel.OpenBriefingComparison((sender as Button)?.Tag as string);

    internal static StrategyDeskHeroState BuildDeskHeroState(
        StrategyWorkspaceSummary summary,
        ActiveRunContext? activeRun,
        WorkspaceWorkflowSummary workflow)
        => StrategyWorkspaceShellPresentationService.BuildDeskHeroState(summary, activeRun, workflow);

    internal static WorkspaceCommandGroup BuildCommandGroup(
        bool canPromoteActiveRun = false,
        bool canOpenTradingCockpit = false)
        => StrategyWorkspaceShellPresentationService.BuildCommandGroup(canPromoteActiveRun, canOpenTradingCockpit);

    internal static StrategyDeskHeroTone ParseHeroTone(string? tone)
        => StrategyWorkspaceShellPresentationService.ParseHeroTone(tone);

    private void ApplyTone(Border border, TextBlock textBlock, StrategyDeskHeroTone tone)
    {
        var (backgroundKey, borderKey) = tone switch
        {
            StrategyDeskHeroTone.Success => ("ConsoleAccentGreenAlpha10Brush", "SuccessColorBrush"),
            StrategyDeskHeroTone.Warning => ("ConsoleAccentOrangeAlpha10Brush", "WarningColorBrush"),
            _ => ("ConsoleAccentBlueAlpha10Brush", "InfoColorBrush")
        };

        border.Background = GetBrush(backgroundKey);
        border.BorderBrush = GetBrush(borderKey);
        textBlock.Foreground = GetBrush(borderKey);
    }

    private Brush GetBrush(string resourceKey)
        => TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
}
