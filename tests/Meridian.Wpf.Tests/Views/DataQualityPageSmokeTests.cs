using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class DataQualityPageSmokeTests
{
    [Fact]
    public void DataQualityPage_ShouldResolveWithApplicationResources()
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

            Window? hostWindow = null;
            try
            {
                var exception = Record.Exception(() =>
                {
                    var page = serviceProvider.GetRequiredService<DataQualityPage>();
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
}
