using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Service Manager page.
/// Wraps <see cref="BackendServiceManager"/> and <see cref="LoggingService"/> with
/// bindable status properties and async command objects.
/// </summary>
public sealed class ServiceManagerViewModel : BindableBase
{
    private readonly BackendServiceManager _serviceManager;
    private readonly LoggingService _loggingService;

    private string _statusText = "Unknown";
    private string _healthText = "Unknown";
    private string _processIdText = "-";
    private string _executablePathText = "Not registered";
    private string _operationResultText = string.Empty;
    private bool _isBusy;
    private bool _isRunning;
    private bool _isInstalled;

    internal ServiceManagerViewModel(BackendServiceManager serviceManager, LoggingService loggingService)
    {
        _serviceManager = serviceManager;
        _loggingService = loggingService;

        InstallCommand = new AsyncRelayCommand(InstallAsync, () => CanInstall);
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
        StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
        RestartCommand = new AsyncRelayCommand(RestartAsync, () => CanRestart);
        RefreshCommand = new AsyncRelayCommand(RefreshStatusAsync, () => CanRefresh);
    }

    // ── Status properties ──────────────────────────────────────────────────

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string HealthText
    {
        get => _healthText;
        private set => SetProperty(ref _healthText, value);
    }

    public string ProcessIdText
    {
        get => _processIdText;
        private set => SetProperty(ref _processIdText, value);
    }

    public string ExecutablePathText
    {
        get => _executablePathText;
        private set => SetProperty(ref _executablePathText, value);
    }

    public string OperationResultText
    {
        get => _operationResultText;
        private set => SetProperty(ref _operationResultText, value);
    }

    public string ServiceStateBadgeText => IsRunning
        ? "Running"
        : IsInstalled
            ? "Installed / stopped"
            : "Not installed";

    public string RecommendedActionText => IsBusy
        ? "An operation is in progress. Wait for the current lifecycle action to complete before issuing another command."
        : !IsInstalled
            ? "Install the workstation backend on this machine before opening execution-dependent pages."
            : !IsRunning
                ? "Start the backend before using live blotter, execution, or service-dependent diagnostics."
                : "Service is running. Refresh after configuration or registration changes to confirm the current runtime posture.";

    public string RuntimePostureText => IsRunning
        ? $"Health posture: {HealthText}."
        : "Runtime posture: offline until the backend service is started.";

    public string RegistrationPostureText => string.Equals(ExecutablePathText, "Not registered", StringComparison.OrdinalIgnoreCase)
        ? "No registered executable path is available yet."
        : "Registered executable path is available for lifecycle management.";

    // ── State flags ────────────────────────────────────────────────────────

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyCanExecuteChanged();
                RaiseDerivedStateChanged();
            }
        }
    }

    private bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                NotifyCanExecuteChanged();
                RaiseDerivedStateChanged();
            }
        }
    }

    private bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (SetProperty(ref _isInstalled, value))
            {
                NotifyCanExecuteChanged();
                RaiseDerivedStateChanged();
            }
        }
    }

    // ── CanExecute helpers (bound by XAML IsEnabled) ───────────────────────

    public bool CanInstall => !IsBusy;
    public bool CanStart => !IsBusy && IsInstalled && !IsRunning;
    public bool CanStop => !IsBusy && IsRunning;
    public bool CanRestart => !IsBusy && IsInstalled;
    public bool CanRefresh => !IsBusy;

    // ── Commands ───────────────────────────────────────────────────────────

    public IAsyncRelayCommand InstallCommand { get; }
    public IAsyncRelayCommand StartCommand { get; }
    public IAsyncRelayCommand StopCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }

    // ── Public API ─────────────────────────────────────────────────────────

    public Task InitializeAsync(CancellationToken ct = default) => RefreshStatusAsync(ct);

    // ── Private implementation ─────────────────────────────────────────────

    private async Task InstallAsync(CancellationToken ct = default)
        => await ExecuteOperationAsync(() => _serviceManager.InstallAsync(ct: ct), ct);

    private async Task StartAsync(CancellationToken ct = default)
        => await ExecuteOperationAsync(() => _serviceManager.StartAsync(ct), ct);

    private async Task StopAsync(CancellationToken ct = default)
        => await ExecuteOperationAsync(() => _serviceManager.StopAsync(ct), ct);

    private async Task RestartAsync(CancellationToken ct = default)
        => await ExecuteOperationAsync(() => _serviceManager.RestartAsync(ct), ct);

    private async Task ExecuteOperationAsync(
        Func<Task<BackendServiceOperationResult>> operation,
        CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var result = await operation();
            OperationResultText = result.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OperationResultText = $"Operation failed: {ex.Message}";
            _loggingService.LogError("Service manager operation failed", ex);
        }
        finally
        {
            await RefreshStatusAsync(ct);
            IsBusy = false;
        }
    }

    private async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        var status = await _serviceManager.GetStatusAsync(ct);

        StatusText = status.IsRunning ? "Running" : "Stopped";
        HealthText = status.IsHealthy ? "Healthy" : "Not healthy";
        ProcessIdText = status.ProcessId?.ToString() ?? "-";
        ExecutablePathText = status.ExecutablePath ?? "Not registered";

        if (string.IsNullOrWhiteSpace(OperationResultText))
        {
            OperationResultText = status.StatusMessage;
        }

        IsRunning = status.IsRunning;
        IsInstalled = status.IsInstalled;
        RaiseDerivedStateChanged();
    }

    private void NotifyCanExecuteChanged()
    {
        RaisePropertyChanged(nameof(CanInstall));
        RaisePropertyChanged(nameof(CanStart));
        RaisePropertyChanged(nameof(CanStop));
        RaisePropertyChanged(nameof(CanRestart));
        RaisePropertyChanged(nameof(CanRefresh));
        InstallCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private void RaiseDerivedStateChanged()
    {
        RaisePropertyChanged(nameof(ServiceStateBadgeText));
        RaisePropertyChanged(nameof(RecommendedActionText));
        RaisePropertyChanged(nameof(RuntimePostureText));
        RaisePropertyChanged(nameof(RegistrationPostureText));
    }
}
