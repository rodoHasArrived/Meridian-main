using Meridian.Wpf.Tests.Support;
using Meridian.Wpf.Views;

namespace Meridian.Wpf.Tests.Views;

public sealed class SystemHealthPageSmokeTests
{
    [Fact]
    public void SystemHealthPage_ShouldInstantiateWithApplicationResources()
    {
        WpfTestThread.Run(() =>
        {
            RunMatUiAutomationFacade.EnsureApplicationResources();

            var exception = Record.Exception(() => new SystemHealthPage());

            exception.Should().BeNull();
        });
    }
}
