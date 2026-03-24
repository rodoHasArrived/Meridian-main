using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Provider health page — thin code-behind.
/// All state, HTTP loading, timers, and connection-event tracking live in
/// <see cref="ProviderHealthViewModel"/>.
/// </summary>
public partial class ProviderHealthPage : Page
{
    private readonly ProviderHealthViewModel _viewModel;

    public ProviderHealthPage(
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.LoggingService loggingService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();

        _viewModel = new ProviderHealthViewModel(
            statusService, connectionService, loggingService, notificationService);
        DataContext = _viewModel;

        Unloaded += OnPageUnloaded;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.StartAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Stop();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private async void ProviderAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string providerId)
            await _viewModel.ToggleProviderConnectionAsync(providerId);
    }

    private void ProviderDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string providerId) return;
        var details = _viewModel.GetProviderDetails(providerId);
        if (!string.IsNullOrEmpty(details))
            MessageBox.Show(details, "Provider Details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ClearHistory();
}
