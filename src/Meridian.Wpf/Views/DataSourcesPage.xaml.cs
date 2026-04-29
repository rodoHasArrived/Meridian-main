using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for managing multiple data source configurations.
/// Code-behind is limited to:
///  – constructor DI and DataContext wiring
///  – PasswordBox read (WPF security restriction prevents binding Password)
///  – PasswordBox restore when editing an existing Polygon source
/// All business logic lives in <see cref="DataSourcesViewModel"/>.
/// </summary>
public partial class DataSourcesPage : Page
{
    private readonly DataSourcesViewModel _viewModel;

    public DataSourcesPage(WpfServices.ConfigService configService)
    {
        InitializeComponent();
        _viewModel = new DataSourcesViewModel(configService);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    // ── Save – reads PasswordBox before delegating ────────────────────────

    private async void SaveDataSource_Click(object sender, RoutedEventArgs e)
    {
        // PasswordBox.Password cannot be data-bound; read it here before Save.
        _viewModel.PolygonApiKey = PolygonApiKeyBox.Password;
        await _viewModel.SaveSourceCommand.ExecuteAsync(null);

        // Sync back PasswordBox after a successful save / edit-start
        if (!_viewModel.IsEditPanelVisible)
            PolygonApiKeyBox.Password = string.Empty;
    }

    // ── Edit form initialization – syncs combo selections ─────────────────
    // Raised when the edit panel becomes visible with a pre-populated form.

    private void EditPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        // Restore PasswordBox when editing an existing Polygon source
        PolygonApiKeyBox.Password = _viewModel.SelectedProvider == "Polygon"
            ? _viewModel.PolygonApiKey
            : string.Empty;
    }
}
