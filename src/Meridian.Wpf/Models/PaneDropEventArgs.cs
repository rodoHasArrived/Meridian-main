namespace Meridian.Wpf.Models;

public enum PaneDropAction
{
    Replace,
    SplitLeft,
    SplitRight,
    SplitBelow,
    OpenTab,
    FloatWindow
}

/// <summary>
/// Event data raised when the user drops a page-tag onto a split pane.
/// </summary>
public sealed class PaneDropEventArgs : EventArgs
{
    /// <summary>The navigation tag of the page that was dragged and dropped.</summary>
    public string PageTag { get; }

    /// <summary>Zero-based index of the pane onto which the drop occurred.</summary>
    public int TargetPaneIndex { get; }

    /// <summary>The requested drop behavior inside the target host.</summary>
    public PaneDropAction Action { get; }

    public PaneDropEventArgs(string pageTag, int targetPaneIndex, PaneDropAction action = PaneDropAction.Replace)
    {
        PageTag = pageTag;
        TargetPaneIndex = targetPaneIndex;
        Action = action;
    }
}
