using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class MainPageSmokeTests
{
    [Fact]
    public void MainPage_ShouldInstantiateWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.SetServiceProvider(services);

            var exception = Record.Exception(() => services.GetRequiredService<MainPage>());

            exception.Should().BeNull();
        });
    }

    [Fact]
    public void SplitPaneDrop_SplitRight_AssignsDraggedPageToNewPane()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.SetServiceProvider(services);

            var page = services.GetRequiredService<MainPage>();
            page.DataContext.Should().BeOfType<MainPageViewModel>();
            var viewModel = (MainPageViewModel)page.DataContext!;
            viewModel.SplitPane.AssignPageToPane("Dashboard", 0);

            var method = typeof(MainPage).GetMethod("OnSplitPanePaneDropRequested", BindingFlags.Instance | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            method!.Invoke(page, [page, new PaneDropEventArgs("StrategyRuns", 0, PaneDropAction.SplitRight)]);

            viewModel.SplitPane.SelectedLayout.PaneCount.Should().Be(2);
            viewModel.SplitPane.GetAssignedPageTag(0).Should().Be("Dashboard");
            viewModel.SplitPane.GetAssignedPageTag(1).Should().Be("StrategyRuns");
            viewModel.SplitPane.ActivePaneIndex.Should().Be(1);
        });
    }
}
