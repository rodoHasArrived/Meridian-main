using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Tables;

/// <summary>
/// Displays read-only rows with explicit columns and row and column virtualization.
/// </summary>
public sealed class DenseDataGridControl : DataGrid
{
    public DenseDataGridControl()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
        ConfigureVirtualization(this);
    }

    internal static void ConfigureVirtualization(DataGrid grid)
    {
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        ScrollViewer.SetCanContentScroll(grid, true);
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
    }
}

/// <summary>
/// Provides a content surface for table search and filter controls.
/// </summary>
public sealed class SearchFilterBarControl : ContentControl
{
}

/// <summary>
/// Displays read-only key-value facts using virtualized rows and columns.
/// </summary>
public sealed class KeyValueFactGridControl : DataGrid
{
    public KeyValueFactGridControl()
    {
        IsReadOnly = true;
        DenseDataGridControl.ConfigureVirtualization(this);
    }
}

/// <summary>
/// Displays read-only activity-log rows using row and column virtualization.
/// </summary>
public sealed class ActivityLogGridControl : DataGrid
{
    public ActivityLogGridControl()
    {
        IsReadOnly = true;
        DenseDataGridControl.ConfigureVirtualization(this);
    }
}
