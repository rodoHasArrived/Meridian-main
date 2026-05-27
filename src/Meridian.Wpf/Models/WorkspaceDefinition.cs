namespace Meridian.Wpf.Models;

/// <summary>
/// Represents a single page entry within a workspace's sub-navigation.
/// </summary>
public sealed record WorkspacePageEntry(
    string PageName,      // matches Pages.cs constant
    string Label,         // display name in sub-nav
    string? Icon = null);

/// <summary>
/// Represents a top-level workspace in compatibility template storage.
/// Legacy ids may still map to current visible workspaces.
/// with its associated pages and metadata.
/// </summary>
public sealed record WorkspaceDefinition(
    string Id,                                        // unique identifier (e.g., "research")
    string Label,                                     // display name (e.g., "Strategy")
    string Icon,                                      // emoji or symbol (e.g., "🔬")
    IReadOnlyList<WorkspacePageEntry> Pages,          // pages in this workspace
    string? LayoutXml = null);                        // serialised AvalonDock layout (null → default)
