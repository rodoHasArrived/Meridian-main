namespace Meridian.Wpf.Services;

/// <summary>
/// Persists and restores the QuantScript page layout (chart/editor row heights and active tab).
/// </summary>
public interface IQuantScriptLayoutService
{
    (double ChartHeight, double EditorHeight) LoadRowHeights();
    void SaveRowHeights(double chartHeight, double editorHeight);
    int LoadLastActiveTab();
    void SaveLastActiveTab(int tabIndex);
}
