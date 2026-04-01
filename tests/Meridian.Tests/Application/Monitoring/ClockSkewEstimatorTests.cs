using FluentAssertions;
using Meridian.Application.Monitoring;

namespace Meridian.Tests.Application.Monitoring;

public sealed class ClockSkewEstimatorTests
{
    private static ClockSkewEstimator CreateSut(double alpha = 0.05) => new(alpha);

    private static DateTimeOffset At(long epochMs) =>
        DateTimeOffset.UnixEpoch.AddMilliseconds(epochMs);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(-1.0)]
    public void Constructor_WithAlphaAtOrBelowZero_ThrowsArgumentOutOfRangeException(double alpha)
    {
        var act = () => new ClockSkewEstimator(alpha);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("alpha");
    }

    [Theory]
    [InlineData(1.1)]
    [InlineData(2.0)]
    [InlineData(100.0)]
    public void Constructor_WithAlphaAboveOne_ThrowsArgumentOutOfRangeException(double alpha)
    {
        var act = () => new ClockSkewEstimator(alpha);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("alpha");
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Constructor_WithValidAlpha_DoesNotThrow(double alpha)
    {
        var act = () => new ClockSkewEstimator(alpha);

        act.Should().NotThrow();
    }

    [Fact]
    public void GetEstimatedSkewMs_ForUnknownProvider_ReturnsZero()
    {
        var sut = CreateSut();

        var skew = sut.GetEstimatedSkewMs("unknown-provider");

        skew.Should().Be(0.0);
    }

    [Fact]
    public void RecordObservation_FirstObservation_SeedsEwmaDirectly()
    {
        var sut = CreateSut(alpha: 0.1);
        var exchange = At(1_000);
        var received = At(1_100); // 100ms skew

        sut.RecordObservation("provider-a", exchange, received);

        sut.GetEstimatedSkewMs("provider-a").Should().BeApproximately(100.0, precision: 0.001);
    }

    [Fact]
    public void RecordObservation_SubsequentObservations_AppliesEwmaFormula()
    {
        // With alpha=0.1: ewma = 0.1 * new + 0.9 * old
        var sut = CreateSut(alpha: 0.1);
        var exchange = At(0);

        sut.RecordObservation("p", exchange, At(100));  // ewma = 100
        sut.RecordObservation("p", exchange, At(200));  // ewma = 0.1*200 + 0.9*100 = 20+90 = 110

        sut.GetEstimatedSkewMs("p").Should().BeApproximately(110.0, precision: 0.001);
    }

    [Fact]
    public void RecordObservation_MultipleObservations_ConvergesExponentiallyTowardNewValue()
    {
        var sut = CreateSut(alpha: 0.5);
        var exchange = At(0);

        sut.RecordObservation("p", exchange, At(100)); // ewma = 100
        sut.RecordObservation("p", exchange, At(200)); // ewma = 0.5*200 + 0.5*100 = 150
        sut.RecordObservation("p", exchange, At(200)); // ewma = 0.5*200 + 0.5*150 = 175

        sut.GetEstimatedSkewMs("p").Should().BeApproximately(175.0, precision: 0.001);
    }

    [Fact]
    public void RecordObservation_WithNegativeSkew_ProviderAheadOfLocal_TracksProperly()
    {
        // Exchange timestamp is ahead of received time → negative skew
        var sut = CreateSut(alpha: 1.0); // alpha=1 = no smoothing, tracks latest directly
        var received = At(1_000);
        var exchange = At(1_050); // exchange is 50ms ahead → skew = -50ms

        sut.RecordObservation("p", exchange, received);

        sut.GetEstimatedSkewMs("p").Should().BeApproximately(-50.0, precision: 0.001);
    }

    [Fact]
    public void GetAllSnapshots_WithNoObservations_ReturnsEmptyDictionary()
    {
        var sut = CreateSut();

        var snapshots = sut.GetAllSnapshots();

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSnapshots_AfterObservations_ContainsAllProviders()
    {
        var sut = CreateSut();
        var exchange = At(0);

        sut.RecordObservation("provA", exchange, At(100));
        sut.RecordObservation("provB", exchange, At(200));

        var snapshots = sut.GetAllSnapshots();

        snapshots.Should().ContainKey("provA").And.ContainKey("provB");
    }

    [Fact]
    public void GetAllSnapshots_TracksSampleCount()
    {
        var sut = CreateSut();
        var exchange = At(0);

        sut.RecordObservation("p", exchange, At(100));
        sut.RecordObservation("p", exchange, At(110));
        sut.RecordObservation("p", exchange, At(120));

        var snapshot = sut.GetAllSnapshots()["p"];

        snapshot.SampleCount.Should().Be(3);
    }

    [Fact]
    public void GetAllSnapshots_TracksMinAndMaxSkew()
    {
        var sut = CreateSut(alpha: 0.1);
        var exchange = At(0);

        sut.RecordObservation("p", exchange, At(50));   // skew = 50ms
        sut.RecordObservation("p", exchange, At(200));  // skew = 200ms
        sut.RecordObservation("p", exchange, At(10));   // skew = 10ms

        var snapshot = sut.GetAllSnapshots()["p"];

        snapshot.MinSkewMs.Should().BeApproximately(10.0, precision: 0.001);
        snapshot.MaxSkewMs.Should().BeApproximately(200.0, precision: 0.001);
    }

    [Fact]
    public void RecordObservation_IsCaseInsensitive_SameProviderDifferentCase()
    {
        var sut = CreateSut(alpha: 1.0);
        var exchange = At(0);

        sut.RecordObservation("MyProvider", exchange, At(100));
        sut.RecordObservation("myprovider", exchange, At(200)); // should update same bucket

        // With alpha=1.0, latest value wins
        sut.GetEstimatedSkewMs("MYPROVIDER").Should().BeApproximately(200.0, precision: 0.001);
        sut.GetAllSnapshots().Should().HaveCount(1, "case-insensitive lookup should merge into one provider");
    }

    [Fact]
    public void GetAllSnapshots_SnapshotEstimatedSkewMatchesGetEstimatedSkewMs()
    {
        var sut = CreateSut();
        var exchange = At(0);

        sut.RecordObservation("p", exchange, At(75));

        var snapshot = sut.GetAllSnapshots()["p"];
        var direct = sut.GetEstimatedSkewMs("p");

        snapshot.EstimatedSkewMs.Should().BeApproximately(direct, precision: 0.001);
    }

    [Fact]
    public async Task RecordObservation_ConcurrentUpdates_DoesNotThrow()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sut = CreateSut(alpha: 0.1);
        var exchange = At(0);

        var tasks = Enumerable.Range(0, 200)
            .Select(i => Task.Run(() =>
                sut.RecordObservation("shared-provider", exchange, At(i * 10L)), cts.Token))
            .ToArray();

        await Task.WhenAll(tasks);

        var snapshot = sut.GetAllSnapshots()["shared-provider"];
        snapshot.SampleCount.Should().Be(200);
    }
}
