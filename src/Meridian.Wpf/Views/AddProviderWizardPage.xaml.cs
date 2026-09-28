using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Contracts.Configuration;
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
    private IReadOnlyList<ProviderCredentialStatus> _credentialStatuses = [];

    // The vault field schema the credential service reported for the selected provider, or null
    // when it reported none (the provider is not vault-managed, or the status read failed).
    // Editors are built from this schema, never from the local catalog, whose field names differ.
    private IReadOnlyList<ProviderCredentialFieldMetadataDto>? _serviceFields;

    // True while a Test or Save awaits the credential service. Provider selection and the other
    // wizard command are ignored until it completes, so results and shared inputs cannot be
    // applied to a different provider than the one the operation started for.
    private bool _operationInProgress;

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
        // The wizard writes provider-wide records, so it reads provider-wide status rather than the
        // tenant readiness view that also counts credentials on the tenant's own connections.
        var credentialStatuses = await _settingsConfigService.GetProviderCredentialStatusesAsync(providerWideOnly: true);
        _credentialStatuses = credentialStatuses;

        _viewModel.LoadProviderCatalog(providers, credentialStatuses);
        _viewModel.CurrentStep = 1;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (_operationInProgress || sender is not Button button || button.Tag is not string providerId)
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
        _serviceFields = null;

        if (_selectedProvider == null)
            return;

        _serviceFields = FindStatus(_credentialStatuses, _selectedProvider)?.CredentialFields;
        if (_serviceFields is null)
        {
            _viewModel.ApplyUnmanagedCredentialsInfo(_selectedProvider.DisplayName, LocalCatalogRequiresCredentials(_selectedProvider));
            return;
        }

        _viewModel.ApplyCredentialsInfo(_selectedProvider.DisplayName, _serviceFields.Count > 0);

        foreach (var field in _serviceFields)
        {
            // Editors start blank: secrets are never read back from the vault or the process
            // environment, and a blank field keeps whatever value the vault already retains.
            var label = new TextBlock
            {
                Text = field.Label,
                Style = (Style)FindResource("FormLabelStyle"),
                Margin = new Thickness(0, 0, 0, 4),
            };

            FrameworkElement input = field.InputKind == ProviderCredentialInputKindDto.Password
                ? new SecretInputControl
                {
                    Secret = string.Empty,
                    Tag = field.Name,
                    InputAutomationId = $"AddProviderCredentialInput_{field.Name}",
                    RevealAutomationId = $"AddProviderCredentialReveal_{field.Name}",
                    InputAutomationName = field.Label,
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
        if (provider == null || _operationInProgress)
            return;

        var serviceFields = _serviceFields;
        if (serviceFields is null)
        {
            if (LocalCatalogRequiresCredentials(provider))
            {
                _viewModel.SetConnectionTestError(UnmanagedCredentialsMessage(provider));
                return;
            }

            _viewModel.SetConnectionTestSuccess();
            _viewModel.CurrentStep = 3;
            return;
        }

        var fields = CollectEnteredCredentialFields();
        if (fields.Count == 0 && !serviceFields.Any(field => field.Required))
        {
            // The vault needs no credentials for this provider (for example Interactive Brokers,
            // whose host and port are connection settings), so there is nothing to verify.
            _viewModel.SetConnectionTestSuccess();
            _viewModel.CurrentStep = 3;
            return;
        }

        _operationInProgress = true;
        _viewModel.SetConnectionTestTesting(provider.DisplayName);
        try
        {
            await PersistCredentialsAsync(provider, fields);
            var verified = await _settingsConfigService.VerifyProviderCredentialsAsync(provider.Id);

            if (verified)
                _viewModel.SetConnectionTestSuccess();
            else
                _viewModel.SetConnectionTestUnverified();
            _viewModel.CurrentStep = 3;
        }
        catch (CredentialServiceRefusedException ex)
        {
            _viewModel.SetConnectionTestError(ex.Message);
        }
        catch (Exception)
        {
            _viewModel.SetConnectionTestError();
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        var provider = _selectedProvider;
        if (provider == null || _operationInProgress)
            return;

        // Capture every input before the first await; the shared controls belong to whichever
        // provider is selected when they are read.
        var fields = CollectEnteredCredentialFields();
        var backfillOptions = provider.SupportsHistorical
            ? new Meridian.Contracts.Configuration.BackfillProviderOptionsDto { Enabled = EnableBackfillCheck.IsChecked == true }
            : null;
        if (backfillOptions is not null && int.TryParse(PriorityBox.Text, out var priority) && priority >= 0)
            backfillOptions.Priority = priority;

        var serviceFields = _serviceFields;
        if (serviceFields is null && LocalCatalogRequiresCredentials(provider))
        {
            _viewModel.SetSaveError(UnmanagedCredentialsMessage(provider));
            return;
        }

        _operationInProgress = true;
        try
        {
            if (fields.Count > 0)
            {
                await PersistCredentialsAsync(provider, fields);
            }
            else if (serviceFields?.Any(field => field.Required) == true)
            {
                // Nothing was entered, so success depends on credentials the vault already holds.
                // Re-read the service rather than trusting the status captured when the page opened.
                var current = FindStatus(await _settingsConfigService.GetProviderCredentialStatusesAsync(providerWideOnly: true), provider);
                if (current?.State != CredentialState.Configured)
                {
                    _viewModel.SetSaveError("Enter the required credentials. The credential service reports none saved for this provider.");
                    return;
                }
            }

            if (backfillOptions is not null)
                await _configService.SetBackfillProviderOptionsAsync(provider.Id, backfillOptions);

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
        finally
        {
            _operationInProgress = false;
        }
    }

    /// <summary>
    /// Saves only the fields the operator filled in through the authenticated credential service,
    /// which writes the provider's encrypted vault record. Nothing is written to the process or
    /// user environment, and blank fields are omitted because the vault treats blanks as deletions.
    /// </summary>
    private async Task PersistCredentialsAsync(ProviderCatalogEntry provider, Dictionary<string, string?> fields)
    {
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

    private static ProviderCredentialStatus? FindStatus(IReadOnlyList<ProviderCredentialStatus> statuses, ProviderCatalogEntry provider)
        => statuses.FirstOrDefault(status => string.Equals(status.ProviderId, provider.Id, StringComparison.OrdinalIgnoreCase));

    private static bool LocalCatalogRequiresCredentials(ProviderCatalogEntry provider)
        => provider.CredentialFields.Any(field => field.Required);

    private static string UnmanagedCredentialsMessage(ProviderCatalogEntry provider)
        => $"{provider.DisplayName} credentials are not managed by the authenticated credential service, or its status " +
           "could not be read, so they cannot be saved from this wizard.";
}
