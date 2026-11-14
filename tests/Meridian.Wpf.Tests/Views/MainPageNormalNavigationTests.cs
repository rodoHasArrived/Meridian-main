using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class MainPageNormalNavigationTests
{
    [Fact]
    public void LeafCommandAfterWorkspaceLaunch_ShouldUseContentFrameAndRetainTheWorkspacePane()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var accountingContent = paneHost.GetPaneFrame(0)!.Content;
            accountingContent.Should().BeOfType<AccountingWorkspaceShellPage>();

            facade.ViewModel.NavigateToPageCommand.Execute("RunMat");
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ViewModel.CurrentPageTag.Should().Be("RunMat");
            facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<RunMatPage>();
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("RunMat");
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("AccountingShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(accountingContent);
            AutomationProperties.GetItemStatus(paneHost.GetPaneFrame(0)!).Should().Be("AccountingShell");
        });
    }

    [Fact]
    public void DirectServiceNavigationAfterWorkspaceLaunch_ShouldUseContentFrameWithoutDuplicatingHistory()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var accountingContent = paneHost.GetPaneFrame(0)!.Content;
            accountingContent.Should().BeOfType<AccountingWorkspaceShellPage>();
            var navigationService = NavigationService.Instance;
            var historyCount = navigationService.GetBreadcrumbs().Count;

            navigationService.NavigateTo("RunMat").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ViewModel.CurrentPageTag.Should().Be("RunMat");
            facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<RunMatPage>();
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("RunMat");
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("AccountingShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(accountingContent);
            AutomationProperties.GetItemStatus(paneHost.GetPaneFrame(0)!).Should().Be("AccountingShell");
            navigationService.GetBreadcrumbs().Should().HaveCount(historyCount + 1,
                "selecting the visible navigation surface must not issue a second navigation");
        });
    }

    [Theory]
    [InlineData(WorkspaceChromePresentationMode.Standalone)]
    [InlineData(WorkspaceChromePresentationMode.Docked)]
    public void ExplicitForeignFrameNavigation_ShouldKeepTheCallerFrameAndMainPaneContent(
        WorkspaceChromePresentationMode presentationMode)
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var accountingContent = paneHost.GetPaneFrame(0)!.Content;
            accountingContent.Should().BeOfType<AccountingWorkspaceShellPage>();
            var mainFrameContent = facade.ContentFrame.Content;
            var mainFrameStatus = AutomationProperties.GetItemStatus(facade.ContentFrame);
            var callerFrame = new Frame();
            var navigationService = NavigationService.Instance;
            navigationService.Initialize(callerFrame, presentationMode);

            navigationService.NavigateTo("RunMat").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationHostInspector.ResolveInnermostContent(callerFrame.Content)
                .Should().BeOfType<RunMatPage>();
            NavigationService.GetContentPageTag(callerFrame.Content).Should().Be("RunMat");
            facade.ContentFrame.Content.Should().BeSameAs(mainFrameContent,
                "the main shell must not redirect navigation explicitly bound to another frame");
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be(mainFrameStatus);
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("AccountingShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(accountingContent);
            AutomationProperties.GetItemStatus(paneHost.GetPaneFrame(0)!).Should().Be("AccountingShell");
        });
    }

    [Fact]
    public void NormalNavigationAfterLayoutRebuild_ShouldUseTheCurrentVisibleFrames()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var removedPaneFrame = paneHost.GetPaneFrame(0)!;
            var removedPaneContent = removedPaneFrame.Content;
            removedPaneContent.Should().BeOfType<AccountingWorkspaceShellPage>();

            facade.ViewModel.PaneHost.SelectLayoutCommand.Execute(PaneLayouts.StrategyData);
            RunMatUiAutomationFacade.DrainDispatcher();
            paneHost.GetPaneFrame(0).Should().NotBeSameAs(removedPaneFrame);
            facade.ViewModel.SelectWorkspaceCommand.Execute("data");
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "DataShell", 0);
            var dataContent = paneHost.GetPaneFrame(0)!.Content;

            NavigationService.Instance.NavigateTo("RunMat").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<RunMatPage>();
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("RunMat");
            facade.ViewModel.PaneHost.SelectedLayout.Should().BeSameAs(PaneLayouts.StrategyData);
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("DataShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(dataContent);
            removedPaneFrame.Content.Should().BeSameAs(removedPaneContent,
                "navigation must not target a pane removed during the layout rebuild");
        });
    }

    [Fact]
    public void WorkspaceCommands_ShouldReplaceTheActivePaneWithTheCanonicalWorkspaceBody()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            foreach (var (workspaceId, pageTag) in new[]
                     {
                         ("data", "DataShell"),
                         ("trading", "TradingShell"),
                         ("accounting", "AccountingShell")
                     })
            {
                facade.ViewModel.SelectWorkspaceCommand.Execute(workspaceId);
                RunMatUiAutomationFacade.DrainDispatcher();

                AssertVisibleWorkspaceHome(facade, paneHost, pageTag, 0);
            }
        });
    }

    [Fact]
    public void WorkspaceCommands_ShouldAddOneNativeJournalEntryPerRouteAndRestorePriorBodies()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var frame = paneHost.GetPaneFrame(0)!;
            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType<AccountingWorkspaceShellPage>();
            var accountingContent = frame.Content;
            var baselineBackStackCount = frame.BackStack?.Cast<object>().Count() ?? 0;

            facade.ViewModel.SelectWorkspaceCommand.Execute("data");
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType(ShellNavigationCatalog.GetPage("DataShell")!.PageType);
            var dataContent = frame.Content;
            (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(baselineBackStackCount + 1);

            facade.ViewModel.SelectWorkspaceCommand.Execute("trading");
            RunMatUiAutomationFacade.DrainDispatcher();

            paneHost.GetPaneFrame(0).Should().BeSameAs(frame);
            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType(ShellNavigationCatalog.GetPage("TradingShell")!.PageType);
            var tradingContent = frame.Content;
            (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(baselineBackStackCount + 2);

            frame.CanGoBack.Should().BeTrue();
            facade.ViewModel.GoBackCommand.Execute(null);
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "DataShell", 0);
            facade.ViewModel.CurrentWorkspace.Should().Be("data");
            AssertRetainedHistoryAfterPaneSync(facade, frame, dataContent, baselineBackStackCount + 1, 1);
            AssertVisibleWorkspaceHome(facade, paneHost, "DataShell", 0);

            frame.CanGoBack.Should().BeTrue();
            facade.ViewModel.GoBackCommand.Execute(null);
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "AccountingShell", 0);
            facade.ViewModel.CurrentWorkspace.Should().Be("accounting");
            AssertRetainedHistoryAfterPaneSync(facade, frame, accountingContent, baselineBackStackCount, 2);
            AssertVisibleWorkspaceHome(facade, paneHost, "AccountingShell", 0);

            frame.CanGoForward.Should().BeTrue();
            frame.GoForward();
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "DataShell", 0);
            facade.ViewModel.CurrentWorkspace.Should().Be("data");
            AssertRetainedHistoryAfterPaneSync(facade, frame, dataContent, baselineBackStackCount + 1, 1);
            AssertVisibleWorkspaceHome(facade, paneHost, "DataShell", 0);

            frame.CanGoForward.Should().BeTrue();
            frame.GoForward();
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "TradingShell", 0);
            facade.ViewModel.CurrentWorkspace.Should().Be("trading");
            AssertRetainedHistoryAfterPaneSync(facade, frame, tradingContent, baselineBackStackCount + 2, 0);
            AssertVisibleWorkspaceHome(facade, paneHost, "TradingShell", 0);
        });
    }

    [Fact]
    public void WorkflowJournalNavigation_ShouldSynchronizeTheLandedPageWithoutReplacingItsInstance()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var accountingContent = paneHost.GetPaneFrame(0)!.Content;
            facade.ViewModel.NavigateToPageCommand.Execute("RunMat");
            RunMatUiAutomationFacade.DrainDispatcher();
            var frame = facade.ContentFrame;
            var runMatContent = frame.Content;
            var baselineBackStackCount = frame.BackStack?.Cast<object>().Count() ?? 0;
            facade.ViewModel.NavigateToPageCommand.Execute("Backtest");
            RunMatUiAutomationFacade.DrainDispatcher();
            var backtestContent = frame.Content;

            facade.ViewModel.GoBackCommand.Execute(null);
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertWorkflowHistoryLanding("RunMat", runMatContent, baselineBackStackCount, 1);

            frame.CanGoForward.Should().BeTrue();
            frame.GoForward();
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertWorkflowHistoryLanding("Backtest", backtestContent, baselineBackStackCount + 1, 0);

            void AssertWorkflowHistoryLanding(string pageTag, object content, int backCount, int forwardCount)
            {
                facade.ViewModel.CurrentPageTag.Should().Be(pageTag);
                facade.ViewModel.CurrentWorkspace.Should().Be("strategy");
                facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
                facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
                AutomationProperties.GetItemStatus(frame).Should().Be(pageTag);
                NavigationHostInspector.ResolveInnermostContent(frame.Content)
                    .Should().BeOfType(ShellNavigationCatalog.GetPage(pageTag)!.PageType);
                AssertRetainedHistoryAfterPaneSync(facade, frame, content, backCount, forwardCount);
                facade.ViewModel.CurrentPageTag.Should().Be(pageTag);
                facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("AccountingShell");
                paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(accountingContent);
            }
        });
    }

    [Fact]
    public void InactivePaneJournalNavigation_ShouldNotChangeTheActiveShellRoute()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.ViewModel.PaneHost.SelectLayoutCommand.Execute(PaneLayouts.TradingCockpit);
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.ViewModel.SelectWorkspaceCommand.Execute("data");
            RunMatUiAutomationFacade.DrainDispatcher();
            var inactiveFrame = paneHost.GetPaneFrame(0)!;
            facade.ViewModel.PaneHost.SetActivePaneCommand.Execute(1);
            facade.ViewModel.SelectWorkspaceCommand.Execute("trading");
            RunMatUiAutomationFacade.DrainDispatcher();
            var activeFrame = paneHost.GetPaneFrame(1)!;
            var activeContent = activeFrame.Content;

            inactiveFrame.CanGoBack.Should().BeTrue();
            inactiveFrame.GoBack();
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationService.GetContentPageTag(inactiveFrame.Content).Should().Be("AccountingShell");
            AssertVisibleWorkspaceHome(facade, paneHost, "TradingShell", 1);
            facade.ViewModel.CurrentWorkspace.Should().Be("trading");
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("DataShell");
            activeFrame.Content.Should().BeSameAs(activeContent);
            NavigationService.Instance.IsNavigationFrame(activeFrame, WorkspaceChromePresentationMode.Docked)
                .Should().BeTrue();
        });
    }

    [Theory]
    [InlineData(WorkspaceChromePresentationMode.Standalone)]
    [InlineData(WorkspaceChromePresentationMode.Docked)]
    public void FormerShellFrameJournalNavigation_ShouldNotTakeOverAnExplicitForeignFrame(
        WorkspaceChromePresentationMode presentationMode)
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.ViewModel.SelectWorkspaceCommand.Execute("data");
            RunMatUiAutomationFacade.DrainDispatcher();
            var formerFrame = paneHost.GetPaneFrame(0)!;
            var foreignFrame = new Frame();
            NavigationService.Instance.Initialize(foreignFrame, presentationMode);

            formerFrame.CanGoBack.Should().BeTrue();
            formerFrame.GoBack();
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationService.GetContentPageTag(formerFrame.Content).Should().Be("AccountingShell");
            facade.ViewModel.CurrentPageTag.Should().Be("DataShell");
            facade.ViewModel.CurrentWorkspace.Should().Be("data");
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("DataShell");
            foreignFrame.Content.Should().BeNull();
            NavigationService.Instance.IsNavigationFrame(foreignFrame, presentationMode).Should().BeTrue();
        });
    }

    [Fact]
    public void NormalCommandsAfterActivePaneChanges_ShouldPreserveTheLayoutAndOtherPaneInstances()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade(validateScopes: true);
            var paneHost = InitializePaneHost(facade);
            facade.ViewModel.PaneHost.SelectLayoutCommand.Execute(PaneLayouts.TradingCockpit);
            RunMatUiAutomationFacade.DrainDispatcher();
            var initialPages = new[] { "AccountingShell", "DataShell", "TradingShell" };
            for (var paneIndex = 0; paneIndex < initialPages.Length; paneIndex++)
            {
                facade.ViewModel.PaneHost.AssignPageToPane(initialPages[paneIndex], paneIndex);
                RunMatUiAutomationFacade.DrainDispatcher();
            }

            facade.ViewModel.PaneHost.SetActivePaneCommand.Execute(0);
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            var originalContent = Enumerable.Range(0, 3)
                .Select(paneIndex => paneHost.GetPaneFrame(paneIndex)!.Content).ToArray();
            for (var paneIndex = 0; paneIndex < initialPages.Length; paneIndex++)
            {
                NavigationHostInspector.ResolveInnermostContent(originalContent[paneIndex])
                    .Should().BeOfType(ShellNavigationCatalog.GetPage(initialPages[paneIndex])!.PageType);
            }

            facade.ViewModel.NavigateToPageCommand.Execute("RunMat");
            RunMatUiAutomationFacade.DrainDispatcher();
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<RunMatPage>();

            facade.ViewModel.PaneHost.SetActivePaneCommand.Execute(1);
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.ViewModel.NavigateToPageCommand.Execute("Backtest");
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<BacktestPage>();
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("Backtest");
            Enumerable.Range(0, 3).Select(facade.ViewModel.PaneHost.GetAssignedPageTag)
                .Should().Equal(initialPages);
            for (var paneIndex = 0; paneIndex < initialPages.Length; paneIndex++)
            {
                paneHost.GetPaneFrame(paneIndex)!.Content.Should().BeSameAs(originalContent[paneIndex]);
            }

            facade.ViewModel.SelectWorkspaceCommand.Execute("accounting");
            RunMatUiAutomationFacade.DrainDispatcher();

            AssertVisibleWorkspaceHome(facade, paneHost, "AccountingShell", 1);
            facade.ViewModel.PaneHost.SelectedLayout.Should().BeSameAs(PaneLayouts.TradingCockpit);
            Enumerable.Range(0, 3).Select(facade.ViewModel.PaneHost.GetAssignedPageTag)
                .Should().Equal("AccountingShell", "AccountingShell", "TradingShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeSameAs(originalContent[0]);
            paneHost.GetPaneFrame(2)!.Content.Should().BeSameAs(originalContent[2]);
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("Backtest");
        });
    }

    private static SplitPaneHostControl InitializePaneHost(MainPageUiAutomationFacade facade)
    {
        var paneHost = (SplitPaneHostControl)facade.Page.FindName("SplitPaneHost");
        paneHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, paneHost));
        RunMatUiAutomationFacade.DrainDispatcher();
        return paneHost;
    }

    private static void AssertRetainedHistoryAfterPaneSync(
        MainPageUiAutomationFacade facade,
        Frame frame,
        object content,
        int backStackCount,
        int forwardStackCount)
    {
        var breadcrumbCount = NavigationService.Instance.GetBreadcrumbs().Count;
        frame.Content.Should().BeSameAs(content);
        (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(backStackCount);
        (frame.ForwardStack?.Cast<object>().Count() ?? 0).Should().Be(forwardStackCount);

        facade.ViewModel.PaneHost.SetActivePaneCommand.Execute(facade.ViewModel.PaneHost.ActivePaneIndex);
        RunMatUiAutomationFacade.DrainDispatcher();

        frame.Content.Should().BeSameAs(content, "pane synchronization must retain the landed journal instance");
        (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(backStackCount);
        (frame.ForwardStack?.Cast<object>().Count() ?? 0).Should().Be(forwardStackCount);
        NavigationService.Instance.GetBreadcrumbs().Should().HaveCount(breadcrumbCount,
            "synchronizing a landed journal entry must not issue another navigation");
    }

    private static void AssertVisibleWorkspaceHome(
        MainPageUiAutomationFacade facade,
        SplitPaneHostControl paneHost,
        string pageTag,
        int paneIndex)
    {
        var frame = paneHost.GetPaneFrame(paneIndex)!;
        facade.ViewModel.CurrentPageTag.Should().Be(pageTag);
        facade.ViewModel.PaneHost.ActivePaneIndex.Should().Be(paneIndex);
        facade.ViewModel.PaneHost.GetAssignedPageTag(paneIndex).Should().Be(pageTag);
        facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Visible);
        facade.ContentFrame.Visibility.Should().Be(Visibility.Collapsed);
        NavigationHostInspector.ResolveInnermostContent(frame.Content)
            .Should().BeOfType(ShellNavigationCatalog.GetPage(pageTag)!.PageType);
        AutomationProperties.GetItemType(frame).Should().Be("ActiveWorkspacePane");
        AutomationProperties.GetItemStatus(frame).Should().Be(pageTag);
    }
}
