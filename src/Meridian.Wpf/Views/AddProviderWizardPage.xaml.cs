using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Ui.Services.Services;
using CredentialFieldInfo = Meridian.Contracts.Api.CredentialFieldInfo;
using ProviderCatalogEntry = Meridian.Ui.Services.Services.ProviderCatalogEntry;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Multi-step wizard for adding and configuring a new data provider.
/// Guides the user through provider selection, credential entry, connection testing, and configuration.
/// </summary>
public partial class AddProviderWizardPage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.ConfigService _configService;
    private readonly SettingsConfigurationService _settingsConfigService;

    private IReadOnlyList<ProviderCatalogEntry> _allProviders = Array.Empty<ProviderCatalogEntry>();
    private ProviderCatalogEntry? _selectedProvider;
    private string _activeFilter = "all";

    public AddProviderWizardPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _configService = WpfServices.ConfigService.Instance;
        _settingsConfigService = SettingsConfigurationService.Instance;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _allProviders = _settingsConfigService.GetProviderCatalog();
        var credentialStatuses = _settingsConfigService.GetProviderCredentialStatuses();

        var viewModels = _allProviders.Select(p =>
        {
            var credStatus = credentialStatuses.FirstOrDefault(c => c.ProviderId == p.Id);
            return new ProviderCatalogViewModel(p, credStatus);
        }).ToList();

        ProviderCatalogList.ItemsSource = viewModels;
        UpdateStepProgress(1);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("Settings");
    }

    private void FilterAll_Click(object sender, RoutedEventArgs e) => ApplyFilter("all");
    private void FilterFree_Click(object sender, RoutedEventArgs e) => ApplyFilter("free");
    private void FilterStreaming_Click(object sender, RoutedEventArgs e) => ApplyFilter("streaming");
    private void FilterHistorical_Click(object sender, RoutedEventArgs e) => ApplyFilter("historical");

    private void ApplyFilter(string filter)
    {
        _activeFilter = filter;
        var credentialStatuses = _settingsConfigService.GetProviderCredentialStatuses();

        var filtered = filter switch
        {
            "free" => _allProviders.Where(p => p.Tier is ProviderTier.Free or ProviderTier.FreeWithAccount),
            "streaming" => _allProviders.Where(p => p.SupportsStreaming),
            "historical" => _allProviders.Where(p => p.SupportsHistorical),
            _ => _allProviders.AsEnumerable(),
        };

        ProviderCatalogList.ItemsSource = filtered.Select(p =>
        {
            var credStatus = credentialStatuses.FirstOrDefault(c => c.ProviderId == p.Id);
            return new ProviderCatalogViewModel(p, credStatus);
        }).ToList();

        // Update button styles
        FilterAllBtn.Style = (Style)FindResource(filter == "all" ? "SecondaryButtonStyle" : "GhostButtonStyle");
        FilterFreeBtn.Style = (Style)FindResource(filter == "free" ? "SecondaryButtonStyle" : "GhostButtonStyle");
        FilterStreamingBtn.Style = (Style)FindResource(filter == "streaming" ? "SecondaryButtonStyle" : "GhostButtonStyle");
        FilterHistoricalBtn.Style = (Style)FindResource(filter == "historical" ? "SecondaryButtonStyle" : "GhostButtonStyle");
    }

    private void ProviderCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string providerId) return;

        _selectedProvider = _allProviders.FirstOrDefault(p => p.Id == providerId);
        if (_selectedProvider == null) return;

        // Update right panel
        SelectedProviderName.Text = _selectedProvider.DisplayName;
        SelectedProviderDescription.Text = _selectedProvider.Description;
        ProviderDetailsGrid.Visibility = Visibility.Visible;

        DetailStreamingText.Text = _selectedProvider.SupportsStreaming ? "Yes" : "No";
        DetailHistoricalText.Text = _selectedProvider.SupportsHistorical ? "Yes" : "No";
        DetailSearchText.Text = _selectedProvider.SupportsSymbolSearch ? "Yes" : "No";
        DetailRateLimitText.Text = _selectedProvider.RateLimitPerMinute > 0
            ? $"{_selectedProvider.RateLimitPerMinute}/min"
            : "None";
        DetailCredentialsText.Text = _selectedProvider.CredentialFields.Length > 0
            ? $"{_selectedProvider.CredentialFields.Length} required"
            : "None";

        // Show Step 2
        Step2Panel.Visibility = Visibility.Visible;
        Step3Panel.Visibility = Visibility.Visible;
        Step4Panel.Visibility = Visibility.Visible;

        BuildCredentialFields();
        UpdateStepProgress(2);
    }

    private void BuildCredentialFields()
    {
        CredentialFieldsPanel.Children.Clear();

        if (_selectedProvider == null) return;

        if (_selectedProvider.CredentialFields.Length == 0)
        {
            CredentialsInfoText.Text = $"{_selectedProvider.DisplayName} does not require API credentials.";
            NoCredentialsNeededText.Visibility = Visibility.Visible;
            return;
        }

        NoCredentialsNeededText.Visibility = Visibility.Collapsed;
        CredentialsInfoText.Text = $"Enter your {_selectedProvider.DisplayName} credentials. " +
            "These will be stored as user environment variables.";

        foreach (var field in _selectedProvider.CredentialFields)
        {
            var envVar = field.EnvironmentVariable ?? string.Empty;
            var currentValue = GetConfiguredEnvironmentValue(field) ?? "";

            var label = new TextBlock
            {
                Text = field.DisplayName,
                Style = (Style)FindResource("FormLabelStyle"),
                Margin = new Thickness(0, 0, 0, 4)
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
                Margin = new Thickness(0, 2, 0, 12)
            };

            CredentialFieldsPanel.Children.Add(label);
            CredentialFieldsPanel.Children.Add(textBox);
            CredentialFieldsPanel.Children.Add(envHint);
        }
    }

    private void TestProviderConnection_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider == null) return;

        // Save credentials first so the connectivity test can use them
        SaveCredentials();

        ConnectionTestDot.Fill = (Brush)FindResource("WarningColorBrush");
        ConnectionTestStatusText.Text = $"Testing {_selectedProvider.DisplayName} connectivity...";

        // Simulate connection test (actual implementation would call ConnectivityTestService)
        var hasCredentials = _selectedProvider.CredentialFields.Length == 0 ||
            _selectedProvider.CredentialFields
                .Where(field => field.Required)
                .All(HasConfiguredEnvironmentValue);

        if (hasCredentials)
        {
            ConnectionTestDot.Fill = (Brush)FindResource("SuccessColorBrush");
            ConnectionTestStatusText.Text = "Credentials configured. Provider ready.";
            UpdateStepProgress(3);
        }
        else
        {
            ConnectionTestDot.Fill = (Brush)FindResource("ErrorColorBrush");
            ConnectionTestStatusText.Text = "Missing credentials. Please fill in all required fields above.";
        }
    }

    private async void SaveProvider_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProvider == null) return;

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
                {
                    options.Priority = priority;
                }

                await _configService.SetBackfillProviderOptionsAsync(_selectedProvider.Id, options);
            }

            UpdateStepProgress(4);
            SaveStatusText.Text = $"{_selectedProvider.DisplayName} configured successfully.";
            SaveStatusText.Foreground = (Brush)FindResource("SuccessColorBrush");

            _notificationService.NotifySuccess(
                "Provider Added",
                $"{_selectedProvider.DisplayName} has been configured and is ready to use.");
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = $"Failed to save: {ex.Message}";
            SaveStatusText.Foreground = (Brush)FindResource("ErrorColorBrush");
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
                {
                    Environment.SetEnvironmentVariable(envVar, value, EnvironmentVariableTarget.User);
                }
            }
        }
    }

    private void UpdateStepProgress(int completedUpTo)
    {
        var successBrush = (Brush)FindResource("SuccessColorBrush");
        var activeBrush = (Brush)FindResource("InfoColorBrush");
        var pendingBrush = (Brush)FindResource("ConsoleTextMutedBrush");

        Step1Dot.Fill = completedUpTo >= 1 ? successBrush : pendingBrush;
        Step2Dot.Fill = completedUpTo >= 2 ? (completedUpTo > 2 ? successBrush : activeBrush) : pendingBrush;
        Step3Dot.Fill = completedUpTo >= 3 ? (completedUpTo > 3 ? successBrush : activeBrush) : pendingBrush;
        Step4Dot.Fill = completedUpTo >= 4 ? successBrush : pendingBrush;
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
}

/// <summary>
/// View model for provider catalog cards in the wizard.
/// </summary>
internal sealed class ProviderCatalogViewModel
{
    public ProviderCatalogViewModel(ProviderCatalogEntry entry, ProviderCredentialStatus? credStatus)
    {
        Id = entry.Id;
        DisplayName = entry.DisplayName;
        Description = entry.Description;
        SupportsStreaming = entry.SupportsStreaming;
        SupportsHistorical = entry.SupportsHistorical;

        TierLabel = entry.Tier switch
        {
            ProviderTier.Free => "FREE",
            ProviderTier.FreeWithAccount => "FREE*",
            ProviderTier.LimitedFree => "LIMITED",
            ProviderTier.Premium => "PREMIUM",
            _ => "FREE",
        };

        TierBrush = entry.Tier switch
        {
            ProviderTier.Free or ProviderTier.FreeWithAccount =>
                new SolidColorBrush(Color.FromArgb(40, 63, 185, 80)),
            ProviderTier.LimitedFree =>
                new SolidColorBrush(Color.FromArgb(40, 210, 153, 34)),
            ProviderTier.Premium =>
                new SolidColorBrush(Color.FromArgb(40, 88, 166, 255)),
            _ => new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
        };

        CredentialStatusBrush = credStatus?.State switch
        {
            CredentialState.Configured => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            CredentialState.Partial => new SolidColorBrush(Color.FromRgb(210, 153, 34)),
            CredentialState.Missing => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
            CredentialState.NotRequired => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
            _ => new SolidColorBrush(Color.FromRgb(139, 148, 158)),
        };

        StreamingVisibility = entry.SupportsStreaming ? Visibility.Visible : Visibility.Collapsed;
        HistoricalVisibility = entry.SupportsHistorical ? Visibility.Visible : Visibility.Collapsed;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public bool SupportsStreaming { get; }
    public bool SupportsHistorical { get; }
    public string TierLabel { get; }
    public Brush TierBrush { get; }
    public Brush CredentialStatusBrush { get; }
    public Visibility StreamingVisibility { get; }
    public Visibility HistoricalVisibility { get; }
}
