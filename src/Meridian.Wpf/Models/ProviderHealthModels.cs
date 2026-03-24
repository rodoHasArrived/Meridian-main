using System;
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
