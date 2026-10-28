using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Provides the pane-host view-model specialization used by the split-pane workspace.
/// </summary>
public sealed class SplitPaneViewModel : PaneHostViewModel
{
    public SplitPaneViewModel(IShellRouteRegistry? routeRegistry = null)
        : base(routeRegistry)
    {
    }
}
