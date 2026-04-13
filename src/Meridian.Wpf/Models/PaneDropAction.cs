namespace Meridian.Wpf.Models;

/// <summary>
/// Desired docking behavior when opening a workstation page.
/// </summary>
public enum PaneDropAction : byte
{
    Replace,
    SplitLeft,
    SplitRight,
    SplitBelow,
    OpenTab,
    FloatWindow
}
