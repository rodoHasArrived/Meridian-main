using Microsoft.Extensions.DependencyInjection;
using Meridian.Wpf.Services;
using Meridian.Wpf.Tests.Support;
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
}
