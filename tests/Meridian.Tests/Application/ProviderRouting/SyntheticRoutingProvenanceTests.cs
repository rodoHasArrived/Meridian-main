using FluentAssertions;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Moq;
using Xunit;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class SyntheticRoutingProvenanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Composite_SyntheticNeverOutranksAvailableRealVendor(bool rotationEnabled)
    {
        var real = Provider("real", 5000, false);
        var synthetic = Provider("synthetic", 0, true);
        using var composite = new CompositeHistoricalDataProvider([synthetic.Object, real.Object], enableRateLimitRotation: rotationEnabled);
        var bars = await composite.GetDailyBarsAsync("AAPL", null, null);
        bars.Should().ContainSingle().Which.Source.Should().Be("real");
        synthetic.Verify(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Rotation_SyntheticNeverOutranksRealWhenQuotaIsApproaching(bool rotationEnabled)
    {
        var real = Provider("real", 5000, false);
        var synthetic = Provider("synthetic", 0, true);
        using var tracker = new ProviderRateLimitTracker();
        tracker.RegisterProvider("real", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);
        tracker.RegisterProvider("synthetic", 10, TimeSpan.FromMinutes(1), TimeSpan.Zero);
        for (var i = 0; i < 9; i++)
            tracker.RecordRequest("real");
        var rotation = new ProviderRotationStrategy(tracker, rotationEnabled, 0.8);
        rotation.OrderByAvailability([synthetic.Object, real.Object]).First().Name.Should().Be("real");
    }

    [Fact]
    public async Task Composite_RealFailureCanFallBackToSyntheticWithoutChangingSource()
    {
        var real = Provider("real", 5000, false);
        real.Setup(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Fixture vendor unavailable"));
        var synthetic = Provider("synthetic", 0, true);
        using var composite = new CompositeHistoricalDataProvider([synthetic.Object, real.Object]);
        var bars = await composite.GetDailyBarsAsync("AAPL", null, null);
        bars.Should().ContainSingle().Which.Source.Should().Be("synthetic");
    }

    private static Mock<IHistoricalDataProvider> Provider(string name, int priority, bool simulated)
    {
        var provider = new Mock<IHistoricalDataProvider>();
        provider.Setup(p => p.Name).Returns(name);
        provider.Setup(p => p.DisplayName).Returns(name);
        provider.Setup(p => p.Priority).Returns(priority);
        provider.Setup(p => p.IsSimulated).Returns(simulated);
        provider.Setup(p => p.MaxRequestsPerWindow).Returns(100);
        provider.Setup(p => p.RateLimitWindow).Returns(TimeSpan.FromMinutes(1));
        provider.Setup(p => p.Capabilities).Returns(HistoricalDataCapabilities.None);
        provider.Setup(p => p.GetDailyBarsAsync(It.IsAny<string>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new HistoricalBar("AAPL", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), 100, 105, 95, 102, 1000, name)]);
        return provider;
    }
}
