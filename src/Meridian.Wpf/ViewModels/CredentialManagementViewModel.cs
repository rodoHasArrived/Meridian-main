using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
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
    public string DisplayName { get; init; } = string.Empty;
    public string CredentialType { get; init; } = string.Empty;
    public bool HasCredentials { get; init; }
    public bool RequiresCredentials { get; init; }

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
        private set => SetProperty(ref _isBusy, value);
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
                ((RelayCommand)EditCredentialCommand).NotifyCanExecuteChanged();
                ((RelayCommand)RemoveCredentialCommand).NotifyCanExecuteChanged();
                ((RelayCommand)TestCredentialCommand).NotifyCanExecuteChanged();
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
    {
        ArgumentNullException.ThrowIfNull(credentialService);
        _notificationService = notificationService;

        EditCredentialCommand = new RelayCommand(BeginEdit, () => SelectedCredential != null);
        RemoveCredentialCommand = new RelayCommand(() => _ = RemoveCredentialAsync(), () => SelectedCredential != null);
        TestCredentialCommand = new RelayCommand(() => _ = TestSelectedCredentialAsync(), () => SelectedCredential != null);
        TestAllCredentialsCommand = new RelayCommand(() => _ = TestAllCredentialsAsync());
        SaveCredentialCommand = new RelayCommand(() => _ = SaveCredentialAsync());
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    private int _credentialLoadVersion;

    public async Task LoadCredentialsAsync()
    {
        var version = ++_credentialLoadVersion;
        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var statuses = await SettingsConfigurationService.Instance.GetProviderCredentialStatusesAsync();
        if (version != _credentialLoadVersion)
            return;
        Credentials.Clear();

        foreach (var provider in catalog)
        {
            var status = statuses.FirstOrDefault(s => s.ProviderId == provider.Id);
            var state = status?.State ?? CredentialState.Unavailable;

            Credentials.Add(new CredentialEntryViewModel
            {
                ProviderId = provider.Id,
                DisplayName = provider.DisplayName,
                CredentialType = GetCredentialType(provider),
                HasCredentials = state is CredentialState.Configured or CredentialState.Partial,
                RequiresCredentials = provider.CredentialFields.Length > 0,
                StatusText = state switch
                {
                    CredentialState.Configured => "Configured",
                    CredentialState.Partial => "Partial",
                    CredentialState.Missing => "Missing",
                    CredentialState.NotRequired => "Not required",
                    CredentialState.Unavailable => "Unavailable",
                    _ => "Unknown"
                },
                StatusColor = state switch
                {
                    CredentialState.Configured => "#3FB950",
                    CredentialState.Partial => "#D29922",
                    CredentialState.Missing => "#F85149",
                    _ => "#AABCCD"
                }
            });
        }

        var configured = Credentials.Count(c => c.HasCredentials);
        var total = Credentials.Count(c => c.RequiresCredentials);
        StatusMessage = $"{configured} of {total} providers configured";
        StatusMessageColor = "#AABCCD";
    }

    private void BeginEdit()
    {
        if (SelectedCredential is null)
            return;
        EditFields.Clear();
        IsTestResultVisible = false;

        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var provider = catalog.FirstOrDefault(p => p.Id == SelectedCredential.ProviderId);
        if (provider is null)
            return;

        EditPanelTitle = SelectedCredential.HasCredentials
            ? $"Edit credentials — {SelectedCredential.DisplayName}"
            : $"Add credentials — {SelectedCredential.DisplayName}";

        if (provider.CredentialFields.Length == 0)
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
            foreach (var field in provider.CredentialFields)
            {
                var envVar = field.EnvironmentVariable ?? string.Empty;
                var isSecret = field.DisplayName.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("key", StringComparison.OrdinalIgnoreCase);

                EditFields.Add(new CredentialFieldViewModel
                {
                    Label = field.DisplayName,
                    EnvVarName = envVar,
                    FieldName = field.Name,
                    IsSecret = isSecret,
                    Value = string.Empty
                });
            }
        }

        IsEditPanelVisible = true;
    }

    private async Task SaveCredentialAsync()
    {
        var selected = SelectedCredential;
        if (selected is null || IsBusy)
            return;
        var fields = EditFields.Where(field => !string.IsNullOrWhiteSpace(field.FieldName))
            .ToDictionary(field => field.FieldName, field => (string?)field.Value, StringComparer.OrdinalIgnoreCase);
        if (fields.Count == 0)
            return;
        IsBusy = true;
        try
        {
            await SettingsConfigurationService.Instance.SaveProviderCredentialsAsync(selected.ProviderId, fields);
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
            await SettingsConfigurationService.Instance.RemoveProviderCredentialsAsync(selected.ProviderId);
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
        if (selected is null || selected.IsTesting)
            return;
        selected.IsTesting = true;
        IsTestResultVisible = true;
        TestResultText = $"Testing {selected.DisplayName}�";
        TestResultColor = "#AABCCD";
        var success = false;
        try
        {
            success = await SettingsConfigurationService.Instance.VerifyProviderCredentialsAsync(selected.ProviderId);
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
                await TestSelectedCredentialAsync();
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

    public void Dispose() { }
}
