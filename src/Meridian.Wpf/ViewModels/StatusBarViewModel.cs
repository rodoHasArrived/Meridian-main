using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Services;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the persistent status bar at the bottom of MainWindow.
/// Displays live system state including provider health, throughput, backfills, and errors.
/// Uses PeriodicTimer on a background thread; marshals UI updates via Dispatcher.InvokeAsync.
/// </summary>
public sealed class StatusBarViewModel : BindableBase, IDisposable
{
    // ── Frozen brush resources (cache all static brushes) ──────────────────────

    private static readonly Brush _greenBrush = CreateAndFreezeBrush(76, 175, 80);
    private static readonly Brush _amberBrush = CreateAndFreezeBrush(255, 152, 0);
    private static readonly Brush _redBrush = CreateAndFreezeBrush(244, 67, 54);
    private static readonly Brush _transparentBrush = Brushes.Transparent;

    private static Brush CreateAndFreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly IStatusService _statusService;
    private readonly INotificationService _notificationService;
    
    private PeriodicTimer? _timer;
    private CancellationTokenSource? _cts;

    private string _backendStatus = "Disconnected";
    public string BackendStatus 
    { 
        get => _backendStatus; 
        private set => SetProperty(ref _backendStatus, value); 
    }

    private Brush _statusDotBrush;
    public Brush StatusDotBrush 
    { 
        get => _statusDotBrush; 
        private set => SetProperty(ref _statusDotBrush, value); 
    }

    private int _activeProviderCount = 0;
    public int ActiveProviderCount 
    { 
        get => _activeProviderCount; 
        private set => SetProperty(ref _activeProviderCount, value); 
    }

    private int _totalProviderCount = 0;
    public int TotalProviderCount 
    { 
        get => _totalProviderCount; 
        private set => SetProperty(ref _totalProviderCount, value); 
    }

    private string _throughputLabel = "0 ev/s";
    public string ThroughputLabel 
    { 
        get => _throughputLabel; 
        private set => SetProperty(ref _throughputLabel, value); 
    }

    private int _activeBackfillCount = 0;
    public int ActiveBackfillCount 
    { 
        get => _activeBackfillCount; 
        private set => SetProperty(ref _activeBackfillCount, value); 
    }

    private int _errorCount = 0;
    public int ErrorCount 
    { 
        get => _errorCount; 
        private set => SetProperty(ref _errorCount, value); 
    }

    private string _utcTime = DateTime.UtcNow.ToString("HH:mm:ss") + " UTC";
    public string UtcTime 
    { 
        get => _utcTime; 
        private set => SetProperty(ref _utcTime, value); 
    }

    private bool _hasErrors = false;
    public bool HasErrors 
    { 
        get => _hasErrors; 
        private set => SetProperty(ref _hasErrors, value); 
    }

    private Brush _errorBadgeBrush;
    public Brush ErrorBadgeBrush 
    { 
        get => _errorBadgeBrush; 
        private set => SetProperty(ref _errorBadgeBrush, value); 
    }

    public StatusBarViewModel(
        IStatusService statusService,
        INotificationService notificationService)
    {
        _statusService = statusService ?? throw new ArgumentNullException(nameof(statusService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _statusDotBrush = _greenBrush;
        _errorBadgeBrush = _transparentBrush;
    }

    /// <summary>
    /// Starts the periodic status update timer (1-second interval).
    /// Call this after the window loads.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_timer != null)
            return;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        _ = Task.Run(async () => await UpdateLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>
    /// Periodic update loop: runs on background thread, marshals UI updates.
    /// </summary>
    private async Task UpdateLoopAsync(CancellationToken ct)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(ct))
            {
                await RefreshStatusAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            // Log but don't crash the timer loop
            System.Diagnostics.Debug.WriteLine($"Status bar update error: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes all status bar metrics from services.
    /// Marshals updates to UI thread.
    /// </summary>
    private async Task RefreshStatusAsync(CancellationToken ct)
    {
        try
        {
            // Update UTC time on every tick
            var currentTime = DateTime.UtcNow.ToString("HH:mm:ss");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UtcTime = currentTime + " UTC";
            });

            // Query status service for metrics
            var status = await _statusService.GetStatusAsync(ct);
            if (status != null)
            {
                // Calculate throughput: events per second
                var throughputPerSec = (long)(status.Metrics?.EventsPerSecond ?? 0);
                var throughputLabel = throughputPerSec > 0
                    ? $"{(throughputPerSec / 1000.0):F1}K ev/s"
                    : $"{throughputPerSec} ev/s";

                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ThroughputLabel = throughputLabel;
                });
            }

            // Update backend status based on provider health
            // For now: simulate with simple logic (would query provider health service in full impl)
            var newStatus = "Connected";
            var newDotBrush = _greenBrush;

            if (status?.IsConnected == false)
            {
                newStatus = "Disconnected";
                newDotBrush = _redBrush;
            }
            else if (ActiveProviderCount < TotalProviderCount)
            {
                newStatus = "Degraded";
                newDotBrush = _amberBrush;
            }

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                BackendStatus = newStatus;
                StatusDotBrush = newDotBrush;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Refresh status error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _timer?.Dispose();
        _cts = null;
        _timer = null;
    }
}
