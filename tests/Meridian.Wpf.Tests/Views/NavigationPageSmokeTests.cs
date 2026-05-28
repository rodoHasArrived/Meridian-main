using System.Reflection;
using AvalonDock.Layout;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Models;
using Meridian.Wpf.Services;
using Meridian.Wpf.ViewModels;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

[Collection("NavigationServiceSerialCollection")]
public sealed class NavigationPageSmokeTests
{
    [Fact]
    public void NavigationPages_ShouldResolveWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            Environment.SetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("POLYGON_API_KEY", null);
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            InvokeConfigureServices(configureServices!, services);

            using var serviceProvider = services.BuildServiceProvider();

            var orderBookException = Record.Exception(() => serviceProvider.GetRequiredService<OrderBookPage>());
            orderBookException.Should().BeNull();

            var runRiskException = Record.Exception(() => serviceProvider.GetRequiredService<RunRiskPage>());
            runRiskException.Should().BeNull();

            var notificationCenterException = Record.Exception(() => serviceProvider.GetRequiredService<NotificationCenterPage>());
            notificationCenterException.Should().BeNull();

            var securityMasterException = Record.Exception(() => serviceProvider.GetRequiredService<SecurityMasterPage>());
            securityMasterException.Should().BeNull();

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

    [Theory]
    [InlineData("OrderBook", typeof(OrderBookViewModel))]
    [InlineData("RunRisk", typeof(RunRiskViewModel))]
    [InlineData("NotificationCenter", typeof(NotificationCenterViewModel))]
    [InlineData("SecurityMaster", typeof(SecurityMasterViewModel))]
    [InlineData("LeanIntegration", typeof(LeanIntegrationViewModel))]
    public void PrimaryNavigationPages_ShouldKeepExpectedDataContexts(string pageTag, Type expectedViewModelType)
    {
        WpfTestThread.Run(() =>
        {
            Environment.SetEnvironmentVariable("MERIDIAN_SECURITY_MASTER_CONNECTION_STRING", null);
            Environment.SetEnvironmentVariable("POLYGON_API_KEY", null);
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var services = new ServiceCollection();
            var configureServices = typeof(Meridian.Wpf.App)
                .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);

            configureServices.Should().NotBeNull();
            InvokeConfigureServices(configureServices!, services);

            using var serviceProvider = services.BuildServiceProvider();
            var navigationService = NavigationService.Instance;
            Window? hostWindow = null;

            try
            {
                navigationService.ResetForTests();
                navigationService.SetServiceProvider(serviceProvider);

                var frame = new Frame
                {
                    NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
                };
                hostWindow = new Window
                {
                    Width = 1280,
                    Height = 900,
                    Content = frame
                };

                hostWindow.Show();
                navigationService.Initialize(frame);

                navigationService.NavigateTo(pageTag).Should().BeTrue();
                RunMatUiAutomationFacade.DrainDispatcher();
                hostWindow.UpdateLayout();

                var page = NavigationHostInspector.ResolveInnermostPage(frame.Content);
                page.Should().NotBeNull();
                page!.DataContext.Should().BeOfType(expectedViewModelType);
            }
            finally
            {
                hostWindow?.Close();
                navigationService.ResetForTests();
                RunMatUiAutomationFacade.DrainDispatcher();
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

    [Fact]
    public void MeridianDockingManager_ShouldReplaceFallbackContentOnRetry()
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
                window.Show();
                manager.UpdateLayout();
                window.UpdateLayout();

                manager.LoadPage(
                    "sample",
                    "Sample",
                    WorkspaceShellFallbackContentFactory.CreateDockFailureContent("Sample", new InvalidOperationException("boom")),
                    PaneDropAction.Replace);
                manager.LoadPage("sample", "Sample", new Page(), PaneDropAction.OpenTab);
                RunMatUiAutomationFacade.DrainDispatcher();
                manager.UpdateLayout();
                window.UpdateLayout();

                var openDocumentsField = typeof(MeridianDockingManager).GetField("_openDocuments", BindingFlags.Instance | BindingFlags.NonPublic);
                openDocumentsField.Should().NotBeNull();

                var openDocuments = openDocumentsField!.GetValue(manager).Should().BeAssignableTo<System.Collections.IDictionary>().Subject;
                openDocuments.Contains("sample").Should().BeTrue();

                var descriptor = openDocuments["sample"];
                descriptor.Should().NotBeNull();

                var documentProperty = descriptor!.GetType().GetProperty("Document", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                documentProperty.Should().NotBeNull();

                var document = documentProperty!.GetValue(descriptor).Should().BeOfType<LayoutDocument>().Subject;
                var hostFrame = document.Content.Should().BeOfType<Frame>().Subject;
                hostFrame.Content.Should().BeOfType<Page>();
                hostFrame.Background.Should().Be(Brushes.Transparent);
                WorkspaceShellFallbackContentFactory.IsFallbackContent(document.Content).Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void InvokeConfigureServices(MethodInfo configureServices, IServiceCollection services)
    {
        if (configureServices.GetParameters().Length == 1)
        {
            configureServices.Invoke(null, [services]);
            return;
        }

        var configuration = new ConfigurationBuilder().Build();
        configureServices.Invoke(null, [services, configuration]);
    }
}
