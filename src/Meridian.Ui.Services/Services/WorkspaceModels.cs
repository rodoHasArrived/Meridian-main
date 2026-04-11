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
    public string PreferredPageTag { get; set; } = string.Empty;
    public WorkspaceCategory Category { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastActivatedAt { get; set; }
    public string? LastActivePageTag { get; set; }
    public List<WorkspacePage> Pages { get; set; } = new();
    public List<string> RecentPageTags { get; set; } = new();
    public Dictionary<string, WidgetPosition> WidgetLayout { get; set; } = new();
    public Dictionary<string, string> Filters { get; set; } = new();
    public Dictionary<string, string> Context { get; set; } = new();
    public WindowBounds? WindowBounds { get; set; }
    public SessionState? SessionSnapshot { get; set; }
}

/// <summary>
/// Workspace category.
/// </summary>
public enum WorkspaceCategory : byte
{
    Research,
    Trading,
    DataOperations,
    Governance,
    Custom
}

public static class WorkspaceCategoryExtensions
{
    public static string ToDisplayName(this WorkspaceCategory category)
    {
        return category switch
        {
            WorkspaceCategory.Research => "Research",
            WorkspaceCategory.Trading => "Trading",
            WorkspaceCategory.DataOperations => "Data Operations",
            WorkspaceCategory.Governance => "Governance",
            _ => "Custom"
        };
    }
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
    public List<string> RecentPages { get; set; } = new();
    public Dictionary<string, WidgetPosition> WidgetLayout { get; set; } = new();
    public Dictionary<string, string> ActiveFilters { get; set; } = new();
    public Dictionary<string, string> WorkspaceContext { get; set; } = new();
    public WindowBounds? WindowBounds { get; set; }
    public DateTime SavedAt { get; set; }
    public string? ActiveWorkspaceId { get; set; }
}

/// <summary>
<<<<<<< HEAD
/// Persisted docking and pane composition for a workstation workspace.
/// </summary>
public sealed class WorkstationLayoutState
{
    public string LayoutId { get; set; } = "default";
    public string DisplayName { get; set; } = "Default Layout";
    public string ActivePaneId { get; set; } = "pane-1";
    public string? OperatingContextKey { get; set; }
    public BoundedWindowMode WindowMode { get; set; } = BoundedWindowMode.DockFloat;
    public string? LayoutPresetId { get; set; }
    public string? DockLayoutXml { get; set; }
    public List<WorkstationPaneState> Panes { get; set; } = new();
    public List<FloatingWorkspaceWindowState> FloatingWindows { get; set; } = new();
    public Dictionary<string, string> LayoutContext { get; set; } = new();
    public DateTime SavedAt { get; set; }
}

/// <summary>
/// Bounded workstation shell mode for one-shell dock and float behavior.
/// </summary>
public enum BoundedWindowMode : byte
{
    Focused,
    DockFloat,
    WorkbenchPreset
}

/// <summary>
/// Describes a single docked or floating pane within a workstation layout.
/// </summary>
public sealed class WorkstationPaneState
{
    public string PaneId { get; set; } = string.Empty;
    public string PageTag { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DockZone { get; set; } = "document";
    public bool IsToolPane { get; set; }
    public bool IsPinned { get; set; }
    public bool IsActive { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// Metadata for a floating workspace window that can be restored on the next launch.
/// </summary>
public sealed class FloatingWorkspaceWindowState
{
    public string WindowId { get; set; } = string.Empty;
    public string PaneId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public WindowBounds? Bounds { get; set; }
    public bool IsOpen { get; set; } = true;
}

/// <summary>
/// User-saveable workstation layout preset.
/// </summary>
public sealed class WorkspaceLayoutPreset
{
    public string PresetId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public WorkstationLayoutState Layout { get; set; } = new();
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
=======
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
/// Workspace event args.
/// </summary>
public sealed class WorkspaceEventArgs : EventArgs
{
    public WorkspaceTemplate? Workspace { get; set; }
}
