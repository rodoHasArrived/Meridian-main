using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Models;
using Meridian.Wpf.Shell.ViewModels;

namespace Meridian.Wpf.Shell.Services;

public sealed class ShellNavigationCoordinator : IShellNavigationCoordinator
{
    private readonly INavigationService _navigationService;
    private readonly IPageContentFactory _pageContentFactory;

    public ShellNavigationCoordinator(
        INavigationService navigationService,
        IShellRouteRegistry routeRegistry,
        IPageContentFactory pageContentFactory)
    {
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        RouteRegistry = routeRegistry ?? throw new ArgumentNullException(nameof(routeRegistry));
        _pageContentFactory = pageContentFactory ?? throw new ArgumentNullException(nameof(pageContentFactory));
    }

    public IShellRouteRegistry RouteRegistry { get; }

    public ShellRoute? ResolveRoute(string? pageTag) => RouteRegistry.GetRoute(pageTag);

    public bool IsRouteRegistered(string? pageTag) => RouteRegistry.IsRegistered(pageTag);

    public bool NavigateToRoute(string pageTag, object? parameter = null)
    {
        var route = RouteRegistry.GetRoute(pageTag);
        return route is not null && _navigationService.NavigateTo(route.PageTag, parameter);
    }

    public PaneDropResult ApplyPaneDrop(
        PaneHostViewModel paneHost,
        string? pageTag,
        int targetPaneIndex,
        PaneDropAction action = PaneDropAction.Replace)
    {
        ArgumentNullException.ThrowIfNull(paneHost);

        if (string.IsNullOrWhiteSpace(pageTag))
        {
            return new PaneDropResult(
                paneHost.ActivePaneIndex,
                string.Empty,
                string.Empty,
                action);
        }

        var canonicalPageTag = RouteRegistry.GetCanonicalPageTag(pageTag);
        var activePaneIndex = paneHost.ApplyPaneDrop(canonicalPageTag, targetPaneIndex, action);
        return new PaneDropResult(
            activePaneIndex,
            canonicalPageTag,
            canonicalPageTag,
            action);
    }

    public IReadOnlyList<PaneContentState> GetVisiblePaneContentStates(PaneHostViewModel paneHost)
    {
        ArgumentNullException.ThrowIfNull(paneHost);
        return paneHost.GetVisibleContentStates();
    }

    public FrameworkElement CreatePaneContent(PaneContentState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return _pageContentFactory.CreateContent(state.CanonicalPageTag, state.Parameter, state.PresentationMode);
    }

    public void InitializeNavigationFrame(Frame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _navigationService.Initialize(frame);
    }
}
