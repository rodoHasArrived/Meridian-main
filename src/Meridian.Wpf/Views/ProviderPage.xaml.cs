using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using Meridian.Wpf.ViewModels;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.Views;

/// <summary>
/// Page for viewing and managing market data providers for real-time streaming and historical data.
/// Phase 1: Backfill provider settings panel with enabled toggle, priority, rate limits.
/// Phase 2: Fallback chain preview with health status and config source badges.
/// Phase 3: Driven by provider metadata descriptors for dynamic UI.
/// Phase 4: Validation, dry-run plan, audit trail.
/// </summary>
public partial class ProviderPage : Page
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.ConfigService _configService;
    private readonly BackfillProviderConfigService _providerConfigService;
    private bool _isLoading;

    // Cached brushes — avoids repeated FindResource lookups in ValidateConfig_Click [P1]
    private Brush _validationSuccessBrush = Brushes.Green;
    private Brush _validationWarningBrush = Brushes.Orange;
    private Brush _validationErrorBrush = Brushes.Red;

    public ProviderPage(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.ConfigService configService,
        BackfillProviderConfigService providerConfigService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _notificationService = notificationService;
        _configService = configService;
        _providerConfigService = providerConfigService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // Cache resource brushes once at load time [P1]
        _validationSuccessBrush = (Brush)FindResource("SuccessColorBrush");
        _validationWarningBrush = (Brush)FindResource("WarningColorBrush");
        _validationErrorBrush   = (Brush)FindResource("ErrorColorBrush");

        await LoadProviderSettingsAsync();
    }

    private async System.Threading.Tasks.Task LoadProviderSettingsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;

        try
        {
            var providersConfig = await _configService.GetBackfillProvidersConfigAsync();
            var statuses = await _providerConfigService.GetProviderStatusesAsync(providersConfig);

            // Build view models for the settings list
            var settingsViewModels = statuses.Select(s => new ProviderSettingsViewModel
            {
                ProviderId = s.Metadata.ProviderId,
                DisplayName = s.Metadata.DisplayName,
                IsEnabled = s.Options.Enabled,
                Priority = s.Options.Priority ?? s.Metadata.DefaultPriority,
                DataTypes = string.Join(", ", s.Metadata.DataTypes),
                RateLimitPerMinute = s.Options.RateLimitPerMinute?.ToString() ?? "",
                RateLimitPerHour = s.Options.RateLimitPerHour?.ToString() ?? "",
                ConfigSource = s.EffectiveConfigSource,
                HealthStatus = s.HealthStatus,
                RequiresApiKey = s.Metadata.RequiresApiKey,
                FreeTier = s.Metadata.FreeTier,
            }).ToList();

            ProviderSettingsList.ItemsSource = settingsViewModels;

            // Update fallback chain
            await UpdateFallbackChainAsync(providersConfig);

            // Update audit log
            UpdateAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync(
                "Load Error",
                $"Failed to load provider settings: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async System.Threading.Tasks.Task UpdateFallbackChainAsync(BackfillProvidersConfigDto? config)
    {
        var chain = await _providerConfigService.GetFallbackChainAsync(config);

        var chainViewModels = chain.Select(s => new FallbackChainViewModel
        {
            Priority = (s.Options.Priority ?? s.Metadata.DefaultPriority).ToString(),
            DisplayName = s.Metadata.DisplayName,
            DataTypes = string.Join(", ", s.Metadata.DataTypes),
            HealthStatus = s.HealthStatus,
            RateLimitUsage = FormatRateLimitUsage(s),
            ConfigSource = s.EffectiveConfigSource,
        }).ToList();

        FallbackChainList.ItemsSource = chainViewModels;
        EmptyFallbackChainText.Visibility = chain.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static string FormatRateLimitUsage(BackfillProviderStatusDto status)
    {
        var parts = new System.Collections.Generic.List<string>();
        var limitMin = status.Options.RateLimitPerMinute ?? status.Metadata.DefaultRateLimitPerMinute;
        var limitHr = status.Options.RateLimitPerHour ?? status.Metadata.DefaultRateLimitPerHour;

        if (limitMin.HasValue)
            parts.Add($"{status.RequestsUsedMinute}/{limitMin}/min");
        if (limitHr.HasValue)
            parts.Add($"{status.RequestsUsedHour}/{limitHr}/hr");

        if (status.IsThrottled)
            parts.Add("[throttled]");

        return parts.Count > 0 ? string.Join("  ", parts) : "";
    }

    private void UpdateAuditLog()
    {
        var entries = _providerConfigService.GetAuditLog(20);
        var viewModels = entries.Select(e => new AuditLogViewModel
        {
            Timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            ProviderId = e.ProviderId,
            Action = e.Action,
            Summary = e.Action == "reset"
                ? "Reset to defaults"
                : BackfillProviderConfigService.ComputeAuditDeltaSummary(e.PreviousValue, e.NewValue),
        }).ToList();

        AuditLogList.ItemsSource = viewModels;
        EmptyAuditText.Visibility = entries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void ProviderToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not CheckBox checkBox) return;
        if (checkBox.DataContext is not ProviderSettingsViewModel vm) return;

        try
        {
            var options = await BuildOptionsFromViewModel(vm);
            await _configService.SetBackfillProviderOptionsAsync(vm.ProviderId, options);

            var config = await _configService.GetBackfillProvidersConfigAsync();
            await UpdateFallbackChainAsync(config);
            UpdateAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Save Error", ex.Message);
        }
    }

    private async void PriorityField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not TextBox textBox) return;
        if (textBox.DataContext is not ProviderSettingsViewModel vm) return;

        if (!int.TryParse(textBox.Text, out var priority) || priority < 0)
        {
            _notificationService.NotifyWarning("Validation", "Priority must be a non-negative integer.");
            return;
        }

        try
        {
            vm.Priority = priority;
            var options = await BuildOptionsFromViewModel(vm);

            // Inline validation before save
            var inlineResult = await _configService.ValidateProviderInlineAsync(vm.ProviderId, options);
            if (!inlineResult.IsValid)
            {
                _notificationService.NotifyWarning("Validation", string.Join(" ", inlineResult.Errors));
                return;
            }

            if (inlineResult.HasWarnings)
            {
                vm.InlineWarning = string.Join("; ", inlineResult.Warnings);
            }
            else
            {
                vm.InlineWarning = null;
            }

            await _configService.SetBackfillProviderOptionsAsync(vm.ProviderId, options);

            var config = await _configService.GetBackfillProvidersConfigAsync();
            await UpdateFallbackChainAsync(config);
            UpdateAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Save Error", ex.Message);
        }
    }

    private async void RateLimitField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading || sender is not TextBox textBox) return;
        if (textBox.DataContext is not ProviderSettingsViewModel vm) return;

        try
        {
            var options = await BuildOptionsFromViewModel(vm);

            // Inline validation before save
            var inlineResult = await _configService.ValidateProviderInlineAsync(vm.ProviderId, options);
            if (!inlineResult.IsValid)
            {
                vm.InlineWarning = string.Join("; ", inlineResult.Errors);
                _notificationService.NotifyWarning("Validation", string.Join(" ", inlineResult.Errors));
                return;
            }

            vm.InlineWarning = inlineResult.HasWarnings
                ? string.Join("; ", inlineResult.Warnings)
                : null;

            await _configService.SetBackfillProviderOptionsAsync(vm.ProviderId, options);
            UpdateAuditLog();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            vm.InlineWarning = ex.Message;
            _notificationService.NotifyWarning("Validation", ex.Message);
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Save Error", ex.Message);
        }
    }

    private async void ResetProvider_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var providerId = button.Tag as string;
        if (string.IsNullOrEmpty(providerId)) return;

        try
        {
            await _configService.ResetBackfillProviderOptionsAsync(providerId);
            _notificationService.NotifyInfo("Reset", $"Provider '{providerId}' reset to defaults.");
            await LoadProviderSettingsAsync();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Reset Error", ex.Message);
        }
    }

    private async void ValidateConfig_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = await _configService.ValidateConfigAsync();

            ValidationPanel.Visibility = Visibility.Visible;

            if (result.IsValid && result.Warnings.Length == 0)
            {
                // Use cached brushes — no FindResource per-click [P1]
                ValidationPanel.Background = new SolidColorBrush(Color.FromArgb(30, 0, 180, 0));
                ValidationText.Text = "Configuration is valid.";
                ValidationText.Foreground = _validationSuccessBrush;
            }
            else if (result.IsValid)
            {
                ValidationPanel.Background = new SolidColorBrush(Color.FromArgb(30, 255, 180, 0));
                var text = "Configuration is valid with warnings:\n" +
                    string.Join("\n", result.Warnings.Select(w => $"  - {w}"));
                ValidationText.Text = text;
                ValidationText.Foreground = _validationWarningBrush;
            }
            else
            {
                ValidationPanel.Background = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0));
                var text = "Configuration errors:\n" +
                    string.Join("\n", result.Errors.Select(err => $"  - {err}"));
                if (result.Warnings.Length > 0)
                {
                    text += "\nWarnings:\n" +
                        string.Join("\n", result.Warnings.Select(w => $"  - {w}"));
                }
                ValidationText.Text = text;
                ValidationText.Foreground = _validationErrorBrush;
            }
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Validation Error", ex.Message);
        }
    }

    private async void RefreshProviders_Click(object sender, RoutedEventArgs e)
    {
        await LoadProviderSettingsAsync();
    }

    private async void DryRunPlan_Click(object sender, RoutedEventArgs e)
    {
        var symbolText = DryRunSymbolsInput.Text?.Trim();
        if (string.IsNullOrEmpty(symbolText))
        {
            _notificationService.NotifyWarning("Input Required", "Enter one or more symbols separated by commas.");
            return;
        }

        try
        {
            var symbols = symbolText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var config = await _configService.GetBackfillProvidersConfigAsync();
            var plan = await _providerConfigService.GenerateDryRunPlanAsync(config, symbols);

            if (plan.ValidationErrors.Length > 0)
            {
                _ = _notificationService.NotifyErrorAsync("Plan Error", string.Join("; ", plan.ValidationErrors));
                DryRunResultsPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var viewModels = plan.Symbols.Select(s => new DryRunResultViewModel
            {
                Symbol = s.Symbol,
                SelectedProvider = s.SelectedProvider ?? "none",
                FallbackSequence = $"({string.Join(" > ", s.ProviderSequence)})",
            }).ToList();

            DryRunResultsList.ItemsSource = viewModels;
            DryRunResultsPanel.Visibility = Visibility.Visible;

            if (plan.Warnings.Length > 0)
            {
                DryRunWarningsText.Text = string.Join("\n", plan.Warnings);
                DryRunWarningsText.Visibility = Visibility.Visible;
            }
            else
            {
                DryRunWarningsText.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Dry-Run Error", ex.Message);
        }
    }

    private void TestAllConnections_Click(object sender, RoutedEventArgs e)
    {
        _notificationService.NotifyInfo(
            "Connection Test",
            "Testing connectivity to all configured providers...");
    }

    private void ConfigureProvider_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.NavigateTo("DataSources");
    }

    private static Task<BackfillProviderOptionsDto> BuildOptionsFromViewModel(ProviderSettingsViewModel vm)
    {
        var options = new BackfillProviderOptionsDto
        {
            Enabled = vm.IsEnabled,
            Priority = vm.Priority,
        };

        if (int.TryParse(vm.RateLimitPerMinute, out var rpm) && rpm > 0)
        {
            options.RateLimitPerMinute = rpm;
        }

        if (int.TryParse(vm.RateLimitPerHour, out var rph) && rph > 0)
        {
            options.RateLimitPerHour = rph;
        }

        return Task.FromResult(options);
    }
}

