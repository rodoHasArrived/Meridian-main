using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WpfServices = Meridian.Wpf.Services;

using Meridian.Wpf.Services;
namespace Meridian.Wpf.Views;

public partial class ServiceManagerPage : Page
{
    private readonly BackendServiceManager _serviceManager;
    private readonly WpfServices.LoggingService _loggingService;
    private bool _busy;

    public ServiceManagerPage(
        BackendServiceManager serviceManager,
        WpfServices.LoggingService loggingService)
    {
        InitializeComponent();
        _serviceManager = serviceManager;
        _loggingService = loggingService;
    }

    private async void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteOperationAsync(() => _serviceManager.InstallAsync());
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteOperationAsync(() => _serviceManager.StartAsync());
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteOperationAsync(() => _serviceManager.StopAsync());
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteOperationAsync(() => _serviceManager.RestartAsync());
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshStatusAsync();
    }

    private async Task ExecuteOperationAsync(Func<Task<BackendServiceOperationResult>> operation, CancellationToken ct = default)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            SetBusy(true);
            var result = await operation();
            OperationResultText.Text = result.Message;
        }
        catch (Exception ex)
        {
            OperationResultText.Text = $"Operation failed: {ex.Message}";
            _loggingService.LogError("Service manager operation failed", ex);
        }
        finally
        {
            await RefreshStatusAsync();
            SetBusy(false);
        }
    }

    private async Task RefreshStatusAsync(CancellationToken ct = default)
    {
        var status = await _serviceManager.GetStatusAsync();

        StatusText.Text = status.IsRunning ? "Running" : "Stopped";
        HealthText.Text = status.IsHealthy ? "Healthy" : "Not healthy";
        ProcessIdText.Text = status.ProcessId?.ToString() ?? "-";
        ExecutablePathText.Text = status.ExecutablePath ?? "Not registered";
        if (string.IsNullOrWhiteSpace(OperationResultText.Text))
        {
            OperationResultText.Text = status.StatusMessage;
        }

        InstallButton.IsEnabled = !_busy;
        StartButton.IsEnabled = !_busy && status.IsInstalled && !status.IsRunning;
        StopButton.IsEnabled = !_busy && status.IsRunning;
        RestartButton.IsEnabled = !_busy && status.IsInstalled;
        RefreshButton.IsEnabled = !_busy;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        InstallButton.IsEnabled = !busy;
        StartButton.IsEnabled = !busy;
        StopButton.IsEnabled = !busy;
        RestartButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
    }
}
