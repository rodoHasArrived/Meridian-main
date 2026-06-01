using System.Windows;
using System.Windows.Controls;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Contracts;
using Meridian.Wpf.Models;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Shell.ViewModels;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Shell;

public sealed class ShellNavigationCoordinatorTests
{
    [Fact]
    public void NavigateToRoute_KnownCompatibilityAlias_DelegatesCanonicalTagToNavigationService()
    {
        var navigationService = new FakeNavigationService();
        var coordinator = CreateCoordinator(navigationService);

        var result = coordinator.NavigateToRoute("ResearchShell");

        result.Should().BeTrue();
        navigationService.NavigatedPageTags.Should().Equal("StrategyShell");
    }

    [Fact]
    public void ApplyPaneDrop_KnownAlias_StoresAndReturnsCanonicalTag()
    {
        var coordinator = CreateCoordinator();
        var paneHost = new PaneHostViewModel(new ShellRouteRegistry());
        paneHost.AssignPageToPane("TradingShell", 0);

        var result = coordinator.ApplyPaneDrop(paneHost, "Blotter", 0, PaneDropAction.Replace);

        result.ActivePaneIndex.Should().Be(0);
        result.PageTag.Should().Be("PositionBlotter");
        result.CanonicalPageTag.Should().Be("PositionBlotter");
        paneHost.GetAssignedPageTag(0).Should().Be("PositionBlotter");
        paneHost.GetPaneContentState(0)!.CanonicalPageTag.Should().Be("PositionBlotter");
    }

    [Fact]
    public void CreatePaneContent_UsesFactoryWithExplicitPaneState()
    {
        WpfTestThread.Run(() =>
        {
            var factory = new FakePageContentFactory();
            var coordinator = CreateCoordinator(pageContentFactory: factory);
            var paneState = new PaneHostViewModel(new ShellRouteRegistry());
            paneState.AssignPageToPane("ResearchShell", 0);

            var content = coordinator.CreatePaneContent(paneState.GetPaneContentState(0)!);

            content.Should().BeOfType<Page>();
            factory.RequestedTags.Should().Equal("StrategyShell");
        });
    }

    [Fact]
    public void InitializeNavigationFrame_DelegatesFrameCompatibilityToNavigationService()
    {
        WpfTestThread.Run(() =>
        {
            var navigationService = new FakeNavigationService();
            var coordinator = CreateCoordinator(navigationService);
            var frame = new Frame();

            coordinator.InitializeNavigationFrame(frame);

            navigationService.InitializedFrame.Should().BeSameAs(frame);
        });
    }

    private static ShellNavigationCoordinator CreateCoordinator(
        INavigationService? navigationService = null,
        IPageContentFactory? pageContentFactory = null)
    {
        var registry = new ShellRouteRegistry();
        return new ShellNavigationCoordinator(
            navigationService ?? new FakeNavigationService(),
            registry,
            pageContentFactory ?? new FakePageContentFactory());
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public List<string> NavigatedPageTags { get; } = [];

        public Frame? InitializedFrame { get; private set; }

        public bool IsInitialized => InitializedFrame is not null;

        public bool CanGoBack => false;

        public event EventHandler<NavigationEventArgs>? Navigated;

        public void Initialize(Frame frame) => InitializedFrame = frame;

        public bool NavigateTo(string pageTag, object? parameter = null)
        {
            NavigatedPageTags.Add(pageTag);
            Navigated?.Invoke(this, new NavigationEventArgs { PageTag = pageTag, Parameter = parameter });
            return true;
        }

        public bool NavigateTo(Type pageType, object? parameter = null) => true;

        public void GoBack()
        {
        }

        public Type? GetPageType(string pageTag) => typeof(Page);

        public IReadOnlyList<NavigationEntry> GetBreadcrumbs() => Array.Empty<NavigationEntry>();

        public IReadOnlyCollection<string> GetRegisteredPages() => Array.Empty<string>();

        public bool IsPageRegistered(string pageTag) => true;
    }

    private sealed class FakePageContentFactory : IPageContentFactory
    {
        public List<string> RequestedTags { get; } = [];

        public FrameworkElement CreateContent(
            string pageTag,
            object? parameter = null,
            WorkspaceChromePresentationMode presentationMode = WorkspaceChromePresentationMode.Docked)
        {
            RequestedTags.Add(pageTag);
            return new Page();
        }
    }
}
