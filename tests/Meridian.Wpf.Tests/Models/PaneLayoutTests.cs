using System.ComponentModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Tests.Models;

public sealed class PaneLayoutTests
{
    [Fact]
    public void TwoPaneWorkbenchLayout_UsesCanonicalStrategyLabel()
    {
        PaneLayouts.StrategyData.Kind.Should().Be(PaneLayoutKind.StrategyData);
        PaneLayouts.StrategyData.Label.Should().Be("Strategy + Data");
        PaneLayouts.StrategyData.Label.Should().NotContain("Research");
        PaneLayouts.ResearchData.Should().BeSameAs(PaneLayouts.StrategyData);
        typeof(PaneLayouts)
            .GetField(nameof(PaneLayouts.ResearchData))!
            .GetCustomAttributes(typeof(EditorBrowsableAttribute), inherit: false)
            .Should()
            .ContainSingle(attribute => ((EditorBrowsableAttribute)attribute).State == EditorBrowsableState.Never);
    }
}
