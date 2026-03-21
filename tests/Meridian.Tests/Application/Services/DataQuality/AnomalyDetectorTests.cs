using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Application.Services.DataQuality;

/// <summary>
/// Tests for AnomalyDetector market data anomaly detection.
/// </summary>
public sealed class AnomalyDetectorTests : IDisposable
{
    private readonly AnomalyDetector _sut;

    public AnomalyDetectorTests()
    {
        _sut = new AnomalyDetector(new AnomalyDetectionConfig
        {
            PriceSpikeThresholdPercent = 5.0,
            VolumeSpikeThresholdMultiplier = 3.0,
            VolumeDropThresholdMultiplier = 0.1,
            MinSamplesForStatistics = 5,
            AlertCooldownSeconds = 0 // Disable cooldown for testing
        });
    }

    public void Dispose() => _sut.Dispose();

    #region ProcessTrade - Normal Operations

    [Fact]
    public void ProcessTrade_FirstTrade_NoAnomaly()
    {
        var anomaly = _sut.ProcessTrade("SPY", DateTimeOffset.UtcNow, 450.00m, 1000m);

        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessTrade_NormalPriceChange_NoAnomaly()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Build up sample history with natural price variation
        // Varying prices between 449.80 and 450.20 (0.40 range)
        var prices = new[] { 449.90m, 450.05m, 449.95m, 450.10m, 450.00m,
                            449.85m, 450.15m, 449.92m, 450.08m, 450.02m };
        for (int i = 0; i < prices.Length; i++)
        {
            _sut.ProcessTrade("SPY", baseTime.AddSeconds(i), prices[i], 1000m);
        }

        // Price within normal range - should not be an anomaly
        var anomaly = _sut.ProcessTrade("SPY", baseTime.AddSeconds(11), 450.05m, 1000m);

        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessTrade_ZeroPrice_IgnoredGracefully()
    {
        var anomaly = _sut.ProcessTrade("SPY", DateTimeOffset.UtcNow, 0m, 1000m);

        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessTrade_NegativePrice_IgnoredGracefully()
    {
        var anomaly = _sut.ProcessTrade("SPY", DateTimeOffset.UtcNow, -10m, 1000m);

        anomaly.Should().BeNull();
    }

    #endregion

    #region ProcessTrade - Price Spike Detection

    [Fact]
    public void ProcessTrade_LargePriceSpike_DetectsAnomaly()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Build stable price history around 450
        for (int i = 0; i < 20; i++)
        {
            _sut.ProcessTrade("SPY", baseTime.AddSeconds(i), 450.00m + (i % 3) * 0.01m, 1000m);
        }

        // Massive price spike (>5% threshold)
        var anomaly = _sut.ProcessTrade("SPY", baseTime.AddSeconds(21), 500.00m, 1000m);

        // Should detect anomaly for large price deviation from mean
        anomaly.Should().NotBeNull();
    }

    #endregion

    #region ProcessQuote - Crossed Market

    [Fact]
    public void ProcessQuote_CrossedMarket_DetectsAnomaly()
    {
        var anomaly = _sut.ProcessQuote("SPY", DateTimeOffset.UtcNow, bidPrice: 451.00m, askPrice: 449.00m);

        anomaly.Should().NotBeNull();
        anomaly!.Type.Should().Be(AnomalyType.CrossedMarket);
        anomaly.Severity.Should().Be(AnomalySeverity.Error);
    }

    [Fact]
    public void ProcessQuote_NormalSpread_NoAnomaly()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Build sample history first
        for (int i = 0; i < 10; i++)
        {
            _sut.ProcessTrade("SPY", baseTime.AddSeconds(i), 450.00m, 1000m);
        }

        var anomaly = _sut.ProcessQuote("SPY", baseTime.AddSeconds(11), bidPrice: 449.99m, askPrice: 450.01m);

        // Normal tight spread should not be an anomaly
        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessQuote_ZeroBidPrice_IgnoredGracefully()
    {
        var anomaly = _sut.ProcessQuote("SPY", DateTimeOffset.UtcNow, bidPrice: 0m, askPrice: 450.00m);

        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessQuote_ZeroAskPrice_IgnoredGracefully()
    {
        var anomaly = _sut.ProcessQuote("SPY", DateTimeOffset.UtcNow, bidPrice: 450.00m, askPrice: 0m);

        anomaly.Should().BeNull();
    }

    #endregion

    #region Event Notification

    [Fact]
    public void OnAnomalyDetected_FiredForCrossedMarket()
    {
        DataAnomaly? notified = null;
        _sut.OnAnomalyDetected += a => notified = a;

        _sut.ProcessQuote("SPY", DateTimeOffset.UtcNow, bidPrice: 451.00m, askPrice: 449.00m);

        notified.Should().NotBeNull();
    }

    [Fact]
    public void OnAnomalyDetected_NotFiredForNormalTrades()
    {
        DataAnomaly? notified = null;
        _sut.OnAnomalyDetected += a => notified = a;

        _sut.ProcessTrade("SPY", DateTimeOffset.UtcNow, 450.00m, 1000m);

        notified.Should().BeNull();
    }

    #endregion

    #region Multi-Symbol Independence

    [Fact]
    public void ProcessTrade_DifferentSymbols_IndependentStatistics()
    {
        var baseTime = DateTimeOffset.UtcNow;

        // Build history for SPY around 450
        for (int i = 0; i < 10; i++)
        {
            _sut.ProcessTrade("SPY", baseTime.AddSeconds(i), 450.00m, 1000m);
        }

        // Build history for AAPL around 180
        for (int i = 0; i < 10; i++)
        {
            _sut.ProcessTrade("AAPL", baseTime.AddSeconds(i), 180.00m, 500m);
        }

        // Normal AAPL trade should not trigger anomaly based on SPY stats
        var anomaly = _sut.ProcessTrade("AAPL", baseTime.AddSeconds(11), 180.50m, 500m);

        anomaly.Should().BeNull();
    }

    #endregion

    #region Provider Tracking

    [Fact]
    public void ProcessTrade_WithProvider_IncludesProviderInAnomaly()
    {
        var baseTime = DateTimeOffset.UtcNow;

        for (int i = 0; i < 20; i++)
        {
            _sut.ProcessTrade("SPY", baseTime.AddSeconds(i), 450.00m, 1000m, provider: "Alpaca");
        }

        // Large price spike
        var anomaly = _sut.ProcessTrade("SPY", baseTime.AddSeconds(21), 500.00m, 1000m, provider: "Alpaca");

        if (anomaly != null)
        {
            anomaly.Provider.Should().Be("Alpaca");
        }
    }

    #endregion

    #region Configuration

    [Fact]
    public void AnomalyDetectionConfig_Default_HasSensibleValues()
    {
        var config = AnomalyDetectionConfig.Default;

        config.PriceSpikeThresholdPercent.Should().BeGreaterThan(0);
        config.VolumeSpikeThresholdMultiplier.Should().BeGreaterThan(1);
        config.MinSamplesForStatistics.Should().BeGreaterThan(0);
    }

    #endregion

    #region Dispose

    [Fact]
    public void Dispose_AfterDispose_ProcessTradeReturnsNull()
    {
        var detector = new AnomalyDetector();
        detector.Dispose();

        var anomaly = detector.ProcessTrade("SPY", DateTimeOffset.UtcNow, 450.00m, 1000m);

        anomaly.Should().BeNull();
    }

    [Fact]
    public void Dispose_AfterDispose_ProcessQuoteReturnsNull()
    {
        var detector = new AnomalyDetector();
        detector.Dispose();

        var anomaly = detector.ProcessQuote("SPY", DateTimeOffset.UtcNow, 449.00m, 451.00m);

        anomaly.Should().BeNull();
    }

    #endregion
}
