using System.Threading;
using Meridian.Application.Logging;
using Meridian.Application.Pipeline;
using Meridian.Application.Services;
using Serilog;

namespace Meridian.Application.Monitoring;

/// <summary>
/// Monitors pipeline backpressure and sends alerts when thresholds are exceeded.
/// Implements MON-18: Backpressure Alert.
/// </summary>
public sealed class BackpressureAlertService : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<BackpressureAlertService>();
    private readonly BackpressureAlertConfig _config;
    private readonly Timer _checkTimer;
    private readonly DailySummaryWebhook? _webhook;
    private volatile bool _isDisposed;

    // State tracking
    private Func<PipelineStatistics>? _pipelineStatsProvider;
    private long _lastDroppedCount;
    private bool _isInBackpressureState;
    private DateTimeOffset _backpressureStartTime;
    private int _consecutiveHighUtilization;
    private DateTimeOffset _lastAlertTime = DateTimeOffset.MinValue;

    /// <summary>
    /// Event raised when backpressure is detected.
    /// </summary>
    public event Action<BackpressureAlert>? OnBackpressureDetected;

    /// <summary>
    /// Event raised when backpressure is resolved.
    /// </summary>
    public event Action<BackpressureResolvedEvent>? OnBackpressureResolved;

    public BackpressureAlertService(BackpressureAlertConfig? config = null, DailySummaryWebhook? webhook = null)
    {
        _config = config ?? BackpressureAlertConfig.Default;
        _webhook = webhook;
        _checkTimer = new Timer(
            CheckBackpressure,
            null,
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds),
            TimeSpan.FromSeconds(_config.CheckIntervalSeconds));

        _log.Information(
            "BackpressureAlertService initialized. Thresholds: warning={WarningPercent}%, critical={CriticalPercent}%",
            _config.WarningUtilizationPercent,
            _config.CriticalUtilizationPercent);
    }

    /// <summary>
    /// Registers the pipeline statistics provider.
    /// </summary>
    public void RegisterPipelineProvider(Func<PipelineStatistics> provider)
    {
        _pipelineStatsProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Gets the current backpressure status.
    /// </summary>
    public BackpressureStatus GetStatus()
    {
        var stats = _pipelineStatsProvider?.Invoke();
        if (stats == null)
        {
            return new BackpressureStatus(
                IsActive: false,
                Level: BackpressureLevel.None,
                QueueUtilization: 0,
                DroppedEvents: 0,
                DropRate: 0,
                Duration: TimeSpan.Zero,
                Message: "Pipeline statistics not available");
        }

        var dropRate = stats.Value.PublishedCount > 0
            ? (double)stats.Value.DroppedCount / stats.Value.PublishedCount * 100
            : 0;

        var level = GetBackpressureLevel(stats.Value.QueueUtilization, dropRate);
        var duration = _isInBackpressureState
            ? DateTimeOffset.UtcNow - _backpressureStartTime
            : TimeSpan.Zero;

        return new BackpressureStatus(
            IsActive: _isInBackpressureState,
            Level: level,
            QueueUtilization: stats.Value.QueueUtilization,
            DroppedEvents: stats.Value.DroppedCount,
            DropRate: dropRate,
            Duration: duration,
            Message: GetStatusMessage(level, stats.Value.QueueUtilization, dropRate));
    }

    private void CheckBackpressure(object? state)
    {
        if (_isDisposed)
            return;

        // Timer callbacks must not be async void; fire-and-forget with logging wrapper
        _ = CheckBackpressureCoreAsync();
    }

    private async Task CheckBackpressureCoreAsync(CancellationToken ct = default)
    {
        try
        {
            var stats = _pipelineStatsProvider?.Invoke();
            if (stats == null)
                return;

            var utilization = stats.Value.QueueUtilization;
            var droppedDelta = stats.Value.DroppedCount - _lastDroppedCount;
            var dropRate = stats.Value.PublishedCount > 0
                ? (double)stats.Value.DroppedCount / stats.Value.PublishedCount * 100
                : 0;

            _lastDroppedCount = stats.Value.DroppedCount;

            var level = GetBackpressureLevel(utilization, dropRate);
            var wasInBackpressure = _isInBackpressureState;

            // Track consecutive high utilization
            if (utilization >= _config.WarningUtilizationPercent || droppedDelta > 0)
            {
                _consecutiveHighUtilization++;
                if (!_isInBackpressureState && _consecutiveHighUtilization >= _config.ConsecutiveChecksBeforeAlert)
                {
                    _isInBackpressureState = true;
                    _backpressureStartTime = DateTimeOffset.UtcNow;
                }
            }
            else
            {
                _consecutiveHighUtilization = 0;
                if (_isInBackpressureState)
                {
                    _isInBackpressureState = false;
                    var duration = DateTimeOffset.UtcNow - _backpressureStartTime;

                    _log.Information("Backpressure resolved after {Duration}", duration);

                    try
                    {
                        OnBackpressureResolved?.Invoke(new BackpressureResolvedEvent(
                            ResolvedAt: DateTimeOffset.UtcNow,
                            Duration: duration,
                            TotalDropped: stats.Value.DroppedCount));
                    }
                    catch (Exception ex) { _log.Debug(ex, "Error invoking OnBackpressureResolved event"); }

                    if (_webhook != null && _config.SendWebhookOnResolved)
                    {
                        await SendWebhookAsync($"Backpressure resolved after {duration.TotalMinutes:F1} minutes");
                    }
                }
            }

            // Send alert if in backpressure state
            if (_isInBackpressureState && ShouldSendAlert(level))
            {
                var alert = new BackpressureAlert(
                    Level: level,
                    QueueUtilization: utilization,
                    DroppedEventsRecent: droppedDelta,
                    TotalDropped: stats.Value.DroppedCount,
                    DropRate: dropRate,
                    Duration: DateTimeOffset.UtcNow - _backpressureStartTime,
                    Timestamp: DateTimeOffset.UtcNow,
                    Message: GetAlertMessage(level, utilization, droppedDelta));

                _log.Warning(
                    "Backpressure {Level}: utilization={Utilization:F1}%, dropped={DroppedRecent} (rate: {DropRate:F2}%)",
                    level, utilization, droppedDelta, dropRate);

                try
                {
                    OnBackpressureDetected?.Invoke(alert);
                }
                catch (Exception ex) { _log.Debug(ex, "Error invoking OnBackpressureDetected event"); }

                if (_webhook != null && level >= BackpressureLevel.Warning)
                {
                    await SendWebhookAsync(alert.Message);
                }

                _lastAlertTime = DateTimeOffset.UtcNow;
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Error checking backpressure");
        }
    }

    private BackpressureLevel GetBackpressureLevel(double utilization, double dropRate)
    {
        if (utilization >= _config.CriticalUtilizationPercent || dropRate >= _config.CriticalDropRatePercent)
            return BackpressureLevel.Critical;

        if (utilization >= _config.WarningUtilizationPercent || dropRate >= _config.WarningDropRatePercent)
            return BackpressureLevel.Warning;

        return BackpressureLevel.None;
    }

    private bool ShouldSendAlert(BackpressureLevel level)
    {
        if (level == BackpressureLevel.None)
            return false;

        var timeSinceLastAlert = DateTimeOffset.UtcNow - _lastAlertTime;
        var minInterval = level == BackpressureLevel.Critical
            ? TimeSpan.FromSeconds(_config.CriticalAlertIntervalSeconds)
            : TimeSpan.FromSeconds(_config.WarningAlertIntervalSeconds);

        return timeSinceLastAlert >= minInterval;
    }

    private async Task SendWebhookAsync(string message, CancellationToken ct = default)
    {
        if (_webhook == null)
            return;

        try
        {
            await _webhook.SendMessageAsync(message, "Backpressure Alert");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send backpressure webhook");
        }
    }

    private static string GetStatusMessage(BackpressureLevel level, double utilization, double dropRate)
    {
        return level switch
        {
            BackpressureLevel.Critical => $"Critical backpressure: {utilization:F1}% queue, {dropRate:F2}% drop rate",
            BackpressureLevel.Warning => $"Warning: {utilization:F1}% queue, {dropRate:F2}% drop rate",
            _ => $"Normal: {utilization:F1}% queue utilization"
        };
    }

    private static string GetAlertMessage(BackpressureLevel level, double utilization, long droppedRecent)
    {
        var prefix = level == BackpressureLevel.Critical ? "CRITICAL" : "WARNING";
        return $"{prefix}: Pipeline backpressure detected. Queue at {utilization:F1}%, {droppedRecent} events dropped in last check.";
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _checkTimer.Dispose();
    }
}

/// <summary>
/// Configuration for backpressure alerts.
/// </summary>
public sealed record BackpressureAlertConfig
{
    /// <summary>
    /// Interval between backpressure checks in seconds.
    /// </summary>
    public int CheckIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// Queue utilization percentage that triggers a warning.
    /// </summary>
    public double WarningUtilizationPercent { get; init; } = 70;

    /// <summary>
    /// Queue utilization percentage that triggers a critical alert.
    /// </summary>
    public double CriticalUtilizationPercent { get; init; } = 90;

    /// <summary>
    /// Drop rate percentage that triggers a warning.
    /// </summary>
    public double WarningDropRatePercent { get; init; } = 1;

    /// <summary>
    /// Drop rate percentage that triggers a critical alert.
    /// </summary>
    public double CriticalDropRatePercent { get; init; } = 5;

    /// <summary>
    /// Number of consecutive checks with high utilization before alerting.
    /// </summary>
    public int ConsecutiveChecksBeforeAlert { get; init; } = 3;

    /// <summary>
    /// Minimum interval between warning alerts in seconds.
    /// </summary>
    public int WarningAlertIntervalSeconds { get; init; } = 300;

    /// <summary>
    /// Minimum interval between critical alerts in seconds.
    /// </summary>
    public int CriticalAlertIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Whether to send webhook when backpressure is resolved.
    /// </summary>
    public bool SendWebhookOnResolved { get; init; } = true;

    public static BackpressureAlertConfig Default => new();
}

/// <summary>
/// Backpressure severity level.
/// </summary>
public enum BackpressureLevel : byte
{
    None,
    Warning,
    Critical
}

/// <summary>
/// Current backpressure status.
/// </summary>
public readonly record struct BackpressureStatus(
    bool IsActive,
    BackpressureLevel Level,
    double QueueUtilization,
    long DroppedEvents,
    double DropRate,
    TimeSpan Duration,
    string Message
);

/// <summary>
/// Backpressure alert event.
/// </summary>
public readonly record struct BackpressureAlert(
    BackpressureLevel Level,
    double QueueUtilization,
    long DroppedEventsRecent,
    long TotalDropped,
    double DropRate,
    TimeSpan Duration,
    DateTimeOffset Timestamp,
    string Message
);

/// <summary>
/// Event raised when backpressure is resolved.
/// </summary>
public readonly record struct BackpressureResolvedEvent(
    DateTimeOffset ResolvedAt,
    TimeSpan Duration,
    long TotalDropped
);
