using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>Single data point for the quality trend sparkline.</summary>
public sealed class TrendPoint
{
    public TrendPoint(double score, string label)
    {
        Score = score;
        Label = label;
    }

    public double Score { get; }
    public string Label { get; }
}

/// <summary>Model for symbol quality display.</summary>
public sealed class SymbolQualityModel
{
    public string Symbol { get; set; } = string.Empty;
    public double Score { get; set; }
    public string ScoreFormatted { get; set; } = string.Empty;
    public string Grade { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Issues { get; set; } = string.Empty;
    public DateTimeOffset LastUpdate { get; set; }
    public string LastUpdateFormatted { get; set; } = string.Empty;
}

/// <summary>Model for gap display.</summary>
public sealed class GapModel
{
    public string GapId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
}

/// <summary>Model for alert display.</summary>
public sealed class AlertModel
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string AlertType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public Brush SeverityBrush { get; set; } = Brushes.Gray;
}

/// <summary>Model for anomaly display.</summary>
public sealed class AnomalyModel
{
    public string Symbol { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public SolidColorBrush SeverityColor { get; set; } = new(Colors.Gray);
}

/// <summary>Model for symbol drilldown issue display.</summary>
public sealed class DrilldownIssue
{
    public string Description { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public SolidColorBrush SeverityBrush { get; set; } = new(Colors.Gray);
}
