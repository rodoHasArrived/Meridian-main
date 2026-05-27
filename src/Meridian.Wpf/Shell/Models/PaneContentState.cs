using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Models;

/// <summary>
/// Explicit pane assignment used by the shell to render route content into a WPF frame.
/// </summary>
public sealed record PaneContentState(
    int PaneIndex,
    string PageTag,
    string CanonicalPageTag,
    string WorkspaceId,
    object? Parameter = null,
    WorkspaceChromePresentationMode PresentationMode = WorkspaceChromePresentationMode.Docked);
