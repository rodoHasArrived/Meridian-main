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
    private readonly IServiceManagerBackend _serviceManager;
    private readonly LoggingService _loggingService;

    private string _statusText = "Unknown";
    private string _healthText = "Unknown";
    private string _processIdText = "-";
    private string _executablePathText = "Not registered";
    private string _operationResultText = string.Empty;
    private string _statusLoadStateText = "Status not loaded";
    private string _statusLoadDetailText = "Refresh service status to verify installation, health, and runtime registration.";
    private bool _isBusy;
    private bool _isStatusLoading;
    private bool _isStatusLoadFailed;
    private bool _isRunning;
    private bool _isInstalled;

    internal ServiceManagerViewModel(BackendServiceManager serviceManager, LoggingService loggingService)
        : this(new BackendServiceManagerAdapter(serviceManager), loggingService)
    {
    }

    internal ServiceManagerViewModel(IServiceManagerBackend serviceManager, LoggingService loggingService)
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

    public string StatusLoadStateText
    {
        get => _statusLoadStateText;
        private set => SetProperty(ref _statusLoadStateText, value);
    }

    public string StatusLoadDetailText
    {
        get => _statusLoadDetailText;
        private set => SetProperty(ref _statusLoadDetailText, value);
    }

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

    public bool IsStatusLoading
    {
        get => _isStatusLoading;
        private set
        {
            if (SetProperty(ref _isStatusLoading, value))
            {
                NotifyCanExecuteChanged();
                RaisePropertyChanged(nameof(IsStatusLoadInfoVisible));
            }
        }
    }

    public bool IsStatusLoadFailed
    {
        get => _isStatusLoadFailed;
        private set
        {
            if (SetProperty(ref _isStatusLoadFailed, value))
            {
                RaisePropertyChanged(nameof(IsStatusLoadInfoVisible));
            }
        }
    }

    public bool IsStatusLoadInfoVisible => IsStatusLoading || IsStatusLoadFailed;

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

    public bool CanInstall => !IsBusy && !IsStatusLoading;
    public bool CanStart => !IsBusy && !IsStatusLoading && IsInstalled && !IsRunning;
    public bool CanStop => !IsBusy && !IsStatusLoading && IsRunning;
    public bool CanRestart => !IsBusy && !IsStatusLoading && IsInstalled;
    public bool CanRefresh => !IsBusy && !IsStatusLoading;

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
            try
            {
                await RefreshStatusAsync(ct);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    private async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        IsStatusLoading = true;
        IsStatusLoadFailed = false;
        StatusLoadStateText = "Refreshing service status";
        StatusLoadDetailText = "Checking installation, process registration, and backend health.";

        try
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
            StatusLoadStateText = "Status refreshed";
            StatusLoadDetailText = string.IsNullOrWhiteSpace(status.StatusMessage)
                ? "Service status is current."
                : status.StatusMessage;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            StatusText = "Unknown";
            HealthText = "Unknown";
            ProcessIdText = "-";
            ExecutablePathText = "Not registered";
            OperationResultText = $"Status refresh failed: {ex.Message}";
            IsRunning = false;
            IsInstalled = false;
            IsStatusLoadFailed = true;
            StatusLoadStateText = "Service status unavailable";
            StatusLoadDetailText = ex.Message;
            _loggingService.LogError("Service manager status refresh failed", ex);
        }
        finally
        {
            IsStatusLoading = false;
            NotifyCanExecuteChanged();
            RaiseDerivedStateChanged();
        }
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

internal interface IServiceManagerBackend
{
    Task<BackendServiceOperationResult> InstallAsync(CancellationToken ct = default);
    Task<BackendServiceOperationResult> StartAsync(CancellationToken ct = default);
    Task<BackendServiceOperationResult> StopAsync(CancellationToken ct = default);
    Task<BackendServiceOperationResult> RestartAsync(CancellationToken ct = default);
    Task<BackendServiceStatus> GetStatusAsync(CancellationToken ct = default);
}

internal sealed class BackendServiceManagerAdapter : IServiceManagerBackend
{
    private readonly BackendServiceManager _serviceManager;

    public BackendServiceManagerAdapter(BackendServiceManager serviceManager)
    {
        _serviceManager = serviceManager;
    }

    public Task<BackendServiceOperationResult> InstallAsync(CancellationToken ct = default)
        => _serviceManager.InstallAsync(ct: ct);

    public Task<BackendServiceOperationResult> StartAsync(CancellationToken ct = default)
        => _serviceManager.StartAsync(ct);

    public Task<BackendServiceOperationResult> StopAsync(CancellationToken ct = default)
        => _serviceManager.StopAsync(ct);

    public Task<BackendServiceOperationResult> RestartAsync(CancellationToken ct = default)
        => _serviceManager.RestartAsync(ct);

    public Task<BackendServiceStatus> GetStatusAsync(CancellationToken ct = default)
        => _serviceManager.GetStatusAsync(ct);
}
