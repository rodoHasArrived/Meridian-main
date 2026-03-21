using FluentAssertions;
using Meridian.Application.Monitoring;
using Xunit;

namespace Meridian.Tests.Monitoring;

public sealed class ProviderLatencyServiceTests : IDisposable
{
    private readonly ProviderLatencyService _service;

    public ProviderLatencyServiceTests()
    {
        _service = new ProviderLatencyService();
    }

    [Fact]
    public void RecordLatency_SingleProvider_ShouldTrack()
    {
        // Act
        _service.RecordLatency("Alpaca", 10.0);
        _service.RecordLatency("Alpaca", 15.0);
        _service.RecordLatency("Alpaca", 20.0);

        // Assert
        var histogram = _service.GetHistogram("Alpaca");
        histogram.Should().NotBeNull();
        histogram!.SampleCount.Should().Be(3);
        histogram.MeanMs.Should().BeApproximately(15.0, 0.1);
        histogram.MinMs.Should().BeApproximately(10.0, 0.1);
        histogram.MaxMs.Should().BeApproximately(20.0, 0.1);
    }

    [Fact]
    public void RecordLatency_MultipleProviders_ShouldTrackSeparately()
    {
        // Act
        _service.RecordLatency("Alpaca", 10.0);
        _service.RecordLatency("Polygon", 50.0);
        _service.RecordLatency("IB", 5.0);

        // Assert
        var histograms = _service.GetAllHistograms();
        histograms.Should().HaveCount(3);

        _service.GetHistogram("Alpaca")!.MeanMs.Should().BeApproximately(10.0, 0.1);
        _service.GetHistogram("Polygon")!.MeanMs.Should().BeApproximately(50.0, 0.1);
        _service.GetHistogram("IB")!.MeanMs.Should().BeApproximately(5.0, 0.1);
    }

    [Fact]
    public void GetSummary_ShouldIdentifyFastestAndSlowest()
    {
        // Arrange
        _service.RecordLatency("Fast", 5.0);
        _service.RecordLatency("Medium", 25.0);
        _service.RecordLatency("Slow", 100.0);

        // Act
        var summary = _service.GetSummary();

        // Assert
        summary.FastestProvider.Should().Be("fast");
        summary.SlowestProvider.Should().Be("slow");
        summary.TotalSamples.Should().Be(3);
    }

    [Fact]
    public void GetHistogram_Percentiles_ShouldBeCalculated()
    {
        // Arrange - create a distribution
        for (int i = 1; i <= 100; i++)
        {
            _service.RecordLatency("TestProvider", i);
        }

        // Act
        var histogram = _service.GetHistogram("TestProvider");

        // Assert
        histogram.Should().NotBeNull();
        histogram!.P50Ms.Should().BeApproximately(50.0, 2.0);
        histogram.P95Ms.Should().BeApproximately(95.0, 2.0);
        histogram.P99Ms.Should().BeApproximately(99.0, 2.0);
    }

    [Fact]
    public void GetHistogram_Buckets_ShouldBePopulated()
    {
        // Arrange
        _service.RecordLatency("TestProvider", 0.5); // First bucket
        _service.RecordLatency("TestProvider", 5.0); // ~bucket 2
        _service.RecordLatency("TestProvider", 100.0); // Later bucket
        _service.RecordLatency("TestProvider", 1000.0); // High bucket

        // Act
        var histogram = _service.GetHistogram("TestProvider");

        // Assert
        histogram.Should().NotBeNull();
        histogram!.Buckets.Should().NotBeEmpty();
        histogram.Buckets.Sum(b => b.Count).Should().Be(4);
    }

    [Fact]
    public void GetHighLatencyProviders_ShouldFilterByThreshold()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _service.RecordLatency("Fast", 10.0);
            _service.RecordLatency("Slow", 200.0);
        }

        // Act
        var highLatency = _service.GetHighLatencyProviders(50.0);

        // Assert
        highLatency.Should().HaveCount(1);
        highLatency[0].Provider.Should().Be("slow");
    }

    [Fact]
    public void RecordLatency_WithSymbol_ShouldTrackSymbolCount()
    {
        // Act
        _service.RecordLatency("Alpaca", 10.0, "AAPL");
        _service.RecordLatency("Alpaca", 15.0, "MSFT");
        _service.RecordLatency("Alpaca", 20.0, "AAPL");
        _service.RecordLatency("Alpaca", 25.0, "GOOG");

        // Assert
        var histogram = _service.GetHistogram("Alpaca");
        histogram.Should().NotBeNull();
        histogram!.SymbolCount.Should().Be(3);
    }

    [Fact]
    public void Reset_ShouldClearProviderData()
    {
        // Arrange
        _service.RecordLatency("TestProvider", 10.0);

        // Act
        _service.Reset("TestProvider");

        // Assert
        var histogram = _service.GetHistogram("TestProvider");
        histogram.Should().BeNull();
    }

    [Fact]
    public void ResetAll_ShouldClearAllData()
    {
        // Arrange
        _service.RecordLatency("Provider1", 10.0);
        _service.RecordLatency("Provider2", 20.0);

        // Act
        _service.ResetAll();

        // Assert
        _service.GetAllHistograms().Should().BeEmpty();
    }

    [Fact]
    public void RecordLatency_NegativeValue_ShouldBeIgnored()
    {
        // Act
        _service.RecordLatency("TestProvider", -5.0);

        // Assert
        var histogram = _service.GetHistogram("TestProvider");
        histogram.Should().BeNull();
    }

    [Fact]
    public void RecordLatency_FromTimestamps_ShouldCalculateCorrectly()
    {
        // Arrange
        var eventTime = DateTimeOffset.UtcNow.AddMilliseconds(-50);
        var receiveTime = DateTimeOffset.UtcNow;

        // Act
        _service.RecordLatency("TestProvider", eventTime, receiveTime);

        // Assert
        var histogram = _service.GetHistogram("TestProvider");
        histogram.Should().NotBeNull();
        histogram!.MeanMs.Should().BeApproximately(50.0, 5.0);
    }

    [Fact]
    public void ToJson_ShouldSerializeCorrectly()
    {
        // Arrange
        _service.RecordLatency("Alpaca", 10.0);

        // Act
        var json = _service.ToJson();

        // Assert
        json.Should().Contain("alpaca", "JSON should use camelCase");
        json.Should().Contain("fastestProvider");
        json.Should().Contain("globalP50Ms");
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
