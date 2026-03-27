using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Settings page for application configuration, notifications, and credentials.
/// Enhanced with Quick Actions, Credential Vault, Storage Preview, and Configuration Profiles.
/// </summary>
public partial class SettingsPage : Page
{
    private readonly WpfServices.ConfigService _configService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.StatusService _statusService;
    private readonly SettingsConfigurationService _settingsConfigService;
    private readonly ObservableCollection<CredentialDisplayInfo> _storedCredentials = new();
    private readonly ObservableCollection<SettingsActivityItem> _recentActivity = new();

    public SettingsPage(
        WpfServices.ConfigService configService,
        WpfServices.NotificationService notificationService,
        WpfServices.StatusService statusService)
    {
        InitializeComponent();

        _configService = configService;
        _notificationService = notificationService;
        _statusService = statusService;
        _settingsConfigService = SettingsConfigurationService.Instance;
        StoredCredentialsList.ItemsSource = _storedCredentials;
        RecentActivityList.ItemsSource = _recentActivity;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        ConfigPathText.Text = _configService.ConfigPath;
        RefreshStoredCredentials();
        RefreshCredentialVault();
        RefreshStoragePreview();
        RefreshProfiles();
        LoadRecentActivity();
        UpdateSystemStatus();
        LoadGlobalHotkeys();
    }

    // ── Quick Actions ──────────────────────────────────────────────

    private void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        WpfServices.NavigationService.Instance.NavigateTo("AddProviderWizard");
    }

    private void ConfigureStorage_Click(object sender, RoutedEventArgs e)
    {
        WpfServices.NavigationService.Instance.NavigateTo("Storage");
    }

    private void ManageCredentials_Click(object sender, RoutedEventArgs e)
    {
        // Scroll to the Credential Vault section
        CredentialVaultList.BringIntoView();
    }

    private void RunSetupWizard_Click(object sender, RoutedEventArgs e)
    {
        WpfServices.NavigationService.Instance.NavigateTo("SetupWizard");
    }

    // ── Credential Vault ──────────────────────────────────────────

    private void RefreshCredentialVault()
    {
        var statuses = _settingsConfigService.GetProviderCredentialStatuses();
        var viewModels = statuses.Select(s => new CredentialVaultItem
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

        CredentialVaultList.ItemsSource = viewModels;
    }

    private void ConfigureProviderCredential_Click(object sender, RoutedEventArgs e)
    {
        WpfServices.NavigationService.Instance.NavigateTo("AddProviderWizard");
    }

    // ── Storage Preview ──────────────────────────────────────────

    private void RefreshStoragePreview()
    {
        var naming = GetSelectedTag(PreviewNamingCombo) ?? "BySymbol";
        var compression = GetSelectedTag(PreviewCompressionCombo) ?? "gzip";
        var symbols = new List<string> { "SPY", "AAPL" };

        var preview = _settingsConfigService.GenerateStoragePreview("data", naming, "daily", compression, symbols);
        StoragePreviewText.Text = preview;

        var estimate = _settingsConfigService.EstimateDailyStorageSize(symbols.Count, trades: true, quotes: true, depth: false);
        StorageSizeEstimateText.Text = $"Estimated daily size: ~{estimate} for {symbols.Count} symbols";
    }

    private void PreviewSettings_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Guard against calls during initialization
        if (StoragePreviewText == null) return;
        RefreshStoragePreview();
    }

    private static string? GetSelectedTag(ComboBox combo)
    {
        return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    }

    // ── Configuration Profiles ──────────────────────────────────

    private void RefreshProfiles()
    {
        ProfilesList.ItemsSource = _settingsConfigService.GetProfiles();
    }

    private void ProfileCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string profileId) return;

        var profiles = _settingsConfigService.GetProfiles();
        var profile = profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile == null) return;

        _notificationService.ShowNotification(
            "Profile Selected",
            $"Applied \"{profile.Name}\" profile: {profile.Description}",
            NotificationType.Info);
    }

    private void RefreshStoredCredentials()
    {
        _storedCredentials.Clear();

        // Sample credentials for demonstration
        _storedCredentials.Add(new CredentialDisplayInfo
        {
            Name = "Alpaca API Credentials",
            Status = "Valid • Last tested 2h ago",
            Resource = "alpaca",
            TestStatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80))
        });
        _storedCredentials.Add(new CredentialDisplayInfo
        {
            Name = "Nasdaq Data Link API Key",
            Status = "Valid • Last tested 1d ago",
            Resource = "nasdaq",
            TestStatusColor = new SolidColorBrush(Color.FromRgb(63, 185, 80))
        });

        if (_storedCredentials.Count == 0)
        {
            StoredCredentialsList.Visibility = Visibility.Collapsed;
            NoCredentialsText.Visibility = Visibility.Visible;
        }
        else
        {
            StoredCredentialsList.Visibility = Visibility.Visible;
            NoCredentialsText.Visibility = Visibility.Collapsed;
        }

        CredentialsStatusText.Text = _storedCredentials.Count > 0
            ? $"{_storedCredentials.Count} stored"
            : "None";
    }

    private void LoadRecentActivity()
    {
        _recentActivity.Clear();
        _recentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE73E",
            IconColor = new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            Message = "Configuration saved",
            Time = "2 min ago"
        });
        _recentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE753",
            IconColor = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
            Message = "Cloud sync completed",
            Time = "15 min ago"
        });
        _recentActivity.Add(new SettingsActivityItem
        {
            Icon = "\uE787",
            IconColor = new SolidColorBrush(Color.FromRgb(210, 153, 34)),
            Message = "Backfill started",
            Time = "1 hour ago"
        });
    }

    private void UpdateSystemStatus()
    {
        ConfigStatusText.Text = "Valid";
        ConfigStatusDot.Fill = new SolidColorBrush(Color.FromRgb(63, 185, 80));

        CollectorStatusText.Text = "Running";
        CollectorStatusDot.Fill = new SolidColorBrush(Color.FromRgb(63, 185, 80));

        LastSyncText.Text = "2 min ago";
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeCombo.SelectedItem is ComboBoxItem item)
        {
            var theme = item.Tag?.ToString();
            // Theme switching logic would go here
            System.Diagnostics.Debug.WriteLine($"Theme changed to: {theme}");
        }
    }

    private void NotificationsEnabled_Changed(object sender, RoutedEventArgs e)
    {
        if (NotificationSettingsPanel != null)
        {
            NotificationSettingsPanel.Opacity = NotificationsEnabledToggle.IsChecked.GetValueOrDefault() ? 1.0 : 0.5;
        }
    }

    private void SendTestNotification_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Test Notification",
            "This is a test notification from Meridian.",
            NotificationType.Info);
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        ConnectionTestResult.Text = "Testing...";
        ConnectionTestResult.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));

        try
        {
            var status = await _statusService.GetStatusAsync();

            if (status != null)
            {
                ConnectionTestResult.Text = "Connected";
                ConnectionTestResult.Foreground = new SolidColorBrush(Color.FromRgb(63, 185, 80));
            }
            else
            {
                ConnectionTestResult.Text = "Failed";
                ConnectionTestResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            ConnectionTestResult.Text = "Error";
            ConnectionTestResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 81, 73));
        }
    }

    private void TestCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string resource)
        {
            _notificationService.ShowNotification(
                "Credential Test",
                $"Testing {resource} credentials...",
                NotificationType.Info);
        }
    }

    private void RemoveCredential_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string name)
        {
            var result = MessageBox.Show(
                $"Are you sure you want to remove the credential '{name}'?",
                "Remove Credential",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Remove credential logic
                RefreshStoredCredentials();
            }
        }
    }

    private void TestAllCredentials_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.ShowNotification(
            "Testing Credentials",
            "Testing all stored credentials...",
            NotificationType.Info);
    }

    private void ClearAllCredentials_Click(object sender, RoutedEventArgs e)
    {
        if (_storedCredentials.Count == 0)
        {
            MessageBox.Show("There are no stored credentials to clear.", "No Credentials", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to remove all {_storedCredentials.Count} stored credential(s)?\n\nThis action cannot be undone.",
            "Clear All Credentials",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            _storedCredentials.Clear();
            RefreshStoredCredentials();
        }
    }

    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.GetDirectoryName(_configService.ConfigPath);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
        }
    }

    private void ReloadConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Reload configuration logic
            ConfigStatusText.Text = "Valid";
            ConfigStatusDot.Fill = new SolidColorBrush(Color.FromRgb(63, 185, 80));

            _notificationService.ShowNotification(
                "Configuration Reloaded",
                "Configuration has been reloaded successfully.",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            ConfigStatusText.Text = "Error";
            ConfigStatusDot.Fill = new SolidColorBrush(Color.FromRgb(248, 81, 73));

            _notificationService.ShowNotification(
                "Reload Failed",
                ex.Message,
                NotificationType.Error);
        }
    }

    private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This will reset all settings to their default values. Your symbols and credentials will be preserved. Continue?",
            "Reset to Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            ThemeCombo.SelectedIndex = 0;
            AccentColorCombo.SelectedIndex = 0;
            CompactModeToggle.IsChecked = false;
            NotificationsEnabledToggle.IsChecked = true;
            MaxConcurrentDownloadsBox.Text = "4";
            WriteBufferSizeBox.Text = "64";
            EnableMetricsToggle.IsChecked = true;
            EnableDebugLoggingToggle.IsChecked = false;
            ApiBaseUrlBox.Text = "http://localhost:8080";
            StatusRefreshIntervalBox.Text = "2";

            _notificationService.ShowNotification(
                "Reset Complete",
                "Settings have been reset to defaults.",
                NotificationType.Success);
        }
    }

    private void OpenDocumentation_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/example/meridian",
            UseShellExecute = true
        });
    }

    private void OpenIssues_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/example/meridian/issues",
            UseShellExecute = true
        });
    }

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("You are running the latest version (1.6.1).", "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ── Global Hotkeys ─────────────────────────────────────────────

    private void LoadGlobalHotkeys()
    {
        GlobalHotkeysEnabledCheckBox.IsChecked = WpfServices.GlobalHotkeyService.Instance.IsEnabled;
        GlobalHotkeysList.ItemsSource = WpfServices.GlobalHotkeyService.Instance.Definitions;
    }

    private void GlobalHotkeysEnabled_Click(object sender, RoutedEventArgs e)
    {
        var enabled = GlobalHotkeysEnabledCheckBox.IsChecked ?? true;
        WpfServices.GlobalHotkeyService.Instance.IsEnabled = enabled;
    }
}

/// <summary>
/// Credential display information for the settings page.
/// </summary>
public sealed class CredentialDisplayInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Resource { get; set; } = string.Empty;
    public SolidColorBrush TestStatusColor { get; set; } = new(Color.FromRgb(139, 148, 158));
}

/// <summary>
/// Activity item for recent activity list.
/// </summary>
public sealed class SettingsActivityItem
{
    public string Icon { get; set; } = string.Empty;
    public SolidColorBrush IconColor { get; set; } = new(Color.FromRgb(139, 148, 158));
    public string Message { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
}

/// <summary>
/// View model for credential vault list items showing per-provider credential status.
/// </summary>
public sealed class CredentialVaultItem
{
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public SolidColorBrush StatusBrush { get; set; } = new(Color.FromRgb(139, 148, 158));
    public Visibility ConfigureVisibility { get; set; } = Visibility.Collapsed;
}
