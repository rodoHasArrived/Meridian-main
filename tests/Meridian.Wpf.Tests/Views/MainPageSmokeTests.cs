using System.Reflection;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class MainPageSmokeTests
{
    [Fact]
    public void MainPage_ShouldInstantiateWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            using var services = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.ResetForTests();
            NavigationService.Instance.SetServiceProvider(services);

            MainPage? page = null;
            try
            {
                var exception = Record.Exception(() => page = services.GetRequiredService<MainPage>());

                exception.Should().BeNull();
            }
            finally
            {
                if (page?.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                NavigationService.Instance.ResetForTests();
            }
        });
    }

    [Fact]
    public void MainPage_ShouldInstantiateAfterThemeServiceInitialization()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            ThemeService.Instance.Initialize(new Window());

            using var services = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.ResetForTests();
            NavigationService.Instance.SetServiceProvider(services);

            MainPage? page = null;
            try
            {
                var exception = Record.Exception(() => page = services.GetRequiredService<MainPage>());

                exception.Should().BeNull();
            }
            finally
            {
                if (page?.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                NavigationService.Instance.ResetForTests();
            }
        });
    }

    [Fact]
    public void MainPage_WaitForShellReadyAsync_ShouldCompleteAfterLoaded()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            using var services = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.ResetForTests();
            NavigationService.Instance.SetServiceProvider(services);

            var page = services.GetRequiredService<MainPage>();
            try
            {
                var waitTask = page.WaitForShellReadyAsync();

                waitTask.IsCompleted.Should().BeFalse();

                RunMatUiAutomationFacade.InvokeMainPageLoaded(page);

                waitTask.IsCompleted.Should().BeTrue();
            }
            finally
            {
                if (page.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                NavigationService.Instance.ResetForTests();
            }
        });
    }

    [Fact]
    public void SplitPaneDrop_SplitRight_AssignsDraggedPageToNewPane()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            using var services = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.ResetForTests();
            NavigationService.Instance.SetServiceProvider(services);

            var page = services.GetRequiredService<MainPage>();
            try
            {
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
            }
            finally
            {
                if (page.DataContext is IDisposable disposable)
                {
                    disposable.Dispose();
                }

                NavigationService.Instance.ResetForTests();
            }
        });
    }

    [Fact]
    public void NavigationService_CreatePageContent_ShouldResolveSecurityMasterFromMainPageTestProvider()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            using var services = (ServiceProvider)RunMatUiAutomationFacade.CreateMainPageServiceProvider();
            NavigationService.Instance.ResetForTests();
            NavigationService.Instance.SetServiceProvider(services);

            try
            {
                FrameworkElement? content = null;
                var exception = Record.Exception(() =>
                    content = NavigationService.Instance.CreatePageContent("SecurityMaster"));

                exception.Should().BeNull();
                content.Should().NotBeNull();
                NavigationHostInspector.ResolveInnermostPage(content)
                    .Should().BeOfType<SecurityMasterPage>();
            }
            finally
            {
                NavigationService.Instance.ResetForTests();
            }
        });
    }
}
