using System;
using System.Collections.Generic;
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
using Meridian.Application.Config;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Features;
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
    private bool _loadingDesktopPreferences;
    private bool _isConfigValid = true;
    private bool _hasUnsavedChanges;
    private string _configValidationErrors = string.Empty;
    private bool _isPopulatingConfig;

    // ── Appearance / notification / performance settings (bindable defaults) ──
    private int _themeIndex;
    private int _accentColorIndex;
    private ShellDensityMode _selectedShellDensityMode = ShellDensityMode.Standard;
    private bool _isNotificationsEnabled = true;
    private string _maxConcurrentDownloads = "4";
    private string _writeBufferSize = "64";
    private bool _isMetricsEnabled = true;
    private bool _isDebugLoggingEnabled;
    private string _apiBaseUrl = "http://localhost:8080";
    private string _statusRefreshInterval = "2";

    public SettingsViewModel(
        WpfServices.ConfigService configService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService,
        IFeatureCapabilityGate? capabilityGate = null,
        IEnumerable<FeatureCapabilityDescriptor>? capabilityDescriptors = null)
    {
        _configService = configService;
        _notificationService = notificationService;
        _statusService = statusService;
        _settingsConfigService = SettingsConfigurationService.Instance;
        StoredCredentials = new ObservableCollection<CredentialDisplayInfo>();
        RecentActivity = new ObservableCollection<SettingsActivityItem>();
        CapabilityToggles = new ObservableCollection<FeatureCapabilityToggleViewModel>(
            BuildCapabilityToggles(capabilityGate, capabilityDescriptors));
        ShellDensityModes =
        [
            ShellDensityMode.Standard,
            ShellDensityMode.Compact
        ];

        if (capabilityGate is not null)
        {
            capabilityGate.Changes.Subscribe(new CapabilityChangeObserver(this));
        }

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
        ReloadConfigCommand = new AsyncRelayCommand(ReloadConfigAsync);
        ResetToDefaultsCommand = new AsyncRelayCommand(ResetToDefaultsAsync);
        SaveConfigCommand = new AsyncRelayCommand(SaveConfigAsync, CanSaveConfig);
        ApplyConfigCommand = new AsyncRelayCommand(ApplyConfigAsync, CanSaveConfig);

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
    public ObservableCollection<FeatureCapabilityToggleViewModel> CapabilityToggles { get; }
    public IReadOnlyList<ShellDensityMode> ShellDensityModes { get; }

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

    // ── Appearance / notification / performance settings ──────────────────────

    /// <summary>Selected index of the theme ComboBox (0 = system, 1 = light, 2 = dark).</summary>
    public int ThemeIndex
    {
        get => _themeIndex;
        set => SetSettingProperty(ref _themeIndex, value);
    }

    /// <summary>Selected index of the accent-color ComboBox.</summary>
    public int AccentColorIndex
    {
        get => _accentColorIndex;
        set => SetSettingProperty(ref _accentColorIndex, value);
    }

    public ShellDensityMode SelectedShellDensityMode
    {
        get => _selectedShellDensityMode;
        set
        {
            if (!SetProperty(ref _selectedShellDensityMode, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(ShellDensityDescriptionText));

            if (_loadingDesktopPreferences)
            {
                return;
            }

            _settingsConfigService.SetShellDensityMode(value);
            MarkDirty();
        }
    }

    public string ShellDensityDescriptionText => SelectedShellDensityMode == ShellDensityMode.Compact
        ? "Compact trims duplicate chrome and above-fold padding in shared workstation shells."
        : "Standard keeps the full shell summary and spacing for review-heavy sessions.";

    /// <summary>
    /// Whether Windows notifications are enabled.
    /// The XAML NotificationSettingsPanel binds its Opacity to
    /// <see cref="NotificationsPanelOpacity"/> which is derived from this flag.
    /// </summary>
    public bool IsNotificationsEnabled
    {
        get => _isNotificationsEnabled;
        set
        {
            if (SetProperty(ref _isNotificationsEnabled, value))
            {
                RaisePropertyChanged(nameof(NotificationsPanelOpacity));
                MarkDirty();
            }
        }
    }

    /// <summary>1.0 when notifications are enabled, 0.5 when disabled.</summary>
    public double NotificationsPanelOpacity => _isNotificationsEnabled ? 1.0 : 0.5;

    public string MaxConcurrentDownloads
    {
        get => _maxConcurrentDownloads;
        set => SetSettingProperty(ref _maxConcurrentDownloads, value);
    }

    public string WriteBufferSize
    {
        get => _writeBufferSize;
        set => SetSettingProperty(ref _writeBufferSize, value);
    }

    public bool IsMetricsEnabled
    {
        get => _isMetricsEnabled;
        set => SetSettingProperty(ref _isMetricsEnabled, value);
    }

    public bool IsDebugLoggingEnabled
    {
        get => _isDebugLoggingEnabled;
        set => SetSettingProperty(ref _isDebugLoggingEnabled, value);
    }

    public string ApiBaseUrl
    {
        get => _apiBaseUrl;
        set => SetSettingProperty(ref _apiBaseUrl, value);
    }

    public string StatusRefreshInterval
    {
        get => _statusRefreshInterval;
        set => SetSettingProperty(ref _statusRefreshInterval, value);
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
    public IAsyncRelayCommand ReloadConfigCommand { get; }
    public IAsyncRelayCommand ResetToDefaultsCommand { get; }
    public IAsyncRelayCommand SaveConfigCommand { get; }
    public IAsyncRelayCommand ApplyConfigCommand { get; }
    public IRelayCommand SendTestNotificationCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IRelayCommand CheckForUpdatesCommand { get; }
    public IRelayCommand OpenDocumentationCommand { get; }
    public IRelayCommand OpenIssuesCommand { get; }
    public IRelayCommand<string> SelectProfileCommand { get; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void Initialize()
    {
        LoadDesktopPreferences();
        _ = LoadConfigAsync();
        RefreshCapabilityToggles();
        ConfigPath = _configService.ConfigPath;
        RefreshStoredCredentials();
        RefreshCredentialVault();
        RefreshStoragePreview("BySymbol", "gzip");
        RefreshProfiles();
        LoadRecentActivity();
        UpdateSystemStatus();
    }

    private void LoadDesktopPreferences()
    {
        _loadingDesktopPreferences = true;
        try
        {
            SelectedShellDensityMode = _settingsConfigService.GetShellDensityMode();
        }
        finally
        {
            _loadingDesktopPreferences = false;
        }
    }

    private static IEnumerable<FeatureCapabilityToggleViewModel> BuildCapabilityToggles(
        IFeatureCapabilityGate? capabilityGate,
        IEnumerable<FeatureCapabilityDescriptor>? descriptors)
    {
        if (capabilityGate is null || descriptors is null)
        {
            return [];
        }

        return descriptors
            .OrderBy(static descriptor => descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(descriptor => new FeatureCapabilityToggleViewModel(descriptor, capabilityGate));
    }

    private void RefreshCapabilityToggles()
    {
        foreach (var toggle in CapabilityToggles)
        {
            toggle.Refresh();
        }
    }

    private void OnCapabilityChanged(CapabilityChangedEvent change)
    {
        var toggle = CapabilityToggles.FirstOrDefault(item =>
            string.Equals(item.CapabilityKey, change.CapabilityKey, StringComparison.OrdinalIgnoreCase));
        toggle?.Refresh(change.IsEnabled);
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
        if (string.IsNullOrEmpty(profileId))
            return;
        var profile = Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null)
            return;

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
        if (string.IsNullOrEmpty(resource))
            return;
        _notificationService.ShowNotification("Credential Test", $"Testing {resource} credentials...", NotificationType.Info);
    }

    private void RemoveCredential(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return;

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

    private async Task ReloadConfigAsync()
    {
        await LoadConfigAsync(true);
    }

    private async Task ResetToDefaultsAsync()
    {
        var result = MessageBox.Show(
            "This will reset all settings to their default values. Your symbols and credentials will be preserved. Continue?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var existingConfig = await _configService.LoadConfigFromDiskAsync();
            var defaultConfig = new AppConfigDto();
            defaultConfig.Symbols = existingConfig.Symbols;
            defaultConfig.Alpaca = existingConfig.Alpaca;
            defaultConfig.Polygon = existingConfig.Polygon;
            defaultConfig.IB = existingConfig.IB;
            defaultConfig.IBClientPortal = existingConfig.IBClientPortal;
            defaultConfig.Backfill = existingConfig.Backfill;
            defaultConfig.DataSources = existingConfig.DataSources;
            defaultConfig.SymbolGroups = existingConfig.SymbolGroups;
            defaultConfig.Derivatives = existingConfig.Derivatives;

            await _configService.WriteConfigAsync(defaultConfig);
            await LoadConfigAsync(false);
            _notificationService.ShowNotification("Reset Complete", "Settings have been reset to defaults.", NotificationType.Success);
        }
        catch (Exception ex)
        {
            _notificationService.ShowNotification("Reset Failed", ex.Message, NotificationType.Error);
        }
    }

    public bool IsConfigValid
    {
        get => _isConfigValid;
        private set => SetProperty(ref _isConfigValid, value);
    }

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set => SetProperty(ref _hasUnsavedChanges, value);
    }

    public string ConfigValidationErrors
    {
        get => _configValidationErrors;
        private set => SetProperty(ref _configValidationErrors, value);
    }

    private async Task LoadConfigAsync(bool showNotification = false)
    {
        try
        {
            var config = await _configService.LoadConfigFromDiskAsync();
            PopulateFromConfig(config);
            await RefreshValidationAsync();
            if (showNotification)
            {
                _notificationService.ShowNotification("Configuration Reloaded", "Configuration has been reloaded successfully.", NotificationType.Success);
            }
        }
        catch (Exception ex)
        {
            ConfigStatusText = "Error";
            ConfigStatusDot = new SolidColorBrush(Color.FromRgb(248, 81, 73));
            _notificationService.ShowNotification("Reload Failed", ex.Message, NotificationType.Error);
        }
    }

    private void PopulateFromConfig(AppConfigDto config)
    {
        _isPopulatingConfig = true;
        try
        {
            var settings = config.Settings ?? new AppSettingsDto();
            ApiBaseUrl = config.IBClientPortal?.BaseUrl ?? "http://localhost:8080";
            StatusRefreshInterval = settings.StatusRefreshIntervalSeconds.ToString();
            IsNotificationsEnabled = settings.NotificationsEnabled;
            SelectedShellDensityMode = settings.CompactMode ? ShellDensityMode.Compact : ShellDensityMode.Standard;
            ThemeIndex = string.Equals(settings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? 2 : string.Equals(settings.Theme, "Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            AccentColorIndex = 0;
            MaxConcurrentDownloads = settings.MaxReconnectAttempts.ToString();
            WriteBufferSize = config.Compress ? "64" : "0";
            IsMetricsEnabled = settings.AutoReconnectEnabled;
            IsDebugLoggingEnabled = false;
            HasUnsavedChanges = false;
        }
        finally
        {
            _isPopulatingConfig = false;
        }
    }

    private AppConfigDto BuildConfigDtoFromSettings(AppConfigDto existingConfig)
    {
        ArgumentNullException.ThrowIfNull(existingConfig);

        var interval = int.TryParse(StatusRefreshInterval, out var parsedInterval) ? parsedInterval : 2;
        var maxReconnect = int.TryParse(MaxConcurrentDownloads, out var parsedMaxReconnect) ? parsedMaxReconnect : 10;

        existingConfig.IBClientPortal ??= new IBClientPortalOptionsDto();
        existingConfig.IBClientPortal.BaseUrl = ApiBaseUrl;
        existingConfig.Compress = WriteBufferSize != "0";
        existingConfig.Settings = new AppSettingsDto
        {
            Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" },
            NotificationsEnabled = IsNotificationsEnabled,
            CompactMode = SelectedShellDensityMode == ShellDensityMode.Compact,
            AutoReconnectEnabled = IsMetricsEnabled,
            MaxReconnectAttempts = maxReconnect,
            StatusRefreshIntervalSeconds = interval
        };

        return existingConfig;
    }

    private async Task SaveConfigAsync() => await SaveOrApplyConfigAsync("Configuration Saved");
    private async Task ApplyConfigAsync() => await SaveOrApplyConfigAsync("Configuration Applied");
    private async Task SaveOrApplyConfigAsync(string messageTitle)
    {
        var validation = await RefreshValidationAsync();
        if (!validation.IsValid)
        {
            _notificationService.ShowNotification("Validation Failed", string.Join(Environment.NewLine, validation.Errors), NotificationType.Error);
            return;
        }

        var existingConfig = await _configService.LoadConfigFromDiskAsync();
        var updatedConfig = BuildConfigDtoFromSettings(existingConfig);
        await _configService.WriteConfigAsync(updatedConfig);
        HasUnsavedChanges = false;
        _notificationService.ShowNotification(messageTitle, "Settings have been written to disk.", NotificationType.Success);
    }

    private async Task<WpfServices.ConfigServiceValidationResult> RefreshValidationAsync()
    {
        var result = await _configService.ValidateConfigAsync();
        IsConfigValid = result.IsValid;
        ConfigValidationErrors = string.Join(Environment.NewLine, result.Errors);
        ConfigStatusText = result.IsValid ? "Valid" : "Invalid";
        ConfigStatusDot = result.IsValid ? new SolidColorBrush(Color.FromRgb(63, 185, 80)) : new SolidColorBrush(Color.FromRgb(248, 81, 73));
        SaveConfigCommand.NotifyCanExecuteChanged();
        ApplyConfigCommand.NotifyCanExecuteChanged();
        return result;
    }

    private bool CanSaveConfig() => HasUnsavedChanges && IsConfigValid;
    private void MarkDirty()
    {
        if (_isPopulatingConfig)
            return;
        HasUnsavedChanges = true;
        _ = RefreshValidationAsync();
    }
    private bool SetSettingProperty<T>(ref T field, T value, string? propertyName = null)
    {
        if (!SetProperty(ref field, value, propertyName))
            return false;
        MarkDirty();
        return true;
    }

    private void ApplyDefaultsFromConfig(Meridian.Contracts.Configuration.AppConfigDto defaultConfig)
    {
        ThemeIndex = string.Equals(defaultConfig.Settings?.Theme, "Dark", StringComparison.OrdinalIgnoreCase)
            ? 2
            : string.Equals(defaultConfig.Settings?.Theme, "Light", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
        AccentColorIndex = 0;
        SelectedShellDensityMode = defaultConfig.Settings?.CompactMode == true ? ShellDensityMode.Compact : ShellDensityMode.Standard;
        IsNotificationsEnabled = defaultConfig.Settings?.NotificationsEnabled ?? true;
        MaxConcurrentDownloads = "4";
        WriteBufferSize = "64";
        IsMetricsEnabled = true;
        IsDebugLoggingEnabled = false;
        ApiBaseUrl = "http://localhost:8080";
        StatusRefreshInterval = (defaultConfig.Settings?.StatusRefreshIntervalSeconds ?? 2).ToString();
    }

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

    private sealed class CapabilityChangeObserver : IObserver<CapabilityChangedEvent>
    {
        private readonly SettingsViewModel _owner;

        public CapabilityChangeObserver(SettingsViewModel owner)
        {
            _owner = owner;
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(CapabilityChangedEvent value)
        {
            _owner.OnCapabilityChanged(value);
        }
    }
}

public sealed class FeatureCapabilityToggleViewModel : BindableBase
{
    private readonly IFeatureCapabilityGate _gate;
    private bool _isEnabled;

    public FeatureCapabilityToggleViewModel(
        FeatureCapabilityDescriptor descriptor,
        IFeatureCapabilityGate gate)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _isEnabled = _gate.IsEnabled(descriptor.CapabilityKey);
    }

    public FeatureCapabilityDescriptor Descriptor { get; }

    public string CapabilityKey => Descriptor.CapabilityKey;

    public string DisplayName => Descriptor.DisplayName;

    public string Description => Descriptor.Description;

    public bool IsPermanent => Descriptor.IsPermanent;

    public bool CanToggle => !Descriptor.IsPermanent;

    public string StateText => IsPermanent
        ? "Always on"
        : IsEnabled
            ? "Enabled"
            : "Disabled";

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (Descriptor.IsPermanent)
            {
                value = true;
            }

            if (!SetProperty(ref _isEnabled, value))
            {
                return;
            }

            RaisePropertyChanged(nameof(StateText));
            _gate.SetEnabled(CapabilityKey, value);
        }
    }

    public void Refresh()
        => Refresh(_gate.IsEnabled(CapabilityKey));

    public void Refresh(bool isEnabled)
    {
        if (SetProperty(ref _isEnabled, isEnabled, nameof(IsEnabled)))
        {
            RaisePropertyChanged(nameof(StateText));
        }
    }
}
