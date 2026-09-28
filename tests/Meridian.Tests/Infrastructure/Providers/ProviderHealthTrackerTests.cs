using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderHealthTrackerTests
{
    [Fact]
    public void AliasUpdatesAndBackoffShareOneCanonicalFamily()
    {
        var tracker = new ProviderHealthTracker(["ib", "interactive-brokers", "ibkr"], TimeSpan.FromMinutes(1));

        tracker.RecordFailure(" INTERACTIVE-BROKERS ", "test failure");

        tracker.Health.Should().ContainSingle();
        tracker.Health["ibkr"].ProviderName.Should().Be("ibkr");
        tracker.Health["ibkr"].IsAvailable.Should().BeFalse();
        tracker.IsInBackoffPeriod("ib").Should().BeTrue();
        tracker.IsInBackoffPeriod("ib-sim").Should().BeFalse();

        tracker.ClearFailure("ibkr");
        tracker.UpdateHealth("ib", true);

        tracker.IsInBackoffPeriod("interactive-brokers").Should().BeFalse();
        tracker.Health["ibkr"].IsAvailable.Should().BeTrue();
    }
}
