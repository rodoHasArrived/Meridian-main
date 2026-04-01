namespace Meridian.Wpf.Services;

/// <summary>
/// Stores QuantScript page layout settings (row heights, active tab) in memory
/// for the current session. Values are reset when the application restarts.
/// </summary>
public sealed class QuantScriptLayoutService : IQuantScriptLayoutService
{
    private const double DefaultChartHeight = 300;
    private const double DefaultEditorHeight = 280;
    private const int DefaultActiveTab = 0;

    private double _chartHeight = DefaultChartHeight;
    private double _editorHeight = DefaultEditorHeight;
    private int _lastActiveTab = DefaultActiveTab;

    public (double ChartHeight, double EditorHeight) LoadRowHeights()
        => (_chartHeight, _editorHeight);

    public void SaveRowHeights(double chartHeight, double editorHeight)
    {
        _chartHeight = chartHeight > 0 ? chartHeight : DefaultChartHeight;
        _editorHeight = editorHeight > 0 ? editorHeight : DefaultEditorHeight;
    }

    public int LoadLastActiveTab() => _lastActiveTab;

    public void SaveLastActiveTab(int tabIndex)
    {
        if (tabIndex >= 0) _lastActiveTab = tabIndex;
    }
}
