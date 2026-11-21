using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.Ui.Services;
using Meridian.Ui.Services.ProviderDiagnostics;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;
using WpfServices = Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Page-level ViewModel for the Provider page.
/// All state, collections, async orchestration, and commands live here.
/// </summary>
public sealed class ProviderViewModel : BindableBase
{
    private readonly WpfServices.NotificationService _notificationService;
    private readonly WpfServices.ConfigService _configService;
    private readonly ProviderConfigurationApiClient _client;
    public ProviderAccountingViewModel ProviderAccounting { get; }
    public ProviderActivationViewModel Activation { get; }
    private string _hostSettingsMessage = string.Empty;

    public string HostSettingsMessage { get => _hostSettingsMessage; private set => SetProperty(ref _hostSettingsMessage, value); }

    // ── Loading guard ────────────────────────────────────────────────────────
    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RaiseCommandStateChanged();
            }
        }
    }

    // ── Validation panel state ───────────────────────────────────────────────
    private bool _isValidationPanelVisible;
    public bool IsValidationPanelVisible
    {
        get => _isValidationPanelVisible;
        private set
        {
            if (SetProperty(ref _isValidationPanelVisible, value))
            {
                UpdateProviderPresentation();
            }
        }
    }

    private string _validationText = string.Empty;
    public string ValidationText
    {
        get => _validationText;
        private set
        {
            if (SetProperty(ref _validationText, value))
            {
                UpdateProviderPresentation();
            }
        }
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
        set
        {
            if (SetProperty(ref _dryRunSymbolsInput, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(DryRunTooltip));
                DryRunCommand.NotifyCanExecuteChanged();
            }
        }
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

    public WorkstationTableModel<ProviderSettingsViewModel> ProviderSettingsTable { get; }
    public WorkstationTableModel<FallbackChainViewModel> FallbackChainTable { get; }
    public WorkstationTableModel<DryRunResultViewModel> DryRunPlanTable { get; }
    public WorkstationTableModel<AuditLogViewModel> AuditLogTable { get; }

    private ProviderSettingsViewModel? _selectedProviderSetting;
    public ProviderSettingsViewModel? SelectedProviderSetting
    {
        get => _selectedProviderSetting;
        set
        {
            if (SetProperty(ref _selectedProviderSetting, value))
            {
                SelectedProviderInspector = BuildProviderSettingsInspector(value);
                ProviderActionInspector = BuildProviderActionInspector(
                    value,
                    IsLoading,
                    SaveSelectedProviderTooltip,
                    ResetSelectedProviderTooltip,
                    IsValidationPanelVisible ? ValidationText : "Configuration validation has not run in this session.");
                RaiseSelectedProviderStateChanged();
            }
        }
    }

    private FallbackChainViewModel? _selectedFallbackProvider;
    public FallbackChainViewModel? SelectedFallbackProvider
    {
        get => _selectedFallbackProvider;
        set
        {
            if (SetProperty(ref _selectedFallbackProvider, value))
            {
                SelectedFallbackInspector = BuildFallbackInspector(value);
            }
        }
    }

    private DryRunResultViewModel? _selectedDryRunResult;
    public DryRunResultViewModel? SelectedDryRunResult
    {
        get => _selectedDryRunResult;
        set
        {
            if (SetProperty(ref _selectedDryRunResult, value))
            {
                SelectedDryRunInspector = BuildDryRunInspector(value);
            }
        }
    }

    private AuditLogViewModel? _selectedAuditLog;
    public AuditLogViewModel? SelectedAuditLog
    {
        get => _selectedAuditLog;
        set
        {
            if (SetProperty(ref _selectedAuditLog, value))
            {
                SelectedAuditInspector = BuildAuditInspector(value);
            }
        }
    }

    private InspectorPanelModel _selectedProviderInspector = BuildProviderSettingsInspector(null);
    public InspectorPanelModel SelectedProviderInspector
    {
        get => _selectedProviderInspector;
        private set => SetProperty(ref _selectedProviderInspector, value);
    }

    private InspectorPanelModel _providerActionInspector = BuildProviderActionInspector(
        selected: null,
        isLoading: false,
        saveTooltip: BuildSaveSelectedProviderTooltip(null, isLoading: false),
        resetTooltip: BuildResetSelectedProviderTooltip(null, isLoading: false),
        validationText: "Configuration validation has not run in this session.");
    public InspectorPanelModel ProviderActionInspector
    {
        get => _providerActionInspector;
        private set => SetProperty(ref _providerActionInspector, value);
    }

    private InspectorPanelModel _selectedFallbackInspector = BuildFallbackInspector(null);
    public InspectorPanelModel SelectedFallbackInspector
    {
        get => _selectedFallbackInspector;
        private set => SetProperty(ref _selectedFallbackInspector, value);
    }

    private InspectorPanelModel _selectedDryRunInspector = BuildDryRunInspector(null);
    public InspectorPanelModel SelectedDryRunInspector
    {
        get => _selectedDryRunInspector;
        private set => SetProperty(ref _selectedDryRunInspector, value);
    }

    private InspectorPanelModel _selectedAuditInspector = BuildAuditInspector(null);
    public InspectorPanelModel SelectedAuditInspector
    {
        get => _selectedAuditInspector;
        private set => SetProperty(ref _selectedAuditInspector, value);
    }

    private WorkstationStateModel _backfillPostureState = BuildBackfillPostureState(
        providerCount: 0,
        enabledProviderCount: 0,
        fallbackCount: 0,
        isLoading: false,
        validationText: "Configuration validation has not run in this session.",
        hasValidationResult: false);
    public WorkstationStateModel BackfillPostureState
    {
        get => _backfillPostureState;
        private set => SetProperty(ref _backfillPostureState, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────
    public IAsyncRelayCommand RefreshProvidersCommand { get; }
    public IAsyncRelayCommand ValidateConfigCommand { get; }
    public IRelayCommand TestAllConnectionsCommand { get; }
    public IAsyncRelayCommand DryRunCommand { get; }
    public IAsyncRelayCommand SaveSelectedProviderCommand { get; }
    public IAsyncRelayCommand ResetSelectedProviderCommand { get; }

    public string ProviderScopeText
    {
        get
        {
            var enabledCount = ProviderSettings.Count(provider => provider.IsEnabled);
            return $"{ProviderSettings.Count} providers; {enabledCount} enabled; {FallbackChain.Count} fallback entries";
        }
    }

    public string BackfillActionStateTitle => IsLoading
        ? "Provider settings loading"
        : SelectedProviderSetting is null
            ? "Select a provider to edit"
            : $"Editing {SelectedProviderSetting.DisplayName}";

    public string BackfillActionStateDetail => IsLoading
        ? "Provider commands are disabled while Meridian refreshes retained backfill settings."
        : SelectedProviderSetting is null
            ? "Save and reset stay disabled until a backfill provider row is selected."
            : "Save changes to the host and review their runtime activation above.";

    public string RefreshProvidersTooltip => IsLoading
        ? "Wait for provider settings to finish loading before refreshing."
        : "Refresh retained backfill provider settings and fallback evidence.";

    public string ValidateConfigTooltip => IsLoading
        ? "Wait for provider settings to finish loading before validating configuration."
        : "Validate the current desktop configuration before backfill changes are committed.";

    public string TestConnectionsTooltip => IsLoading
        ? "Wait for provider settings to finish loading before testing connections."
        : "Start provider connection checks from the configured desktop provider services.";

    public string DryRunTooltip => IsLoading
        ? "Wait for provider settings to finish loading before previewing a backfill plan."
        : string.IsNullOrWhiteSpace(DryRunSymbolsInput)
            ? "Enter one or more symbols before previewing a dry-run backfill plan."
            : "Preview which enabled providers would be selected for the entered symbols.";

    public string SaveSelectedProviderTooltip => BuildSaveSelectedProviderTooltip(SelectedProviderSetting, IsLoading);

    public string ResetSelectedProviderTooltip => BuildResetSelectedProviderTooltip(SelectedProviderSetting, IsLoading);

    public bool HasSelectedProviderInlineWarning => SelectedProviderSetting?.HasInlineWarning == true;

    public ProviderViewModel(
        WpfServices.NotificationService notificationService,
        WpfServices.ConfigService configService,
        ProviderConfigurationApiClient client,
        IProviderDiagnosticsApiClient providerDiagnosticsApiClient)
    {
        _notificationService = notificationService;
        _configService = configService;
        _client = client;
        ProviderAccounting = new ProviderAccountingViewModel(providerDiagnosticsApiClient);
        Activation = new ProviderActivationViewModel(client, LoadProviderSettingsAsync);
        Activation.StatusChanged += (_, _) => UpdateActivationRows();

        ProviderSettingsTable = new WorkstationTableModel<ProviderSettingsViewModel>(
            ProviderSettings,
            [
                new("Provider", nameof(ProviderSettingsViewModel.DisplayName), 150),
                new("Enabled", nameof(ProviderSettingsViewModel.EnabledText), 80),
                new("Activation", nameof(ProviderSettingsViewModel.ActivationState), 118),
                new("Priority", nameof(ProviderSettingsViewModel.Priority), 80),
                new("Data Types", nameof(ProviderSettingsViewModel.DataTypes), 220),
                new("Rate/min", nameof(ProviderSettingsViewModel.RateLimitPerMinute), 90),
                new("Rate/hr", nameof(ProviderSettingsViewModel.RateLimitPerHour), 90),
                new("Source", nameof(ProviderSettingsViewModel.ConfigSource), 90),
                new("Health", nameof(ProviderSettingsViewModel.HealthStatus), 90)
            ],
            "Backfill provider settings",
            "No backfill providers loaded",
            "Refresh provider settings before editing priority, rate limits, or fallback posture.");
        FallbackChainTable = new WorkstationTableModel<FallbackChainViewModel>(
            FallbackChain,
            [
                new("Priority", nameof(FallbackChainViewModel.Priority), 80),
                new("Provider", nameof(FallbackChainViewModel.DisplayName), 150),
                new("Data Types", nameof(FallbackChainViewModel.DataTypes), 240),
                new("Rate Usage", nameof(FallbackChainViewModel.RateLimitUsage), 150),
                new("Health", nameof(FallbackChainViewModel.HealthStatus), 90),
                new("Source", nameof(FallbackChainViewModel.ConfigSource), 90)
            ],
            "Fallback chain preview",
            "No enabled fallback providers",
            "Enable at least one backfill provider before Meridian can build a fallback chain.");
        DryRunPlanTable = new WorkstationTableModel<DryRunResultViewModel>(
            DryRunResults,
            [
                new("Symbol", nameof(DryRunResultViewModel.Symbol), 90),
                new("Selected Provider", nameof(DryRunResultViewModel.SelectedProvider), 160),
                new("Fallback Sequence", nameof(DryRunResultViewModel.FallbackSequence), 360)
            ],
            "Dry-run backfill plan",
            "No dry-run plan",
            "Enter symbols and preview a dry-run plan before executing backfill changes.");
        AuditLogTable = new WorkstationTableModel<AuditLogViewModel>(
            AuditLog,
            [
                new("Timestamp", nameof(AuditLogViewModel.Timestamp), 150),
                new("Provider", nameof(AuditLogViewModel.ProviderId), 120),
                new("Action", nameof(AuditLogViewModel.Action), 90),
                new("Summary", nameof(AuditLogViewModel.Summary), 360)
            ],
            "Provider configuration audit",
            "No provider changes",
            "Provider configuration changes will appear after edits are saved.");

        RefreshProvidersCommand = new AsyncRelayCommand(RefreshProviderEvidenceAsync, CanRunProviderAction);
        ValidateConfigCommand = new AsyncRelayCommand(ValidateConfigAsync, CanRunProviderAction);
        TestAllConnectionsCommand = new RelayCommand(TestAllConnections, CanRunProviderAction);
        DryRunCommand = new AsyncRelayCommand(ExecuteDryRunAsync, CanPreviewDryRun);
        SaveSelectedProviderCommand = new AsyncRelayCommand(SaveSelectedProviderAsync, CanSaveSelectedProvider);
        ResetSelectedProviderCommand = new AsyncRelayCommand(ResetSelectedProviderAsync, CanResetSelectedProvider);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        await Task.WhenAll(
            LoadProviderSettingsAsync(ct),
            ProviderAccounting.StartAsync(ct));
    }

    public void Stop()
    {
        Activation.CancelPending();
        RefreshProvidersCommand.Cancel();
        SaveSelectedProviderCommand.Cancel();
        ResetSelectedProviderCommand.Cancel();
        ProviderAccounting.Stop();
    }

    private async Task RefreshProviderEvidenceAsync(CancellationToken ct = default)
    {
        try
        {
            await Task.WhenAll(LoadProviderSettingsAsync(ct), ProviderAccounting.RefreshAsync(ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    public async Task LoadProviderSettingsAsync(CancellationToken ct = default)
    {
        if (_isLoading)
            return;
        IsLoading = true;
        var selectedProviderId = SelectedProviderSetting?.ProviderId;

        try
        {
            var statuses = await _client.GetBackfillProvidersAsync(ct);

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
                TierId = s.Options.TierId,
                DefaultPriority = s.Metadata.DefaultPriority
            });

            ProviderSettings.Clear();
            foreach (var item in items)
                ProviderSettings.Add(item);
            SelectedProviderSetting = string.IsNullOrWhiteSpace(selectedProviderId)
                ? SelectedProviderSetting
                : ProviderSettings.FirstOrDefault(provider =>
                    string.Equals(provider.ProviderId, selectedProviderId, StringComparison.OrdinalIgnoreCase));

            HostSettingsMessage = string.Empty;
            await Task.WhenAll(RefreshFallbackChainAsync(statuses, ct), RefreshAuditLogAsync(ct), Activation.RefreshAsync(ct));
            UpdateActivationRows();
            UpdateProviderPresentation();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (InvalidOperationException ex)
        {
            HostSettingsMessage = $"Could not refresh provider settings. {ex.Message}";
            await Activation.RefreshAsync(ct);
            _ = _notificationService.NotifyErrorAsync(
                "Load Error",
                $"Failed to load provider settings: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task RefreshFallbackChainAsync(BackfillProviderStatusDto[] statuses, CancellationToken ct)
    {
        var selectedProviderName = SelectedFallbackProvider?.DisplayName;
        IReadOnlyList<EffectiveBackfillProvider> chain;
        try
        {
            chain = await _client.GetEffectiveOrderAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            FallbackChain.Clear();
            IsEmptyFallbackChain = true;
            HostSettingsMessage = $"The active fallback order is unavailable. {ex.Message}";
            return;
        }

        var items = chain.Select(provider =>
        {
            var status = statuses.FirstOrDefault(status => string.Equals(status.Metadata.ProviderId, provider.ProviderId, StringComparison.OrdinalIgnoreCase));
            return new FallbackChainViewModel
            {
                Priority = provider.Priority.ToString(CultureInfo.InvariantCulture),
                DisplayName = provider.DisplayName,
                DataTypes = status is null ? string.Empty : string.Join(", ", status.Metadata.DataTypes),
                HealthStatus = status?.HealthStatus ?? "unknown",
                RateLimitUsage = status is null ? string.Empty : FormatRateLimitUsage(status),
                ConfigSource = status?.EffectiveConfigSource ?? "runtime"
            };
        });

        FallbackChain.Clear();
        foreach (var item in items)
            FallbackChain.Add(item);

        IsEmptyFallbackChain = chain.Count == 0;
        SelectedFallbackProvider = string.IsNullOrWhiteSpace(selectedProviderName)
            ? SelectedFallbackProvider
            : FallbackChain.FirstOrDefault(provider =>
                string.Equals(provider.DisplayName, selectedProviderName, StringComparison.OrdinalIgnoreCase));
        UpdateProviderPresentation();
    }

    private async Task RefreshAuditLogAsync(CancellationToken ct)
    {
        var selectedAuditKey = SelectedAuditLog is null
            ? string.Empty
            : $"{SelectedAuditLog.Timestamp}|{SelectedAuditLog.ProviderId}|{SelectedAuditLog.Action}";
        ProviderConfigAuditEntryDto[] entries;
        try
        {
            entries = await _client.GetBackfillAuditAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            AuditLog.Clear();
            IsEmptyAuditLog = true;
            HostSettingsMessage = $"The host's provider audit is unavailable. {ex.Message}";
            return;
        }

        var items = entries.Take(20).Select(e => new AuditLogViewModel
        {
            Timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
            ProviderId = e.ProviderId,
            Action = e.Action,
            Summary = e.Action == "reset"
                ? "Reset to defaults"
                : e.NewValue is null ? $"Saved by {e.Source}" : BackfillProviderConfigService.ComputeAuditDeltaSummary(e.PreviousValue, e.NewValue),
        });

        AuditLog.Clear();
        foreach (var item in items)
            AuditLog.Add(item);

        IsEmptyAuditLog = entries.Length == 0;
        SelectedAuditLog = string.IsNullOrWhiteSpace(selectedAuditKey)
            ? SelectedAuditLog
            : AuditLog.FirstOrDefault(entry =>
                string.Equals(
                    $"{entry.Timestamp}|{entry.ProviderId}|{entry.Action}",
                    selectedAuditKey,
                    StringComparison.OrdinalIgnoreCase));
        UpdateProviderPresentation();
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

            UpdateProviderPresentation();
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
            var plan = await _client.PreviewBackfillAsync(symbols, ct);

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
            SelectedDryRunResult = DryRunResults.FirstOrDefault();

            if (plan.Warnings.Length > 0)
            {
                DryRunWarningsText = string.Join("\n", plan.Warnings);
                IsDryRunWarningsVisible = true;
            }
            else
            {
                IsDryRunWarningsVisible = false;
            }

            UpdateProviderPresentation();
        }
        catch (Exception ex)
        {
            _ = _notificationService.NotifyErrorAsync("Dry-Run Error", ex.Message);
        }
    }

    public Task OnProviderToggleChangedAsync(ProviderSettingsViewModel provider)
        => SaveProviderAsync(provider, reset: false, CancellationToken.None);

    public Task OnPriorityLostFocusAsync(ProviderSettingsViewModel provider, string rawText)
    {
        if (!int.TryParse(rawText, out var priority) || priority is < 0 or > 10000)
        {
            _notificationService.NotifyWarning("Validation", "Priority must be between 0 and 10000.");
            return Task.CompletedTask;
        }

        provider.Priority = priority;
        return SaveProviderAsync(provider, reset: false, CancellationToken.None);
    }

    public Task OnRateLimitLostFocusAsync(ProviderSettingsViewModel provider)
        => SaveProviderAsync(provider, reset: false, CancellationToken.None);

    public Task OnResetProviderAsync(string providerId)
        => SaveProviderAsync(ProviderSettings.FirstOrDefault(provider =>
            string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)), reset: true, CancellationToken.None);

    private Task SaveSelectedProviderAsync(CancellationToken ct = default)
        => SaveProviderAsync(SelectedProviderSetting, reset: false, ct);

    private Task ResetSelectedProviderAsync(CancellationToken ct = default)
        => SaveProviderAsync(SelectedProviderSetting, reset: true, ct);

    private async Task SaveProviderAsync(ProviderSettingsViewModel? provider, bool reset, CancellationToken ct)
    {
        if (provider is null || IsLoading)
            return;

        IsLoading = true;
        try
        {
            var edit = reset
                ? new BackfillProviderEdit(Enabled: true, Priority: provider.DefaultPriority)
                : BuildProviderEdit(provider);
            var saved = await _client.SaveBackfillProviderAsync(provider.ProviderId, edit, ct);
            provider.IsEnabled = saved.Enabled;
            provider.Priority = saved.Priority ?? provider.DefaultPriority;
            provider.TierId = saved.TierId;
            provider.RateLimitPerMinute = saved.RateLimitPerMinute?.ToString(CultureInfo.InvariantCulture) ?? "";
            provider.RateLimitPerHour = saved.RateLimitPerHour?.ToString(CultureInfo.InvariantCulture) ?? "";
            provider.InlineWarning = null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            provider.InlineWarning = ex.Message;
            SelectedProviderInspector = BuildProviderSettingsInspector(SelectedProviderSetting);
            OnPropertyChanged(nameof(HasSelectedProviderInlineWarning));
            _notificationService.NotifyWarning("Provider settings", ex.Message);
            return;
        }
        finally
        {
            IsLoading = false;
        }

        await LoadProviderSettingsAsync(ct);
        if (string.IsNullOrEmpty(HostSettingsMessage))
            HostSettingsMessage = "Provider settings saved to the host.";
    }

    private void UpdateActivationRows()
    {
        foreach (var provider in ProviderSettings)
            provider.UpdateActivation(Activation.Find(provider.ProviderId, "backfill"));

        SelectedProviderInspector = BuildProviderSettingsInspector(SelectedProviderSetting);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool CanRunProviderAction()
        => !IsLoading;

    private bool CanPreviewDryRun()
        => !IsLoading && !string.IsNullOrWhiteSpace(DryRunSymbolsInput);

    private bool CanSaveSelectedProvider()
        => CanUseSelectedProvider(SelectedProviderSetting, IsLoading);

    private bool CanResetSelectedProvider()
        => CanUseSelectedProvider(SelectedProviderSetting, IsLoading);

    internal static bool CanUseSelectedProvider(ProviderSettingsViewModel? selected, bool isLoading)
        => selected is not null && !isLoading;

    internal static string BuildSaveSelectedProviderTooltip(ProviderSettingsViewModel? selected, bool isLoading)
    {
        if (isLoading)
        {
            return "Wait for provider settings to finish loading before saving changes.";
        }

        return selected is null
            ? "Select a backfill provider before saving provider settings."
            : $"Save priority, enabled state, and rate limits for {selected.DisplayName}.";
    }

    internal static string BuildResetSelectedProviderTooltip(ProviderSettingsViewModel? selected, bool isLoading)
    {
        if (isLoading)
        {
            return "Wait for provider settings to finish loading before resetting a provider.";
        }

        return selected is null
            ? "Select a backfill provider before resetting provider settings."
            : $"Reset {selected.DisplayName} to default backfill settings.";
    }

    internal static WorkstationStateModel BuildBackfillPostureState(
        int providerCount,
        int enabledProviderCount,
        int fallbackCount,
        bool isLoading,
        string validationText,
        bool hasValidationResult)
    {
        if (isLoading)
        {
            return WorkstationStateModel.Loading(
                "Provider settings loading",
                "Reading retained backfill provider settings before edits are available.");
        }

        if (providerCount <= 0)
        {
            return WorkstationStateModel.Empty(
                "Backfill providers not loaded",
                "Refresh provider settings before editing priorities, limits, fallback order, or audit evidence.",
                "Refresh providers",
                "Backfill provider settings");
        }

        if (enabledProviderCount <= 0)
        {
            return WorkstationStateModel.Blocked(
                "Backfill provider setup blocked",
                "No enabled backfill provider is available for historical data recovery.",
                new WorkstationActionPostureModel(
                    "Enable provider",
                    "Select a provider row, enable it in the inspector, and save the selected provider settings.",
                    "Backfill provider settings",
                    "Data workspace",
                    WorkstationReadinessTone.Blocked,
                    WorkspaceTone.Warning),
                evidenceText: hasValidationResult ? validationText : "Configuration validation has not run.");
        }

        return WorkstationStateModel.Ready(
            "Backfill provider setup ready",
            $"{enabledProviderCount}/{providerCount} providers are enabled for historical-data fallback.",
            "Review fallback",
            $"{fallbackCount} fallback entries",
            hasValidationResult ? validationText : "Configuration validation has not run.");
    }

    internal static InspectorPanelModel BuildProviderSettingsInspector(ProviderSettingsViewModel? selected)
    {
        if (selected is null)
        {
            return new InspectorPanelModel
            {
                Title = "No provider selected",
                Subtitle = "Backfill settings",
                Detail = "Select a backfill provider row before editing priority, enabled state, rate limits, or reset posture.",
                Badge = new WorkstationBadgeModel("Selection", "Waiting", "\uE946", WorkspaceTone.Neutral),
                Facts =
                [
                    new("Provider", "Select row"),
                    new("Enabled", "-"),
                    new("Priority", "-"),
                    new("Rate limits", "-")
                ]
            };
        }

        return new InspectorPanelModel
        {
            Title = selected.DisplayName,
            Subtitle = selected.ProviderId,
            Detail = selected.HasInlineWarning
                ? selected.InlineWarning ?? "Provider settings need review."
                : selected.ActivationDetail,
            Badge = new WorkstationBadgeModel(
                "Backfill",
                selected.EnabledText,
                selected.IsEnabled ? "\uE73E" : "\uE783",
                selected.IsEnabled ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Priority", selected.Priority.ToString(CultureInfo.InvariantCulture)),
                new("Data types", string.IsNullOrWhiteSpace(selected.DataTypes) ? "-" : selected.DataTypes),
                new("Rate/min", string.IsNullOrWhiteSpace(selected.RateLimitPerMinute) ? "Default" : selected.RateLimitPerMinute),
                new("Rate/hr", string.IsNullOrWhiteSpace(selected.RateLimitPerHour) ? "Default" : selected.RateLimitPerHour),
                new("Source", selected.ConfigSource),
                new("Health", selected.HealthStatus),
                new("Activation", selected.ActivationState),
                new("Credentials", selected.CredentialText),
                new("Tier", selected.FreeTierText)
            ]
        };
    }

    internal static InspectorPanelModel BuildProviderActionInspector(
        ProviderSettingsViewModel? selected,
        bool isLoading,
        string saveTooltip,
        string resetTooltip,
        string validationText)
        => new()
        {
            Title = selected is null ? "Provider actions unavailable" : "Provider actions",
            Subtitle = "Selected backfill provider",
            Detail = selected is null
                ? "Save and reset are disabled until a provider row is selected."
                : $"Save or reset retained settings for {selected.DisplayName}.",
            Badge = new WorkstationBadgeModel(
                "Actions",
                CanUseSelectedProvider(selected, isLoading) ? "Ready" : "Blocked",
                CanUseSelectedProvider(selected, isLoading) ? "\uE73E" : "\uE783",
                CanUseSelectedProvider(selected, isLoading) ? WorkspaceTone.Success : WorkspaceTone.Warning),
            Facts =
            [
                new("Save", saveTooltip),
                new("Reset", resetTooltip),
                new("Validation", validationText),
                new("Mutation", "Selected provider only")
            ]
        };

    internal static InspectorPanelModel BuildFallbackInspector(FallbackChainViewModel? selected)
    {
        if (selected is null)
        {
            return new InspectorPanelModel
            {
                Title = "No fallback row selected",
                Subtitle = "Fallback chain",
                Detail = "Select a fallback row to inspect effective priority, rate usage, and configuration source.",
                Badge = new WorkstationBadgeModel("Fallback", "Waiting", "\uE946", WorkspaceTone.Neutral),
                Facts = [new("Provider", "Select row"), new("Priority", "-"), new("Health", "-"), new("Source", "-")]
            };
        }

        return new InspectorPanelModel
        {
            Title = selected.DisplayName,
            Subtitle = $"Priority {selected.Priority}",
            Detail = "Fallback rows are derived from enabled backfill providers and current configuration overrides.",
            Badge = new WorkstationBadgeModel("Health", selected.HealthStatus, "\uE7BA", WorkspaceTone.Info),
            Facts =
            [
                new("Data types", string.IsNullOrWhiteSpace(selected.DataTypes) ? "-" : selected.DataTypes),
                new("Rate usage", string.IsNullOrWhiteSpace(selected.RateLimitUsage) ? "Default" : selected.RateLimitUsage),
                new("Source", selected.ConfigSource),
                new("Action", "Review before dry-run")
            ]
        };
    }

    internal static InspectorPanelModel BuildDryRunInspector(DryRunResultViewModel? selected)
    {
        if (selected is null)
        {
            return new InspectorPanelModel
            {
                Title = "No dry-run row selected",
                Subtitle = "Backfill plan",
                Detail = "Preview a dry-run plan and select a symbol row before reviewing provider routing.",
                Badge = new WorkstationBadgeModel("Plan", "Waiting", "\uE946", WorkspaceTone.Neutral),
                Facts = [new("Symbol", "-"), new("Selected provider", "-"), new("Fallback", "-")]
            };
        }

        return new InspectorPanelModel
        {
            Title = selected.Symbol,
            Subtitle = selected.SelectedProvider,
            Detail = "Dry-run results show provider selection without executing a backfill.",
            Badge = new WorkstationBadgeModel("Dry run", "Preview", "\uE9D2", WorkspaceTone.Info),
            Facts =
            [
                new("Selected provider", selected.SelectedProvider),
                new("Fallback sequence", selected.FallbackSequence),
                new("Mutation", "None")
            ]
        };
    }

    internal static InspectorPanelModel BuildAuditInspector(AuditLogViewModel? selected)
    {
        if (selected is null)
        {
            return new InspectorPanelModel
            {
                Title = "No audit row selected",
                Subtitle = "Provider configuration audit",
                Detail = "Select an audit row to inspect the retained provider configuration change.",
                Badge = new WorkstationBadgeModel("Audit", "Waiting", "\uE946", WorkspaceTone.Neutral),
                Facts = [new("Provider", "-"), new("Action", "-"), new("Time", "-")]
            };
        }

        return new InspectorPanelModel
        {
            Title = selected.ProviderId,
            Subtitle = selected.Action,
            Detail = selected.Summary,
            Badge = new WorkstationBadgeModel("Audit", "Retained", "\uE9D5", WorkspaceTone.Info),
            Facts =
            [
                new("Timestamp", selected.Timestamp),
                new("Provider", selected.ProviderId),
                new("Action", selected.Action),
                new("Summary", selected.Summary)
            ]
        };
    }

    private void RaiseCommandStateChanged()
    {
        OnPropertyChanged(nameof(ProviderScopeText));
        OnPropertyChanged(nameof(BackfillActionStateTitle));
        OnPropertyChanged(nameof(BackfillActionStateDetail));
        OnPropertyChanged(nameof(RefreshProvidersTooltip));
        OnPropertyChanged(nameof(ValidateConfigTooltip));
        OnPropertyChanged(nameof(TestConnectionsTooltip));
        OnPropertyChanged(nameof(DryRunTooltip));
        OnPropertyChanged(nameof(SaveSelectedProviderTooltip));
        OnPropertyChanged(nameof(ResetSelectedProviderTooltip));
        OnPropertyChanged(nameof(HasSelectedProviderInlineWarning));
        RefreshProvidersCommand?.NotifyCanExecuteChanged();
        ValidateConfigCommand?.NotifyCanExecuteChanged();
        TestAllConnectionsCommand?.NotifyCanExecuteChanged();
        DryRunCommand?.NotifyCanExecuteChanged();
        SaveSelectedProviderCommand?.NotifyCanExecuteChanged();
        ResetSelectedProviderCommand?.NotifyCanExecuteChanged();
        UpdateProviderPresentation();
    }

    private void RaiseSelectedProviderStateChanged()
    {
        OnPropertyChanged(nameof(BackfillActionStateTitle));
        OnPropertyChanged(nameof(BackfillActionStateDetail));
        OnPropertyChanged(nameof(SaveSelectedProviderTooltip));
        OnPropertyChanged(nameof(ResetSelectedProviderTooltip));
        OnPropertyChanged(nameof(HasSelectedProviderInlineWarning));
        SaveSelectedProviderCommand.NotifyCanExecuteChanged();
        ResetSelectedProviderCommand.NotifyCanExecuteChanged();
    }

    private void UpdateProviderPresentation()
    {
        var validationText = IsValidationPanelVisible
            ? ValidationText
            : "Configuration validation has not run in this session.";
        BackfillPostureState = BuildBackfillPostureState(
            ProviderSettings.Count,
            ProviderSettings.Count(provider => provider.IsEnabled),
            FallbackChain.Count,
            IsLoading,
            validationText,
            IsValidationPanelVisible);
        ProviderActionInspector = BuildProviderActionInspector(
            SelectedProviderSetting,
            IsLoading,
            SaveSelectedProviderTooltip,
            ResetSelectedProviderTooltip,
            validationText);
        OnPropertyChanged(nameof(ProviderScopeText));
        OnPropertyChanged(nameof(BackfillActionStateTitle));
        OnPropertyChanged(nameof(BackfillActionStateDetail));
    }

    internal static BackfillProviderEdit BuildProviderEdit(ProviderSettingsViewModel provider)
        => new(provider.IsEnabled, provider.Priority, provider.TierId,
            ParseRateLimit(provider.RateLimitPerMinute, "minute"),
            ParseRateLimit(provider.RateLimitPerHour, "hour"));

    private static int? ParseRateLimit(string text, string period)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0)
            return limit;

        throw new ArgumentException($"Rate limit per {period} must be a positive integer.");
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
