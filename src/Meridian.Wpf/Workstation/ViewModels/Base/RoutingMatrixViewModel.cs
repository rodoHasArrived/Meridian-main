using System.Collections.ObjectModel;

namespace Meridian.Wpf.Workstation.ViewModels.Base;

/// <summary>
/// Carries a routing source, target, and status for matrix presentation.
/// </summary>
public sealed class RoutingMatrixRow
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}

/// <summary>
/// Exposes an observable collection of source-to-target routing rows.
/// </summary>
public sealed class RoutingMatrixViewModel : Meridian.Wpf.ViewModels.BindableBase
{
    public ObservableCollection<RoutingMatrixRow> Rows { get; } = [];
}
