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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("Storage page failed to load.", ex);
        }
    }
}
