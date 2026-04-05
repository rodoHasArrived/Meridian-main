using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Models;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class NavigationPageSmokeTests
{
    [Fact]
    public void NavigationPages_ShouldResolveWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            configureServices!.Invoke(null, [services, configuration]);

            using var serviceProvider = services.BuildServiceProvider();

            var orderBookException = Record.Exception(() => serviceProvider.GetRequiredService<OrderBookPage>());
            orderBookException.Should().BeNull();

            var runRiskException = Record.Exception(() => serviceProvider.GetRequiredService<RunRiskPage>());
            runRiskException.Should().BeNull();

            var notificationCenterException = Record.Exception(() => serviceProvider.GetRequiredService<NotificationCenterPage>());
            notificationCenterException.Should().BeNull();

            Window? leanIntegrationHost = null;
            try
            {
                var leanIntegrationException = Record.Exception(() =>
                {
                    var leanIntegrationPage = serviceProvider.GetRequiredService<LeanIntegrationPage>();
                    leanIntegrationHost = new Window
                    {
                        Width = 1280,
                        Height = 900,
                        Content = new Frame
                        {
                            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden,
                            Content = leanIntegrationPage
                        }
                    };

                    leanIntegrationHost.Show();
                    leanIntegrationHost.UpdateLayout();
                });

                leanIntegrationException.Should().BeNull();
            }
            finally
            {
                leanIntegrationHost?.Close();
            }
        });
    }

    [Fact]
    public void MeridianDockingManager_ShouldHostPagesInsideFrames()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var manager = new MeridianDockingManager
            {
                Width = 1200,
                Height = 800
            };

            var window = new Window
            {
                Width = 1280,
                Height = 900,
                Content = manager
            };

            try
            {
                var exception = Record.Exception(() =>
                {
                    manager.LoadPage("sample", "Sample", new Page(), PaneDropAction.Replace);
                    window.Show();
                    manager.UpdateLayout();
                    window.UpdateLayout();
                });

                exception.Should().BeNull();
            }
            finally
            {
                window.Close();
            }
        });
    }
}
