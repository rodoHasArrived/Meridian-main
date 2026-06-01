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

    // PR-01: quick actions pinned to this workspace template
    public List<WorkspaceQuickAction> QuickActions { get; set; } = new();
}

/// <summary>
/// Persisted workspace category.
/// Numeric values are stable because saved workspace state may contain them.
/// Legacy member names remain as aliases for source and persisted-state compatibility.
/// </summary>
public enum WorkspaceCategory : byte
{
    Strategy = 0,
    Trading,
    Data,
    Accounting,
    Custom,
    [Obsolete("Use Strategy. Research is retained only as a compatibility alias.")]
    Research = Strategy,
    [Obsolete("Use Data. DataOperations is retained only as a compatibility alias.")]
    DataOperations = Data,
    [Obsolete("Use Accounting. Governance is retained only as a compatibility alias.")]
    Governance = Accounting
}

public static class WorkspaceCategoryExtensions
{
    public static string ToDisplayName(this WorkspaceCategory category)
    {
        return category switch
        {
            WorkspaceCategory.Strategy => "Strategy",
            WorkspaceCategory.Trading => "Trading",
            WorkspaceCategory.Data => "Data",
            WorkspaceCategory.Accounting => "Accounting",
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
    public WorkstationLayoutState? WorkstationLayout { get; set; }

    // PR-01: lightweight summary stats cached at save-time for fast shell rendering
    public WorkspaceSummaryStats? CachedSummaryStats { get; set; }
}

/// <summary>
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
/// Workspace event args.
/// </summary>
public sealed class WorkspaceEventArgs : EventArgs
{
    public WorkspaceTemplate? Workspace { get; set; }
}

// ── PR-01: Shell Hardening additions ────────────────────────────────────────

/// <summary>
/// Named quick action attached to a workspace. Each action carries a target page tag and
/// an optional tone so shells can render it as a contextual button or menu item without
/// requiring a full workflow summary fetch.
/// </summary>
public sealed class WorkspaceQuickAction
{
    /// <summary>Stable identifier used for deduplication and analytics.</summary>
    public string ActionId { get; set; } = string.Empty;

    /// <summary>Human-readable action label shown in the shell tile.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Description shown as tooltip or secondary text.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Navigation target invoked when the action is activated.</summary>
    public string TargetPageTag { get; set; } = string.Empty;

    /// <summary>Visual tone: default, primary, success, warning, or danger.</summary>
    public string Tone { get; set; } = "default";

    /// <summary>When false the action is rendered but not interactive (e.g. locked behind context).</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Display order within the action list (lower = earlier).</summary>
    public int Order { get; set; }
}

/// <summary>
/// Lightweight workspace summary statistics surfaced in shell tiles, session restore hints,
/// and the quick-action rail. Computed on demand from the active session; not persisted.
/// </summary>
public sealed class WorkspaceSummaryStats
{
    /// <summary>Number of strategy runs currently in a running or paused state.</summary>
    public int ActiveRunCount { get; set; }

    /// <summary>Number of runs flagged for review (failed, cancelled, or promotion pending).</summary>
    public int PendingReviewCount { get; set; }

    /// <summary>Number of completed runs eligible for paper or live promotion.</summary>
    public int PromotionCandidateCount { get; set; }

    /// <summary>Whether at least one run in this workspace has a ledger reference.</summary>
    public bool HasLedgerCoverage { get; set; }

    /// <summary>Whether at least one run in this workspace has a portfolio reference.</summary>
    public bool HasPortfolioCoverage { get; set; }

    /// <summary>When the most recently active page in this workspace was last visited.</summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    /// <summary>
    /// Human-readable one-line digest e.g. "3 active · 2 review · ledger covered".
    /// Produced by <c>WorkspaceService.BuildSummaryDigest</c>; empty when stats are all zero.
    /// </summary>
    public string Digest { get; set; } = string.Empty;
}
