using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// System health monitoring page — thin code-behind.
/// All state, timers, and data-loading logic live in <see cref="SystemHealthViewModel"/>.
/// </summary>
public partial class SystemHealthPage : Page
{
    private readonly SystemHealthViewModel _viewModel;

    public SystemHealthPage()
    {
        InitializeComponent();

        _viewModel = new SystemHealthViewModel(
            SystemHealthService.Instance,
            WpfServices.LoggingService.Instance);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.StartAsync();
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
            global::Meridian.Wpf.Services.LoggingService.Instance.LogError("System Health page failed to load.", ex);
        }
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();

}
