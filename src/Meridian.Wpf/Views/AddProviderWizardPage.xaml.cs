using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        var providers = _settingsConfigService.GetProviderCatalog();
        var credentialStatuses = await _settingsConfigService.GetProviderCredentialStatusesAsync();

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
            // Editors start blank: secrets are never read back from the vault or the process
            // environment, and a blank field keeps whatever value the vault already retains.
            var automationKey = field.EnvironmentVariable ?? field.Name;
            var isSecret = IsSecretCredentialField(field);

            var label = new TextBlock
            {
                Text = field.DisplayName,
                Style = (Style)FindResource("FormLabelStyle"),
                Margin = new Thickness(0, 0, 0, 4),
            };

            FrameworkElement input = isSecret
                ? new SecretInputControl
                {
                    Secret = string.Empty,
                    Tag = field.Name,
                    InputAutomationId = $"AddProviderCredentialInput_{automationKey}",
                    RevealAutomationId = $"AddProviderCredentialReveal_{automationKey}",
                    InputAutomationName = field.DisplayName,
                    RevealAutomationName = "Show or hide provider credential",
                    RevealToolTip = "Show or hide provider credential",
                }
                : new TextBox
                {
                    Style = (Style)FindResource("FormTextBoxStyle"),
                    Text = string.Empty,
                    Tag = field.Name,
                };

            var storageHint = new TextBlock
            {
                Text = field.Required
                    ? "Required. Stored in the encrypted credential vault; leave blank to keep the current value."
                    : "Optional. Stored in the encrypted credential vault; leave blank to keep the current value.",
                FontSize = 11,
                Foreground = (Brush)FindResource("ConsoleTextMutedBrush"),
                Margin = new Thickness(0, 2, 0, 12),
            };

            CredentialFieldsPanel.Children.Add(label);
            CredentialFieldsPanel.Children.Add(input);
            CredentialFieldsPanel.Children.Add(storageHint);
        }
    }

    private async void TestProviderConnection_Click(object sender, RoutedEventArgs e)
    {
        var provider = _selectedProvider;
        if (provider == null)
            return;

        if (provider.CredentialFields.Length == 0)
        {
            _viewModel.SetConnectionTestSuccess();
            _viewModel.CurrentStep = 3;
            return;
        }

        _viewModel.SetConnectionTestTesting(provider.DisplayName);
        try
        {
            await PersistCredentialsAsync(provider);
            var verified = await _settingsConfigService.VerifyProviderCredentialsAsync(provider.Id);
            if (!ReferenceEquals(provider, _selectedProvider))
                return;

            if (verified)
                _viewModel.SetConnectionTestSuccess();
            else
                _viewModel.SetConnectionTestUnverified();
            _viewModel.CurrentStep = 3;
        }
        catch (Exception)
        {
            if (ReferenceEquals(provider, _selectedProvider))
                _viewModel.SetConnectionTestError();
        }
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        var provider = _selectedProvider;
        if (provider == null)
            return;

        try
        {
            await PersistCredentialsAsync(provider);

            if (provider.SupportsHistorical)
            {
                var options = new Meridian.Contracts.Configuration.BackfillProviderOptionsDto
                {
                    Enabled = EnableBackfillCheck.IsChecked == true,
                };

                if (int.TryParse(PriorityBox.Text, out var priority) && priority >= 0)
                    options.Priority = priority;

                await _configService.SetBackfillProviderOptionsAsync(provider.Id, options);
            }

            _viewModel.CurrentStep = 4;
            _viewModel.SetSaveSuccess(provider.DisplayName);

            _notificationService.NotifySuccess(
                "Provider Added",
                $"{provider.DisplayName} has been configured. Use Test Connection to verify its credentials.");
        }
        catch (Exception ex)
        {
            _viewModel.SetSaveError(ex.Message);
        }
    }

    /// <summary>
    /// Saves only the fields the operator filled in through the authenticated credential service,
    /// which writes the provider's encrypted vault record. Nothing is written to the process or
    /// user environment, and blank fields are omitted because the vault treats blanks as deletions.
    /// </summary>
    private async Task PersistCredentialsAsync(ProviderCatalogEntry provider)
    {
        var fields = CollectEnteredCredentialFields();
        if (fields.Count == 0)
            return;

        await _settingsConfigService.SaveProviderCredentialsAsync(provider.Id, fields);
    }

    private Dictionary<string, string?> CollectEnteredCredentialFields()
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in CredentialFieldsPanel.Children)
        {
            var (fieldName, value) = child switch
            {
                TextBox { Tag: string name } textBox => (name, textBox.Text),
                SecretInputControl { Tag: string name } secretInput => (name, secretInput.Secret),
                _ => (null, null)
            };

            if (!string.IsNullOrWhiteSpace(fieldName) && !string.IsNullOrWhiteSpace(value))
                fields[fieldName] = value.Trim();
        }

        return fields;
    }

    private static bool IsSecretCredentialField(CredentialFieldInfo field)
    {
        return field.DisplayName.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || field.DisplayName.Contains("token", StringComparison.OrdinalIgnoreCase)
            || field.DisplayName.Contains("key", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
            || field.Name.Contains("key", StringComparison.OrdinalIgnoreCase);
    }
}
