using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Meridian.Contracts.Api;
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

    // Drop rate above this fraction (1%) flips backend status to Degraded.
    internal const double DegradedDropRateThreshold = 0.01;

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

    // Cumulative dropped count from the previous tick; used to compute the delta
    // shown in the error badge so operators see *new* drops rather than lifetime totals.
    private long _previousDroppedTotal;
    private bool _hasPreviousDrops;

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

    private string _statusToolTip = "Connecting…";
    /// <summary>Free-form tooltip describing the most recent status snapshot.</summary>
    public string StatusToolTip
    {
        get => _statusToolTip;
        private set => SetProperty(ref _statusToolTip, value);
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
    public Task StartAsync(CancellationToken ct = default)
    {
        if (_timer != null)
            return Task.CompletedTask;

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

        _ = Task.Run(async () => await UpdateLoopAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
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
        catch (Exception)
        {
            // Status bar update error — non-fatal, timer loop continues on next tick
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
            var throughput = status?.Metrics?.EventsPerSecond ?? 0;
            var dropRate = status?.Metrics?.DropRate ?? 0;
            var droppedTotal = status?.Metrics?.Dropped ?? 0;

            var throughputLabel = FormatThroughput(throughput);

            // Compute new drops since the last tick. The first sample only seeds the
            // baseline so a long-running collector does not light the badge red on
            // first connect with a large lifetime total.
            var newDrops = 0L;
            if (_hasPreviousDrops)
            {
                newDrops = Math.Max(0, droppedTotal - _previousDroppedTotal);
            }
            _previousDroppedTotal = droppedTotal;
            _hasPreviousDrops = true;

            var (backendStatus, dotBrush) = DeriveBackendStatus(status, dropRate);
            var toolTip = BuildToolTip(backendStatus, throughput, dropRate, droppedTotal);
            var totalNewDrops = newDrops > int.MaxValue ? int.MaxValue : (int)newDrops;
            var errorBadgeBrush = totalNewDrops > 0 ? _redBrush : _transparentBrush;

            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ThroughputLabel = throughputLabel;
                BackendStatus = backendStatus;
                StatusDotBrush = dotBrush;
                ErrorCount = totalNewDrops;
                HasErrors = totalNewDrops > 0;
                ErrorBadgeBrush = errorBadgeBrush;
                StatusToolTip = toolTip;
            });
        }
        catch (Exception)
        {
            // Refresh error is non-fatal — status bar will retry on the next timer tick
        }
    }

    /// <summary>
    /// Formats a per-second throughput rate using K (thousand) and M (million) suffixes.
    /// Designed to keep the status bar readable at any pipeline rate.
    /// </summary>
    internal static string FormatThroughput(double eventsPerSecond)
    {
        if (eventsPerSecond < 0 || double.IsNaN(eventsPerSecond) || double.IsInfinity(eventsPerSecond))
        {
            return "0 ev/s";
        }

        if (eventsPerSecond < 1_000)
        {
            return ((long)eventsPerSecond).ToString(CultureInfo.InvariantCulture) + " ev/s";
        }

        if (eventsPerSecond < 1_000_000)
        {
            return (eventsPerSecond / 1_000.0).ToString("F1", CultureInfo.InvariantCulture) + "K ev/s";
        }

        return (eventsPerSecond / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M ev/s";
    }

    /// <summary>
    /// Returns the operator-facing status label and dot colour derived from a status snapshot.
    /// "Disconnected" overrides everything; otherwise drop rate above
    /// <see cref="DegradedDropRateThreshold"/> downgrades to "Degraded".
    /// </summary>
    internal static (string Status, Brush DotBrush) DeriveBackendStatus(StatusResponse? status, double dropRate)
    {
        if (status is null || status.IsConnected == false)
        {
            return ("Disconnected", _redBrush);
        }

        if (dropRate > DegradedDropRateThreshold)
        {
            return ("Degraded", _amberBrush);
        }

        return ("Connected", _greenBrush);
    }

    private static string BuildToolTip(string backendStatus, double eventsPerSecond, double dropRate, long droppedTotal)
    {
        var rate = FormatThroughput(eventsPerSecond);
        var dropPct = (dropRate * 100).ToString("F2", CultureInfo.InvariantCulture);
        return $"Status: {backendStatus}\nThroughput: {rate}\nDrop rate: {dropPct}%\nDropped (lifetime): {droppedTotal:N0}";
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
