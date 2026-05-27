using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.ViewModels;

public sealed class SplitPaneViewModel : PaneHostViewModel
{
    public SplitPaneViewModel(IShellRouteRegistry? routeRegistry = null)
        : base(routeRegistry)
    {
    }
}
