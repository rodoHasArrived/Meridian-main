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

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void GenerateDiagnostics_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.GenerateDiagnosticsAsync();
}
