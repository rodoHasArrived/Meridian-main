using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class WorkstationPageSmokeTests
{
    [Theory]
    [InlineData(typeof(EventReplayPage))]
    [InlineData(typeof(PositionBlotterPage))]
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
    [InlineData("EventReplay", typeof(EventReplayPage))]
    [InlineData("PositionBlotter", typeof(PositionBlotterPage))]
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
                frame.Content.Should().BeOfType(expectedPageType);
            }
            finally
            {
                hostWindow?.Close();
                navigationService.ResetForTests();
            }
        });
    }
}
