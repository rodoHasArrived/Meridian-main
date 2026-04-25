using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>Display model for a streaming provider status card.</summary>
public sealed class ProviderStatusModel
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public SolidColorBrush StatusColor { get; set; } = new(Colors.Gray);
    public string LatencyText { get; set; } = string.Empty;
    public string UptimeText { get; set; } = string.Empty;
    public string ActionText { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public ProviderSparklineItem? SparklineItem { get; set; }
}

/// <summary>Display model for a backfill provider card.</summary>
public sealed class BackfillProviderModel
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public SolidColorBrush StatusColor { get; set; } = new(Colors.Gray);
    public string RateLimitText { get; set; } = string.Empty;
    public string LastUsedText { get; set; } = string.Empty;
}

/// <summary>Display model for a connection history event row.</summary>
public sealed class ConnectionEventModel
{
    public string Message { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string TimeText { get; set; } = string.Empty;
    public SolidColorBrush EventColor { get; set; } = new(Colors.Gray);
}

/// <summary>Severity classification for connection history events.</summary>
public enum EventType : byte
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Display model for provider sparkline visualization with latency history and reconnect count.</summary>
public sealed class ProviderSparklineItem : INotifyPropertyChanged
{
    private static readonly SolidColorBrush SparklineGreen;
    private static readonly SolidColorBrush AmberBrush;
    private readonly Queue<double> _latencySamples = new(capacity: 60);
    private int _reconnectCount;
    private bool _reconnectBadgeAmber;
    private PointCollection _sparklinePoints = new();

    static ProviderSparklineItem()
    {
        SparklineGreen = new SolidColorBrush(Color.FromRgb(76, 175, 80));
        SparklineGreen.Freeze();

        AmberBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
        AmberBrush.Freeze();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProviderName { get; set; } = string.Empty;

    public PointCollection SparklinePoints
    {
        get => _sparklinePoints;
        private set => SetProperty(ref _sparklinePoints, value);
    }

    public int ReconnectCount
    {
        get => _reconnectCount;
        set
        {
            if (SetProperty(ref _reconnectCount, value))
            {
                ReconnectBadgeAmber = value > 0;
            }
        }
    }

    public bool ReconnectBadgeAmber
    {
        get => _reconnectBadgeAmber;
        private set => SetProperty(ref _reconnectBadgeAmber, value);
    }

    public SolidColorBrush SparklineStroke => SparklineGreen;
    public SolidColorBrush BadgeBackground => AmberBrush;

    public void AddLatencySample(double latencyMs)
    {
        _latencySamples.Enqueue(latencyMs);

        while (_latencySamples.Count > 60)
            _latencySamples.Dequeue();

        NormalizeSparkline();
    }

    private void NormalizeSparkline()
    {
        if (_latencySamples.Count == 0)
        {
            SparklinePoints = new PointCollection();
            return;
        }

        var points = new PointCollection();
        var maxLatency = _latencySamples.Max();
        if (maxLatency == 0)
            maxLatency = 1;

        double canvasHeight = 40.0;
        double canvasWidth = 280.0;
        double pointWidth = canvasWidth / Math.Max(_latencySamples.Count - 1, 1);

        int index = 0;
        foreach (var sample in _latencySamples)
        {
            var normalizedHeight = (sample / maxLatency) * canvasHeight;
            var y = canvasHeight - normalizedHeight;
            var x = index * pointWidth;
            points.Add(new Point(x, y));
            index++;
        }

        SparklinePoints = points;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
