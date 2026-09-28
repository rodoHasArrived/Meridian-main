using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services.Services;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>Represents a single credential entry shown in the list.</summary>
public sealed class CredentialEntryViewModel : BindableBase
{
    private string _statusText = string.Empty;
    private string _statusColor = "#AABCCD";
    private bool _isTesting;

    public string ProviderId { get; init; } = string.Empty;
    public string ConnectionId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string CredentialType { get; init; } = string.Empty;
    public bool HasCredentials { get; set; }
    public bool RequiresCredentials { get; init; }

    /// <summary>
    /// Vault field schema the credential service reported for this connection, or null until a
    /// status read supplies it. Editors use these names; the local catalog's names are not the
    /// vault schema (Tiingo's local field is "Token", the vault accepts "ApiKey").
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<ProviderCredentialFieldMetadataDto>? ServiceFields { get; set; }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string StatusColor
    {
        get => _statusColor;
        set => SetProperty(ref _statusColor, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }
}

/// <summary>Represents a single form field for entering a credential value.</summary>
public sealed class CredentialFieldViewModel : BindableBase
{
    private string _value = string.Empty;

    public string Label { get; init; } = string.Empty;
    public string EnvVarName { get; init; } = string.Empty;
    public string FieldName { get; init; } = string.Empty;
    public bool IsSecret { get; init; }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }
}

/// <summary>
/// ViewModel for the Credential Management page.
/// Manages listing, adding, editing, testing, and removing API credentials
/// for all registered data providers.
/// </summary>
public sealed class CredentialManagementViewModel : BindableBase, IDisposable
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly SettingsConfigurationService _settingsService;

    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _statusMessageColor = "#AABCCD";
    private CredentialEntryViewModel? _selectedCredential;
    private bool _isEditPanelVisible;
    private string _editPanelTitle = string.Empty;
    private bool _isTestResultVisible;
    private string _testResultText = string.Empty;
    private string _testResultColor = "#AABCCD";

    public ObservableCollection<CredentialEntryViewModel> Credentials { get; } = new();
    public ObservableCollection<CredentialFieldViewModel> EditFields { get; } = new();

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCredentialCommands();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string StatusMessageColor
    {
        get => _statusMessageColor;
        private set => SetProperty(ref _statusMessageColor, value);
    }

    public CredentialEntryViewModel? SelectedCredential
    {
        get => _selectedCredential;
        set
        {
            if (SetProperty(ref _selectedCredential, value))
            {
                IsEditPanelVisible = false;
                IsTestResultVisible = false;
                NotifyCredentialCommands();
                SelectionStatusLoad = LoadSelectedStatusAsync(value);
            }
        }
    }

    public bool IsEditPanelVisible
    {
        get => _isEditPanelVisible;
        private set => SetProperty(ref _isEditPanelVisible, value);
    }

    public string EditPanelTitle
    {
        get => _editPanelTitle;
        private set => SetProperty(ref _editPanelTitle, value);
    }

    public bool IsTestResultVisible
    {
        get => _isTestResultVisible;
        private set => SetProperty(ref _isTestResultVisible, value);
    }

    public string TestResultText
    {
        get => _testResultText;
        private set => SetProperty(ref _testResultText, value);
    }

    public string TestResultColor
    {
        get => _testResultColor;
        private set => SetProperty(ref _testResultColor, value);
    }

    public ICommand EditCredentialCommand { get; }
    public ICommand RemoveCredentialCommand { get; }
    public ICommand TestCredentialCommand { get; }
    public ICommand TestAllCredentialsCommand { get; }
    public ICommand SaveCredentialCommand { get; }
    public ICommand CancelEditCommand { get; }

    public CredentialManagementViewModel(
        WpfServices.CredentialService credentialService,
        WpfServices.NotificationService notificationService)
        : this(SettingsConfigurationService.Instance, notificationService)
    {
        ArgumentNullException.ThrowIfNull(credentialService);
    }

    internal CredentialManagementViewModel(SettingsConfigurationService settingsService, WpfServices.NotificationService notificationService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        EditCredentialCommand = new RelayCommand(BeginEdit, () => SelectedCredential != null && !IsBusy);
        RemoveCredentialCommand = new AsyncRelayCommand(RemoveCredentialAsync, () => SelectedCredential != null && !IsBusy);
        TestCredentialCommand = new AsyncRelayCommand(TestSelectedCredentialAsync, () => SelectedCredential != null && !IsBusy);
        TestAllCredentialsCommand = new AsyncRelayCommand(TestAllCredentialsAsync, () => !IsBusy && Credentials.Any(row => row.RequiresCredentials));
        SaveCredentialCommand = new AsyncRelayCommand(SaveCredentialAsync, () => SelectedCredential != null && !IsBusy);
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    private void NotifyCredentialCommands()
    {
        ((RelayCommand)EditCredentialCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RemoveCredentialCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)TestCredentialCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)SaveCredentialCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)TestAllCredentialsCommand).NotifyCanExecuteChanged();
    }

    internal Task SelectionStatusLoad { get; private set; } = Task.CompletedTask;

    private int _credentialLoadVersion;
    private int _selectedStatusVersion;

    // Set when the editor opened before the service reported this connection's field schema. The
    // next status read for that connection rebuilds the open editor instead of leaving it inert.
    private bool _editAwaitingSchema;

    public async Task LoadCredentialsAsync()
    {
        var version = ++_credentialLoadVersion;
        SelectedCredential = null;
        Credentials.Clear();
        StatusMessage = "Loading owned connections…";
        try
        {
            var connections = await _settingsService.GetOwnedCredentialConnectionsAsync();
            if (version != _credentialLoadVersion)
                return;
            var catalog = _settingsService.GetProviderCatalog();
            foreach (var connection in connections)
            {
                var provider = catalog.FirstOrDefault(item => string.Equals(item.Id, connection.ProviderFamilyId, StringComparison.OrdinalIgnoreCase));
                if (provider is null)
                    continue;
                Credentials.Add(new CredentialEntryViewModel
                {
                    ProviderId = provider.Id,
                    ConnectionId = connection.ConnectionId,
                    DisplayName = $"{connection.DisplayName} · {connection.ExternalAccountId} · {connection.CredentialEnvironment}",
                    CredentialType = GetCredentialType(provider),
                    RequiresCredentials = provider.CredentialFields.Length > 0,
                    StatusText = "Select to load status",
                    StatusColor = "#AABCCD"
                });
            }
            StatusMessage = Credentials.Count == 0
                ? "No owned credential connections are available. Establish connection ownership before editing credentials."
                : $"{Credentials.Count} owned connections. Select an account and environment to manage credentials.";
        }
        catch (Exception)
        {
            if (version == _credentialLoadVersion)
                StatusMessage = "Owned connections are unavailable from the authenticated service.";
        }
        StatusMessageColor = "#AABCCD";
        NotifyCredentialCommands();
    }

    private async Task LoadSelectedStatusAsync(CredentialEntryViewModel? selected)
    {
        var statusVersion = ++_selectedStatusVersion;
        if (selected is null)
            return;
        var version = _credentialLoadVersion;
        var statuses = await _settingsService.GetProviderCredentialStatusesAsync(connectionId: selected.ConnectionId);
        if (version != _credentialLoadVersion || statusVersion != _selectedStatusVersion || !ReferenceEquals(SelectedCredential, selected) || selected.IsTesting)
            return;
        var status = statuses.FirstOrDefault(item => item.ProviderId == selected.ProviderId);
        selected.ServiceFields = status?.CredentialFields;
        selected.HasCredentials = status?.State is CredentialState.Configured or CredentialState.Partial;
        selected.StatusText = status?.StatusMessage ?? "Credential status is unavailable from the service.";
        selected.StatusColor = status?.State == CredentialState.Configured ? "#3FB950" : "#AABCCD";
        if (_editAwaitingSchema && IsEditPanelVisible)
        {
            _editAwaitingSchema = false;
            BuildEditFields(selected, allowSchemaReload: false);
        }
    }

    private void BeginEdit()
    {
        if (SelectedCredential is null)
            return;
        IsTestResultVisible = false;
        BuildEditFields(SelectedCredential, allowSchemaReload: true);
        IsEditPanelVisible = true;
    }

    private void BuildEditFields(CredentialEntryViewModel selected, bool allowSchemaReload)
    {
        EditFields.Clear();
        EditPanelTitle = selected.HasCredentials
            ? $"Edit credentials — {selected.DisplayName}"
            : $"Add credentials — {selected.DisplayName}";

        var serviceFields = selected.ServiceFields;
        if (serviceFields is null)
        {
            // Informational rows have no FieldName, so Save never submits them.
            if (allowSchemaReload && !selected.IsTesting)
            {
                // The selection's status read is still pending or was superseded (for example by
                // Test All). Start a fresh read; it rebuilds this editor when it completes.
                _editAwaitingSchema = true;
                EditFields.Add(new CredentialFieldViewModel
                {
                    Label = "Loading credential fields",
                    EnvVarName = string.Empty,
                    IsSecret = false,
                    Value = "Reading the credential fields for this connection from the service."
                });
                SelectionStatusLoad = LoadSelectedStatusAsync(selected);
                return;
            }

            EditFields.Add(new CredentialFieldViewModel
            {
                Label = "Credential fields unavailable",
                EnvVarName = string.Empty,
                IsSecret = false,
                Value = "The credential service did not report the fields for this connection. Reselect it to try again."
            });
        }
        else if (serviceFields.Count == 0)
        {
            EditFields.Add(new CredentialFieldViewModel
            {
                Label = "No credentials required",
                EnvVarName = string.Empty,
                IsSecret = false,
                Value = "This provider does not require API credentials."
            });
        }
        else
        {
            foreach (var field in serviceFields)
            {
                EditFields.Add(new CredentialFieldViewModel
                {
                    Label = field.Label,
                    EnvVarName = field.Name,
                    FieldName = field.Name,
                    IsSecret = field.InputKind == ProviderCredentialInputKindDto.Password,
                    Value = string.Empty
                });
            }
        }
    }

    private async Task SaveCredentialAsync()
    {
        var selected = SelectedCredential;
        if (selected is null || IsBusy)
            return;
        // Editors start blank and the vault treats a blank value as a field deletion, so only fields
        // the operator actually filled in are submitted. Untouched fields keep their retained values.
        var fields = EditFields.Where(field => !string.IsNullOrWhiteSpace(field.FieldName) && !string.IsNullOrWhiteSpace(field.Value))
            .ToDictionary(field => field.FieldName, field => (string?)field.Value, StringComparer.OrdinalIgnoreCase);
        if (fields.Count == 0)
        {
            _notificationService.ShowNotification("Nothing to Save",
                "Enter a value for at least one credential field. Blank fields keep their current values.", NotificationType.Warning);
            return;
        }
        IsBusy = true;
        try
        {
            await _settingsService.SaveProviderCredentialsAsync(selected.ProviderId, fields, selected.ConnectionId);
            IsEditPanelVisible = false;
            EditFields.Clear();
            await LoadCredentialsAsync();
            _notificationService.ShowNotification("Credentials Saved",
                $"Credentials for {selected.DisplayName} have been saved.", NotificationType.Success);
        }
        catch (Exception)
        {
            _notificationService.ShowNotification("Save Failed",
                "Credential persistence was not confirmed by the authenticated service.", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelEdit()
    {
        _editAwaitingSchema = false;
        IsEditPanelVisible = false;
        EditFields.Clear();
        IsTestResultVisible = false;
    }

    private async Task RemoveCredentialAsync()
    {
        var selected = SelectedCredential;
        if (selected is null || IsBusy)
            return;
        IsBusy = true;
        try
        {
            await _settingsService.RemoveProviderCredentialsAsync(selected.ProviderId, selected.ConnectionId);
            IsEditPanelVisible = false;
            IsTestResultVisible = false;
            EditFields.Clear();
            await LoadCredentialsAsync();
            _notificationService.ShowNotification("Credentials Removed",
                $"Credentials for {selected.DisplayName} have been removed.", NotificationType.Info);
        }
        catch (Exception)
        {
            _notificationService.ShowNotification("Remove Failed",
                "Credential removal was not confirmed by the authenticated service.", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task TestSelectedCredentialAsync()
    {
        var selected = SelectedCredential;
        if (selected is null || IsBusy)
            return;
        IsBusy = true;
        try
        {
            await VerifyCredentialAsync(selected);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task VerifyCredentialAsync(CredentialEntryViewModel selected)
    {
        if (selected.IsTesting)
            return;
        selected.IsTesting = true;
        ++_selectedStatusVersion;
        IsTestResultVisible = true;
        TestResultText = $"Testing {selected.DisplayName}�";
        TestResultColor = "#AABCCD";
        var success = false;
        try
        {
            success = await _settingsService.VerifyProviderCredentialsAsync(selected.ProviderId, selected.ConnectionId);
        }
        catch (Exception)
        {
            // Transport failure cannot establish verification or expose response details.
        }
        finally
        {
            selected.IsTesting = false;
        }
        selected.StatusText = success ? "Verified" : "Not verified";
        selected.StatusColor = success ? "#3FB950" : "#D29922";
        if (ReferenceEquals(SelectedCredential, selected))
        {
            TestResultText = success
                ? $"{selected.DisplayName}: verification acknowledged by the service."
                : $"{selected.DisplayName}: verification was not confirmed by the service.";
            TestResultColor = selected.StatusColor;
        }
    }

    private async Task TestAllCredentialsAsync()
    {
        if (IsBusy)
            return;
        IsBusy = true;
        StatusMessage = "Testing all credentials�";
        StatusMessageColor = "#AABCCD";
        try
        {
            var entries = Credentials.Where(c => c.RequiresCredentials).ToList();
            foreach (var cred in entries)
            {
                SelectedCredential = cred;
                await VerifyCredentialAsync(cred);
            }
            var ok = entries.Count(c => c.StatusText == "Verified");
            StatusMessage = $"{ok} of {entries.Count} providers verified by the service";
            StatusMessageColor = ok == entries.Count ? "#3FB950" : "#D29922";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string GetCredentialType(ProviderCatalogEntry provider)
    {
        var count = provider.CredentialFields.Length;
        return count switch
        {
            0 => "None",
            1 => "API Key",
            _ => "Key + Secret",
        };
    }

    public void Dispose()
    {
        ++_credentialLoadVersion;
        SelectedCredential = null;
    }
}
