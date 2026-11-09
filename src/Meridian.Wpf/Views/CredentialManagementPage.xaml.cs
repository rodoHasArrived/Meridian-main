using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Credential Management page: lists all provider API credentials, supports
/// add/edit/remove/test flows. All logic lives in CredentialManagementViewModel.
/// </summary>
public partial class CredentialManagementPage : Page
{
    private readonly CredentialManagementViewModel _viewModel;
    private readonly WpfServices.NavigationService _navigationService;

    public CredentialManagementPage(
        CredentialManagementViewModel viewModel,
        WpfServices.NavigationService navigationService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _navigationService = navigationService;

        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
        => await global::Meridian.Wpf.Services.AsyncHandlerGuard.RunAsync(() => PageLoadedCoreAsync(sender, e), nameof(OnPageLoaded)).ConfigureAwait(true);

    private async System.Threading.Tasks.Task PageLoadedCoreAsync(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadCredentialsAsync();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

}
