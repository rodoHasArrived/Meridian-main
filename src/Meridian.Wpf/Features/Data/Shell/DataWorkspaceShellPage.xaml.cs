using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Features.Data.Shell;

public partial class DataWorkspaceShellPage : DataWorkspaceShellPageBase
{
    private readonly FundContextService _fundContextService;
    private readonly WorkstationOperatingContextService? _operatingContextService;

    public DataWorkspaceShellPage(
        NavigationService navigationService,
        DataWorkspaceShellStateProvider stateProvider,
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
        await RestoreDockLayoutAsync(DataDockManager);
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        DataViewModel.Stop();
        DataViewModel.RefreshRequested -= OnRefreshRequested;
        DataViewModel.ActionRequested -= OnActionRequested;
        _ = SaveDockLayoutAsync(DataDockManager);
    }

    private async Task RefreshShellAsync()
        => DataViewModel.RefreshAsync();

    private async void OnQueueStateActionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string actionId })
        {
            await ExecuteActionAsync(actionId, navigate: false);
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => OpenWorkspacePage(DataDockManager, e.PageTag, e.Action);

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
        => await DataViewModel.ExecuteActionAsync(actionId, navigate);

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
                OpenWorkspacePage(DataDockManager, e.ActionId, e.DockAction);
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
