using FluentAssertions;
using Meridian.Platform.Performance;

namespace Meridian.Tests.Platform.Performance;

public sealed class CoLocationProfileActivatorTests
{
    [Fact]
    public void Constructor_ExposesInactiveActivatorThroughInterface()
    {
        ICoLocationProfileActivator activator = new CoLocationProfileActivator();

        activator.IsActive.Should().BeFalse();
        activator.Should().BeOfType<CoLocationProfileActivator>();
    }
}
