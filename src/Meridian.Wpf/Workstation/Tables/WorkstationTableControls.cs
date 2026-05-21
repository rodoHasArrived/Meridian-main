using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Tables;

public sealed class DenseDataGridControl : DataGrid
{
    public DenseDataGridControl()
    {
        AutoGenerateColumns = false;
        IsReadOnly = true;
    }
}

public sealed class SearchFilterBarControl : ContentControl;

public sealed class KeyValueFactGridControl : DataGrid;

public sealed class ActivityLogGridControl : DataGrid;
