namespace Meridian.Ui.Services;

/// <summary>
/// Service for advanced charting with candlesticks and technical indicators.
/// Provides data transformation and indicator calculations for chart rendering.
/// </summary>
public sealed class ChartingService
{
    private readonly BackfillService _backfillService;
    private readonly LiveDataService _liveDataService;

    public ChartingService()
    {
        _backfillService = BackfillService.Instance;
        _liveDataService = LiveDataService.Instance;
    }

    /// <summary>
    /// Gets OHLCV candlestick data for charting.
    /// </summary>
    public async Task<CandlestickData> GetCandlestickDataAsync(
        string symbol,
        ChartTimeframe timeframe,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        var data = new CandlestickData
        {
            Symbol = symbol,
            Timeframe = timeframe
        };

        // Get historical bars from backfill service
        var bars = await _backfillService.GetHistoricalBarsAsync(
            symbol, fromDate, toDate, ct);

        foreach (var bar in bars)
        {
            data.Candles.Add(new Candlestick
            {
                Timestamp = bar.ToTimestampUtc().DateTime,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume
            });
        }

        // Calculate price statistics
        if (data.Candles.Count > 0)
        {
            data.HighestPrice = data.Candles.Max(c => c.High);
            data.LowestPrice = data.Candles.Min(c => c.Low);
            data.TotalVolume = data.Candles.Sum(c => c.Volume);
            data.AverageVolume = data.TotalVolume / data.Candles.Count;
        }

        return data;
    }

    /// <summary>
    /// Calculates Simple Moving Average (SMA).
    /// </summary>
    public IndicatorData CalculateSma(CandlestickData data, int period)
    {
        var indicator = new IndicatorData
        {
            Name = $"SMA({period})",
            Type = IndicatorType.Overlay
        };

        if (data.Candles.Count < period)
            return indicator;

        for (int i = period - 1; i < data.Candles.Count; i++)
        {
            var sum = data.Candles.Skip(i - period + 1).Take(period).Sum(c => c.Close);
            indicator.Values.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = sum / period
            });
        }

        return indicator;
    }

    /// <summary>
    /// Calculates Exponential Moving Average (EMA).
    /// </summary>
    public IndicatorData CalculateEma(CandlestickData data, int period)
    {
        var indicator = new IndicatorData
        {
            Name = $"EMA({period})",
            Type = IndicatorType.Overlay
        };

        if (data.Candles.Count < period)
            return indicator;

        var multiplier = 2.0m / (period + 1);
        var ema = data.Candles.Take(period).Average(c => c.Close);

        indicator.Values.Add(new IndicatorValue
        {
            Timestamp = data.Candles[period - 1].Timestamp,
            Value = ema
        });

        for (int i = period; i < data.Candles.Count; i++)
        {
            ema = (data.Candles[i].Close - ema) * multiplier + ema;
            indicator.Values.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = ema
            });
        }

        return indicator;
    }

    /// <summary>
    /// Calculates Volume Weighted Average Price (VWAP).
    /// </summary>
    public IndicatorData CalculateVwap(CandlestickData data)
    {
        var indicator = new IndicatorData
        {
            Name = "VWAP",
            Type = IndicatorType.Overlay
        };

        decimal cumulativeTypicalPriceVolume = 0;
        decimal cumulativeVolume = 0;

        foreach (var candle in data.Candles)
        {
            var typicalPrice = (candle.High + candle.Low + candle.Close) / 3;
            cumulativeTypicalPriceVolume += typicalPrice * candle.Volume;
            cumulativeVolume += candle.Volume;

            indicator.Values.Add(new IndicatorValue
            {
                Timestamp = candle.Timestamp,
                Value = cumulativeVolume > 0 ? cumulativeTypicalPriceVolume / cumulativeVolume : 0
            });
        }

        return indicator;
    }

    /// <summary>
    /// Calculates Relative Strength Index (RSI).
    /// </summary>
    public IndicatorData CalculateRsi(CandlestickData data, int period = 14)
    {
        var indicator = new IndicatorData
        {
            Name = $"RSI({period})",
            Type = IndicatorType.Oscillator,
            MinValue = 0,
            MaxValue = 100,
            OverboughtLevel = 70,
            OversoldLevel = 30
        };

        if (data.Candles.Count < period + 1)
            return indicator;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < data.Candles.Count; i++)
        {
            var change = data.Candles[i].Close - data.Candles[i - 1].Close;
            gains.Add(Math.Max(0, change));
            losses.Add(Math.Max(0, -change));
        }

        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        for (int i = period; i <= gains.Count; i++)
        {
            var rs = avgLoss == 0 ? 100 : avgGain / avgLoss;
            var rsi = 100 - (100 / (1 + rs));

            indicator.Values.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = rsi
            });

            if (i < gains.Count)
            {
                avgGain = (avgGain * (period - 1) + gains[i]) / period;
                avgLoss = (avgLoss * (period - 1) + losses[i]) / period;
            }
        }

        return indicator;
    }

    /// <summary>
    /// Calculates Moving Average Convergence Divergence (MACD).
    /// </summary>
    public MacdData CalculateMacd(CandlestickData data, int fastPeriod = 12, int slowPeriod = 26, int signalPeriod = 9)
    {
        var macd = new MacdData
        {
            Name = $"MACD({fastPeriod},{slowPeriod},{signalPeriod})"
        };

        if (data.Candles.Count < slowPeriod)
            return macd;

        var fastEma = CalculateEmaValues(data.Candles.Select(c => c.Close).ToList(), fastPeriod);
        var slowEma = CalculateEmaValues(data.Candles.Select(c => c.Close).ToList(), slowPeriod);

        // Calculate MACD line
        var macdLine = new List<decimal>();
        for (int i = slowPeriod - 1; i < data.Candles.Count; i++)
        {
            var fastIndex = i - (slowPeriod - fastPeriod);
            if (fastIndex >= 0 && fastIndex < fastEma.Count)
            {
                macdLine.Add(fastEma[fastIndex] - slowEma[i - slowPeriod + 1]);
            }
        }

        // Calculate signal line
        var signalLine = CalculateEmaValues(macdLine, signalPeriod);

        // Populate results
        for (int i = 0; i < macdLine.Count; i++)
        {
            var candleIndex = slowPeriod - 1 + i;
            if (candleIndex < data.Candles.Count)
            {
                macd.MacdLine.Add(new IndicatorValue
                {
                    Timestamp = data.Candles[candleIndex].Timestamp,
                    Value = macdLine[i]
                });

                if (i >= signalPeriod - 1)
                {
                    var signalIndex = i - signalPeriod + 1;
                    macd.SignalLine.Add(new IndicatorValue
                    {
                        Timestamp = data.Candles[candleIndex].Timestamp,
                        Value = signalLine[signalIndex]
                    });

                    macd.Histogram.Add(new IndicatorValue
                    {
                        Timestamp = data.Candles[candleIndex].Timestamp,
                        Value = macdLine[i] - signalLine[signalIndex]
                    });
                }
            }
        }

        return macd;
    }

    /// <summary>
    /// Calculates Bollinger Bands.
    /// </summary>
    public BollingerBandsData CalculateBollingerBands(CandlestickData data, int period = 20, decimal stdDevMultiplier = 2)
    {
        var bb = new BollingerBandsData
        {
            Name = $"BB({period},{stdDevMultiplier:F1})"
        };

        if (data.Candles.Count < period)
            return bb;

        for (int i = period - 1; i < data.Candles.Count; i++)
        {
            var window = data.Candles.Skip(i - period + 1).Take(period).Select(c => c.Close).ToList();
            var sma = window.Average();
            var stdDev = CalculateStdDev(window);

            bb.MiddleBand.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = sma
            });

            bb.UpperBand.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = sma + stdDevMultiplier * stdDev
            });

            bb.LowerBand.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = sma - stdDevMultiplier * stdDev
            });

            // Calculate %B
            var bandWidth = bb.UpperBand.Last().Value - bb.LowerBand.Last().Value;
            bb.PercentB.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i].Timestamp,
                Value = bandWidth > 0
                    ? (data.Candles[i].Close - bb.LowerBand.Last().Value) / bandWidth * 100
                    : 50
            });
        }

        return bb;
    }

    /// <summary>
    /// Calculates Average True Range (ATR).
    /// </summary>
    public IndicatorData CalculateAtr(CandlestickData data, int period = 14)
    {
        var indicator = new IndicatorData
        {
            Name = $"ATR({period})",
            Type = IndicatorType.Separate
        };

        if (data.Candles.Count < period + 1)
            return indicator;

        var trueRanges = new List<decimal>();

        for (int i = 1; i < data.Candles.Count; i++)
        {
            var current = data.Candles[i];
            var previous = data.Candles[i - 1];

            var tr = Math.Max(
                current.High - current.Low,
                Math.Max(
                    Math.Abs(current.High - previous.Close),
                    Math.Abs(current.Low - previous.Close)));

            trueRanges.Add(tr);
        }

        var atr = trueRanges.Take(period).Average();

        indicator.Values.Add(new IndicatorValue
        {
            Timestamp = data.Candles[period].Timestamp,
            Value = atr
        });

        for (int i = period; i < trueRanges.Count; i++)
        {
            atr = (atr * (period - 1) + trueRanges[i]) / period;
            indicator.Values.Add(new IndicatorValue
            {
                Timestamp = data.Candles[i + 1].Timestamp,
                Value = atr
            });
        }

        return indicator;
    }

    /// <summary>
    /// Calculates volume profile for price levels.
    /// </summary>
    public VolumeProfileData CalculateVolumeProfile(CandlestickData data, int buckets = 20)
    {
        var profile = new VolumeProfileData();

        if (data.Candles.Count == 0)
            return profile;

        var minPrice = data.Candles.Min(c => c.Low);
        var maxPrice = data.Candles.Max(c => c.High);
        var bucketSize = (maxPrice - minPrice) / buckets;

        if (bucketSize <= 0)
            return profile;

        var volumeByBucket = new decimal[buckets];

        foreach (var candle in data.Candles)
        {
            // Distribute volume across price range covered by the candle
            var lowBucket = (int)Math.Max(0, Math.Min(buckets - 1, (candle.Low - minPrice) / bucketSize));
            var highBucket = (int)Math.Max(0, Math.Min(buckets - 1, (candle.High - minPrice) / bucketSize));

            var volumePerBucket = candle.Volume / (highBucket - lowBucket + 1);
            for (int i = lowBucket; i <= highBucket; i++)
            {
                volumeByBucket[i] += volumePerBucket;
            }
        }

        var maxVolume = volumeByBucket.Max();

        for (int i = 0; i < buckets; i++)
        {
            var priceLevel = minPrice + (i + 0.5m) * bucketSize;
            profile.Levels.Add(new VolumePriceLevel
            {
                PriceLevel = priceLevel,
                Volume = volumeByBucket[i],
                Intensity = maxVolume > 0 ? (double)(volumeByBucket[i] / maxVolume) : 0
            });
        }

        // Find Point of Control (highest volume price level)
        var pocIndex = Array.IndexOf(volumeByBucket, maxVolume);
        profile.PointOfControl = minPrice + (pocIndex + 0.5m) * bucketSize;

        // Calculate Value Area (70% of volume)
        var totalVolume = volumeByBucket.Sum();
        var targetVolume = totalVolume * 0.7m;
        var sortedBuckets = volumeByBucket.Select((v, i) => new { Volume = v, Index = i })
            .OrderByDescending(x => x.Volume)
            .ToList();

        decimal accumulatedVolume = 0;
        var includedBuckets = new List<int>();
        foreach (var bucket in sortedBuckets)
        {
            includedBuckets.Add(bucket.Index);
            accumulatedVolume += bucket.Volume;
            if (accumulatedVolume >= targetVolume)
                break;
        }

        if (includedBuckets.Count > 0)
        {
            profile.ValueAreaHigh = minPrice + (includedBuckets.Max() + 1) * bucketSize;
            profile.ValueAreaLow = minPrice + includedBuckets.Min() * bucketSize;
        }
        else
        {
            // No volume data - use full price range
            profile.ValueAreaHigh = maxPrice;
            profile.ValueAreaLow = minPrice;
        }

        return profile;
    }

    /// <summary>
    /// Gets available technical indicators.
    /// </summary>
    public IReadOnlyList<TechnicalIndicatorInfo> GetAvailableIndicators()
    {
        return new List<TechnicalIndicatorInfo>
        {
            new() { Id = "sma", Name = "Simple Moving Average", ShortName = "SMA", Category = "Trend", DefaultParams = "20" },
            new() { Id = "ema", Name = "Exponential Moving Average", ShortName = "EMA", Category = "Trend", DefaultParams = "20" },
            new() { Id = "vwap", Name = "Volume Weighted Average Price", ShortName = "VWAP", Category = "Trend", DefaultParams = "" },
            new() { Id = "rsi", Name = "Relative Strength Index", ShortName = "RSI", Category = "Momentum", DefaultParams = "14" },
            new() { Id = "macd", Name = "MACD", ShortName = "MACD", Category = "Momentum", DefaultParams = "12,26,9" },
            new() { Id = "bb", Name = "Bollinger Bands", ShortName = "BB", Category = "Volatility", DefaultParams = "20,2" },
            new() { Id = "atr", Name = "Average True Range", ShortName = "ATR", Category = "Volatility", DefaultParams = "14" },
            new() { Id = "volume", Name = "Volume", ShortName = "Vol", Category = "Volume", DefaultParams = "" },
            new() { Id = "volume_profile", Name = "Volume Profile", ShortName = "VP", Category = "Volume", DefaultParams = "20" }
        };
    }

    private List<decimal> CalculateEmaValues(List<decimal> values, int period)
    {
        var results = new List<decimal>();
        if (values.Count < period)
            return results;

        var multiplier = 2.0m / (period + 1);
        var ema = values.Take(period).Average();
        results.Add(ema);

        for (int i = period; i < values.Count; i++)
        {
            ema = (values[i] - ema) * multiplier + ema;
            results.Add(ema);
        }

        return results;
    }

    private decimal CalculateStdDev(List<decimal> values)
    {
        if (values.Count == 0)
            return 0;
        var avg = values.Average();
        var sumOfSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumOfSquares / values.Count));
    }
}

/// <summary>
/// Chart timeframe.
/// </summary>
public enum ChartTimeframe : byte
{
    Minute1,
    Minute5,
    Minute15,
    Minute30,
    Hour1,
    Hour4,
    Daily,
    Weekly,
    Monthly
}

/// <summary>
/// OHLCV candlestick data.
/// </summary>
public sealed class CandlestickData
{
    public string Symbol { get; set; } = string.Empty;
    public ChartTimeframe Timeframe { get; set; }
    public List<Candlestick> Candles { get; } = new();
    public decimal HighestPrice { get; set; }
    public decimal LowestPrice { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal AverageVolume { get; set; }
}

/// <summary>
/// Single OHLCV candlestick.
/// </summary>
public sealed class Candlestick
{
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }

    public bool IsBullish => Close >= Open;
    public decimal Body => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
    public decimal Range => High - Low;
}

/// <summary>
/// Technical indicator type.
/// </summary>
public enum IndicatorType : byte
{
    Overlay,    // Drawn on price chart
    Oscillator, // Separate pane with fixed range
    Separate    // Separate pane with dynamic range
}

/// <summary>
/// Generic indicator data.
/// </summary>
public sealed class IndicatorData
{
    public string Name { get; set; } = string.Empty;
    public IndicatorType Type { get; set; }
    public List<IndicatorValue> Values { get; } = new();
    public decimal MinValue { get; set; }
    public decimal MaxValue { get; set; }
    public decimal? OverboughtLevel { get; set; }
    public decimal? OversoldLevel { get; set; }
}

/// <summary>
/// Single indicator value.
/// </summary>
public sealed class IndicatorValue
{
    public DateTime Timestamp { get; set; }
    public decimal Value { get; set; }
}

/// <summary>
/// MACD indicator data.
/// </summary>
public sealed class MacdData
{
    public string Name { get; set; } = string.Empty;
    public List<IndicatorValue> MacdLine { get; } = new();
    public List<IndicatorValue> SignalLine { get; } = new();
    public List<IndicatorValue> Histogram { get; } = new();
}

/// <summary>
/// Bollinger Bands data.
/// </summary>
public sealed class BollingerBandsData
{
    public string Name { get; set; } = string.Empty;
    public List<IndicatorValue> UpperBand { get; } = new();
    public List<IndicatorValue> MiddleBand { get; } = new();
    public List<IndicatorValue> LowerBand { get; } = new();
    public List<IndicatorValue> PercentB { get; } = new();
}

/// <summary>
/// Volume profile data.
/// </summary>
public sealed class VolumeProfileData
{
    public List<VolumePriceLevel> Levels { get; } = new();
    public decimal PointOfControl { get; set; }
    public decimal ValueAreaHigh { get; set; }
    public decimal ValueAreaLow { get; set; }
}

/// <summary>
/// Volume at price level.
/// </summary>
public sealed class VolumePriceLevel
{
    public decimal PriceLevel { get; set; }
    public decimal Volume { get; set; }
    public double Intensity { get; set; }
}

/// <summary>
/// Technical indicator info.
/// </summary>
public sealed class TechnicalIndicatorInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string DefaultParams { get; set; } = string.Empty;
}
