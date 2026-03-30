namespace Meridian.Wpf.Services;

/// <summary>
/// Persists and restores the QuantScript page column layout.
/// </summary>
public interface IQuantScriptLayoutService
{
    (double LeftWidth, double RightWidth) LoadColumnWidths();
    void SaveColumnWidths(double leftWidth, double rightWidth);
    int LoadLastActiveTab();
    void SaveLastActiveTab(int tabIndex);
}
