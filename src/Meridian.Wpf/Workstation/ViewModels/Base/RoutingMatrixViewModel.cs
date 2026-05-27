using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

public sealed class RoutingMatrixRow
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

public sealed class RoutingMatrixViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    public ObservableCollection<RoutingMatrixRow> Rows { get; } = [];
}
