using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class WorkstationPageSmokeTests
{
    [Theory]
    [InlineData(typeof(AnalysisExportPage))]
    [InlineData(typeof(EventReplayPage))]
    [InlineData(typeof(PositionBlotterPage))]
    [InlineData(typeof(ServiceManagerPage))]
    [InlineData(typeof(SettingsPage))]
    [InlineData(typeof(WelcomePage))]
    public void WorkstationPages_ShouldResolveWithApplicationResources(Type pageType)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();

            Window? hostWindow = null;
            try
            {
                var exception = Record.Exception(() =>
                {
                    var page = (Page)serviceProvider.GetRequiredService(pageType);
                    hostWindow = new Window
                    {
                        Width = 1280,
                        Height = 900,
                        Content = new Frame
                        {
                            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden,
                            Content = page
                        }
                    };

                    hostWindow.Show();
                    hostWindow.UpdateLayout();
                });

                exception.Should().BeNull();
            }
            finally
            {
                hostWindow?.Close();
            }
        });
    }

    [Theory]
    [InlineData("AnalysisExport", typeof(AnalysisExportPage))]
    [InlineData("EventReplay", typeof(EventReplayPage))]
    [InlineData("PositionBlotter", typeof(PositionBlotterPage))]
    [InlineData("ServiceManager", typeof(ServiceManagerPage))]
    [InlineData("Settings", typeof(SettingsPage))]
    [InlineData("Welcome", typeof(WelcomePage))]
    public void WorkstationPages_ShouldNavigateByTag(string pageTag, Type expectedPageType)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services]);

            using var serviceProvider = services.BuildServiceProvider();

            var navigationService = NavigationService.Instance;
            navigationService.ResetForTests();
            navigationService.SetServiceProvider(serviceProvider);

            var frame = new Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };

            Window? hostWindow = null;
            try
            {
                hostWindow = new Window
                {
                    Width = 1280,
                    Height = 900,
                    Content = frame
                };

                hostWindow.Show();
                navigationService.Initialize(frame);

                var navigated = navigationService.NavigateTo(pageTag);
                RunMatUiAutomationFacade.DrainDispatcher();
                frame.UpdateLayout();
                hostWindow.UpdateLayout();

                navigated.Should().BeTrue();
                frame.Content.Should().BeOfType<WorkspaceDeepPageHostPage>();
                NavigationHostInspector.ResolveInnermostPage(frame.Content).Should().BeOfType(expectedPageType);
            }
            finally
            {
                hostWindow?.Close();
                navigationService.ResetForTests();
            }
        });
    }
}
