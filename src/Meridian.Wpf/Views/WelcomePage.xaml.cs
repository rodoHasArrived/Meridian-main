using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;
using Meridian.Ui.Services.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Welcome page — thin code-behind.
/// All state management, data loading, and commands live in <see cref="WelcomePageViewModel"/>.
/// </summary>
public partial class WelcomePage : Page
{
    private readonly WelcomePageViewModel _viewModel;

    public WelcomePage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService,
        WpfServices.ConnectionService connectionService,
        WpfServices.ConfigService configService)
    {
        InitializeComponent();

        _viewModel = new WelcomePageViewModel(
            navigationService, notificationService, statusService, connectionService, configService);
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.LoadAsync();

    // ── Step card click relays ─────────────────────────────────────────────────────────
    private void StepProvider_CardClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.NavigateToProviderCommand.Execute(null);

    private void StepSymbols_CardClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.NavigateToSymbolsCommand.Execute(null);

    private void StepStorage_CardClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.NavigateToStorageCommand.Execute(null);

    private void StepDashboard_CardClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.NavigateToDashboardCommand.Execute(null);

    private void StepDataQuality_CardClick(object sender, MouseButtonEventArgs e) =>
        _viewModel.NavigateToDataQualityCommand.Execute(null);

    // ── Button click relays ───────────────────────────────────────────────────────────
    private void StepProvider_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NavigateToProviderCommand.Execute(null);

    private void StepSymbols_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NavigateToSymbolsCommand.Execute(null);

    private void StepStorage_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NavigateToStorageCommand.Execute(null);

    private void StepDashboard_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NavigateToDashboardCommand.Execute(null);

    private void StepDataQuality_Click(object sender, RoutedEventArgs e) =>
        _viewModel.NavigateToDataQualityCommand.Execute(null);

    private void OpenDocumentation_Click(object sender, RoutedEventArgs e) =>
        _viewModel.OpenDocumentationCommand.Execute(null);
}
