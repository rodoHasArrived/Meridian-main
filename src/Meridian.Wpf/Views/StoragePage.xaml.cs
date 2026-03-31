using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Storage configuration and analytics page — thin code-behind.
/// All metric loading and preview generation live in <see cref="StorageViewModel"/>.
/// </summary>
public partial class StoragePage : Page
{
    private readonly StorageViewModel _viewModel;

    public StoragePage()
    {
        InitializeComponent();

        _viewModel = new StorageViewModel(
            StorageAnalyticsService.Instance,
            SettingsConfigurationService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        RefreshFileTreePreview();
    }

    private void StorageConfig_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null) return;
        RefreshFileTreePreview();
    }

    /// <summary>Reads current control values and delegates preview generation to the ViewModel.</summary>
    private void RefreshFileTreePreview()
    {
        var naming      = GetSelectedTag(NamingConventionCombo) ?? "BySymbol";
        var compression = GetSelectedTag(CompressionCombo)      ?? "gzip";
        var rootPath    = DataDirectoryBox.Text;

        _viewModel.RefreshPreview(rootPath, naming, compression);
    }

    private static string? GetSelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
}
