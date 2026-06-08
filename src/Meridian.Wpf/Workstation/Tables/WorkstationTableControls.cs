using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Tables;

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

public sealed class SearchFilterBarControl : ContentControl
{
}

public sealed class KeyValueFactGridControl : DataGrid
{
    public KeyValueFactGridControl()
    {
        IsReadOnly = true;
        DenseDataGridControl.ConfigureVirtualization(this);
    }
}

public sealed class ActivityLogGridControl : DataGrid
{
    public ActivityLogGridControl()
    {
        IsReadOnly = true;
        DenseDataGridControl.ConfigureVirtualization(this);
    }
}
