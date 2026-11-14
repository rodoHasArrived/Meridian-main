using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Models;
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
            InvokeConfigureServices(configureServices!, services);

            using var navigationHost = new DesktopNavigationTestHost(services);
            var serviceProvider = navigationHost.Services;

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
            InvokeConfigureServices(configureServices!, services);

            using var navigationHost = new DesktopNavigationTestHost(services);
            var serviceProvider = navigationHost.Services;

            var navigationService = NavigationService.Instance;

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
            }
        });
    }

    [Theory]
    [InlineData("ServiceManager", typeof(ServiceManagerPage))]
    [InlineData("Welcome", typeof(WelcomePage))]
    [InlineData("OrderBook", typeof(OrderBookPage))]
    public void ConsecutiveNavigationHosts_ShouldOwnFreshWorkspaceScopes(string pageTag, Type expectedPageType)
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();
            WorkspaceScopeProbe? previousProbe = null;
            var workspaceId = ShellNavigationCatalog.InferWorkspaceIdForPageTag(pageTag)!;

            for (var hostIndex = 0; hostIndex < 2; hostIndex++)
            {
                var services = new ServiceCollection();
                var configureServices = typeof(Meridian.Wpf.App)
                    .GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
                configureServices.Should().NotBeNull();
                InvokeConfigureServices(configureServices!, services);

                var rootOwner = new object();
                services.AddScoped(_ => new WorkspaceScopeProbe(rootOwner));
                WorkspaceScopeProbe? currentProbe = null;
                using (var navigationHost = new DesktopNavigationTestHost(services))
                {
                    var workspaceService = navigationHost.Services.GetRequiredService<WorkspaceService>();
                    var workspaceScope = workspaceService.GetOrCreateWorkspaceScope(workspaceId);
                    workspaceScope.Should().NotBeNull();
                    currentProbe = workspaceScope!.ServiceProvider.GetRequiredService<WorkspaceScopeProbe>();
                    currentProbe.Owner.Should().BeSameAs(rootOwner,
                        "a cached scope must belong to this test's root provider");
                    currentProbe.Should().NotBeSameAs(previousProbe);

                    var frame = new Frame
                    {
                        NavigationUIVisibility = NavigationUIVisibility.Hidden
                    };
                    var window = new Window { Width = 1280, Height = 900, Content = frame };
                    try
                    {
                        window.Show();
                        NavigationService.Instance.Initialize(frame);
                        NavigationService.Instance.NavigateTo(pageTag).Should().BeTrue();
                        RunMatUiAutomationFacade.DrainDispatcher();
                        NavigationHostInspector.ResolveInnermostPage(frame.Content)
                            .Should().BeOfType(expectedPageType);
                        WorkspaceService.Instance.GetWorkspaceScope(workspaceId).Should().BeSameAs(workspaceScope);
                    }
                    finally
                    {
                        window.Close();
                    }
                }

                currentProbe!.IsDisposed.Should().BeTrue("workspace scopes must end before their root provider");
                WorkspaceService.Instance.GetWorkspaceScope(workspaceId).Should().BeNull();
                previousProbe = currentProbe;
            }
        });
    }

    private sealed class WorkspaceScopeProbe(object owner) : IDisposable
    {
        public object Owner { get; } = owner;
        public bool IsDisposed { get; private set; }
        public void Dispose() => IsDisposed = true;
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
