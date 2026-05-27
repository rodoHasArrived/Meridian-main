using Meridian.Wpf.Services;
using Meridian.Wpf.Shell.Services;
using Meridian.Wpf.Tests.Support;

namespace Meridian.Wpf.Tests.Shell;

[Collection("NavigationServiceSerialCollection")]
public sealed class PageContentFactoryTests : IDisposable
{
    public PageContentFactoryTests()
    {
        NavigationService.Instance.ResetForTests();
    }

    public void Dispose()
    {
        NavigationService.Instance.ResetForTests();
    }

    [Fact]
    public void CreateContent_KnownCompatibilityAlias_ReturnsFrameworkElement()
    {
        WpfTestThread.Run(() =>
        {
            var factory = new PageContentFactory(NavigationService.Instance);

            var content = factory.CreateContent("ResearchShell");

            content.Should().NotBeNull();
        });
    }

    [Fact]
    public void CreateContent_UnknownRoute_ThrowsInsteadOfReturningMisroutedContent()
    {
        WpfTestThread.Run(() =>
        {
            var factory = new PageContentFactory(NavigationService.Instance);

            var act = () => factory.CreateContent("NotARegisteredShellRoute");

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*NotARegisteredShellRoute*");
        });
    }
}
