using System;

namespace Meridian.Wpf.Shell.Session;

public sealed record DesktopWindowBounds(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);

public sealed record DesktopWindowState(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized,
    DateTime SavedAt)
{
    public DesktopWindowBounds ToBounds() => new(Left, Top, Width, Height, IsMaximized);
}
