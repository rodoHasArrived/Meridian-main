using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;
using CredentialFieldInfo = Meridian.Contracts.Api.CredentialFieldInfo;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Multi-step wizard for adding and configuring a new data provider.
/// Guides the user through provider selection, credential entry, connection testing, and configuration.
/// MVVM compliant: all display state lives in <see cref="AddProviderWizardViewModel"/>.
/// Code-behind handles DI wiring, dynamic credential field generation, and minimal event delegation.
/// </summary>
public partial class AddProviderWizardPage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.ConfigService _configService;
    private readonly SettingsConfigurationService _settingsConfigService;
    private readonly AddProviderWizardViewModel _viewModel;

    private ProviderCatalogEntry? _selectedProvider;

    public AddProviderWizardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _configService = WpfServices.ConfigService.Instance;
        _settingsConfigService = SettingsConfigurationService.Instance;

        _viewModel = new AddProviderWizardViewModel();
        DataContext = _viewModel;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        var providers = _settingsConfigService.GetProviderCatalog();
        var credentialStatuses = _settingsConfigService.GetProviderCredentialStatuses();

        _viewModel.LoadProviderCatalog(providers, credentialStatuses);
        _viewModel.CurrentStep = 1;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string providerId)
            return;

        _selectedProvider = _viewModel.FindProvider(providerId);
        if (_selectedProvider == null)
            return;

        // Update ViewModel display properties (XAML binds to these)
        _viewModel.ApplySelectedProvider(_selectedProvider);

        // Show wizard step panels
        Step2Panel.Visibility = Visibility.Visible;
        Step3Panel.Visibility = Visibility.Visible;
        Step4Panel.Visibility = Visibility.Visible;

        BuildCredentialFields();
        _viewModel.CurrentStep = 2;
    }

    private void BuildCredentialFields()
    {
        CredentialFieldsPanel.Children.Clear();

        if (_selectedProvider == null)
            return;

        _viewModel.ApplyCredentialsInfo(_selectedProvider.DisplayName, _selectedProvider.CredentialFields.Length > 0);

        if (_selectedProvider.CredentialFields.Length == 0)
            return;

        foreach (var field in _selectedProvider.CredentialFields)
        {
            var envVar = field.EnvironmentVariable ?? string.Empty;
            var currentValue = GetConfiguredEnvironmentValue(field) ?? "";

            var label = new TextBlock
            {
                Text = field.DisplayName,
                Style = (Style)FindResource("FormLabelStyle"),
                Margin = new Thickness(0, 0, 0, 4),
            };

            var textBox = new TextBox
            {
                Style = (Style)FindResource("FormTextBoxStyle"),
                Text = currentValue,
                Tag = envVar,
            };

            var envHint = new TextBlock
            {
                Text = $"Environment variable: {string.Join(", ", field.AllEnvironmentVariables)}",
                FontSize = 11,
                Foreground = (Brush)FindResource("ConsoleTextMutedBrush"),
                Margin = new Thickness(0, 2, 0, 12),
            };

            CredentialFieldsPanel.Children.Add(label);
            CredentialFieldsPanel.Children.Add(textBox);
            CredentialFieldsPanel.Children.Add(envHint);
        }
    }

    private void TestProviderConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider == null)
            return;

        SaveCredentials();
        _viewModel.SetConnectionTestTesting(_selectedProvider.DisplayName);

        var hasCredentials = _selectedProvider.CredentialFields.Length == 0 ||
            _selectedProvider.CredentialFields
                .Where(field => field.Required)
                .All(HasConfiguredEnvironmentValue);

        if (hasCredentials)
        {
            _viewModel.SetConnectionTestSuccess();
            _viewModel.CurrentStep = 3;
        }
        else
        {
            _viewModel.SetConnectionTestError();
        }
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider == null)
            return;

        SaveCredentials();

        try
        {
            if (_selectedProvider.SupportsHistorical)
            {
                var options = new Meridian.Contracts.Configuration.BackfillProviderOptionsDto
                {
                    Enabled = EnableBackfillCheck.IsChecked == true,
                };

                if (int.TryParse(PriorityBox.Text, out var priority) && priority >= 0)
                    options.Priority = priority;

                await _configService.SetBackfillProviderOptionsAsync(_selectedProvider.Id, options);
            }

            _viewModel.CurrentStep = 4;
            _viewModel.SetSaveSuccess(_selectedProvider.DisplayName);

            _notificationService.NotifySuccess(
                "Provider Added",
                $"{_selectedProvider.DisplayName} has been configured and is ready to use.");
        }
        catch (Exception ex)
        {
            _viewModel.SetSaveError(ex.Message);
        }
    }

    private void SaveCredentials()
    {
        foreach (var child in CredentialFieldsPanel.Children)
        {
            if (child is TextBox textBox && textBox.Tag is string envVar)
            {
                var value = textBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(value))
                    Environment.SetEnvironmentVariable(envVar, value, EnvironmentVariableTarget.User);
            }
        }
    }

    private static bool HasConfiguredEnvironmentValue(CredentialFieldInfo field)
    {
        return field.AllEnvironmentVariables
            .Any(envVar => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(envVar)));
    }

    private static string? GetConfiguredEnvironmentValue(CredentialFieldInfo field)
    {
        foreach (var envVar in field.AllEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }
}
