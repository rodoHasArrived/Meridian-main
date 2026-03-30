using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Configuration;
using Meridian.Ui.Services;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Page-level ViewModel for the Provider page.
/// All state, collections, async orchestration, and commands live here;
/// the code-behind is thinned to lifecycle wiring and row-level event delegation.
/// </summary>
public sealed class ProviderViewModel : BindableBase
{
    private readonly WpfServices.NavigationService _navigationService;
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.ConfigService _configService;
    private readonly BackfillProviderConfigService _providerConfigService;

    // ── Loading guard ────────────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    // ── Validation panel state ───────────────────────────────────────────────
    private bool _isValidationPanelVisible;
    public bool IsValidationPanelVisible
    {
        get => _isValidationPanelVisible;
        private set => SetProperty(ref _isValidationPanelVisible, value);
    }

    private string _validationText = string.Empty;
    public string ValidationText
    {
        get => _validationText;
        private set => SetProperty(ref _validationText, value);
    }

    // Validation state enum exposed as three bool properties for DataTrigger convenience.
    private bool _validationIsSuccess;
    public bool ValidationIsSuccess
    {
        get => _validationIsSuccess;
        private set => SetProperty(ref _validationIsSuccess, value);
    }

    private bool _validationIsWarning;
    public bool ValidationIsWarning
    {
        get => _validationIsWarning;
        private set => SetProperty(ref _validationIsWarning, value);
    }

    private bool _validationIsError;
    public bool ValidationIsError
    {
        get => _validationIsError;
        private set => SetProperty(ref _validationIsError, value);
    }

    // ── Fallback chain / audit empty-state ───────────────────────────────────
    private bool _isEmptyFallbackChain;
    public bool IsEmptyFallbackChain
    {
        get => _isEmptyFallbackChain;
        private set => SetProperty(ref _isEmptyFallbackChain, value);
    }

    private bool _isEmptyAuditLog;
    public bool IsEmptyAuditLog
    {
        get => _isEmptyAuditLog;
        private set => SetProperty(ref _isEmptyAuditLog, value);
    }

    // ── Dry-run panel state ──────────────────────────────────────────────────
    private bool _isDryRunPanelVisible;
    public bool IsDryRunPanelVisible
    {
        get => _isDryRunPanelVisible;
        private set => SetProperty(ref _isDryRunPanelVisible, value);
    }

    private string _dryRunSymbolsInput = "SPY,AAPL,MSFT";
    public string DryRunSymbolsInput
    {
        get => _dryRunSymbolsInput;
        set => SetProperty(ref _dryRunSymbolsInput, value);
    }

    private string _dryRunWarningsText = string.Empty;
    public string DryRunWarningsText
    {
        get => _dryRunWarningsText;
        private set => SetProperty(ref _dryRunWarningsText, value);
    }

    private bool _isDryRunWarningsVisible;
    public bool IsDryRunWarningsVisible
    {
        get => _isDryRunWarningsVisible;
        private set => SetProperty(ref _isDryRunWarningsVisible, value);
    }

    // ── Observable collections ───────────────────────────────────────────────
    public ObservableCollection<ProviderSettingsViewModel> ProviderSettings { get; } = new();
    public ObservableCollection<FallbackChainViewModel> FallbackChain { get; } = new();
    public ObservableCollection<AuditLogViewModel> AuditLog { get; } = new();
    public ObservableCollection<DryRunResultViewModel> DryRunResults { get; } = new();

    // ── Commands ─────────────────────────────────────────────────────────────
    public ICommand RefreshProvidersCommand { get; }
    public ICommand ValidateConfigCommand { get; }
    public ICommand TestAllConnectionsCommand { get; }
    public ICommand DryRunCommand { get; }

    public ProviderViewModel(
        WpfServices.NavigationService navigationService,
        WpfServices.NotificationService notificationService,
        WpfServices.ConfigService configService,
        BackfillProviderConfigService providerConfigService)
    {
        _navigationService = navigationService;
        _notificationService = notificationService;
        _configService = configService;
        _providerConfigService = providerConfigService;

        RefreshProvidersCommand = new AsyncRelayCommand(LoadProviderSettingsAsync);
        ValidateConfigCommand = new AsyncRelayCommand(ValidateConfigAsync);
        TestAllConnectionsCommand = new RelayCommand(TestAllConnections);
        DryRunCommand = new AsyncRelayCommand(ExecuteDryRunAsync);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        await LoadProviderSettingsAsync(ct);
    }

    public void Stop() { }

    // ── Data loading ─────────────────────────────────────────────────────────

    public async Task LoadProviderSettingsAsync(CancellationToken ct = default)
    {
        if (_isLoading) return;
        IsLoading = true;

        try
        {
            var providersConfig = await _configService.GetBackfillProvidersConfigAsync();
            var statuses = await _providerConfigService.GetProviderStatusesAsync(providersConfig);

            var items = statuses.Select(s => new ProviderSettingsViewModel
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
            });

            ProviderSettings.Clear();
            foreach (var item in items)
                ProviderSettings.Add(item);

            await RefreshFallbackChainAsync(providersConfig, ct);
            RefreshAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync(
                "Load Error",
                $"Failed to load provider settings: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshFallbackChainAsync(BackfillProvidersConfigDto? config, CancellationToken ct = default)
    {
        var chain = await _providerConfigService.GetFallbackChainAsync(config);

        var items = chain.Select(s => new FallbackChainViewModel
        {
            Priority = (s.Options.Priority ?? s.Metadata.DefaultPriority).ToString(),
            DisplayName = s.Metadata.DisplayName,
            DataTypes = string.Join(", ", s.Metadata.DataTypes),
            HealthStatus = s.HealthStatus,
            RateLimitUsage = FormatRateLimitUsage(s),
            ConfigSource = s.EffectiveConfigSource,
        });

        FallbackChain.Clear();
        foreach (var item in items)
            FallbackChain.Add(item);

        IsEmptyFallbackChain = chain.Count == 0;
    }

    private void RefreshAuditLog()
    {
        var entries = _providerConfigService.GetAuditLog(20);

        var items = entries.Select(e => new AuditLogViewModel
        {
            Timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            ProviderId = e.ProviderId,
            Action = e.Action,
            Summary = e.Action == "reset"
                ? "Reset to defaults"
                : BackfillProviderConfigService.ComputeAuditDeltaSummary(e.PreviousValue, e.NewValue),
        });

        AuditLog.Clear();
        foreach (var item in items)
            AuditLog.Add(item);

        IsEmptyAuditLog = entries.Count == 0;
    }

    // ── Command implementations ───────────────────────────────────────────────

    private async Task ValidateConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await _configService.ValidateConfigAsync();

            IsValidationPanelVisible = true;

            if (result.IsValid && result.Warnings.Length == 0)
            {
                ValidationText = "Configuration is valid.";
                ValidationIsSuccess = true;
                ValidationIsWarning = false;
                ValidationIsError = false;
            }
            else if (result.IsValid)
            {
                ValidationText = "Configuration is valid with warnings:\n" +
                    string.Join("\n", result.Warnings.Select(w => $"  - {w}"));
                ValidationIsSuccess = false;
                ValidationIsWarning = true;
                ValidationIsError = false;
            }
            else
            {
                var text = "Configuration errors:\n" +
                    string.Join("\n", result.Errors.Select(err => $"  - {err}"));
                if (result.Warnings.Length > 0)
                    text += "\nWarnings:\n" +
                        string.Join("\n", result.Warnings.Select(w => $"  - {w}"));
                ValidationText = text;
                ValidationIsSuccess = false;
                ValidationIsWarning = false;
                ValidationIsError = true;
            }
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Validation Error", ex.Message);
        }
    }

    private void TestAllConnections()
    {
        _notificationService.NotifyInfo(
            "Connection Test",
            "Testing connectivity to all configured providers...");
    }

    private async Task ExecuteDryRunAsync(CancellationToken ct = default)
    {
        var symbolText = DryRunSymbolsInput?.Trim();
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
                IsDryRunPanelVisible = false;
                return;
            }

            DryRunResults.Clear();
            foreach (var s in plan.Symbols)
            {
                DryRunResults.Add(new DryRunResultViewModel
                {
                    Symbol = s.Symbol,
                    SelectedProvider = s.SelectedProvider ?? "none",
                    FallbackSequence = $"({string.Join(" > ", s.ProviderSequence)})",
                });
            }

            IsDryRunPanelVisible = true;

            if (plan.Warnings.Length > 0)
            {
                DryRunWarningsText = string.Join("\n", plan.Warnings);
                IsDryRunWarningsVisible = true;
            }
            else
            {
                IsDryRunWarningsVisible = false;
            }
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Dry-Run Error", ex.Message);
        }
    }

    // ── Row-level interaction helpers (called from code-behind event handlers) ─

    public async Task OnProviderToggleChangedAsync(ProviderSettingsViewModel vm)
    {
        if (_isLoading) return;

        try
        {
            var options = BuildOptionsFromViewModel(vm);
            await _configService.SetBackfillProviderOptionsAsync(vm.ProviderId, options);

            var config = await _configService.GetBackfillProvidersConfigAsync();
            await RefreshFallbackChainAsync(config);
            RefreshAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Save Error", ex.Message);
        }
    }

    public async Task OnPriorityLostFocusAsync(ProviderSettingsViewModel vm, string rawText)
    {
        if (_isLoading) return;

        if (!int.TryParse(rawText, out var priority) || priority < 0)
        {
            _notificationService.NotifyWarning("Validation", "Priority must be a non-negative integer.");
            return;
        }

        try
        {
            vm.Priority = priority;
            var options = BuildOptionsFromViewModel(vm);

            var inlineResult = await _configService.ValidateProviderInlineAsync(vm.ProviderId, options);
            if (!inlineResult.IsValid)
            {
                _notificationService.NotifyWarning("Validation", string.Join(" ", inlineResult.Errors));
                return;
            }

            vm.InlineWarning = inlineResult.HasWarnings
                ? string.Join("; ", inlineResult.Warnings)
                : null;

            await _configService.SetBackfillProviderOptionsAsync(vm.ProviderId, options);

            var config = await _configService.GetBackfillProvidersConfigAsync();
            await RefreshFallbackChainAsync(config);
            RefreshAuditLog();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Save Error", ex.Message);
        }
    }

    public async Task OnRateLimitLostFocusAsync(ProviderSettingsViewModel vm)
    {
        if (_isLoading) return;

        try
        {
            var options = BuildOptionsFromViewModel(vm);

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
            RefreshAuditLog();
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

    public async Task OnResetProviderAsync(string providerId)
    {
        try
        {
            await _configService.ResetBackfillProviderOptionsAsync(providerId);
            _notificationService.NotifyInfo("Reset", $"Provider '{providerId}' reset to defaults.");
            await LoadProviderSettingsAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Reset Error", ex.Message);
        }
    }

    public void OnConfigureProvider()
    {
        _navigationService.NavigateTo("DataSources");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static BackfillProviderOptionsDto BuildOptionsFromViewModel(ProviderSettingsViewModel vm)
    {
        var options = new BackfillProviderOptionsDto
        {
            Enabled = vm.IsEnabled,
            Priority = vm.Priority,
        };

        if (int.TryParse(vm.RateLimitPerMinute, out var rpm) && rpm > 0)
            options.RateLimitPerMinute = rpm;

        if (int.TryParse(vm.RateLimitPerHour, out var rph) && rph > 0)
            options.RateLimitPerHour = rph;

        return options;
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
}
