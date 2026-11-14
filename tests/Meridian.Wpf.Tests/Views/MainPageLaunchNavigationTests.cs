using System.Windows;
using System.Windows.Automation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class MainPageLaunchNavigationTests
{
    [Theory]
    [InlineData("AccountingShell")]
    [InlineData("TradingShell")]
    [InlineData("PortfolioShell")]
    [InlineData("ReportingShell")]
    [InlineData("DataShell")]
    [InlineData("SettingsShell")]
    [InlineData("ResearchShell")]
    public void WorkspaceHomeLaunch_ShouldRenderTheRequestedPageInTheVisiblePane(string pageTag)
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();
            var paneHost = InitializePaneHost(facade);
            facade.Page.NavigateToLaunchPage("StrategyShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.Page.NavigateToLaunchPage(pageTag).Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            var expectedPage = ShellNavigationCatalog.GetPage(pageTag)!;
            var visibleFrame = paneHost.GetPaneFrame(facade.ViewModel.PaneHost.ActivePaneIndex)!;
            facade.ViewModel.CurrentPageTag.Should().Be(expectedPage.PageTag);
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be(expectedPage.PageTag);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Visible);
            facade.ContentFrame.Visibility.Should().Be(Visibility.Collapsed);
            visibleFrame.Content.Should().BeOfType(expectedPage.PageType,
                "the shell header alone does not prove which workspace body is displayed");
            AutomationProperties.GetAutomationId(visibleFrame).Should().Be("WorkspacePane0");
            AutomationProperties.GetItemType(visibleFrame).Should().Be("ActiveWorkspacePane");
            AutomationProperties.GetItemStatus(visibleFrame).Should().Be(expectedPage.PageTag);
        });
    }

    [Fact]
    public void WorkspaceHomeLaunch_ShouldPreserveTheLayoutAndOtherPaneInstances()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();
            var paneHost = InitializePaneHost(facade);
            facade.ViewModel.PaneHost.SelectLayoutCommand.Execute(PaneLayouts.TradingCockpit);
            RunMatUiAutomationFacade.DrainDispatcher();

            var initialPages = new[] { "StrategyShell", "DataShell", "TradingShell" };
            for (var index = 0; index < initialPages.Length; index++)
            {
                facade.ViewModel.PaneHost.AssignPageToPane(initialPages[index], index);
                RunMatUiAutomationFacade.DrainDispatcher();
            }

            facade.ViewModel.PaneHost.SetActivePaneCommand.Execute(0);
            RunMatUiAutomationFacade.DrainDispatcher();
            var secondPaneContent = paneHost.GetPaneFrame(1)!.Content;
            var thirdPaneContent = paneHost.GetPaneFrame(2)!.Content;
            secondPaneContent.Should().BeOfType(ShellNavigationCatalog.GetPage("DataShell")!.PageType);
            thirdPaneContent.Should().BeOfType(ShellNavigationCatalog.GetPage("TradingShell")!.PageType);

            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ViewModel.PaneHost.SelectedLayout.Should().BeSameAs(PaneLayouts.TradingCockpit);
            facade.ViewModel.PaneHost.ActivePaneIndex.Should().Be(0);
            Enumerable.Range(0, 3).Select(facade.ViewModel.PaneHost.GetAssignedPageTag)
                .Should().Equal("AccountingShell", "DataShell", "TradingShell");
            paneHost.GetPaneFrame(0)!.Content.Should().BeOfType<AccountingWorkspaceShellPage>();
            paneHost.GetPaneFrame(1)!.Content.Should().BeSameAs(secondPaneContent);
            paneHost.GetPaneFrame(2)!.Content.Should().BeSameAs(thirdPaneContent);
            AutomationProperties.GetItemType(paneHost.GetPaneFrame(1)!).Should().Be("WorkspacePane");
        });
    }

    [Fact]
    public void LeafLaunchThenWorkspaceHome_ShouldUseTheCorrespondingVisibleSurface()
    {
        WpfTestThread.Run(() =>
        {
            using var facade = new MainPageUiAutomationFacade();
            var paneHost = InitializePaneHost(facade);

            facade.Page.NavigateToLaunchPage("AccountingShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();
            facade.Page.NavigateToLaunchPage("RunMat").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ContentFrame.Visibility.Should().Be(Visibility.Visible);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Collapsed);
            NavigationHostInspector.ResolveInnermostContent(facade.ContentFrame.Content)
                .Should().BeOfType<RunMatPage>();
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("RunMat");
            facade.ViewModel.PaneHost.GetAssignedPageTag(0).Should().Be("AccountingShell");

            facade.Page.NavigateToLaunchPage("DataShell").Should().BeTrue();
            RunMatUiAutomationFacade.DrainDispatcher();

            facade.ContentFrame.Visibility.Should().Be(Visibility.Collapsed);
            facade.WorkspaceHomeSurface.Visibility.Should().Be(Visibility.Visible);
            var visibleFrame = paneHost.GetPaneFrame(0)!;
            visibleFrame.Content.Should().BeOfType(ShellNavigationCatalog.GetPage("DataShell")!.PageType);
            AutomationProperties.GetItemStatus(visibleFrame).Should().Be("DataShell");
            AutomationProperties.GetItemStatus(facade.ContentFrame).Should().Be("RunMat",
                "rendered identity must describe actual frame content independently of the shell tag");
        });
    }

    private static SplitPaneHostControl InitializePaneHost(MainPageUiAutomationFacade facade)
    {
        var paneHost = (SplitPaneHostControl)facade.Page.FindName("SplitPaneHost");
        paneHost.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, paneHost));
        RunMatUiAutomationFacade.DrainDispatcher();
        return paneHost;
    }
}
