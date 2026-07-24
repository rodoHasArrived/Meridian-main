using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for managing multiple data source configurations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – secret input read/restore for the Polygon API key
/// All business logic lives in <see cref="DataSourcesViewModel"/>.
/// </summary>
public partial class DataSourcesPage : Page
{
    private readonly DataSourcesViewModel _viewModel;

    public DataSourcesPage(
        WpfServices.ConfigService configService,
        WpfServices.FirstRunService firstRunService)
    {
        InitializeComponent();
        _viewModel = new DataSourcesViewModel(configService, firstRunService);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LoadAsync();
        }
        catch (System.OperationCanceledException)
        {
            // Navigation cancelled the in-flight load before it completed; benign during teardown.
            global::Meridian.Wpf.Services.LoggingService.Instance.LogDebug(
                "Page load cancelled during navigation.",
                ("page", GetType().Name));
        }
        catch (System.Exception ex)
        {
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Data Sources page failed to load.", ex);
        }
    }

    // ── Save – reads secret input before delegating ───────────────────────

    private async void SaveDataSource_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.PolygonApiKey = PolygonApiKeyInput.Secret;
        await _viewModel.SaveSourceCommand.ExecuteAsync(null);

        // Sync back the secret input after a successful save / edit-start.
        if (!_viewModel.IsEditPanelVisible)
            PolygonApiKeyInput.ClearSecret();
    }

    // ── Edit form initialization – syncs combo selections ─────────────────
    // Raised when the edit panel becomes visible with a pre-populated form.

    private void EditPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        // Restore the secret input when editing an existing Polygon source.
        PolygonApiKeyInput.Secret = _viewModel.SelectedProvider == "Polygon"
            ? _viewModel.PolygonApiKey
            : string.Empty;
    }
}
