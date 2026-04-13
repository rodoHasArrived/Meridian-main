namespace Meridian.Wpf.Models;

/// <summary>
/// Event data raised when the user drops a page-tag onto a split pane.
/// </summary>
public sealed class PaneDropEventArgs : EventArgs
{
    /// <summary>The navigation tag of the page that was dragged and dropped.</summary>
    public string PageTag { get; }

    /// <summary>Zero-based index of the pane onto which the drop occurred.</summary>
    public int TargetPaneIndex { get; }

    /// <summary>The requested docking action for the dropped page.</summary>
    public PaneDropAction Action { get; }

    public PaneDropEventArgs(string pageTag, int targetPaneIndex, PaneDropAction action = PaneDropAction.Replace)
    {
        PageTag = pageTag;
        TargetPaneIndex = targetPaneIndex;
        Action = action;
    }
}
