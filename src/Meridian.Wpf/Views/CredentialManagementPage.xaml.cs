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

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.LoadCredentials();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.Dispose();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

    /// <summary>
    /// Routes PasswordBox changes back to the matching CredentialFieldViewModel
    /// so the ViewModel stays authoritative for all field values.
    /// PasswordBox does not support two-way data binding natively.
    /// </summary>
    private void CredentialField_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox box) return;

        var envVarName = box.Tag as string;
        if (string.IsNullOrEmpty(envVarName)) return;

        var field = _viewModel.EditFields.FirstOrDefault(f => f.EnvVarName == envVarName);
        if (field is not null)
            field.Value = box.Password;
    }
}
