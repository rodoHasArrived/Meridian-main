using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Xunit;

namespace Meridian.Tests.Monitoring.DataQuality;

public sealed class CompletenessScoreCalculatorTests : IDisposable
{
    private readonly CompletenessScoreCalculator _calculator;

    public CompletenessScoreCalculatorTests()
    {
        _calculator = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 100,
            TradingStartHour = 9,
            TradingStartMinute = 30,
            TradingEndHour = 16,
            TradingEndMinute = 0
        });
    }

    [Fact]
    public void RecordEvent_ShouldTrackEvents()
    {
        // Arrange
        var symbol = "AAPL";
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);

        // Act
        for (int i = 0; i < 100; i++)
        {
            _calculator.RecordEvent(symbol, timestamp.AddMinutes(i), "Trade");
        }

        var score = _calculator.GetScore(symbol, DateOnly.FromDateTime(timestamp.DateTime));

        // Assert
        score.Should().NotBeNull();
        score!.Symbol.Should().Be(symbol);
        score.ActualEvents.Should().Be(100);
    }

    [Fact]
    public void GetScore_WithNoData_ShouldReturnNull()
    {
        // Act
        var score = _calculator.GetScore("UNKNOWN", DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        score.Should().BeNull();
    }

    [Fact]
    public void GetAllScores_ShouldReturnAllTrackedScores()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        _calculator.RecordEvent("AAPL", timestamp, "Trade");
        _calculator.RecordEvent("MSFT", timestamp, "Trade");

        // Act
        var scores = _calculator.GetAllScores();

        // Assert
        scores.Should().HaveCount(2);
        scores.Select(s => s.Symbol).Should().Contain("AAPL");
        scores.Select(s => s.Symbol).Should().Contain("MSFT");
    }

    [Fact]
    public void GetSummary_ShouldReturnCorrectStatistics()
    {
        // Arrange
        var timestamp = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 50; i++)
        {
            _calculator.RecordEvent("AAPL", timestamp.AddMinutes(i), "Trade");
        }

        // Act
        var summary = _calculator.GetSummary();

        // Assert
        summary.SymbolsTracked.Should().Be(1);
        summary.TotalEvents.Should().Be(50);
    }

    public void Dispose()
    {
        _calculator.Dispose();
    }
}

public sealed class GapAnalyzerTests : IDisposable
{
    private readonly GapAnalyzer _analyzer;

    public GapAnalyzerTests()
    {
        _analyzer = new GapAnalyzer(new GapAnalyzerConfig
        {
            GapThresholdSeconds = 60,
            TradingStartHour = 9,
            TradingEndHour = 16
        });
    }

    [Fact]
    public void RecordEvent_WithNoGap_ShouldNotDetectGap()
    {
        // Arrange
        var timestamp1 = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timestamp2 = timestamp1.AddSeconds(30);

        // Act
        _analyzer.RecordEvent("AAPL", "Trade", timestamp1, 1);
        _analyzer.RecordEvent("AAPL", "Trade", timestamp2, 2);

        var gaps = _analyzer.GetGapsForSymbolDate("AAPL", DateOnly.FromDateTime(timestamp1.DateTime));

        // Assert
        gaps.Should().BeEmpty();
    }

    [Fact]
    public void RecordEvent_WithGap_ShouldDetectGap()
    {
        // Arrange
        var timestamp1 = new DateTimeOffset(2024, 1, 15, 10, 0, 0, TimeSpan.Zero);
        var timestamp2 = timestamp1.AddMinutes(5); // 5-minute gap

        // Act
        _analyzer.RecordEvent("AAPL", "Trade", timestamp1, 1);
        _analyzer.RecordEvent("AAPL", "Trade", timestamp2, 2);

        var gaps = _analyzer.GetGapsForSymbolDate("AAPL", DateOnly.FromDateTime(timestamp1.DateTime));

        // Assert
        gaps.Should().HaveCount(1);
        gaps[0].Duration.Should().BeCloseTo(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AnalyzeGaps_ShouldReturnCompleteAnalysis()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var timestamp = new DateTimeOffset(date.Year, date.Month, date.Day, 10, 0, 0, TimeSpan.Zero);
        _analyzer.RecordEvent("AAPL", "Trade", timestamp, 1);
        _analyzer.RecordEvent("AAPL", "Trade", timestamp.AddMinutes(5), 2);

        // Act
        var analysis = _analyzer.AnalyzeGaps("AAPL", date);

        // Assert
        analysis.Should().NotBeNull();
        analysis.Symbol.Should().Be("AAPL");
        analysis.TotalGaps.Should().BeGreaterThanOrEqualTo(0);
        analysis.Timeline.Should().NotBeEmpty();
    }

    [Fact]
    public void GenerateTimeline_ShouldIncludeMarketSessions()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // Act
        var timeline = _analyzer.GenerateTimeline("AAPL", date);

        // Assert
        timeline.Should().NotBeEmpty();
    }

    public void Dispose()
    {
        _analyzer.Dispose();
    }
}

public sealed class SequenceErrorTrackerTests : IDisposable
{
    private readonly SequenceErrorTracker _tracker;

    public SequenceErrorTrackerTests()
    {
        _tracker = new SequenceErrorTracker(new SequenceErrorConfig
        {
            GapThreshold = 1,
            SignificantGapSize = 10,
            ResetThreshold = 1000
        });
    }

    [Fact]
    public void CheckSequence_WithNormalSequence_ShouldNotDetectError()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        var error1 = _tracker.CheckSequence("AAPL", "Trade", 1, timestamp);
        var error2 = _tracker.CheckSequence("AAPL", "Trade", 2, timestamp.AddSeconds(1));
        var error3 = _tracker.CheckSequence("AAPL", "Trade", 3, timestamp.AddSeconds(2));

        // Assert
        error1.Should().BeNull(); // First event
        error2.Should().BeNull();
        error3.Should().BeNull();
    }

    [Fact]
    public void CheckSequence_WithGap_ShouldDetectGapError()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        _tracker.CheckSequence("AAPL", "Trade", 1, timestamp);
        var error = _tracker.CheckSequence("AAPL", "Trade", 5, timestamp.AddSeconds(1));

        // Assert
        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.Gap);
        error.GapSize.Should().Be(3); // Missing 2, 3, 4
    }

    [Fact]
    public void CheckSequence_WithOutOfOrder_ShouldDetectError()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        _tracker.CheckSequence("AAPL", "Trade", 1, timestamp);
        _tracker.CheckSequence("AAPL", "Trade", 5, timestamp.AddSeconds(1));
        var error = _tracker.CheckSequence("AAPL", "Trade", 3, timestamp.AddSeconds(2));

        // Assert
        error.Should().NotBeNull();
        error!.ErrorType.Should().Be(SequenceErrorType.OutOfOrder);
    }

    [Fact]
    public void GetStatistics_ShouldReturnCorrectCounts()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        _tracker.CheckSequence("AAPL", "Trade", 1, timestamp);
        _tracker.CheckSequence("AAPL", "Trade", 10, timestamp.AddSeconds(1)); // Gap

        // Act
        var stats = _tracker.GetStatistics();

        // Assert
        stats.TotalEventsChecked.Should().Be(2);
        stats.TotalErrors.Should().BeGreaterThanOrEqualTo(1);
    }

    public void Dispose()
    {
        _tracker.Dispose();
    }
}

public sealed class AnomalyDetectorTests : IDisposable
{
    private readonly AnomalyDetector _detector;

    public AnomalyDetectorTests()
    {
        _detector = new AnomalyDetector(new AnomalyDetectionConfig
        {
            PriceSpikeThresholdPercent = 5.0,
            VolumeSpikeThresholdMultiplier = 5.0,
            MinSamplesForStatistics = 10,
            EnablePriceAnomalies = true,
            EnableVolumeAnomalies = true,
            AlertCooldownSeconds = 0 // Disable cooldown for tests
        });
    }

    [Fact]
    public void ProcessTrade_WithNormalPrice_ShouldNotDetectAnomaly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Build up history with stable prices
        for (int i = 0; i < 20; i++)
        {
            _detector.ProcessTrade("AAPL", timestamp.AddSeconds(i), 150.00m + (i * 0.01m), 100, "Provider1");
        }

        // Act - Normal price within range
        var anomaly = _detector.ProcessTrade("AAPL", timestamp.AddSeconds(21), 150.25m, 100, "Provider1");

        // Assert
        anomaly.Should().BeNull();
    }

    [Fact]
    public void ProcessTrade_WithPriceSpike_ShouldDetectAnomaly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Build up history with stable prices around 150
        for (int i = 0; i < 20; i++)
        {
            _detector.ProcessTrade("AAPL", timestamp.AddSeconds(i), 150.00m, 100, "Provider1");
        }

        // Act - Significant price spike
        var anomaly = _detector.ProcessTrade("AAPL", timestamp.AddSeconds(21), 200.00m, 100, "Provider1");

        // Assert
        anomaly.Should().NotBeNull();
        anomaly!.Type.Should().Be(AnomalyType.PriceSpike);
    }

    [Fact]
    public void ProcessQuote_WithCrossedMarket_ShouldDetectAnomaly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act - Bid > Ask (crossed market)
        var anomaly = _detector.ProcessQuote("AAPL", timestamp, 151.00m, 150.00m, "Provider1");

        // Assert
        anomaly.Should().NotBeNull();
        anomaly!.Type.Should().Be(AnomalyType.CrossedMarket);
    }

    [Fact]
    public void GetStatistics_ShouldReturnCorrectCounts()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Build up history
        for (int i = 0; i < 20; i++)
        {
            _detector.ProcessTrade("AAPL", timestamp.AddSeconds(i), 150.00m, 100, "Provider1");
        }

        // Trigger an anomaly
        _detector.ProcessTrade("AAPL", timestamp.AddSeconds(21), 200.00m, 100, "Provider1");

        // Act
        var stats = _detector.GetStatistics();

        // Assert
        stats.TotalAnomalies.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void AcknowledgeAnomaly_ShouldMarkAsAcknowledged()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        _detector.ProcessQuote("AAPL", timestamp, 151.00m, 150.00m, "Provider1"); // Crossed market

        var anomalies = _detector.GetAnomalies("AAPL", 1);
        anomalies.Should().HaveCount(1);
        var anomalyId = anomalies[0].Id;

        // Act
        var result = _detector.AcknowledgeAnomaly(anomalyId);

        // Assert
        result.Should().BeTrue();
        var acknowledged = _detector.GetAnomalies("AAPL", 1);
        acknowledged[0].IsAcknowledged.Should().BeTrue();
    }

    [Fact]
    public void ProcessTrade_WithVolumeSpike_ShouldDetectAnomaly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Build up history with stable volume around 100
        for (int i = 0; i < 20; i++)
        {
            _detector.ProcessTrade("MSFT", timestamp.AddSeconds(i), 300.00m, 100, "Provider1");
        }

        // Act - Significant volume spike (10x average with threshold of 5x)
        var anomaly = _detector.ProcessTrade("MSFT", timestamp.AddSeconds(21), 300.00m, 1000, "Provider1");

        // Assert
        anomaly.Should().NotBeNull();
        anomaly!.Type.Should().Be(AnomalyType.VolumeSpike);
        anomaly.Symbol.Should().Be("MSFT");
        anomaly.Severity.Should().BeOneOf(AnomalySeverity.Warning, AnomalySeverity.Error);
    }

    [Fact]
    public void ProcessTrade_WithVolumeDrop_ShouldDetectAnomaly()
    {
        // Arrange - Create detector with specific volume drop threshold
        using var detector = new AnomalyDetector(new AnomalyDetectionConfig
        {
            PriceSpikeThresholdPercent = 5.0,
            VolumeSpikeThresholdMultiplier = 5.0,
            VolumeDropThresholdMultiplier = 0.2, // Detect when volume < 20% of average
            MinSamplesForStatistics = 10,
            EnablePriceAnomalies = true,
            EnableVolumeAnomalies = true,
            AlertCooldownSeconds = 0 // Disable cooldown for tests
        });

        var timestamp = DateTimeOffset.UtcNow;

        // Build up history with stable volume around 1000
        for (int i = 0; i < 20; i++)
        {
            detector.ProcessTrade("GOOGL", timestamp.AddSeconds(i), 150.00m, 1000, "Provider1");
        }

        // Act - Significant volume drop (10 is only 1% of average 1000, well below 20% threshold)
        var anomaly = detector.ProcessTrade("GOOGL", timestamp.AddSeconds(21), 150.00m, 10, "Provider1");

        // Assert
        anomaly.Should().NotBeNull();
        anomaly!.Type.Should().Be(AnomalyType.VolumeDrop);
        anomaly.Symbol.Should().Be("GOOGL");
        anomaly.Description.Should().Contain("Volume drop");
    }

    [Fact]
    public void ProcessTrade_WithNormalVolume_ShouldNotDetectVolumeAnomaly()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Build up history with volume around 100
        for (int i = 0; i < 20; i++)
        {
            _detector.ProcessTrade("TSLA", timestamp.AddSeconds(i), 250.00m, 100, "Provider1");
        }

        // Act - Normal volume (120 is within normal range, not spike or drop)
        var anomaly = _detector.ProcessTrade("TSLA", timestamp.AddSeconds(21), 250.00m, 120, "Provider1");

        // Assert - No volume anomaly expected (though price anomaly might occur if price changed significantly)
        if (anomaly != null)
        {
            anomaly.Type.Should().NotBe(AnomalyType.VolumeSpike);
            anomaly.Type.Should().NotBe(AnomalyType.VolumeDrop);
        }
    }

    public void Dispose()
    {
        _detector.Dispose();
    }
}

public sealed class LatencyHistogramTests : IDisposable
{
    private readonly LatencyHistogram _histogram;

    public LatencyHistogramTests()
    {
        _histogram = new LatencyHistogram();
    }

    [Fact]
    public void RecordLatency_ShouldTrackSamples()
    {
        // Arrange & Act
        for (int i = 0; i < 100; i++)
        {
            _histogram.RecordLatency("AAPL", i * 0.1, "Provider1");
        }

        var distribution = _histogram.GetDistribution("AAPL", "Provider1");

        // Assert
        distribution.Should().NotBeNull();
        distribution!.SampleCount.Should().Be(100);
        distribution.MinLatencyMs.Should().BeGreaterThanOrEqualTo(0);
        distribution.MaxLatencyMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GetPercentile_ShouldReturnCorrectValues()
    {
        // Arrange
        for (int i = 1; i <= 100; i++)
        {
            _histogram.RecordLatency("AAPL", i, "Provider1");
        }

        // Act
        var p50 = _histogram.GetPercentile("AAPL", 50, "Provider1");
        var p99 = _histogram.GetPercentile("AAPL", 99, "Provider1");

        // Assert
        p50.Should().BeApproximately(50, 5); // ~50ms median
        p99.Should().BeApproximately(99, 2); // ~99ms p99
    }

    [Fact]
    public void GetBuckets_ShouldReturnDistribution()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            _histogram.RecordLatency("AAPL", i, "Provider1");
        }

        // Act
        var buckets = _histogram.GetBuckets("AAPL", "Provider1");

        // Assert
        buckets.Should().NotBeEmpty();
        buckets.Sum(b => b.Percentage).Should().BeApproximately(100, 1);
    }

    [Fact]
    public void GetHighLatencySymbols_ShouldIdentifySlowSymbols()
    {
        // Arrange
        for (int i = 0; i < 100; i++)
        {
            _histogram.RecordLatency("AAPL", 10, "Provider1"); // Fast
            _histogram.RecordLatency("SLOW", 500, "Provider1"); // Slow
        }

        // Act
        var highLatency = _histogram.GetHighLatencySymbols(100);

        // Assert
        highLatency.Should().Contain(x => x.Symbol == "SLOW");
        highLatency.Should().NotContain(x => x.Symbol == "AAPL");
    }

    public void Dispose()
    {
        _histogram.Dispose();
    }
}

public sealed class CrossProviderComparisonServiceTests : IDisposable
{
    private readonly CrossProviderComparisonService _service;

    public CrossProviderComparisonServiceTests()
    {
        _service = new CrossProviderComparisonService(new CrossProviderConfig
        {
            PriceDiscrepancyThresholdPercent = 0.5,
            ComparisonWindowSeconds = 60
        });
    }

    [Fact]
    public void RecordTrade_FromMultipleProviders_ShouldTrack()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        _service.RecordTrade("AAPL", "Provider1", timestamp, 150.00m, 100, 1);
        _service.RecordTrade("AAPL", "Provider2", timestamp, 150.00m, 100, 1);

        var providers = _service.GetProvidersForSymbol("AAPL");

        // Assert
        providers.Should().HaveCount(2);
        providers.Should().Contain("Provider1");
        providers.Should().Contain("Provider2");
    }

    [Fact]
    public void Compare_ShouldRecommendBestProvider()
    {
        // Arrange
        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var timestamp = new DateTimeOffset(date.Year, date.Month, date.Day, 10, 0, 0, TimeSpan.Zero);

        // Provider1 has more data
        for (int i = 0; i < 100; i++)
        {
            _service.RecordTrade("AAPL", "Provider1", timestamp.AddSeconds(i), 150.00m, 100, i);
        }

        // Provider2 has less data
        for (int i = 0; i < 50; i++)
        {
            _service.RecordTrade("AAPL", "Provider2", timestamp.AddSeconds(i * 2), 150.00m, 100, i);
        }

        // Act
        var comparison = _service.Compare("AAPL", date);

        // Assert
        comparison.Should().NotBeNull();
        comparison.RecommendedProvider.Should().Be("Provider1");
    }

    [Fact]
    public void GetStatistics_ShouldReturnOverview()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        _service.RecordTrade("AAPL", "Provider1", timestamp, 150.00m, 100, 1);
        _service.RecordTrade("MSFT", "Provider2", timestamp, 300.00m, 50, 1);

        // Act
        var stats = _service.GetStatistics();

        // Assert
        stats.SymbolsTracked.Should().Be(2);
        stats.ProvidersActive.Should().Be(2);
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}

public sealed class DataQualityMonitoringServiceTests : IAsyncLifetime
{
    private DataQualityMonitoringService? _service;

    public Task InitializeAsync()
    {
        _service = new DataQualityMonitoringService(new DataQualityMonitoringConfig
        {
            CompletenessConfig = new CompletenessConfig { ExpectedEventsPerHour = 100 },
            AnomalyConfig = new AnomalyDetectionConfig
            {
                MinSamplesForStatistics = 5,
                EnableStaleDataDetection = false
            }
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_service != null)
        {
            await _service.DisposeAsync();
        }
    }

    [Fact]
    public void ProcessTrade_ShouldUpdateAllComponents()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;

        // Act
        _service!.ProcessTrade("AAPL", timestamp, 150.00m, 100, 1, "Provider1", 5.0);

        // Assert
        var health = _service.GetSymbolHealth("AAPL");
        health.Should().NotBeNull();
        health!.State.Should().Be(HealthState.Healthy);
    }

    [Fact]
    public void GetRealTimeMetrics_ShouldReturnSnapshot()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        _service!.ProcessTrade("AAPL", timestamp, 150.00m, 100, 1, "Provider1", 5.0);

        // Act
        var metrics = _service.GetRealTimeMetrics();

        // Assert
        metrics.Should().NotBeNull();
        metrics.ActiveSymbols.Should().BeGreaterThanOrEqualTo(1);
        metrics.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void GetDashboard_ShouldReturnCompleteDashboard()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        _service!.ProcessTrade("AAPL", timestamp, 150.00m, 100, 1, "Provider1", 5.0);

        // Act
        var dashboard = _service.GetDashboard();

        // Assert
        dashboard.Should().NotBeNull();
        dashboard.RealTimeMetrics.Should().NotBeNull();
        dashboard.CompletenessStats.Should().NotBeNull();
        dashboard.GapStats.Should().NotBeNull();
        dashboard.AnomalyStats.Should().NotBeNull();
        dashboard.LatencyStats.Should().NotBeNull();
    }

    [Fact]
    public async Task GenerateDailyReportAsync_ShouldGenerateReport()
    {
        // Arrange
        var timestamp = DateTimeOffset.UtcNow;
        for (int i = 0; i < 10; i++)
        {
            _service!.ProcessTrade("AAPL", timestamp.AddMinutes(i), 150.00m, 100, i, "Provider1", 5.0);
        }

        // Act
        var report = await _service!.GenerateDailyReportAsync(DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        report.Should().NotBeNull();
        report.Date.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        report.SymbolsAnalyzed.Should().BeGreaterThanOrEqualTo(0);
    }
}
