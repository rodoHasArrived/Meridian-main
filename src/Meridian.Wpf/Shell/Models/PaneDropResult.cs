using Meridian.Wpf.Models;

namespace Meridian.Wpf.Shell.Models;

/// <summary>
/// Result of applying a route drop into the shell pane host.
/// </summary>
public sealed record PaneDropResult(
    int ActivePaneIndex,
    string PageTag,
    string CanonicalPageTag,
    PaneDropAction Action);
