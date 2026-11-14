using FluentAssertions;
using Meridian.Infrastructure.Adapters.Core;
using Xunit;

namespace Meridian.Tests.Infrastructure.Providers;

public sealed class ProviderRateLimitHistoryTests
{
    [Fact]
    public void Read_RetainsOnlyBoundedNewestEventsAndFiltersProvider()
    {
        var history = new ProviderRateLimitHistory(2);
        history.Record("polygon", null);
        history.Record("tiingo", null);
        history.Record("polygon", null, "stooq", 429);
        var snapshot = history.Read("POLYGON", 24);
        snapshot.Should().ContainSingle();
        snapshot[0].EventType.Should().Be("rotated");
        snapshot[0].NextProvider.Should().Be("stooq");
        snapshot[0].HttpStatus.Should().Be(429);
        history.Read("tiingo", 24).Should().ContainSingle();
        history.Read("missing", 24).Should().BeEmpty();
    }

    [Fact]
    public void Read_DoesNotInventHttpStatusForUnknownThrottle()
    {
        var history = new ProviderRateLimitHistory();
        history.Record("polygon", null);
        history.Read("polygon", 24)[0].HttpStatus.Should().BeNull();
        history.Read("polygon", 1, DateTimeOffset.UtcNow.AddHours(2)).Should().BeEmpty();
    }

    [Fact]
    public void ConcurrentWriters_KeepCapacityAndCoherentSnapshots()
    {
        var history = new ProviderRateLimitHistory(10);
        Parallel.For(0, 1000, _ => history.Record("polygon", null));
        history.Read("polygon", 24).Should().HaveCount(10);
    }
}
