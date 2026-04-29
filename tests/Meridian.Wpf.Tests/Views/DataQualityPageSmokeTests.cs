using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class DataQualityPageSmokeTests
{
    [Fact]
    public void DataQualityPageSource_ShouldWireSymbolSearchRecovery()
    {
        var xaml = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataQualityPage.xaml"));
        var code = File.ReadAllText(RunMatUiAutomationFacade.GetRepoFilePath(@"src\Meridian.Wpf\Views\DataQualityPage.xaml.cs"));

        xaml.Should().Contain("Text=\"{Binding SymbolFilterScopeText}\"");
        xaml.Should().Contain("Text=\"{Binding SymbolEmptyStateTitle}\"");
        xaml.Should().Contain("Text=\"{Binding SymbolEmptyStateDetail}\"");
        xaml.Should().Contain("AutomationProperties.AutomationId=\"DataQualityClearSymbolFilterButton\"");
        code.Should().Contain("ClearSymbolFilter_Click");
        code.Should().Contain("_viewModel.ClearSymbolFilter()");
    }

    [Fact]
    public void DataQualityPage_ShouldResolveWithApplicationResources()
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
