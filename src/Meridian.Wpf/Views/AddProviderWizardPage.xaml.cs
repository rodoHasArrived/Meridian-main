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
using ProviderIdentity = Meridian.Infrastructure.Adapters.Core.ProviderIdentity;
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
    private readonly SettingsConfigurationService _settingsConfigService;
    private readonly AddProviderWizardViewModel _viewModel;

    private ProviderCatalogEntry? _selectedProvider;
    private IReadOnlyList<ProviderCredentialStatus> _credentialStatuses = [];
    private IReadOnlyList<ProviderCredentialStatus> _catalogCredentialStatuses = [];

    // The vault field schema the credential service reported for the selected provider, or null
    // when it reported none (the provider is not vault-managed, or the status read failed).
    // Editors are built from this schema, never from the local catalog, whose field names differ.
    private IReadOnlyList<ProviderCredentialFieldMetadataDto>? _serviceFields;

    // Commands for one scope do not overlap. Selecting another owned connection cancels its
    // predecessor and invalidates delayed responses before they can update the new editors.
    private bool _operationInProgress;
    private bool _clearingPersistedFields;

    public AddProviderWizardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
        : this(navigationService, notificationService, SettingsConfigurationService.Instance)
    {
    }

    internal AddProviderWizardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        SettingsConfigurationService settingsConfigService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _settingsConfigService = settingsConfigService;

        _viewModel = new AddProviderWizardViewModel();
        DataContext = _viewModel;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
        => await LoadProviderCatalogAsync();

    internal async Task LoadProviderCatalogAsync()
    {
        var generation = _credentialScopeGeneration;
        var providers = _settingsConfigService.GetProviderCatalog();
        var catalogStatuses = await _settingsConfigService.GetProviderCredentialStatusesAsync();
        await LoadOwnedConnectionsAsync(generation);
        if (!IsCurrentCredentialScope(generation)) return;
        _credentialStatuses = [];
        _catalogCredentialStatuses = catalogStatuses;
        _viewModel.LoadProviderCatalog(providers, catalogStatuses);
        _viewModel.CurrentStep = 1;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string providerId })
            SelectProvider(providerId);
    }

    internal void SelectProvider(string providerId)
    {
        if (_operationInProgress)
            return;

        ResetOwnedConnectionScope();
        _selectedProvider = _viewModel.FindProvider(providerId);
        if (_selectedProvider == null)
        {
            _viewModel.OwnedConnections.Clear();
            return;
        }

        ApplyOwnedConnectionChoices();

        // Update ViewModel display properties (XAML binds to these)
        _viewModel.ApplySelectedProvider(_selectedProvider);
        _viewModel.ResetConnectionTest();
        _viewModel.SaveStatusText = string.Empty;

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

        _serviceFields = _selectedOwnedConnection is null ? null : FindStatus(_credentialStatuses, _selectedProvider)?.CredentialFields;
        if (_serviceFields is null)
        {
            if (FindStatus(_catalogCredentialStatuses, _selectedProvider)?.CredentialFields is { Count: 0 })
                _viewModel.ApplyCredentialsInfo(_selectedProvider.DisplayName, false);
            else
            {
                _viewModel.ApplyUnmanagedCredentialsInfo(_selectedProvider.DisplayName, true);
                _viewModel.CredentialsInfoText = "Select an existing owned connection to load its retained credential schema. No provider-wide fallback is used.";
            }
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

            if (input is SecretInputControl secretInput)
                secretInput.SecretChanged += CredentialEditorChanged;
            else if (input is TextBox textBox)
                textBox.TextChanged += CredentialEditorChanged;

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
        => await TestProviderConnectionAsync();

    internal async Task TestProviderConnectionAsync()
    {
        var provider = _selectedProvider;
        if (provider is null || _operationInProgress) return;
        if (!TryGetOwnedCredentialScope(provider, out var connection)) return;
        var generation = _credentialScopeGeneration;
        var ct = _credentialScopeCts.Token;
        var fields = CollectEnteredCredentialFields();
        _operationInProgress = true;
        _viewModel.SetConnectionTestTesting(provider.DisplayName);
        try
        {
            await ValidateOwnedConnectionAsync(provider, connection!, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            await PersistCredentialsAsync(provider, fields, connection!.ConnectionId, generation, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            var verified = await _settingsConfigService.VerifyProviderCredentialsAsync(provider.Id, connection.ConnectionId, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            if (CollectEnteredCredentialFields().Count > 0)
                _viewModel.ResetConnectionTest("Credentials changed. Test connection to verify the current edits.");
            else if (verified) _viewModel.SetConnectionTestSuccess();
            else _viewModel.SetConnectionTestUnverified();
            _viewModel.CurrentStep = 3;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { if (IsCurrentCredentialScope(generation)) _viewModel.SetConnectionTestError(ex is CredentialServiceRefusedException ? ex.Message : "The selected connection could not be verified. Reload its ownership and credential status."); }
        finally { if (IsCurrentCredentialScope(generation)) _operationInProgress = false; }
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
        => await SaveProviderAsync();

    internal async Task SaveProviderAsync()
    {
        var provider = _selectedProvider;
        if (provider is null || _operationInProgress) return;
        if (!TryGetOwnedCredentialScope(provider, out var connection)) return;
        var generation = _credentialScopeGeneration;
        var ct = _credentialScopeCts.Token;
        var fields = CollectEnteredCredentialFields();
        var serviceFields = _serviceFields!;
        _operationInProgress = true;
        try
        {
            await ValidateOwnedConnectionAsync(provider, connection!, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            await PersistCredentialsAsync(provider, fields, connection!.ConnectionId, generation, ct);
            if (!IsCurrentCredentialScope(generation)) return;
            _credentialStatuses = await _settingsConfigService.GetProviderCredentialStatusesAsync(ct, connection.ConnectionId);
            if (!IsCurrentCredentialScope(generation)) return;
            var current = FindStatus(_credentialStatuses, provider);
            if (serviceFields.Any(field => field.Required) && current?.State != CredentialState.Configured)
            {
                _viewModel.SetConnectionTestError("The credential service could not confirm saved credentials for this selected connection.");
                _viewModel.SetSaveError(current?.State == CredentialState.Partial
                    ? "The entered credentials were saved, but required fields are still missing. Enter the remaining required credentials."
                    : "Enter the required credentials. The service reports none saved for this selected connection.");
                return;
            }
            var hasPendingEdits = CollectEnteredCredentialFields().Count > 0;
            var verified = !hasPendingEdits && (current is
                { State: CredentialState.Configured, VerificationState: ProviderVerificationStateDto.Verified, LastVerifiedAt: not null }
                or { State: CredentialState.NotRequired });
            if (hasPendingEdits) _viewModel.ResetConnectionTest("Credentials changed during the operation. Save and test the current edits.");
            else if (verified) _viewModel.SetConnectionTestSuccess();
            else _viewModel.SetConnectionTestUnverified();
            _viewModel.CurrentStep = 4;
            _viewModel.SetSaveSuccess(provider.DisplayName);
            _viewModel.SaveStatusText = $"Credentials saved for owned connection {connection.ConnectionId}. Runtime activation is managed separately."
                + (hasPendingEdits ? " New credential edits remain unsaved." : string.Empty);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrentCredentialScope(generation)) return;
            _viewModel.SetConnectionTestError("The save was not confirmed. Reload the selected connection after resolving the error.");
            _viewModel.SetSaveError(ex.Message);
        }
        finally { if (IsCurrentCredentialScope(generation)) _operationInProgress = false; }
    }

    /// <summary>
    /// Saves only the fields the operator filled in through the authenticated credential service,
    /// which writes the selected owned connection's encrypted vault record. Nothing is written to the process or
    /// user environment, and blank fields are omitted because the vault treats blanks as deletions.
    /// </summary>
    private async Task PersistCredentialsAsync(ProviderCatalogEntry provider, Dictionary<string, string?> fields, string connectionId, long generation, System.Threading.CancellationToken ct)
    {
        if (fields.Count == 0)
            return;

        await _settingsConfigService.SaveProviderCredentialsAsync(provider.Id, fields, connectionId, ct);
        if (!IsCurrentCredentialScope(generation)) return;

        // Release persisted secrets rather than retaining a second plaintext copy to remember
        // which values were tested. Do not erase an edit made while the save awaited the service.
        _clearingPersistedFields = true;
        try
        {
            foreach (var child in CredentialFieldsPanel.Children)
            {
                switch (child)
                {
                    case TextBox { Tag: string textName } textBox when fields.TryGetValue(textName, out var savedText) &&
                        string.Equals(textBox.Text.Trim(), savedText, StringComparison.Ordinal):
                        textBox.Clear();
                        break;
                    case SecretInputControl { Tag: string secretName } secretInput when fields.TryGetValue(secretName, out var savedSecret) &&
                        string.Equals(secretInput.Secret.Trim(), savedSecret, StringComparison.Ordinal):
                        secretInput.ClearSecret();
                        break;
                }
            }
        }
        finally
        {
            _clearingPersistedFields = false;
        }
    }

    private void CredentialEditorChanged(object? sender, EventArgs e)
    {
        if (!_clearingPersistedFields)
            _viewModel.ResetConnectionTest("Credentials changed. Test connection to verify the current edits.");
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
        => statuses.FirstOrDefault(status => ProviderIdentity.EqualsId(status.ProviderId, provider.Id));

    private static bool LocalCatalogRequiresCredentials(ProviderCatalogEntry provider)
        => provider.CredentialFields.Any(field => field.Required);

    private static string UnmanagedCredentialsMessage(ProviderCatalogEntry provider)
        => $"{provider.DisplayName} credentials are not managed by the authenticated credential service, or its status " +
           "could not be read, so they cannot be saved from this wizard.";
}
