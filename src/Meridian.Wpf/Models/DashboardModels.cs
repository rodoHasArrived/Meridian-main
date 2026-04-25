using System.Windows.Media;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Models;

/// <summary>Activity item for the dashboard activity feed.</summary>
public sealed class DashboardActivityItem
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RelativeTime { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = string.Empty;
    public Brush IconBackground { get; set; } = Brushes.Transparent;
}

/// <summary>Symbol performance row for the dashboard symbol list.</summary>
public sealed class SymbolPerformanceItem
{
    public string Symbol { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusColor { get; set; } = Brushes.Transparent;
    public string EventRate { get; set; } = string.Empty;
    public string TotalEvents { get; set; } = string.Empty;
    public string LastEventTime { get; set; } = string.Empty;
    public string HealthScore { get; set; } = string.Empty;
    public Brush HealthColor { get; set; } = Brushes.Transparent;
    public string HealthIcon { get; set; } = string.Empty;
    public PointCollection TrendPoints { get; set; } = new();
    public Brush TrendColor { get; set; } = Brushes.Transparent;
}

/// <summary>Symbol freshness row for the data freshness panel.</summary>
public sealed class SymbolFreshnessItem
{
    public string Symbol { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Transparent;
}

/// <summary>Operational KPI item for the portfolio operations dashboard.</summary>
public sealed class DashboardOperationsMetricItem
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Brush AccentBrush { get; set; } = Brushes.Transparent;
}

/// <summary>Data-quality category summary for the portfolio operations dashboard.</summary>
public sealed class DashboardDataQualityCategoryItem
{
    public string Category { get; set; } = string.Empty;
    public string Count { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public double Completion { get; set; }
    public Brush StatusBrush { get; set; } = Brushes.Transparent;
}

/// <summary>Upcoming maturity row for the portfolio operations dashboard.</summary>
public sealed class DashboardUpcomingMaturityItem
{
    public string Issuer { get; set; } = string.Empty;
    public string MaturityDate { get; set; } = string.Empty;
    public string ParValue { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Transparent;
}

/// <summary>Holdings snapshot row for the portfolio operations dashboard.</summary>
public sealed class DashboardHoldingSnapshotItem
{
    public string Cusip { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string AssetClass { get; set; } = string.Empty;
    public string Rating { get; set; } = string.Empty;
    public string Coupon { get; set; } = string.Empty;
    public string Maturity { get; set; } = string.Empty;
    public string ParValue { get; set; } = string.Empty;
    public string BookValue { get; set; } = string.Empty;
    public string MarketValue { get; set; } = string.Empty;
    public string UnrealizedGainLoss { get; set; } = string.Empty;
    public Brush UnrealizedGainLossBrush { get; set; } = Brushes.Transparent;
    public string DataStatus { get; set; } = string.Empty;
    public Brush DataStatusBackground { get; set; } = Brushes.Transparent;
    public Brush DataStatusBorderBrush { get; set; } = Brushes.Transparent;
    public Brush DataStatusForeground { get; set; } = Brushes.Transparent;
}

/// <summary>Portfolio data service status item for the dashboard status banner.</summary>
public sealed class DashboardServiceStatusItem
{
    public string ServiceName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public Brush StatusBrush { get; set; } = Brushes.Transparent;
}

/// <summary>
/// Severity level for integrity events.
/// Used for badge counting without fragile brush-reference comparisons (P3 fix).
/// </summary>
public enum IntegrityEventSeverity : byte
{
    Info,
    Warning,
    Critical
}

/// <summary>
/// Integrity event row for the dashboard integrity panel.
/// Inherits <see cref="BindableBase"/> so <see cref="IsNotAcknowledged"/> supports UI notification (M8 fix).
/// </summary>
public sealed class IntegrityEventItem : BindableBase
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string EventTypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RelativeTime { get; set; } = string.Empty;
    public Brush SeverityColor { get; set; } = Brushes.Transparent;
    public IntegrityEventSeverity Severity { get; set; } = IntegrityEventSeverity.Warning;

    private bool _isNotAcknowledged = true;

    public bool IsNotAcknowledged
    {
        get => _isNotAcknowledged;
        set => SetProperty(ref _isNotAcknowledged, value);
    }
}
