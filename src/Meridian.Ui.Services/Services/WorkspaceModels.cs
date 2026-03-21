using System;
using System.Collections.Generic;

namespace Meridian.Ui.Services;

/// <summary>
/// Workspace template definition.
/// </summary>
public sealed class WorkspaceTemplate
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public WorkspaceCategory Category { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<WorkspacePage> Pages { get; set; } = new();
    public Dictionary<string, WidgetPosition> WidgetLayout { get; set; } = new();
    public Dictionary<string, string> Filters { get; set; } = new();
    public WindowBounds? WindowBounds { get; set; }
}

/// <summary>
/// Workspace category.
/// </summary>
public enum WorkspaceCategory : byte
{
    Monitoring,
    Backfill,
    Storage,
    Analysis,
    Custom
}

/// <summary>
/// Page within a workspace.
/// </summary>
public sealed class WorkspacePage
{
    public string PageTag { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public double ScrollPosition { get; set; }
    public Dictionary<string, object> PageState { get; set; } = new();
}

/// <summary>
/// Widget position in a workspace layout.
/// </summary>
public sealed class WidgetPosition
{
    public int Row { get; set; }
    public int Column { get; set; }
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    public bool IsVisible { get; set; } = true;
    public bool IsExpanded { get; set; } = true;
}

/// <summary>
/// Window bounds for multi-monitor support.
/// </summary>
public sealed class WindowBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? MonitorId { get; set; }
    public bool IsMaximized { get; set; }
}

/// <summary>
/// Session state for restore.
/// </summary>
public sealed class SessionState
{
    public string ActivePageTag { get; set; } = "Dashboard";
    public List<WorkspacePage> OpenPages { get; set; } = new();
    public Dictionary<string, WidgetPosition> WidgetLayout { get; set; } = new();
    public Dictionary<string, string> ActiveFilters { get; set; } = new();
    public WindowBounds? WindowBounds { get; set; }
    public DateTime SavedAt { get; set; }
    public string? ActiveWorkspaceId { get; set; }
}

/// <summary>
/// Workspace event args.
/// </summary>
public sealed class WorkspaceEventArgs : EventArgs
{
    public WorkspaceTemplate? Workspace { get; set; }
}
