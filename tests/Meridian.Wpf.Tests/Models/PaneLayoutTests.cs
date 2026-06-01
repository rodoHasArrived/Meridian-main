using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class PaneLayoutTests
{
    [Fact]
    public void TwoPaneWorkbenchLayout_UsesCanonicalStrategyLabel()
    {
        PaneLayouts.ResearchData.Kind.Should().Be(PaneLayoutKind.ResearchData);
        PaneLayouts.ResearchData.Label.Should().Be("Strategy + Data");
        PaneLayouts.ResearchData.Label.Should().NotContain("Research");
    }
}
