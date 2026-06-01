using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Ui.Services;
using Meridian.Ui.Services.Services;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

public sealed class SetupWizardViewModel : BindableBase
{
    private const string InstructionsUrl = "https://github.com/example/meridian/blob/main/docs/guides/configuration.md";

    private readonly ISetupWizardStateService _stateService;
    private readonly ISetupWizardNotificationSink _notificationSink;
    private readonly ISetupWizardNavigator _navigator;
    private readonly ISetupWizardLinkLauncher _linkLauncher;

    private string _configPath = string.Empty;
    private string _selectedProvider = string.Empty;
    private string _storageLocation = "data";
    private string _nasdaqApiKey = string.Empty;
    private string _openFigiApiKey = string.Empty;
    private string _backendStatusText = "Checking backend status...";
    private string _backendStatusDetailText = "No connection check performed yet.";
    private string _backendStatusTone = "Neutral";
    private string _configStatusText = string.Empty;
    private string _presetStatusText = string.Empty;
    private string _validationStatusText = string.Empty;
    private bool _isBusy;

    public SetupWizardViewModel(
        ISetupWizardStateService stateService,
        ISetupWizardNotificationSink notificationSink,
        ISetupWizardNavigator navigator,
        ISetupWizardLinkLauncher linkLauncher)
    {
        _stateService = stateService;
        _notificationSink = notificationSink;
        _navigator = navigator;
        _linkLauncher = linkLauncher;

        LoadCommand = new AsyncRelayCommand(LoadAsync, CanRunCommand);
        ApplyPresetCommand = new AsyncRelayCommand<string>(ApplyPresetAsync, _ => CanRunCommand());
        TestConnectionCommand = new AsyncRelayCommand(RefreshBackendStatusAsync, CanRunCommand);
        StartBackendCommand = new AsyncRelayCommand(StartBackendAsync, CanRunCommand);
        SaveAndContinueCommand = new AsyncRelayCommand(SaveAndContinueAsync, CanRunCommand);
        UseDefaultStorageCommand = new RelayCommand(() => StorageLocation = "data", CanRunCommand);
        OpenInstructionsCommand = new RelayCommand(
            () => _linkLauncher.Open(InstructionsUrl),
            CanRunCommand);
    }

    public ObservableCollection<string> ProviderOptions { get; } = new();

    public ObservableCollection<SetupPreset> Presets { get; } = new();

    public AsyncRelayCommand LoadCommand { get; }

    public AsyncRelayCommand<string> ApplyPresetCommand { get; }

    public AsyncRelayCommand TestConnectionCommand { get; }

    public AsyncRelayCommand StartBackendCommand { get; }

    public AsyncRelayCommand SaveAndContinueCommand { get; }

    public RelayCommand UseDefaultStorageCommand { get; }

    public RelayCommand OpenInstructionsCommand { get; }

    public string ConfigPath
    {
        get => _configPath;
        private set => SetProperty(ref _configPath, value);
    }

    public string SelectedProvider
    {
        get => _selectedProvider;
        set => SetProperty(ref _selectedProvider, value ?? string.Empty);
    }

    public string StorageLocation
    {
        get => _storageLocation;
        set => SetProperty(ref _storageLocation, value ?? string.Empty);
    }

    public string NasdaqApiKey
    {
        get => _nasdaqApiKey;
        set => SetProperty(ref _nasdaqApiKey, value ?? string.Empty);
    }

    public string OpenFigiApiKey
    {
        get => _openFigiApiKey;
        set => SetProperty(ref _openFigiApiKey, value ?? string.Empty);
    }

    public string BackendStatusText
    {
        get => _backendStatusText;
        private set => SetProperty(ref _backendStatusText, value);
    }

    public string BackendStatusDetailText
    {
        get => _backendStatusDetailText;
        private set => SetProperty(ref _backendStatusDetailText, value);
    }

    public string BackendStatusTone
    {
        get => _backendStatusTone;
        private set => SetProperty(ref _backendStatusTone, value);
    }

    public string ConfigStatusText
    {
        get => _configStatusText;
        private set => SetProperty(ref _configStatusText, value);
    }

    public string PresetStatusText
    {
        get => _presetStatusText;
        private set => SetProperty(ref _presetStatusText, value);
    }

    public string ValidationStatusText
    {
        get => _validationStatusText;
        private set => SetProperty(ref _validationStatusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            LoadCommand.NotifyCanExecuteChanged();
            ApplyPresetCommand.NotifyCanExecuteChanged();
            TestConnectionCommand.NotifyCanExecuteChanged();
            StartBackendCommand.NotifyCanExecuteChanged();
            SaveAndContinueCommand.NotifyCanExecuteChanged();
            UseDefaultStorageCommand.NotifyCanExecuteChanged();
            OpenInstructionsCommand.NotifyCanExecuteChanged();
        }
    }

    public async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            ConfigPath = _stateService.ConfigFilePath;

            ReplaceItems(ProviderOptions, _stateService.ProviderOptions);
            ReplaceItems(Presets, _stateService.GetSetupPresets());

            var configuration = await _stateService.LoadConfigurationAsync();
            ReplaceItems(ProviderOptions, configuration.ProviderOptions);
            SelectedProvider = configuration.SelectedProvider;
            StorageLocation = configuration.StorageLocation;
            ConfigStatusText = configuration.StatusText;

            var apiKeys = _stateService.LoadStoredApiKeys();
            NasdaqApiKey = apiKeys.NasdaqDataLinkApiKey;
            OpenFigiApiKey = apiKeys.OpenFigiApiKey;

            await EnsureBackendAvailableAsync();
        });
    }

    private async Task ApplyPresetAsync(string? presetId)
    {
        if (string.IsNullOrWhiteSpace(presetId))
        {
            return;
        }

        var preset = Presets.FirstOrDefault(p => p.Id == presetId);
        if (preset is null)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            PresetStatusText = $"Applying \"{preset.Name}\" preset...";

            var recommendedProvider = preset.RecommendedProviders.FirstOrDefault(p =>
                _stateService.ProviderOptions.Contains(p, StringComparer.OrdinalIgnoreCase))
                ?? _stateService.ProviderOptions[0];

            SelectedProvider = recommendedProvider;

            try
            {
                await _stateService.ApplyPresetAsync(preset, recommendedProvider);
                PresetStatusText = $"Applied \"{preset.Name}\" preset: {preset.DefaultSymbols.Length} symbols, " +
                                   $"{(preset.SubscribeDepth ? "L2 depth" : "L1 only")}, " +
                                   $"{(preset.EnableBackfill ? "backfill enabled" : "no backfill")}.";
                _notificationSink.NotifySuccess("Setup Preset", $"\"{preset.Name}\" preset applied successfully.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PresetStatusText = $"Failed to apply preset: {ex.Message}";
                await _notificationSink.NotifyErrorAsync("Setup Preset", $"Failed to apply preset: {ex.Message}");
            }
        });
    }

    private async Task RefreshBackendStatusAsync()
    {
        await RunBusyAsync(RefreshBackendStatusCoreAsync);
    }

    private async Task StartBackendAsync()
    {
        await RunBusyAsync(async () =>
        {
            var result = await _stateService.StartBackendAsync();
            if (result.Success)
            {
                _notificationSink.NotifyInfo("Backend", result.Message);
            }
            else
            {
                _notificationSink.NotifyWarning("Backend", result.Message);
            }

            await RefreshBackendStatusCoreAsync();
        });
    }

    private async Task SaveAndContinueAsync()
    {
        await RunBusyAsync(async () =>
        {
            ValidationStatusText = "Saving configuration...";

            if (!TryValidateInputs(out var provider, out var storageLocation))
            {
                return;
            }

            var saveResult = await _stateService.SaveConfigurationAsync(provider, storageLocation);
            if (!saveResult.Success)
            {
                ValidationStatusText = saveResult.Message;
                await _notificationSink.NotifyErrorAsync("Setup Wizard", "Failed to save configuration.");
                return;
            }

            ConfigStatusText = saveResult.Message;
            _stateService.SaveApiKeys(NasdaqApiKey, OpenFigiApiKey);

            var startResult = await _stateService.StartBackendAsync();
            if (!startResult.Success)
            {
                ValidationStatusText = $"Saved config, but backend start failed: {startResult.Message}";
                _notificationSink.NotifyWarning("Setup Wizard", "Configuration saved, but backend is offline.");
                return;
            }

            var backendResult = await _stateService.CheckBackendAsync();
            ApplyBackendResult(backendResult);
            if (!backendResult.IsHealthy)
            {
                ValidationStatusText = $"Saved config, but backend is unreachable: {backendResult.Message}";
                _notificationSink.NotifyWarning("Setup Wizard", "Configuration saved, but backend is offline.");
                return;
            }

            ValidationStatusText = "Configuration saved and backend verified. Opening Strategy Workspace...";
            _notificationSink.NotifySuccess("Setup Wizard", "Setup complete. Welcome!");

            _navigator.NavigateTo("StrategyShell");
        });
    }

    private async Task EnsureBackendAvailableAsync()
    {
        var backendResult = await _stateService.CheckBackendAsync();
        if (backendResult.IsHealthy)
        {
            ApplyBackendResult(backendResult);
            return;
        }

        BackendStatusText = "Starting backend service...";
        BackendStatusDetailText = "First run now auto-starts the backend service.";
        BackendStatusTone = "Warning";

        var startResult = await _stateService.StartBackendAsync();
        if (!startResult.Success)
        {
            _notificationSink.NotifyWarning("Backend", startResult.Message);
        }

        await RefreshBackendStatusCoreAsync();
    }

    private async Task RefreshBackendStatusCoreAsync()
    {
        BackendStatusText = "Checking backend status...";
        BackendStatusDetailText = "Testing backend health endpoint...";
        BackendStatusTone = "Warning";

        var result = await _stateService.CheckBackendAsync();
        ApplyBackendResult(result);
    }

    private void ApplyBackendResult(SetupWizardBackendCheckResult result)
    {
        if (result.IsHealthy)
        {
            BackendStatusTone = "Success";
            BackendStatusText = "Backend is running.";
            BackendStatusDetailText = $"Latency: {result.LatencyMs} ms - {result.ServiceStatusMessage}";
            return;
        }

        BackendStatusTone = "Error";
        BackendStatusText = "Backend not reachable.";
        BackendStatusDetailText = $"{result.Message} - {result.ServiceStatusMessage}";
    }

    private bool TryValidateInputs(out string provider, out string storageLocation)
    {
        provider = SelectedProvider;
        storageLocation = StorageLocation.Trim();

        if (string.IsNullOrWhiteSpace(provider))
        {
            ValidationStatusText = "Select a default provider before continuing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(storageLocation))
        {
            ValidationStatusText = "Enter a storage location before continuing.";
            return false;
        }

        return true;
    }

    private bool CanRunCommand() => !IsBusy;

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
