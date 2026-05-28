using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Workstation.Controls;
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
        await _viewModel.ActivateAsync();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) =>
        _viewModel.Deactivate();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAsync();

    private void AddProvider_Click(object sender, RoutedEventArgs e) =>
        _viewModel.OpenAddProviderWizard();

    private void OpenSettings_Click(object sender, RoutedEventArgs e) =>
        _viewModel.OpenProviderSettings();

    private void RunDiagnostics_Click(object sender, RoutedEventArgs e) =>
        _viewModel.RunSelectedProviderDiagnostics();

    private async void OnProviderManagementCommandInvoked(object sender, WorkstationCommandInvokedEventArgs e) =>
        await _viewModel.ExecuteProviderManagementCommandAsync(e.Command.Id);

    private async void ProviderAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string providerId)
            await _viewModel.ToggleProviderConnectionAsync(providerId);
    }

    private void ProviderDetails_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string providerId)
            return;
        var details = _viewModel.GetProviderDetails(providerId);
        if (!string.IsNullOrEmpty(details))
            MessageBox.Show(details, "Provider Details", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e) =>
        _viewModel.ClearHistory();
}
