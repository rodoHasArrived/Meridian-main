using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Features.Data.Shell;

public partial class DataWorkspaceShellPage : DataOperationsWorkspaceShellPageBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;

    public DataWorkspaceShellPage(
        NavigationService navigationService,
        DataOperationsWorkspaceShellStateProvider stateProvider,
        DataWorkspaceShellViewModel viewModel,
        FundContextService fundContextService,
        WorkstationOperatingContextService? operatingContextService = null)
        : base(navigationService, stateProvider, viewModel)
    {
        InitializeComponent();
        _fundContextService = fundContextService ?? throw new ArgumentNullException(nameof(fundContextService));
        _operatingContextService = operatingContextService;
    }

    private DataWorkspaceShellViewModel DataViewModel => (DataWorkspaceShellViewModel)ViewModel;

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        DataViewModel.RefreshRequested += OnRefreshRequested;
        DataViewModel.ActionRequested += OnActionRequested;
        DataViewModel.Start();

        await RefreshShellAsync();
        await RestoreDockLayoutAsync(DataOperationsDockManager);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DataViewModel.Stop();
        DataViewModel.RefreshRequested -= OnRefreshRequested;
        DataViewModel.ActionRequested -= OnActionRequested;
        _ = SaveDockLayoutAsync(DataOperationsDockManager);
    }

    private async Task RefreshShellAsync()
    {
        await DataViewModel.RefreshAsync();
        ApplyViewModelState();
    }

    private void ApplyViewModelState()
    {
        var viewModel = DataViewModel;

        CommandBar.CommandGroup = viewModel.CommandGroup;
        OperationsHeroScopeText.Text = viewModel.HeroScopeText;
        OperationsHeroSummaryText.Text = viewModel.HeroSummaryText;
        ApplyHeroState(viewModel.HeroState);
        OperationsHeroMetricsList.ItemsSource = viewModel.HeroMetrics;
        QueueScopeBadgeText.Text = viewModel.QueueScopeBadgeText;
        QueueSummaryText.Text = viewModel.QueueSummaryText;
        ProviderQueueList.ItemsSource = viewModel.ProviderQueueItems;
        BackfillQueueList.ItemsSource = viewModel.BackfillQueueItems;
        StorageQueueList.ItemsSource = viewModel.StorageQueueItems;
        ApplyQueueState(ProviderQueueList, ProviderQueueStateContainer, viewModel.ProviderQueueState);
        ApplyQueueState(BackfillQueueList, BackfillQueueStateContainer, viewModel.BackfillQueueState);
        ApplyQueueState(StorageQueueList, StorageQueueStateContainer, viewModel.StorageQueueState);
        OperationsSummaryTitleText.Text = viewModel.OperationsSummaryTitleText;
        OperationsSummaryDetailText.Text = viewModel.OperationsSummaryDetailText;
        SummaryProvidersText.Text = viewModel.SummaryProvidersText;
        SummaryProvidersText.Foreground = ResolveToneBrush(viewModel.SummaryProvidersTone);
        SummaryBackfillText.Text = viewModel.SummaryBackfillText;
        SummaryBackfillText.Foreground = ResolveToneBrush(viewModel.SummaryBackfillTone);
        SummaryStorageText.Text = viewModel.SummaryStorageText;
        SummaryStorageText.Foreground = ResolveToneBrush(viewModel.SummaryStorageTone);
        RecentOperationsList.ItemsSource = viewModel.RecentOperations;
    }

    private void ApplyHeroState(DataOperationsHeroState heroState)
    {
        OperationsHeroBadgeText.Text = heroState.BadgeText;
        OperationsHeroBadgeText.Foreground = ResolveToneBrush(heroState.BadgeTone);
        OperationsHeroBadgeBorder.BorderBrush = ResolveToneBrush(heroState.BadgeTone);
        OperationsHeroBadgeBorder.Background = ResolveToneBackgroundBrush(heroState.BadgeTone);
        OperationsHeroFocusText.Text = heroState.FocusText;
        OperationsHeroActionSummaryText.Text = heroState.SummaryText;
        OperationsHeroHandoffTitleText.Text = heroState.HandoffTitleText;
        OperationsHeroHandoffDetailText.Text = heroState.HandoffDetailText;
        OperationsHeroTargetText.Text = heroState.TargetText;
        ApplyHeroActionButton(OperationsHeroPrimaryActionButton, heroState.PrimaryActionLabel, heroState.PrimaryActionId);
        ApplyHeroActionButton(OperationsHeroSecondaryActionButton, heroState.SecondaryActionLabel, heroState.SecondaryActionId);
    }

    private static void ApplyHeroActionButton(Button button, string label, string actionId)
    {
        button.Tag = actionId;
        button.Content = label;
        button.Visibility = string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(actionId)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private Brush ResolveToneBrush(string tone)
    {
        var resourceKey = tone switch
        {
            WorkspaceTone.Success => "SuccessColorBrush",
            WorkspaceTone.Warning => "WarningColorBrush",
            WorkspaceTone.Danger => "ErrorColorBrush",
            WorkspaceTone.Info => "InfoColorBrush",
            _ => "ConsoleTextPrimaryBrush"
        };

        return TryFindResource(resourceKey) as Brush ?? Brushes.White;
    }

    private Brush ResolveToneBackgroundBrush(string tone)
    {
        var resourceKey = tone switch
        {
            WorkspaceTone.Success => "ConsoleAccentGreenAlpha10Brush",
            WorkspaceTone.Warning => "ConsoleAccentOrangeAlpha10Brush",
            WorkspaceTone.Danger => "ConsoleAccentRedAlpha10Brush",
            WorkspaceTone.Info => "ConsoleAccentBlueAlpha10Brush",
            _ => "ConsoleBackgroundMediumBrush"
        };

        return TryFindResource(resourceKey) as Brush ?? Brushes.Transparent;
    }

    private void ApplyQueueState(FrameworkElement queueList, ContentControl stateContainer, WorkspaceQueueRegionState state)
    {
        if (state.IsVisible)
        {
            queueList.Visibility = Visibility.Collapsed;
            stateContainer.Visibility = Visibility.Visible;
            stateContainer.Content = state;
            return;
        }

        queueList.Visibility = Visibility.Visible;
        stateContainer.Visibility = Visibility.Collapsed;
        stateContainer.Content = null;
    }

    private async void OnQueueStateActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ExecuteActionAsync(actionId, navigate: false);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(DataOperationsDockManager, e.PageTag, e.Action);

    private async void OnCommandBarCommandInvoked(object sender, WorkspaceCommandInvokedEventArgs e)
        => await ExecuteActionAsync(e.Command.Id, navigate: true);

    private async void OnDataDecisionInvoked(object sender, WorkspaceDecisionInvokedEventArgs e)
        => await ExecuteActionAsync(e.ActionId, navigate: false);

    private async void OnRecentActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ExecuteActionAsync(actionId, navigate: false);
        }
    }

    private async void OnOperationsHeroPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ExecuteActionAsync(actionId, navigate: false);
        }
    }

    private async void OnOperationsHeroSecondaryActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ExecuteActionAsync(actionId, navigate: false);
        }
    }

    private async Task ExecuteActionAsync(string actionId, bool navigate)
    {
        await DataViewModel.ExecuteActionAsync(actionId, navigate);
        ApplyViewModelState();
    }

    private void OnActionRequested(object? sender, DataWorkspaceShellActionRequestedEventArgs e)
    {
        switch (e.Kind)
        {
            case DataWorkspaceShellActionKind.SwitchContext:
                RequestContextSelection(_fundContextService, _operatingContextService);
                break;
            case DataWorkspaceShellActionKind.Navigate:
                NavigationService.NavigateTo(e.ActionId);
                break;
            case DataWorkspaceShellActionKind.OpenPane:
                OpenWorkspacePage(DataOperationsDockManager, e.ActionId, e.DockAction);
                break;
        }
    }

    private void OnRefreshRequested(object? sender, EventArgs e)
        => DispatchRefresh(RefreshShellAsync);

    private void OpenProviders_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Provider");
    private void OpenBackfill_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Backfill");
    private void OpenSymbols_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Symbols");
    private void OpenStorage_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Storage");
    private void OpenCollectionSessions_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("CollectionSessions");
    private void OpenDataExport_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("DataExport");
    private void OpenSchedules_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("Schedules");
    private void OpenPackageManager_Click(object sender, RoutedEventArgs e) => NavigationService.NavigateTo("PackageManager");
}
