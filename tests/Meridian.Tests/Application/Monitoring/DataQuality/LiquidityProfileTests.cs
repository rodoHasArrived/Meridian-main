using FluentAssertions;
using Meridian.Application.Monitoring.DataQuality;
using Meridian.Contracts.Domain.Enums;
using Xunit;

namespace Meridian.Tests.Application.Monitoring.DataQuality;

/// <summary>
/// Tests for liquidity-aware monitoring thresholds across GapAnalyzer,
/// CompletenessScoreCalculator, AnomalyDetector, and DataFreshnessSlaMonitor.
/// </summary>
public sealed class LiquidityProfileTests
{
    #region LiquidityProfileProvider

    [Theory]
    [InlineData(LiquidityProfile.High, 60)]
    [InlineData(LiquidityProfile.Normal, 120)]
    [InlineData(LiquidityProfile.Low, 600)]
    [InlineData(LiquidityProfile.VeryLow, 1800)]
    [InlineData(LiquidityProfile.Minimal, 3600)]
    public void GetThresholds_ReturnsExpectedGapThreshold(LiquidityProfile profile, int expectedGapSeconds)
    {
        var thresholds = LiquidityProfileProvider.GetThresholds(profile);

        thresholds.GapThresholdSeconds.Should().Be(expectedGapSeconds);
    }

    [Theory]
    [InlineData(LiquidityProfile.High, 1000)]
    [InlineData(LiquidityProfile.Normal, 200)]
    [InlineData(LiquidityProfile.Low, 20)]
    [InlineData(LiquidityProfile.VeryLow, 5)]
    [InlineData(LiquidityProfile.Minimal, 1)]
    public void GetThresholds_ReturnsExpectedEventsPerHour(LiquidityProfile profile, long expectedEvents)
    {
        var thresholds = LiquidityProfileProvider.GetThresholds(profile);

        thresholds.ExpectedEventsPerHour.Should().Be(expectedEvents);
    }

    [Fact]
    public void Resolve_NullProfile_DefaultsToHigh()
    {
        var result = LiquidityProfileProvider.Resolve(null);

        result.Should().Be(LiquidityProfile.High);
    }

    [Theory]
    [InlineData(LiquidityProfile.Low)]
    [InlineData(LiquidityProfile.VeryLow)]
    public void Resolve_ExplicitProfile_ReturnsSameProfile(LiquidityProfile profile)
    {
        var result = LiquidityProfileProvider.Resolve(profile);

        result.Should().Be(profile);
    }

    [Fact]
    public void ClassifyGapSeverity_IlliquidSymbol_5MinuteGapIsMinor()
    {
        // For a VeryLow liquidity symbol (threshold=1800s), a 5-minute gap should be Minor
        var severity = LiquidityProfileProvider.ClassifyGapSeverity(
            TimeSpan.FromMinutes(5), LiquidityProfile.VeryLow);

        severity.Should().Be(GapSeverity.Minor);
    }

    [Fact]
    public void ClassifyGapSeverity_LiquidSymbol_5MinuteGapIsSignificant()
    {
        // For a High liquidity symbol (threshold=60s), a 5-minute gap should be Significant
        var severity = LiquidityProfileProvider.ClassifyGapSeverity(
            TimeSpan.FromMinutes(5), LiquidityProfile.High);

        severity.Should().Be(GapSeverity.Significant);
    }

    [Fact]
    public void InferGapCause_IlliquidSymbol_ShortGapIsNormalQuietPeriod()
    {
        var now = new DateTimeOffset(2026, 2, 23, 16, 0, 0, TimeSpan.Zero); // During trading hours
        var cause = LiquidityProfileProvider.InferGapCause(
            TimeSpan.FromMinutes(15), now, now.AddMinutes(15), LiquidityProfile.Low);

        cause.Should().Be("Normal quiet period for illiquid instrument");
    }

    [Fact]
    public void InferGapCause_LiquidSymbol_ShortGapIsBriefDelay()
    {
        var now = new DateTimeOffset(2026, 2, 23, 16, 0, 0, TimeSpan.Zero);
        var cause = LiquidityProfileProvider.InferGapCause(
            TimeSpan.FromSeconds(90), now, now.AddSeconds(90), LiquidityProfile.High);

        cause.Should().Be("Brief data delay");
    }

    #endregion

    #region GapAnalyzer with Liquidity

    [Fact]
    public void GapAnalyzer_IlliquidSymbol_NoGapFor5MinutePause()
    {
        using var analyzer = new GapAnalyzer(GapAnalyzerConfig.Default);
        analyzer.RegisterSymbolLiquidity("OTCPK:XYZ", LiquidityProfile.VeryLow);

        DataGap? detected = null;
        analyzer.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        analyzer.RecordEvent("OTCPK:XYZ", "trade", baseTime);
        // 5-minute pause -- normal for VeryLow liquidity (threshold = 1800s)
        analyzer.RecordEvent("OTCPK:XYZ", "trade", baseTime.AddMinutes(5));

        detected.Should().BeNull("a 5-minute gap should not be flagged for a VeryLow liquidity symbol");
    }

    [Fact]
    public void GapAnalyzer_LiquidSymbol_GapDetectedFor2MinutePause()
    {
        using var analyzer = new GapAnalyzer(GapAnalyzerConfig.Default);
        // No liquidity registration => defaults to High (60s threshold)

        DataGap? detected = null;
        analyzer.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        analyzer.RecordEvent("SPY", "trade", baseTime);
        analyzer.RecordEvent("SPY", "trade", baseTime.AddMinutes(2));

        detected.Should().NotBeNull("a 2-minute gap exceeds the 60s threshold for High liquidity");
    }

    [Fact]
    public void GapAnalyzer_IlliquidSymbol_GapDetectedWhenExceedingThreshold()
    {
        using var analyzer = new GapAnalyzer(GapAnalyzerConfig.Default);
        analyzer.RegisterSymbolLiquidity("THINLY", LiquidityProfile.Low);

        DataGap? detected = null;
        analyzer.OnGapDetected += g => detected = g;

        var baseTime = DateTimeOffset.UtcNow;
        analyzer.RecordEvent("THINLY", "trade", baseTime);
        // 15-minute pause exceeds Low threshold (600s = 10 min)
        analyzer.RecordEvent("THINLY", "trade", baseTime.AddMinutes(15));

        detected.Should().NotBeNull("a 15-minute gap exceeds the 600s threshold for Low liquidity");
        detected!.Severity.Should().Be(GapSeverity.Moderate, "a 900s gap exceeds the 600s base threshold but is less than 5x (3000s)");
    }

    [Fact]
    public void GapAnalyzer_GetSymbolLiquidity_DefaultsToHigh()
    {
        using var analyzer = new GapAnalyzer(GapAnalyzerConfig.Default);

        analyzer.GetSymbolLiquidity("UNKNOWN").Should().Be(LiquidityProfile.High);
    }

    [Fact]
    public void GapAnalyzer_GetSymbolLiquidity_ReturnsRegistered()
    {
        using var analyzer = new GapAnalyzer(GapAnalyzerConfig.Default);
        analyzer.RegisterSymbolLiquidity("OTC", LiquidityProfile.Minimal);

        analyzer.GetSymbolLiquidity("OTC").Should().Be(LiquidityProfile.Minimal);
    }

    #endregion

    #region CompletenessScoreCalculator with Liquidity

    [Fact]
    public void Completeness_IlliquidSymbol_FewEventsStillGradeWell()
    {
        using var calculator = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 1000, // Default for High liquidity
            TradingStartHour = 13,
            TradingStartMinute = 30,
            TradingEndHour = 20,
            TradingEndMinute = 0
        });

        // Register as VeryLow liquidity => 5 expected events/hour
        calculator.RegisterSymbolLiquidity("ILLIQUID", LiquidityProfile.VeryLow);

        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // Record 30 events spread over trading hours (5 events/hour * 6.5 hours = 32.5 expected)
        var tradingStart = new DateTimeOffset(date.Year, date.Month, date.Day, 13, 30, 0, TimeSpan.Zero);
        for (int i = 0; i < 30; i++)
        {
            calculator.RecordEvent("ILLIQUID", tradingStart.AddMinutes(i * 13), "Trade");
        }

        var score = calculator.GetScore("ILLIQUID", date);
        score.Should().NotBeNull();
        // With 30 events against ~32.5 expected, event score should be high
        score!.Score.Should().BeGreaterThan(0.5, "30 events out of ~32 expected should give a good score");
    }

    [Fact]
    public void Completeness_LiquidSymbol_FewEventsGradesPoorly()
    {
        using var calculator = new CompletenessScoreCalculator(new CompletenessConfig
        {
            ExpectedEventsPerHour = 1000
        });
        // No liquidity registration => uses default 1000 events/hour

        var date = DateOnly.FromDateTime(DateTime.UtcNow);
        var tradingStart = new DateTimeOffset(date.Year, date.Month, date.Day, 13, 30, 0, TimeSpan.Zero);

        // Only 30 events (vs ~6500 expected for High liquidity)
        for (int i = 0; i < 30; i++)
        {
            calculator.RecordEvent("SPY", tradingStart.AddMinutes(i * 13), "Trade");
        }

        var score = calculator.GetScore("SPY", date);
        score.Should().NotBeNull();
        score!.Score.Should().BeLessThan(0.5, "30 events out of ~6500 expected should score poorly");
    }

    #endregion

    #region AnomalyDetector with Liquidity

    [Fact]
    public void AnomalyDetector_RegisterSymbolLiquidity_PreventsStaleAlerts()
    {
        using var detector = new AnomalyDetector(new AnomalyDetectionConfig
        {
            StaleDataThresholdSeconds = 60, // Default for High liquidity
            EnableStaleDataDetection = true,
            AlertCooldownSeconds = 0
        });

        // Register as VeryLow => stale threshold becomes 1800s
        detector.RegisterSymbolLiquidity("THINLY", LiquidityProfile.VeryLow);

        // A symbol that traded 2 minutes ago should NOT be stale for a VeryLow instrument
        var staleSymbols = detector.GetStaleSymbols();
        staleSymbols.Should().NotContain("THINLY");
    }

    #endregion

    #region DataFreshnessSlaMonitor with Liquidity

    [Fact]
    public void SlaMonitor_IlliquidSymbol_RegistersHigherThreshold()
    {
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 60,
            SkipOutsideMarketHours = false
        });

        // Register with VeryLow liquidity
        monitor.RegisterSymbolLiquidity("ILLIQUID", LiquidityProfile.VeryLow);
        monitor.RecordEvent("ILLIQUID");

        var status = monitor.GetSymbolStatus("ILLIQUID");
        status.Should().NotBeNull();
        status!.ThresholdSeconds.Should().Be(1800,
            "VeryLow liquidity should set a 1800s SLA threshold");
    }

    [Fact]
    public void SlaMonitor_LiquidSymbol_UsesDefaultThreshold()
    {
        using var monitor = new DataFreshnessSlaMonitor(new SlaConfig
        {
            DefaultFreshnessThresholdSeconds = 60,
            SkipOutsideMarketHours = false
        });

        monitor.RegisterSymbol("SPY");
        monitor.RecordEvent("SPY");

        var status = monitor.GetSymbolStatus("SPY");
        status.Should().NotBeNull();
        status!.ThresholdSeconds.Should().Be(60);
    }

    #endregion

    #region DataQualityMonitoringService integration

    [Fact]
    public async Task MonitoringService_RegisterSymbolLiquidity_PropagatesAllServices()
    {
        await using var service = new DataQualityMonitoringService();

        service.RegisterSymbolLiquidity("OTC_STOCK", LiquidityProfile.Low);

        // Verify the gap analyzer received the registration
        service.GapAnalyzer.GetSymbolLiquidity("OTC_STOCK").Should().Be(LiquidityProfile.Low);
    }

    [Fact]
    public async Task MonitoringService_IlliquidSymbol_TradeDoesNotTriggerFalseGap()
    {
        await using var service = new DataQualityMonitoringService();

        service.RegisterSymbolLiquidity("PENNY", LiquidityProfile.VeryLow);

        var baseTime = DateTimeOffset.UtcNow;
        service.ProcessTrade("PENNY", baseTime, 0.15m, 100m);
        // 10-minute pause - well within VeryLow threshold of 1800s
        service.ProcessTrade("PENNY", baseTime.AddMinutes(10), 0.16m, 200m);

        var recentGaps = service.GapAnalyzer.GetRecentGaps(10);
        recentGaps.Where(g => g.Symbol == "PENNY").Should().BeEmpty(
            "a 10-minute gap should not be flagged for a VeryLow liquidity symbol");
    }

    #endregion

    #region SymbolConfig LiquidityProfile

    [Fact]
    public void SymbolConfig_DefaultLiquidityIsNull()
    {
        var config = new Meridian.Contracts.Configuration.SymbolConfig("SPY");

        config.LiquidityProfile.Should().BeNull();
    }

    [Fact]
    public void SymbolConfig_CanSetLiquidityProfile()
    {
        var config = new Meridian.Contracts.Configuration.SymbolConfig(
            "OTCPK:XYZ",
            LiquidityProfile: LiquidityProfile.VeryLow);

        config.LiquidityProfile.Should().Be(LiquidityProfile.VeryLow);
    }

    #endregion
}
