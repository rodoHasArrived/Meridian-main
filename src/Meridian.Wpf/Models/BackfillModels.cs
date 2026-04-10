using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>Symbol progress information for backfill tracking.</summary>
public sealed class SymbolProgressInfo
{
    public string Symbol { get; set; } = string.Empty;
    public double Progress { get; set; }
    public string BarsText { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public SolidColorBrush StatusBackground { get; set; } = new(Color.FromArgb(40, 139, 148, 158));
}

/// <summary>Scheduled job information.</summary>
public sealed class ScheduledJobInfo
{
    public string Name { get; set; } = string.Empty;
    public string NextRun { get; set; } = string.Empty;
}

/// <summary>Resumable job information for checkpoint-based resume.</summary>
public sealed class ResumableJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string SymbolsSummary { get; set; } = string.Empty;
    public int PendingCount { get; set; }
    public int TotalBarsDownloaded { get; set; }
    public string DateRange { get; set; } = string.Empty;
}

/// <summary>Gap analysis item for the gap preview.</summary>
public sealed class GapAnalysisItem
{
    public string Symbol { get; set; } = string.Empty;
    public int CoveragePercent { get; set; }
    public string CoverageText { get; set; } = string.Empty;
    public int GapDays { get; set; }
    public string GapDaysText { get; set; } = string.Empty;
    public SolidColorBrush CoverageBrush { get; set; } = new(Color.FromRgb(63, 185, 80));
    public double CoverageWidth { get; set; }
}
