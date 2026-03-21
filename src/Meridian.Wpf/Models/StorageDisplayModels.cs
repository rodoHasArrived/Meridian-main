using System;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Information about a data file for a symbol.
/// </summary>
public sealed class DataFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public string FileSize { get; set; } = string.Empty;
    public string ModifiedDate { get; set; } = string.Empty;
    public string EventCount { get; set; } = string.Empty;
    public string FileIcon { get; set; } = "\uE7C3"; // Document icon
    public Brush? TypeBackground { get; set; }
}

/// <summary>
/// Information about a data gap.
/// </summary>
public sealed class DataGapInfo
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int MissingBars { get; set; }
    public string Description => $"{StartDate:yyyy-MM-dd} to {EndDate:yyyy-MM-dd} ({MissingBars} bars missing)";
}
