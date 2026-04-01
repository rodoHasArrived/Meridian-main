using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Services;
using CredentialFieldInfo = Meridian.Contracts.Api.CredentialFieldInfo;
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
    private readonly WpfServices.CredentialService _credentialService;
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
        _credentialService = credentialService;
        _notificationService = notificationService;

        EditCredentialCommand = new RelayCommand(BeginEdit, () => SelectedCredential != null);
        RemoveCredentialCommand = new RelayCommand(() => _ = RemoveCredentialAsync(), () => SelectedCredential != null);
        TestCredentialCommand = new RelayCommand(() => _ = TestSelectedCredentialAsync(), () => SelectedCredential != null);
        TestAllCredentialsCommand = new RelayCommand(() => _ = TestAllCredentialsAsync());
        SaveCredentialCommand = new RelayCommand(() => _ = SaveCredentialAsync());
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    public void LoadCredentials()
    {
        Credentials.Clear();
        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var statuses = SettingsConfigurationService.Instance.GetProviderCredentialStatuses();

        foreach (var provider in catalog)
        {
            var status = statuses.FirstOrDefault(s => s.ProviderId == provider.Id);
            var state = status?.State ?? CredentialState.NotRequired;

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
        if (SelectedCredential is null) return;
        EditFields.Clear();
        IsTestResultVisible = false;

        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var provider = catalog.FirstOrDefault(p => p.Id == SelectedCredential.ProviderId);
        if (provider is null) return;

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
                var existing = GetConfiguredEnvironmentValue(field) ?? string.Empty;
                var isSecret = field.DisplayName.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                    || field.Name.Contains("key", StringComparison.OrdinalIgnoreCase);

                EditFields.Add(new CredentialFieldViewModel
                {
                    Label = field.DisplayName,
                    EnvVarName = envVar,
                    IsSecret = isSecret,
                    Value = existing
                });
            }
        }

        IsEditPanelVisible = true;
    }

    private async Task SaveCredentialAsync()
    {
        if (SelectedCredential is null) return;

        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var provider = catalog.FirstOrDefault(p => p.Id == SelectedCredential.ProviderId);

        if (provider is null || provider.CredentialFields.Length == 0)
        {
            IsEditPanelVisible = false;
            return;
        }

        IsBusy = true;
        try
        {
            foreach (var field in EditFields)
            {
                if (string.IsNullOrEmpty(field.EnvVarName)) continue;
                Environment.SetEnvironmentVariable(field.EnvVarName, field.Value, EnvironmentVariableTarget.User);
            }

            PersistToVault(provider.Id);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsEditPanelVisible = false;
                LoadCredentials();
            });

            _notificationService.ShowNotification(
                "Credentials Saved",
                $"Credentials for {SelectedCredential.DisplayName} have been saved.",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Save Failed",
                $"Could not save credentials: {ex.Message}",
                NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PersistToVault(string providerId)
    {
        switch (providerId)
        {
            case "alpaca":
            {
                var keyId = EditFields.FirstOrDefault(f =>
                    f.EnvVarName.Contains("KEYID", StringComparison.OrdinalIgnoreCase) ||
                    f.EnvVarName.Contains("KEY_ID", StringComparison.OrdinalIgnoreCase))?.Value;
                var secret = EditFields.FirstOrDefault(f =>
                    f.EnvVarName.Contains("SECRET", StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(keyId) && !string.IsNullOrWhiteSpace(secret))
                    _credentialService.SaveAlpacaCredentials(keyId, secret);
                break;
            }
            case "nasdaq":
            case "nasdaqdatalink":
            {
                var key = EditFields.FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(key))
                    _credentialService.SaveNasdaqApiKey(key);
                break;
            }
            default:
            {
                var key = EditFields.FirstOrDefault()?.Value;
                if (!string.IsNullOrWhiteSpace(key))
                    _credentialService.SaveApiKey($"Meridian.{providerId}", key);
                break;
            }
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
        if (SelectedCredential is null) return;
        IsBusy = true;
        try
        {
            var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
            var provider = catalog.FirstOrDefault(p => p.Id == SelectedCredential.ProviderId);
            if (provider is not null)
            {
                foreach (var field in provider.CredentialFields)
                {
                    foreach (var envVar in field.AllEnvironmentVariables)
                    {
                        Environment.SetEnvironmentVariable(envVar, null, EnvironmentVariableTarget.User);
                    }
                }
            }

            RemoveFromVault(SelectedCredential.ProviderId);

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                IsEditPanelVisible = false;
                IsTestResultVisible = false;
                LoadCredentials();
            });

            _notificationService.ShowNotification(
                "Credentials Removed",
                $"Credentials for {SelectedCredential.DisplayName} have been removed.",
                NotificationType.Info);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification(
                "Remove Failed",
                $"Could not remove credentials: {ex.Message}",
                NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RemoveFromVault(string providerId)
    {
        switch (providerId)
        {
            case "alpaca":
                _credentialService.RemoveAlpacaCredentials();
                break;
            default:
                if (_credentialService.HasCredential($"Meridian.{providerId}"))
                    _credentialService.RemoveCredential($"Meridian.{providerId}");
                break;
        }
    }

    private async Task TestSelectedCredentialAsync()
    {
        if (SelectedCredential is null) return;

        SelectedCredential.IsTesting = true;
        IsTestResultVisible = true;
        TestResultText = $"Testing {SelectedCredential.DisplayName}…";
        TestResultColor = "#AABCCD";

        await Task.Delay(600, CancellationToken.None).ConfigureAwait(false);

        var catalog = SettingsConfigurationService.Instance.GetProviderCatalog();
        var provider = catalog.FirstOrDefault(p => p.Id == SelectedCredential.ProviderId);

        bool success = provider is null || provider.CredentialFields.Length == 0
            || provider.CredentialFields
                .Where(field => field.Required)
                .All(HasConfiguredEnvironmentValue);

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SelectedCredential.IsTesting = false;
            if (success)
            {
                TestResultText = $"✓ {SelectedCredential.DisplayName} credentials are present and ready.";
                TestResultColor = "#3FB950";
                SelectedCredential.StatusText = "Configured";
                SelectedCredential.StatusColor = "#3FB950";
            }
            else
            {
                TestResultText = $"✗ {SelectedCredential.DisplayName} credentials are missing or incomplete.";
                TestResultColor = "#F85149";
                SelectedCredential.StatusText = "Missing";
                SelectedCredential.StatusColor = "#F85149";
            }
        });
    }

    private async Task TestAllCredentialsAsync()
    {
        IsBusy = true;
        StatusMessage = "Testing all credentials…";
        StatusMessageColor = "#AABCCD";

        foreach (var cred in Credentials.Where(c => c.RequiresCredentials).ToList())
        {
            SelectedCredential = cred;
            await TestSelectedCredentialAsync().ConfigureAwait(false);
        }

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var ok = Credentials.Count(c => c.StatusText == "Configured");
            var total = Credentials.Count(c => c.RequiresCredentials);
            StatusMessage = $"{ok} of {total} providers verified";
            StatusMessageColor = ok == total ? "#3FB950" : "#D29922";
            IsBusy = false;
        });
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
            {
                return value;
            }
        }

        return null;
    }

    public void Dispose() { }
}
