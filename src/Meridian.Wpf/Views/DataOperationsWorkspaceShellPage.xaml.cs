using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Data Operations workspace shell — landing page for provider, backfill,
/// symbol, storage, and collection operations.
/// </summary>
public partial class DataOperationsWorkspaceShellPage : Page
{
    private const string WorkspaceId = "data-operations";

    private readonly NavigationService _navigationService;

    public DataOperationsWorkspaceShellPage(NavigationService navigationService)
    {
        InitializeComponent();
        _navigationService = navigationService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await RestoreDockLayoutAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _ = SaveDockLayoutAsync();
    }

    private async System.Threading.Tasks.Task RestoreDockLayoutAsync()
    {
        try
        {
            var xml = await WorkspaceService.Instance.GetDockLayoutAsync(WorkspaceId);
            if (!string.IsNullOrWhiteSpace(xml))
            {
                DataOperationsDockManager.LoadLayout(xml);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[DataOperationsWorkspaceShell] Failed to restore dock layout: {ex.Message}");
        }
    }

    private async System.Threading.Tasks.Task SaveDockLayoutAsync()
    {
        try
        {
            var xml = DataOperationsDockManager.SaveLayout();
            if (!string.IsNullOrWhiteSpace(xml))
            {
                await WorkspaceService.Instance.SaveDockLayoutAsync(WorkspaceId, xml);
            }
        }
        catch (Exception ex)
        {
            LoggingService.Instance.LogError($"[DataOperationsWorkspaceShell] Failed to save dock layout: {ex.Message}");
        }
    }

    private void OnPaneDropRequested(object? sender, PaneDropEventArgs e)
        => _navigationService.NavigateTo(e.PageTag);

    private void OpenProviders_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Provider");

    private void OpenBackfill_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Backfill");

    private void OpenSymbols_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Symbols");

    private void OpenStorage_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("Storage");

    private void OpenCollectionSessions_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("CollectionSessions");

    private void OpenPackageManager_Click(object sender, RoutedEventArgs e)
        => _navigationService.NavigateTo("PackageManager");
}
