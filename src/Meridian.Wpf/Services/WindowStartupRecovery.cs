using System.Windows;

namespace Meridian.Wpf.Services;

/// <summary>
/// Normalizes startup window state when Meridian is launched from shells that provide
/// hidden or tiny initial window metrics.
/// </summary>
public static class WindowStartupRecovery
{
    public static bool NeedsRecovery(
        bool isVisible,
        bool showInTaskbar,
        WindowState windowState,
        double nativeWidth,
        double nativeHeight,
        double minWidth,
        double minHeight)
    {
        return !isVisible
               || !showInTaskbar
               || windowState == WindowState.Minimized
               || nativeWidth < minWidth
               || nativeHeight < minHeight;
    }

    public static Rect ResolveBounds(
        Rect? persistedBounds,
        Rect nativeBounds,
        double minWidth,
        double minHeight,
        double fallbackWidth,
        double fallbackHeight)
    {
        var target = IsUsableBounds(persistedBounds, minWidth, minHeight)
            ? persistedBounds!.Value
            : IsUsableBounds(nativeBounds, minWidth, minHeight)
                ? nativeBounds
                : CreateCenteredBounds(
                    Math.Max(minWidth, fallbackWidth),
                    Math.Max(minHeight, fallbackHeight));

        var width = Math.Max(minWidth, target.Width);
        var height = Math.Max(minHeight, target.Height);

        return EnsureOnScreen(new Rect(target.Left, target.Top, width, height));
    }

    private static bool IsUsableBounds(Rect? bounds, double minWidth, double minHeight)
        => bounds is { IsEmpty: false } value
           && !double.IsNaN(value.Width)
           && !double.IsNaN(value.Height)
           && value.Width >= minWidth
           && value.Height >= minHeight;

    private static Rect CreateCenteredBounds(double width, double height)
    {
        var workArea = SystemParameters.WorkArea;
        var left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        var top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
        return new Rect(left, top, width, height);
    }

    private static Rect EnsureOnScreen(Rect bounds)
    {
        var workArea = SystemParameters.WorkArea;
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        var left = Math.Min(Math.Max(bounds.Left, workArea.Left), workArea.Right - width);
        var top = Math.Min(Math.Max(bounds.Top, workArea.Top), workArea.Bottom - height);
        return new Rect(left, top, width, height);
    }
}
