using FluentAssertions;
using Meridian.Ui.Services;

namespace Meridian.Ui.Tests.Services;

public class ChartingServiceTests
{
    private readonly ChartingService _service = new();

    private static CandlestickData CreateSampleData(int count = 30)
    {
        var data = new CandlestickData { Symbol = "TEST", Timeframe = ChartTimeframe.Daily };
        var basePrice = 100m;
        var random = new Random(42);

        for (int i = 0; i < count; i++)
        {
            var open = basePrice + (decimal)(random.NextDouble() * 5 - 2.5);
            var close = open + (decimal)(random.NextDouble() * 4 - 2);
            var high = Math.Max(open, close) + (decimal)(random.NextDouble() * 2);
            var low = Math.Min(open, close) - (decimal)(random.NextDouble() * 2);
            var volume = (decimal)(random.NextDouble() * 1000000 + 100000);

            data.Candles.Add(new Candlestick
            {
                Timestamp = DateTime.UtcNow.AddDays(-count + i),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });

            basePrice = close;
        }

        data.HighestPrice = data.Candles.Max(c => c.High);
        data.LowestPrice = data.Candles.Min(c => c.Low);
        data.TotalVolume = data.Candles.Sum(c => c.Volume);
        data.AverageVolume = data.TotalVolume / data.Candles.Count;

        return data;
    }

    [Fact]
    public void CalculateSma_WithSufficientData_ReturnsCorrectValues()
    {
        var data = CreateSampleData();
        var result = _service.CalculateSma(data, 10);

        result.Should().NotBeNull();
        result.Name.Should().Be("SMA(10)");
        result.Type.Should().Be(IndicatorType.Overlay);
        result.Values.Should().HaveCount(data.Candles.Count - 10 + 1);

        // Verify first SMA value is average of first 10 closes
        var expectedFirst = data.Candles.Take(10).Average(c => c.Close);
        result.Values[0].Value.Should().Be(expectedFirst);
    }

    [Fact]
    public void CalculateSma_WithInsufficientData_ReturnsEmpty()
    {
        var data = CreateSampleData(5);
        var result = _service.CalculateSma(data, 10);

        result.Values.Should().BeEmpty();
    }

    [Fact]
    public void CalculateEma_WithSufficientData_ReturnsValues()
    {
        var data = CreateSampleData();
        var result = _service.CalculateEma(data, 12);

        result.Should().NotBeNull();
        result.Name.Should().Be("EMA(12)");
        result.Type.Should().Be(IndicatorType.Overlay);
        result.Values.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void CalculateEma_WithInsufficientData_ReturnsEmpty()
    {
        var data = CreateSampleData(5);
        var result = _service.CalculateEma(data, 20);

        result.Values.Should().BeEmpty();
    }

    [Fact]
    public void CalculateRsi_WithSufficientData_ReturnsValuesBetween0And100()
    {
        var data = CreateSampleData();
        var result = _service.CalculateRsi(data, 14);

        result.Should().NotBeNull();
        result.Name.Should().Be("RSI(14)");
        result.Type.Should().Be(IndicatorType.Oscillator);
        result.OverboughtLevel.Should().Be(70);
        result.OversoldLevel.Should().Be(30);
        result.Values.Should().HaveCountGreaterThan(0);

        foreach (var value in result.Values)
        {
            value.Value.Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public void CalculateRsi_WithInsufficientData_ReturnsEmpty()
    {
        var data = CreateSampleData(10);
        var result = _service.CalculateRsi(data, 14);

        result.Values.Should().BeEmpty();
    }

    [Fact]
    public void CalculateMacd_WithSufficientData_ReturnsAllLines()
    {
        var data = CreateSampleData(50);
        var result = _service.CalculateMacd(data);

        result.Should().NotBeNull();
        result.Name.Should().Be("MACD(12,26,9)");
        result.MacdLine.Should().HaveCountGreaterThan(0);
    }

    [Fact]
    public void CalculateMacd_WithInsufficientData_ReturnsEmptyLines()
    {
        var data = CreateSampleData(10);
        var result = _service.CalculateMacd(data);

        result.MacdLine.Should().BeEmpty();
    }

    [Fact]
    public void CalculateBollingerBands_WithSufficientData_ReturnsBands()
    {
        var data = CreateSampleData();
        var result = _service.CalculateBollingerBands(data, 20, 2);

        result.Should().NotBeNull();
        result.Name.Should().Be("BB(20,2.0)");
        result.UpperBand.Should().HaveCountGreaterThan(0);
        result.MiddleBand.Should().HaveCountGreaterThan(0);
        result.LowerBand.Should().HaveCountGreaterThan(0);
        result.PercentB.Should().HaveCountGreaterThan(0);

        // Upper band should always be above middle, middle above lower
        for (int i = 0; i < result.UpperBand.Count; i++)
        {
            result.UpperBand[i].Value.Should().BeGreaterThanOrEqualTo(result.MiddleBand[i].Value);
            result.MiddleBand[i].Value.Should().BeGreaterThanOrEqualTo(result.LowerBand[i].Value);
        }
    }

    [Fact]
    public void CalculateAtr_WithSufficientData_ReturnsPositiveValues()
    {
        var data = CreateSampleData();
        var result = _service.CalculateAtr(data, 14);

        result.Should().NotBeNull();
        result.Name.Should().Be("ATR(14)");
        result.Type.Should().Be(IndicatorType.Separate);
        result.Values.Should().HaveCountGreaterThan(0);

        foreach (var value in result.Values)
        {
            value.Value.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void CalculateVwap_WithData_ReturnsValuesForEachCandle()
    {
        var data = CreateSampleData();
        var result = _service.CalculateVwap(data);

        result.Should().NotBeNull();
        result.Name.Should().Be("VWAP");
        result.Type.Should().Be(IndicatorType.Overlay);
        result.Values.Should().HaveCount(data.Candles.Count);

        foreach (var value in result.Values)
        {
            value.Value.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void CalculateVolumeProfile_WithData_ReturnsLevels()
    {
        var data = CreateSampleData();
        var result = _service.CalculateVolumeProfile(data, 15);

        result.Should().NotBeNull();
        result.Levels.Should().HaveCount(15);
        result.PointOfControl.Should().BeGreaterThan(0);
        result.ValueAreaHigh.Should().BeGreaterThan(result.ValueAreaLow);

        foreach (var level in result.Levels)
        {
            level.PriceLevel.Should().BeGreaterThan(0);
            level.Volume.Should().BeGreaterThanOrEqualTo(0);
            level.Intensity.Should().BeInRange(0, 1);
        }
    }

    [Fact]
    public void CalculateVolumeProfile_WithEmptyData_ReturnsEmptyLevels()
    {
        var data = new CandlestickData { Symbol = "TEST" };
        var result = _service.CalculateVolumeProfile(data);

        result.Levels.Should().BeEmpty();
    }

    [Fact]
    public void GetAvailableIndicators_ReturnsAllIndicators()
    {
        var indicators = _service.GetAvailableIndicators();

        indicators.Should().HaveCountGreaterThanOrEqualTo(7);
        indicators.Select(i => i.Id).Should().Contain(new[] { "sma", "ema", "rsi", "macd", "bb", "atr", "vwap" });
    }

    [Fact]
    public void Candlestick_IsBullish_WhenCloseAboveOrEqualToOpen()
    {
        var bullish = new Candlestick { Open = 100, Close = 105, High = 110, Low = 95 };
        var neutral = new Candlestick { Open = 100, Close = 100, High = 105, Low = 95 };
        var bearish = new Candlestick { Open = 105, Close = 100, High = 110, Low = 95 };

        bullish.IsBullish.Should().BeTrue();
        neutral.IsBullish.Should().BeTrue();
        bearish.IsBullish.Should().BeFalse();
    }

    [Fact]
    public void Candlestick_BodyAndWicks_CalculatedCorrectly()
    {
        var candle = new Candlestick { Open = 100, Close = 105, High = 110, Low = 95, Volume = 1000 };

        candle.Body.Should().Be(5);      // |Close - Open|
        candle.UpperWick.Should().Be(5); // High - Max(Open, Close)
        candle.LowerWick.Should().Be(5); // Min(Open, Close) - Low
        candle.Range.Should().Be(15);    // High - Low
    }
}
