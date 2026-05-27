using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Models;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.Shell.Services;

public interface IShellNavigationCoordinator
{
    IShellRouteRegistry RouteRegistry { get; }

    ShellRoute? ResolveRoute(string? pageTag);

    bool IsRouteRegistered(string? pageTag);

    bool NavigateToRoute(string pageTag, object? parameter = null);

    PaneDropResult ApplyPaneDrop(
        PaneHostViewModel paneHost,
        string? pageTag,
        int targetPaneIndex,
        PaneDropAction action = PaneDropAction.Replace);

    IReadOnlyList<PaneContentState> GetVisiblePaneContentStates(PaneHostViewModel paneHost);

    FrameworkElement CreatePaneContent(PaneContentState state);

    void InitializeNavigationFrame(Frame frame);
}
