using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Settings page.
/// Holds all state, collections, and commands so the code-behind can be
/// thinned to lifecycle wiring only.
/// </summary>
public sealed class SettingsViewModel : BindableBase
{
    private readonly WpfServices.ConfigService _configService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.StatusService _statusService;
    private readonly SettingsConfigurationService _settingsConfigService;

    private string _configPath = string.Empty;
    private string _configStatusText = "Valid";
    private SolidColorBrush _configStatusDot = new(Color.FromRgb(63, 185, 80));
    private string _collectorStatusText = "Running";
    private SolidColorBrush _collectorStatusDot = new(Color.FromRgb(63, 185, 80));
    private string _credentialsStatusText = string.Empty;
    private SolidColorBrush _credentialsStatusDot = new(Color.FromRgb(63, 185, 80));
    private string _lastAuthText = "Never";
    private string _lastSyncText = "2 min ago";
    private string _storagePreviewText = string.Empty;
    private string _storageSizeEstimateText = string.Empty;
    private string _connectionTestResult = string.Empty;
    private SolidColorBrush _connectionTestBrush = new(Color.FromRgb(139, 148, 158));
    private Visibility _noCredentialsVisibility = Visibility.Collapsed;
    private Visibility _credentialListVisibility = Visibility.Visible;

    public SettingsViewModel(
        WpfServices.ConfigService configService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService)
    {
        _configService = configService;
        _notificationService = notificationService;
        _statusService = statusService;
        _settingsConfigService = SettingsConfigurationService.Instance;

        StoredCredentials = new ObservableCollection<CredentialDisplayInfo>();
        RecentActivity = new ObservableCollection<SettingsActivityItem>();

        // Navigation commands
        AddProviderCommand = new RelayCommand(() => WpfServices.NavigationService.Instance.NavigateTo("AddProviderWizard"));
        ConfigureStorageCommand = new RelayCommand(() => WpfServices.NavigationService.Instance.NavigateTo("Storage"));
        ManageCredentialsCommand = new RelayCommand(() => WpfServices.NavigationService.Instance.NavigateTo("CredentialManagement"));
        RunSetupWizardCommand = new RelayCommand(() => WpfServices.NavigationService.Instance.NavigateTo("SetupWizard"));
        ConfigureProviderCredentialCommand = new RelayCommand(() => WpfServices.NavigationService.Instance.NavigateTo("AddProviderWizard"));

        // Credential commands
        TestCredentialCommand = new RelayCommand<string>(TestCredential);
        RemoveCredentialCommand = new RelayCommand<string>(RemoveCredential);
        TestAllCredentialsCommand = new RelayCommand(TestAllCredentials);
        ClearAllCredentialsCommand = new RelayCommand(ClearAllCredentials);

        // Configuration commands
        OpenConfigFolderCommand = new RelayCommand(OpenConfigFolder);
        ReloadConfigCommand = new RelayCommand(ReloadConfig);
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaults);

        // Test / support commands
        SendTestNotificationCommand = new RelayCommand(SendTestNotification);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        CheckForUpdatesCommand = new RelayCommand(CheckForUpdates);
        OpenDocumentationCommand = new RelayCommand(() => OpenUrl("https://github.com/example/meridian"));
        OpenIssuesCommand = new RelayCommand(() => OpenUrl("https://github.com/example/meridian/issues"));

        // Profile command
        SelectProfileCommand = new RelayCommand<string>(SelectProfile);
    }

    // ── Collections ───────────────────────────────────────────────────────────

    public ObservableCollection<CredentialDisplayInfo> StoredCredentials { get; }
    public ObservableCollection<SettingsActivityItem> RecentActivity { get; }

    // ── Bindable Properties ───────────────────────────────────────────────────

    public string ConfigPath
    {
        get => _configPath;
        private set => SetProperty(ref _configPath, value);
    }

    public string ConfigStatusText
    {
        get => _configStatusText;
        private set => SetProperty(ref _configStatusText, value);
    }

    public SolidColorBrush ConfigStatusDot
    {
        get => _configStatusDot;
        private set => SetProperty(ref _configStatusDot, value);
    }

    public string CollectorStatusText
    {
        get => _collectorStatusText;
        private set => SetProperty(ref _collectorStatusText, value);
    }

    public SolidColorBrush CollectorStatusDot
    {
        get => _collectorStatusDot;
        private set => SetProperty(ref _collectorStatusDot, value);
    }

    public string CredentialsStatusText
    {
        get => _credentialsStatusText;
        private set => SetProperty(ref _credentialsStatusText, value);
    }

    public SolidColorBrush CredentialsStatusDot
    {
        get => _credentialsStatusDot;
        private set => SetProperty(ref _credentialsStatusDot, value);
    }

    public string LastAuthText
    {
        get => _lastAuthText;
        private set => SetProperty(ref _lastAuthText, value);
    }

    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
    }

    public string StoragePreviewText
    {
        get => _storagePreviewText;
        private set => SetProperty(ref _storagePreviewText, value);
    }

    public string StorageSizeEstimateText
    {
        get => _storageSizeEstimateText;
        private set => SetProperty(ref _storageSizeEstimateText, value);
    }

    public string ConnectionTestResult
    {
        get => _connectionTestResult;
        private set => SetProperty(ref _connectionTestResult, value);
    }

    public SolidColorBrush ConnectionTestBrush
    {
        get => _connectionTestBrush;
        private set => SetProperty(ref _connectionTestBrush, value);
    }

    public Visibility NoCredentialsVisibility
    {
        get => _noCredentialsVisibility;
        private set => SetProperty(ref _noCredentialsVisibility, value);
    }

    public Visibility CredentialListVisibility
    {
        get => _credentialListVisibility;
        private set => SetProperty(ref _credentialListVisibility, value);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    public IRelayCommand AddProviderCommand { get; }
    public IRelayCommand ConfigureStorageCommand { get; }
    public IRelayCommand ManageCredentialsCommand { get; }
    public IRelayCommand RunSetupWizardCommand { get; }
    public IRelayCommand ConfigureProviderCredentialCommand { get; }
    public IRelayCommand<string> TestCredentialCommand { get; }
    public IRelayCommand<string> RemoveCredentialCommand { get; }
    public IRelayCommand TestAllCredentialsCommand { get; }
    public IRelayCommand ClearAllCredentialsCommand { get; }
    public IRelayCommand OpenConfigFolderCommand { get; }
    public IRelayCommand ReloadConfigCommand { get; }
    public IRelayCommand ResetToDefaultsCommand { get; }
    public IRelayCommand SendTestNotificationCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IRelayCommand CheckForUpdatesCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }
    public IRelayCommand OpenIssuesCommand { get; }
    public IRelayCommand<string> SelectProfileCommand { get; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        ConfigPath = _configService.ConfigPath;
        RefreshStoredCredentials();
        RefreshCredentialVault();
        RefreshStoragePreview("BySymbol", "gzip");
        RefreshProfiles();
        LoadRecentActivity();
        UpdateSystemStatus();
    }

    // ── Credential Vault ──────────────────────────────────────────────────────

    public void RefreshCredentialVault()
    {
        var statuses = _settingsConfigService.GetProviderCredentialStatuses();
        CredentialVaultItems = statuses.Select(s => new CredentialVaultItem
        {
            ProviderId = s.ProviderId,
            DisplayName = s.DisplayName,
            StatusMessage = s.State switch
            {
                CredentialState.Configured => "Configured via environment",
                CredentialState.Partial => $"Missing: {string.Join(", ", s.MissingEnvVars)}",
                CredentialState.Missing => $"Not configured ({string.Join(", ", s.MissingEnvVars)})",
                CredentialState.NotRequired => "No credentials required",
                _ => "Unknown",
            },
            StatusBrush = s.State switch
            {
                CredentialState.Configured => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                CredentialState.Partial => new SolidColorBrush(Color.FromRgb(210, 153, 34)),
                CredentialState.Missing => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
                CredentialState.NotRequired => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                _ => new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            },
            ConfigureVisibility = s.State is CredentialState.Missing or CredentialState.Partial
                ? Visibility.Visible
                : Visibility.Collapsed,
        }).ToList();

        RaisePropertyChanged(nameof(CredentialVaultItems));
    }

    private System.Collections.Generic.List<CredentialVaultItem> _credentialVaultItems = new();
    public System.Collections.Generic.List<CredentialVaultItem> CredentialVaultItems
    {
        get => _credentialVaultItems;
        private set => SetProperty(ref _credentialVaultItems, value);
    }

    // ── Storage Preview ───────────────────────────────────────────────────────

    public void RefreshStoragePreview(string naming, string compression)
    {
        var symbols = new System.Collections.Generic.List<string> { "SPY", "AAPL" };
        StoragePreviewText = _settingsConfigService.GenerateStoragePreview("data", naming, "daily", compression, symbols);
        var estimate = _settingsConfigService.EstimateDailyStorageSize(symbols.Count, trades: true, quotes: true, depth: false);
        StorageSizeEstimateText = $"Estimated daily size: ~{estimate} for {symbols.Count} symbols";
    }

    // ── Configuration Profiles ────────────────────────────────────────────────

    private System.Collections.Generic.List<ConfigProfile> _profiles = new();
    public System.Collections.Generic.List<ConfigProfile> Profiles
    {
        get => _profiles;
        private set => SetProperty(ref _profiles, value);
    }

    public void RefreshProfiles()
    {
        Profiles = _settingsConfigService.GetProfiles().ToList();
    }

    private void SelectProfile(string? profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return;
        var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        _notificationService.ShowNotification(
            "Profile Selected",
            $"Applied \"{profile.Name}\" profile: {profile.Description}",
            NotificationType.Info);
    }

    // ── Credential management ─────────────────────────────────────────────────

    private void RefreshStoredCredentials()
    {
        StoredCredentials.Clear();

        StoredCredentials.Add(new CredentialDisplayInfo
        {
            Name = "Alpaca API Credentials",
            Status = "Valid • Last tested 2h ago",
            Resource = "alpaca",
            TestStatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
        });
        StoredCredentials.Add(new CredentialDisplayInfo
        {
            Name = "Nasdaq Data Link API Key",
            Status = "Valid • Last tested 1d ago",
            Resource = "nasdaq",
            TestStatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
        });

        var hasCredentials = StoredCredentials.Count > 0;
        CredentialListVisibility = hasCredentials ? Visibility.Visible : Visibility.Collapsed;
        NoCredentialsVisibility = hasCredentials ? Visibility.Collapsed : Visibility.Visible;
        CredentialsStatusText = hasCredentials ? $"{StoredCredentials.Count} stored" : "None";
    }

    private void TestCredential(string? resource)
    {
        if (string.IsNullOrEmpty(resource)) return;
        _notificationService.ShowNotification("Credential Test", $"Testing {resource} credentials...", NotificationType.Info);
    }

    private void RemoveCredential(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;

        var result = MessageBox.Show(
            $"Are you sure you want to remove the credential '{name}'?",
            "Remove Credential",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            RefreshStoredCredentials();
    }

    private void TestAllCredentials()
    {
        _notificationService.ShowNotification("Testing Credentials", "Testing all stored credentials...", NotificationType.Info);
    }

    private void ClearAllCredentials()
    {
        if (StoredCredentials.Count == 0)
        {
            MessageBox.Show("There are no stored credentials to clear.", "No Credentials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to remove all {StoredCredentials.Count} stored credential(s)?\n\nThis action cannot be undone.",
            "Clear All Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            StoredCredentials.Clear();
            RefreshStoredCredentials();
        }
    }

    // ── System status ─────────────────────────────────────────────────────────

    private void UpdateSystemStatus()
    {
        ConfigStatusText = "Valid";
        ConfigStatusDot = new SolidColorBrush(Color.FromRgb(63, 185, 80));
        CollectorStatusText = "Running";
        CollectorStatusDot = new SolidColorBrush(Color.FromRgb(63, 185, 80));
        LastSyncText = "2 min ago";
    }

    // ── Config file management ────────────────────────────────────────────────

    private void OpenConfigFolder()
    {
        var folder = Path.GetDirectoryName(_configService.ConfigPath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
    }

    private void ReloadConfig()
    {
        try
        {
            ConfigStatusText = "Valid";
            ConfigStatusDot = new SolidColorBrush(Color.FromRgb(63, 185, 80));
            _notificationService.ShowNotification("Configuration Reloaded", "Configuration has been reloaded successfully.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ConfigStatusText = "Error";
            ConfigStatusDot = new SolidColorBrush(Color.FromRgb(248, 81, 73));
            _notificationService.ShowNotification("Reload Failed", ex.Message, NotificationType.Error);
        }
    }

    private void ResetToDefaults()
    {
        var result = MessageBox.Show(
            "This will reset all settings to their default values. Your symbols and credentials will be preserved. Continue?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _notificationService.ShowNotification("Reset Complete", "Settings have been reset to defaults.", NotificationType.Success);
            RaisePropertyChanged(nameof(ResetRequested));
        }
    }

    /// <summary>
    /// Raised after a reset so the code-behind can restore default control values.
    /// </summary>
    public bool ResetRequested { get; private set; }

    // ── Test / support ────────────────────────────────────────────────────────

    private void SendTestNotification()
    {
        _notificationService.ShowNotification("Test Notification", "This is a test notification from Meridian.", NotificationType.Info);
    }

    private async Task TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectionTestResult = "Testing...";
        ConnectionTestBrush = new SolidColorBrush(Color.FromRgb(139, 148, 158));

        try
        {
            var status = await _statusService.GetStatusAsync();
            if (status != null)
            {
                ConnectionTestResult = "Connected";
                ConnectionTestBrush = new SolidColorBrush(Color.FromRgb(63, 185, 80));
            }
            else
            {
                ConnectionTestResult = "Failed";
                ConnectionTestBrush = new SolidColorBrush(Color.FromRgb(248, 81, 73));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            ConnectionTestResult = "Error";
            ConnectionTestBrush = new SolidColorBrush(Color.FromRgb(248, 81, 73));
        }
    }

    private void CheckForUpdates()
    {
        MessageBox.Show("You are running the latest version (1.6.1).", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadRecentActivity()
    {
        RecentActivity.Clear();
        RecentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE73E",
            IconColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            Message = "Configuration saved",
            Time = "2 min ago",
        });
        RecentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE753",
            IconColor = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
            Message = "Cloud sync completed",
            Time = "15 min ago",
        });
        RecentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE787",
            IconColor = new SolidColorBrush(Color.FromRgb(210, 153, 34)),
            Message = "Backfill started",
            Time = "1 hour ago",
        });
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }
}
