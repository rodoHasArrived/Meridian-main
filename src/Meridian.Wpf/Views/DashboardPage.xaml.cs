using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Dashboard page — thin code-behind.
/// All state, business logic, and timer management live in <see cref="DashboardViewModel"/>.
/// </summary>
public partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.ConnectionService connectionService,
        WpfServices.StatusService statusService,
        WpfServices.MessagingService messagingService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();
        _viewModel = new DashboardViewModel(
            navigationService, connectionService, statusService, messagingService, notificationService);
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e) => _viewModel.Start();

    private void OnPageUnloaded(object sender, RoutedEventArgs e) => _viewModel.Dispose();

    private void AcknowledgeIntegrityEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int id)
        {
            _viewModel.AcknowledgeIntegrityEventCommand.Execute(id);
        }
    }
}
