using System.Collections.Concurrent;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Serilog;
using Skender.Stock.Indicators;

namespace Meridian.Application.Indicators;

/// <summary>
/// Real-time technical indicator calculation service using Skender.Stock.Indicators.
/// Provides 200+ technical indicators with streaming support for live market data.
///
/// Based on: https://github.com/DaveSkender/Stock.Indicators (Apache 2.0)
/// Reference: docs/open-source-references.md #25
/// </summary>
public sealed class TechnicalIndicatorService : IDisposable
{
    private readonly ILogger _log = LoggingSetup.ForContext<TechnicalIndicatorService>();
    private readonly ConcurrentDictionary<string, SymbolIndicatorState> _symbolStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly IndicatorConfiguration _config;
    private bool _disposed;

    public TechnicalIndicatorService(IndicatorConfiguration? config = null)
    {
        _config = config ?? IndicatorConfiguration.Default;
        _log.Information("TechnicalIndicatorService initialized with {IndicatorCount} indicators configured",
            _config.EnabledIndicators.Count);
    }

    /// <summary>
    /// Process a trade event and update all streaming indicators for the symbol.
    /// </summary>
    public IndicatorSnapshot? ProcessTrade(MarketTradeUpdate trade)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TechnicalIndicatorService));

        var state = _symbolStates.GetOrAdd(trade.Symbol, sym => new SymbolIndicatorState(sym, _config));
        return state.AddTrade(trade);
    }

    /// <summary>
    /// Process a quote event and update quote-based indicators.
    /// </summary>
    public IndicatorSnapshot? ProcessQuote(MarketQuoteUpdate quote)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TechnicalIndicatorService));

        var state = _symbolStates.GetOrAdd(quote.Symbol, sym => new SymbolIndicatorState(sym, _config));
        return state.AddQuote(quote);
    }

    /// <summary>
    /// Get the current indicator snapshot for a symbol.
    /// </summary>
    public IndicatorSnapshot? GetSnapshot(string symbol)
    {
        if (_symbolStates.TryGetValue(symbol, out var state))
            return state.GetCurrentSnapshot();
        return null;
    }

    /// <summary>
    /// Get all symbol snapshots.
    /// </summary>
    public IReadOnlyDictionary<string, IndicatorSnapshot> GetAllSnapshots()
    {
        var result = new Dictionary<string, IndicatorSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _symbolStates)
        {
            var snapshot = kvp.Value.GetCurrentSnapshot();
            if (snapshot != null)
                result[kvp.Key] = snapshot;
        }
        return result;
    }

    /// <summary>
    /// Calculate historical indicators from OHLCV bars.
    /// </summary>
    public HistoricalIndicatorResult CalculateHistorical(string symbol, IEnumerable<HistoricalBar> bars)
    {
        var quotes = bars.Select(b => new Quote
        {
            Date = b.ToTimestampUtc().DateTime,
            Open = b.Open,
            High = b.High,
            Low = b.Low,
            Close = b.Close,
            Volume = b.Volume
        }).OrderBy(q => q.Date).ToList();

        if (quotes.Count < 2)
            return new HistoricalIndicatorResult(symbol, new List<IndicatorDataPoint>());

        var results = new List<IndicatorDataPoint>();

        // Calculate SMA
        if (_config.EnabledIndicators.Contains(IndicatorType.SMA))
        {
            foreach (var period in _config.SmaPeriods)
            {
                var sma = quotes.GetSma(period).ToList();
                for (int i = 0; i < sma.Count; i++)
                {
                    if (sma[i].Sma.HasValue)
                    {
                        results.Add(new IndicatorDataPoint(
                            quotes[i].Date,
                            $"SMA_{period}",
                            (decimal)sma[i].Sma.GetValueOrDefault()));
                    }
                }
            }
        }

        // Calculate EMA
        if (_config.EnabledIndicators.Contains(IndicatorType.EMA))
        {
            foreach (var period in _config.EmaPeriods)
            {
                var ema = quotes.GetEma(period).ToList();
                for (int i = 0; i < ema.Count; i++)
                {
                    if (ema[i].Ema.HasValue)
                    {
                        results.Add(new IndicatorDataPoint(
                            quotes[i].Date,
                            $"EMA_{period}",
                            (decimal)ema[i].Ema.GetValueOrDefault()));
                    }
                }
            }
        }

        // Calculate RSI
        if (_config.EnabledIndicators.Contains(IndicatorType.RSI))
        {
            var rsi = quotes.GetRsi(_config.RsiPeriod).ToList();
            for (int i = 0; i < rsi.Count; i++)
            {
                if (rsi[i].Rsi.HasValue)
                {
                    results.Add(new IndicatorDataPoint(
                        quotes[i].Date,
                        $"RSI_{_config.RsiPeriod}",
                        (decimal)rsi[i].Rsi.GetValueOrDefault()));
                }
            }
        }

        // Calculate MACD
        if (_config.EnabledIndicators.Contains(IndicatorType.MACD))
        {
            var macd = quotes.GetMacd(_config.MacdFastPeriod, _config.MacdSlowPeriod, _config.MacdSignalPeriod).ToList();
            for (int i = 0; i < macd.Count; i++)
            {
                if (macd[i].Macd.HasValue)
                {
                    results.Add(new IndicatorDataPoint(quotes[i].Date, "MACD", (decimal)macd[i].Macd.GetValueOrDefault()));
                    if (macd[i].Signal.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "MACD_Signal", (decimal)macd[i].Signal.GetValueOrDefault()));
                    if (macd[i].Histogram.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "MACD_Histogram", (decimal)macd[i].Histogram.GetValueOrDefault()));
                }
            }
        }

        // Calculate Bollinger Bands
        if (_config.EnabledIndicators.Contains(IndicatorType.BollingerBands))
        {
            var bb = quotes.GetBollingerBands(_config.BollingerPeriod, _config.BollingerStdDev).ToList();
            for (int i = 0; i < bb.Count; i++)
            {
                if (bb[i].UpperBand.HasValue)
                {
                    results.Add(new IndicatorDataPoint(quotes[i].Date, "BB_Upper", (decimal)bb[i].UpperBand.GetValueOrDefault()));
                    results.Add(new IndicatorDataPoint(quotes[i].Date, "BB_Middle", (decimal)bb[i].Sma.GetValueOrDefault()));
                    results.Add(new IndicatorDataPoint(quotes[i].Date, "BB_Lower", (decimal)bb[i].LowerBand.GetValueOrDefault()));
                    if (bb[i].Width.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "BB_Width", (decimal)bb[i].Width.GetValueOrDefault()));
                }
            }
        }

        // Calculate ATR (Average True Range)
        if (_config.EnabledIndicators.Contains(IndicatorType.ATR))
        {
            var atr = quotes.GetAtr(_config.AtrPeriod).ToList();
            for (int i = 0; i < atr.Count; i++)
            {
                if (atr[i].Atr.HasValue)
                {
                    results.Add(new IndicatorDataPoint(
                        quotes[i].Date,
                        $"ATR_{_config.AtrPeriod}",
                        (decimal)atr[i].Atr.GetValueOrDefault()));
                }
            }
        }

        // Calculate Stochastic Oscillator
        if (_config.EnabledIndicators.Contains(IndicatorType.Stochastic))
        {
            var stoch = quotes.GetStoch(_config.StochKPeriod, _config.StochDPeriod, _config.StochSmoothPeriod).ToList();
            for (int i = 0; i < stoch.Count; i++)
            {
                if (stoch[i].K.HasValue)
                {
                    results.Add(new IndicatorDataPoint(quotes[i].Date, "Stoch_K", (decimal)stoch[i].K.GetValueOrDefault()));
                    if (stoch[i].D.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "Stoch_D", (decimal)stoch[i].D.GetValueOrDefault()));
                }
            }
        }

        // Calculate VWAP (Volume Weighted Average Price)
        if (_config.EnabledIndicators.Contains(IndicatorType.VWAP))
        {
            var vwap = quotes.GetVwap().ToList();
            for (int i = 0; i < vwap.Count; i++)
            {
                if (vwap[i].Vwap.HasValue)
                {
                    results.Add(new IndicatorDataPoint(
                        quotes[i].Date,
                        "VWAP",
                        (decimal)vwap[i].Vwap.GetValueOrDefault()));
                }
            }
        }

        // Calculate ADX (Average Directional Index)
        if (_config.EnabledIndicators.Contains(IndicatorType.ADX))
        {
            var adx = quotes.GetAdx(_config.AdxPeriod).ToList();
            for (int i = 0; i < adx.Count; i++)
            {
                if (adx[i].Adx.HasValue)
                {
                    results.Add(new IndicatorDataPoint(quotes[i].Date, $"ADX_{_config.AdxPeriod}", (decimal)adx[i].Adx.GetValueOrDefault()));
                    if (adx[i].Pdi.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "DI_Plus", (decimal)adx[i].Pdi.GetValueOrDefault()));
                    if (adx[i].Mdi.HasValue)
                        results.Add(new IndicatorDataPoint(quotes[i].Date, "DI_Minus", (decimal)adx[i].Mdi.GetValueOrDefault()));
                }
            }
        }

        // Calculate OBV (On-Balance Volume)
        if (_config.EnabledIndicators.Contains(IndicatorType.OBV))
        {
            var obv = quotes.GetObv().ToList();
            for (int i = 0; i < obv.Count; i++)
            {
                results.Add(new IndicatorDataPoint(
                    quotes[i].Date,
                    "OBV",
                    (decimal)obv[i].Obv));
            }
        }

        _log.Debug("Calculated {Count} historical indicator data points for {Symbol}", results.Count, symbol);
        return new HistoricalIndicatorResult(symbol, results);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _symbolStates.Clear();
    }
}

/// <summary>
/// Maintains streaming indicator state for a single symbol.
/// </summary>
internal sealed class SymbolIndicatorState
{
    private readonly string _symbol;
    private readonly IndicatorConfiguration _config;
    private readonly List<Quote> _quotes = new();
    private readonly object _lock = new();
    private decimal _lastPrice;
    private decimal _dayHigh = decimal.MinValue;
    private decimal _dayLow = decimal.MaxValue;
    private decimal _dayOpen;
    private decimal _dayVolume;
    private DateTime _currentDay;

    public SymbolIndicatorState(string symbol, IndicatorConfiguration config)
    {
        _symbol = symbol;
        _config = config;
        _currentDay = DateTime.MinValue;  // Initialize to min to handle historical data
    }

    public IndicatorSnapshot? AddTrade(MarketTradeUpdate trade)
    {
        lock (_lock)
        {
            var tradeDate = trade.Timestamp.UtcDateTime.Date;

            // Check for day rollover (handles both forward and backward time)
            if (tradeDate != _currentDay)
            {
                // Finalize previous day's bar
                if (_dayOpen != 0)
                {
                    _quotes.Add(new Quote
                    {
                        Date = _currentDay,
                        Open = _dayOpen,
                        High = _dayHigh,
                        Low = _dayLow,
                        Close = _lastPrice,
                        Volume = _dayVolume
                    });

                    // Keep only recent quotes for efficiency
                    if (_quotes.Count > _config.MaxQuotesHistory)
                        _quotes.RemoveRange(0, _quotes.Count - _config.MaxQuotesHistory);
                }

                // Reset for new day
                _currentDay = tradeDate;
                _dayOpen = trade.Price;
                _dayHigh = trade.Price;
                _dayLow = trade.Price;
                _dayVolume = 0;
            }

            // Update intraday stats
            _lastPrice = trade.Price;
            if (trade.Price > _dayHigh)
                _dayHigh = trade.Price;
            if (trade.Price < _dayLow)
                _dayLow = trade.Price;
            _dayVolume += trade.Size;

            return CalculateCurrentSnapshot();
        }
    }

    public IndicatorSnapshot? AddQuote(MarketQuoteUpdate quote)
    {
        lock (_lock)
        {
            // Update mid-price
            if (quote.BidPrice > 0 && quote.AskPrice > 0)
            {
                _lastPrice = (quote.BidPrice + quote.AskPrice) / 2;
            }

            return CalculateCurrentSnapshot();
        }
    }

    public IndicatorSnapshot? GetCurrentSnapshot()
    {
        lock (_lock)
        {
            return CalculateCurrentSnapshot();
        }
    }

    private IndicatorSnapshot? CalculateCurrentSnapshot()
    {
        if (_quotes.Count < 2)
            return null;

        var indicators = new Dictionary<string, decimal>();

        try
        {
            // Add current partial bar for real-time calculation
            var quotesWithCurrent = _quotes.ToList();
            if (_dayOpen != 0)
            {
                quotesWithCurrent.Add(new Quote
                {
                    Date = DateTime.UtcNow,
                    Open = _dayOpen,
                    High = _dayHigh,
                    Low = _dayLow,
                    Close = _lastPrice,
                    Volume = _dayVolume
                });
            }

            // Calculate SMA
            if (_config.EnabledIndicators.Contains(IndicatorType.SMA))
            {
                foreach (var period in _config.SmaPeriods)
                {
                    if (quotesWithCurrent.Count >= period)
                    {
                        var sma = quotesWithCurrent.GetSma(period).LastOrDefault();
                        if (sma?.Sma.HasValue == true)
                            indicators[$"SMA_{period}"] = (decimal)sma.Sma.Value;
                    }
                }
            }

            // Calculate EMA
            if (_config.EnabledIndicators.Contains(IndicatorType.EMA))
            {
                foreach (var period in _config.EmaPeriods)
                {
                    if (quotesWithCurrent.Count >= period)
                    {
                        var ema = quotesWithCurrent.GetEma(period).LastOrDefault();
                        if (ema?.Ema.HasValue == true)
                            indicators[$"EMA_{period}"] = (decimal)ema.Ema.Value;
                    }
                }
            }

            // Calculate RSI
            if (_config.EnabledIndicators.Contains(IndicatorType.RSI) && quotesWithCurrent.Count >= _config.RsiPeriod + 1)
            {
                var rsi = quotesWithCurrent.GetRsi(_config.RsiPeriod).LastOrDefault();
                if (rsi?.Rsi.HasValue == true)
                    indicators[$"RSI_{_config.RsiPeriod}"] = (decimal)rsi.Rsi.Value;
            }

            // Calculate MACD
            if (_config.EnabledIndicators.Contains(IndicatorType.MACD) && quotesWithCurrent.Count >= _config.MacdSlowPeriod)
            {
                var macd = quotesWithCurrent.GetMacd(_config.MacdFastPeriod, _config.MacdSlowPeriod, _config.MacdSignalPeriod).LastOrDefault();
                if (macd?.Macd.HasValue == true)
                {
                    indicators["MACD"] = (decimal)macd.Macd.Value;
                    if (macd.Signal.HasValue)
                        indicators["MACD_Signal"] = (decimal)macd.Signal.Value;
                    if (macd.Histogram.HasValue)
                        indicators["MACD_Histogram"] = (decimal)macd.Histogram.Value;
                }
            }

            // Calculate Bollinger Bands
            if (_config.EnabledIndicators.Contains(IndicatorType.BollingerBands) && quotesWithCurrent.Count >= _config.BollingerPeriod)
            {
                var bb = quotesWithCurrent.GetBollingerBands(_config.BollingerPeriod, _config.BollingerStdDev).LastOrDefault();
                if (bb?.UpperBand.HasValue == true)
                {
                    indicators["BB_Upper"] = (decimal)bb.UpperBand.Value;
                    indicators["BB_Middle"] = (decimal)bb.Sma!.Value;
                    indicators["BB_Lower"] = (decimal)bb.LowerBand!.Value;
                    if (bb.PercentB.HasValue)
                        indicators["BB_PercentB"] = (decimal)bb.PercentB.Value;
                }
            }

            // Calculate ATR
            if (_config.EnabledIndicators.Contains(IndicatorType.ATR) && quotesWithCurrent.Count >= _config.AtrPeriod + 1)
            {
                var atr = quotesWithCurrent.GetAtr(_config.AtrPeriod).LastOrDefault();
                if (atr?.Atr.HasValue == true)
                    indicators[$"ATR_{_config.AtrPeriod}"] = (decimal)atr.Atr.Value;
            }

            // Add price info
            indicators["LastPrice"] = _lastPrice;
            indicators["DayHigh"] = _dayHigh;
            indicators["DayLow"] = _dayLow;
            indicators["DayVolume"] = _dayVolume;

            return new IndicatorSnapshot(_symbol, DateTimeOffset.UtcNow, indicators);
        }
        catch
        {
            // Gracefully handle indicator calculation errors
            return null;
        }
    }
}

/// <summary>
/// Configuration for which indicators to calculate and their parameters.
/// </summary>
public sealed class IndicatorConfiguration
{
    public HashSet<IndicatorType> EnabledIndicators { get; init; } = new()
    {
        IndicatorType.SMA,
        IndicatorType.EMA,
        IndicatorType.RSI,
        IndicatorType.MACD,
        IndicatorType.BollingerBands,
        IndicatorType.ATR,
        IndicatorType.VWAP
    };

    // SMA/EMA periods
    public int[] SmaPeriods { get; init; } = { 10, 20, 50, 200 };
    public int[] EmaPeriods { get; init; } = { 9, 12, 26 };

    // RSI
    public int RsiPeriod { get; init; } = 14;

    // MACD
    public int MacdFastPeriod { get; init; } = 12;
    public int MacdSlowPeriod { get; init; } = 26;
    public int MacdSignalPeriod { get; init; } = 9;

    // Bollinger Bands
    public int BollingerPeriod { get; init; } = 20;
    public double BollingerStdDev { get; init; } = 2.0;

    // ATR
    public int AtrPeriod { get; init; } = 14;

    // Stochastic
    public int StochKPeriod { get; init; } = 14;
    public int StochDPeriod { get; init; } = 3;
    public int StochSmoothPeriod { get; init; } = 3;

    // ADX
    public int AdxPeriod { get; init; } = 14;

    // History limits
    public int MaxQuotesHistory { get; init; } = 500;

    public static IndicatorConfiguration Default => new();
}

/// <summary>
/// Types of technical indicators supported.
/// </summary>
public enum IndicatorType : byte
{
    SMA,
    EMA,
    RSI,
    MACD,
    BollingerBands,
    ATR,
    Stochastic,
    VWAP,
    ADX,
    OBV,
    CCI,
    Williams,
    Ichimoku,
    Keltner,
    Donchian,
    ParabolicSar,
    SuperTrend
}

/// <summary>
/// A snapshot of all indicator values for a symbol at a point in time.
/// </summary>
public sealed record IndicatorSnapshot(
    string Symbol,
    DateTimeOffset Timestamp,
    IReadOnlyDictionary<string, decimal> Indicators);

/// <summary>
/// A single indicator data point for historical analysis.
/// </summary>
public sealed record IndicatorDataPoint(
    DateTime Timestamp,
    string IndicatorName,
    decimal Value);

/// <summary>
/// Result of historical indicator calculation.
/// </summary>
public sealed record HistoricalIndicatorResult(
    string Symbol,
    IReadOnlyList<IndicatorDataPoint> DataPoints);
