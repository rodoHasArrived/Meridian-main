using FluentAssertions;
using Meridian.Application.Indicators;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Xunit;

namespace Meridian.Tests.Indicators;

/// <summary>
/// Tests for TechnicalIndicatorService.
///
/// Reference: docs/open-source-references.md #25 (Skender.Stock.Indicators)
/// </summary>
public class TechnicalIndicatorServiceTests
{
    [Fact]
    public void ProcessTrade_WithSingleTrade_ShouldNotProduceSnapshot()
    {
        // Arrange
        var service = new TechnicalIndicatorService();
        var trade = CreateTrade("SPY", 450.25m, 100);

        // Act
        var snapshot = service.ProcessTrade(trade);

        // Assert
        // Single trade is not enough for indicator calculation
        snapshot.Should().BeNull();
    }

    [Fact]
    public void ProcessTrade_WithMultipleTrades_ShouldProduceSnapshot()
    {
        // Arrange
        var service = new TechnicalIndicatorService(new IndicatorConfiguration
        {
            EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.SMA },
            SmaPeriods = new[] { 5 }
        });

        // Simulate multiple days of trades
        for (int day = 0; day < 10; day++)
        {
            var dayStart = DateTimeOffset.UtcNow.AddDays(-10 + day);

            // Simulate a trading day
            for (int i = 0; i < 100; i++)
            {
                var trade = new MarketTradeUpdate(
                    Timestamp: dayStart.AddMinutes(i),
                    Symbol: "SPY",
                    Price: 450m + (day * 0.5m) + (i * 0.01m),
                    Size: 100,
                    Aggressor: i % 2 == 0 ? AggressorSide.Buy : AggressorSide.Sell,
                    SequenceNumber: day * 100 + i,
                    StreamId: "TEST",
                    Venue: "TEST"
                );
                service.ProcessTrade(trade);
            }
        }

        // Act
        var snapshot = service.GetSnapshot("SPY");

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.Symbol.Should().Be("SPY");
        snapshot.Indicators.Should().ContainKey("LastPrice");
    }

    [Fact]
    public void CalculateHistorical_WithBars_ShouldCalculateAllIndicators()
    {
        // Arrange
        var service = new TechnicalIndicatorService(new IndicatorConfiguration
        {
            EnabledIndicators = new HashSet<IndicatorType>
            {
                IndicatorType.SMA,
                IndicatorType.EMA,
                IndicatorType.RSI,
                IndicatorType.MACD,
                IndicatorType.BollingerBands
            },
            SmaPeriods = new[] { 10, 20 },
            EmaPeriods = new[] { 12, 26 },
            RsiPeriod = 14,
            MacdFastPeriod = 12,
            MacdSlowPeriod = 26,
            MacdSignalPeriod = 9,
            BollingerPeriod = 20
        });

        var bars = GenerateHistoricalBars("SPY", 100);

        // Act
        var result = service.CalculateHistorical("SPY", bars);

        // Assert
        result.Should().NotBeNull();
        result.Symbol.Should().Be("SPY");
        result.DataPoints.Should().NotBeEmpty();

        // Check that various indicators were calculated
        result.DataPoints.Should().Contain(dp => dp.IndicatorName.StartsWith("SMA_"));
        result.DataPoints.Should().Contain(dp => dp.IndicatorName.StartsWith("EMA_"));
        result.DataPoints.Should().Contain(dp => dp.IndicatorName.StartsWith("RSI_"));
        result.DataPoints.Should().Contain(dp => dp.IndicatorName == "MACD");
        result.DataPoints.Should().Contain(dp => dp.IndicatorName == "BB_Upper");
    }

    [Fact]
    public void CalculateHistorical_WithInsufficientBars_ShouldReturnEmptyResult()
    {
        // Arrange
        var service = new TechnicalIndicatorService();
        var bars = GenerateHistoricalBars("SPY", 1); // Only 1 bar

        // Act
        var result = service.CalculateHistorical("SPY", bars);

        // Assert
        result.Should().NotBeNull();
        result.DataPoints.Should().BeEmpty();
    }

    [Fact]
    public void GetAllSnapshots_WithMultipleSymbols_ShouldReturnAllSnapshots()
    {
        // Arrange
        var service = new TechnicalIndicatorService();
        var symbols = new[] { "SPY", "QQQ", "IWM" };

        // Process trades for multiple symbols
        foreach (var symbol in symbols)
        {
            for (int day = 0; day < 10; day++)
            {
                for (int i = 0; i < 10; i++)
                {
                    var trade = CreateTrade(
                        symbol,
                        100m + symbol.GetHashCode() % 100 + day + i * 0.01m,
                        100,
                        DateTimeOffset.UtcNow.AddDays(-10 + day).AddMinutes(i));

                    service.ProcessTrade(trade);
                }
            }
        }

        // Act
        var snapshots = service.GetAllSnapshots();

        // Assert
        snapshots.Should().HaveCount(3);
        snapshots.Keys.Should().Contain("SPY");
        snapshots.Keys.Should().Contain("QQQ");
        snapshots.Keys.Should().Contain("IWM");
    }

    [Fact]
    public void Configuration_ShouldRespectEnabledIndicators()
    {
        // Arrange
        var config = new IndicatorConfiguration
        {
            EnabledIndicators = new HashSet<IndicatorType> { IndicatorType.SMA },
            SmaPeriods = new[] { 10 }
        };
        var service = new TechnicalIndicatorService(config);
        var bars = GenerateHistoricalBars("SPY", 50);

        // Act
        var result = service.CalculateHistorical("SPY", bars);

        // Assert
        result.DataPoints.Should().Contain(dp => dp.IndicatorName == "SMA_10");
        result.DataPoints.Should().NotContain(dp => dp.IndicatorName.StartsWith("RSI_"));
        result.DataPoints.Should().NotContain(dp => dp.IndicatorName == "MACD");
    }

    [Fact]
    public void Dispose_ShouldClearAllState()
    {
        // Arrange
        var service = new TechnicalIndicatorService();
        service.ProcessTrade(CreateTrade("SPY", 450m, 100));

        // Act
        service.Dispose();

        // Assert
        var snapshots = service.GetAllSnapshots();
        snapshots.Should().BeEmpty();
    }

    private static MarketTradeUpdate CreateTrade(string symbol, decimal price, int size, DateTimeOffset? timestamp = null)
    {
        return new MarketTradeUpdate(
            Timestamp: timestamp ?? DateTimeOffset.UtcNow,
            Symbol: symbol,
            Price: price,
            Size: size,
            Aggressor: AggressorSide.Buy,
            SequenceNumber: 1,
            StreamId: "TEST",
            Venue: "TEST"
        );
    }

    private static IEnumerable<HistoricalBar> GenerateHistoricalBars(string symbol, int count)
    {
        var random = new Random(42);
        var basePrice = 100m;

        for (int i = 0; i < count; i++)
        {
            var dayChange = (decimal)(random.NextDouble() - 0.5) * 2m;
            var close = basePrice + dayChange;

            // Ensure high is the maximum and low is the minimum of open and close
            var potentialHigh = basePrice + (decimal)random.NextDouble() * 1m;
            var potentialLow = basePrice - (decimal)random.NextDouble() * 1m;

            var high = Math.Max(basePrice, Math.Max(close, potentialHigh));
            var low = Math.Min(basePrice, Math.Min(close, potentialLow));

            yield return new HistoricalBar(
                Symbol: "TEST",
                SessionDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-count + i)),
                Open: basePrice,
                High: high,
                Low: low,
                Close: close,
                Volume: random.Next(1000000, 100000000),
                SequenceNumber: i
            );

            basePrice = close;
        }
    }
}
