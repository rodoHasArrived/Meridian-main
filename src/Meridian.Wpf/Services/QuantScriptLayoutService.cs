namespace Meridian.Wpf.Services;

/// <summary>
/// Stores QuantScript page layout settings (column widths, active tab) in memory
/// for the current session. Values are reset when the application restarts.
/// </summary>
public sealed class QuantScriptLayoutService : IQuantScriptLayoutService
{
    private const double DefaultLeftWidth = 220;
    private const double DefaultRightWidth = 380;
    private const int DefaultActiveTab = 0;

    private double _leftWidth = DefaultLeftWidth;
    private double _rightWidth = DefaultRightWidth;
    private int _lastActiveTab = DefaultActiveTab;

    public (double LeftWidth, double RightWidth) LoadColumnWidths()
        => (_leftWidth, _rightWidth);

    public void SaveColumnWidths(double leftWidth, double rightWidth)
    {
        _leftWidth = leftWidth > 0 ? leftWidth : DefaultLeftWidth;
        _rightWidth = rightWidth > 0 ? rightWidth : DefaultRightWidth;
    }

    public int LoadLastActiveTab() => _lastActiveTab;

    public void SaveLastActiveTab(int tabIndex)
    {
        if (tabIndex >= 0) _lastActiveTab = tabIndex;
    }
}
