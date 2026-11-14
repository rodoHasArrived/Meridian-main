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
            var baselineBackStackCount = frame.BackStack?.Cast<object>().Count() ?? 0;

            facade.ViewModel.SelectWorkspaceCommand.Execute("data");
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType(ShellNavigationCatalog.GetPage("DataShell")!.PageType);
            (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(baselineBackStackCount + 1);

            facade.ViewModel.SelectWorkspaceCommand.Execute("trading");
            RunMatUiAutomationFacade.DrainDispatcher();

            paneHost.GetPaneFrame(0).Should().BeSameAs(frame);
            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType(ShellNavigationCatalog.GetPage("TradingShell")!.PageType);
            (frame.BackStack?.Cast<object>().Count() ?? 0).Should().Be(baselineBackStackCount + 2);

            frame.CanGoBack.Should().BeTrue();
            frame.GoBack();
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType(ShellNavigationCatalog.GetPage("DataShell")!.PageType);
            AutomationProperties.GetItemStatus(frame).Should().Be("DataShell");

            frame.CanGoBack.Should().BeTrue();
            frame.GoBack();
            RunMatUiAutomationFacade.DrainDispatcher();

            NavigationHostInspector.ResolveInnermostContent(frame.Content)
                .Should().BeOfType<AccountingWorkspaceShellPage>();
            AutomationProperties.GetItemStatus(frame).Should().Be("AccountingShell");
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
