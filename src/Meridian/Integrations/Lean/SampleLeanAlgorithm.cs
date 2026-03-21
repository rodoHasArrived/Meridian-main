using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Indicators;

namespace Meridian.Integrations.Lean;

/// <summary>
/// Sample Lean algorithm demonstrating how to use Meridian's high-fidelity market data.
/// This algorithm uses tick-by-tick trade data and BBO quotes collected by Meridian
/// to implement a simple microstructure-aware trading strategy.
/// </summary>
public class SampleLeanAlgorithm : QCAlgorithm
{
    private Symbol _symbol = null!;
    private SimpleMovingAverage _sma = null!;
    private decimal _lastMidPrice;
    private int _tradeCount;

    /// <summary>
    /// Initialize the algorithm with custom Meridian data.
    /// </summary>
    public override void Initialize()
    {
        SetStartDate(2024, 1, 1);
        SetEndDate(2024, 1, 5);
        SetCash(100000);

        // Create symbol
        _symbol = AddEquity("SPY", Resolution.Tick).Symbol;

        // Subscribe to custom Meridian trade data
        AddData<MeridianTradeData>("SPY", Resolution.Tick);

        // Subscribe to custom Meridian BBO quote data
        AddData<MeridianQuoteData>("SPY", Resolution.Tick);

        // Initialize indicators
        _sma = SMA(_symbol, 20, Resolution.Minute);

        Log("SampleLeanAlgorithm initialized with Meridian data feeds");
    }

    /// <summary>
    /// OnData event handler - processes incoming market data.
    /// </summary>
    public override void OnData(Slice data)
    {
        // Process custom trade data
        if (data.ContainsKey(_symbol) && data[_symbol] is MeridianTradeData tradeData)
        {
            ProcessTradeData(tradeData);
        }

        // Process custom quote data
        if (data.ContainsKey(_symbol) && data[_symbol] is MeridianQuoteData quoteData)
        {
            ProcessQuoteData(quoteData);
        }

        // Simple trading logic: Buy if price is above SMA, sell if below
        if (_sma.IsReady && !Portfolio.Invested)
        {
            if (Securities[_symbol].Price > _sma.Current.Value)
            {
                SetHoldings(_symbol, 0.5);
                Log($"BUY: Price {Securities[_symbol].Price:F2} > SMA {_sma.Current.Value:F2}");
            }
        }
        else if (_sma.IsReady && Portfolio.Invested)
        {
            if (Securities[_symbol].Price < _sma.Current.Value)
            {
                Liquidate(_symbol);
                Log($"SELL: Price {Securities[_symbol].Price:F2} < SMA {_sma.Current.Value:F2}");
            }
        }
    }

    /// <summary>
    /// Process trade data from Meridian.
    /// Demonstrates access to high-fidelity trade properties like aggressor side, conditions, etc.
    /// </summary>
    private void ProcessTradeData(MeridianTradeData trade)
    {
        _tradeCount++;

        // Example: Monitor aggressive buying
        if (trade.AggressorSide == "Buy" && trade.TradeSize > 1000)
        {
            Debug($"Large aggressive buy: {trade.TradeSize} @ {trade.TradePrice:F2} on {trade.Exchange}");
        }

        // Example: Filter out odd-lot trades or trades with specific conditions
        if (trade.Conditions.Any(c => c.Contains("ODD_LOT")))
        {
            // Skip odd lot trades for certain strategies
            return;
        }

        // Log trade statistics periodically
        if (_tradeCount % 1000 == 0)
        {
            Log($"Processed {_tradeCount} trades. Latest: {trade.TradePrice:F2} x {trade.TradeSize}");
        }
    }

    /// <summary>
    /// Process BBO quote data from Meridian.
    /// Demonstrates access to best bid/offer, spread, and exchange information.
    /// </summary>
    private void ProcessQuoteData(MeridianQuoteData quote)
    {
        _lastMidPrice = quote.MidPrice;

        // Example: Monitor spread widening
        var spreadBps = (quote.Spread / quote.MidPrice) * 10000;
        if (spreadBps > 10) // Spread wider than 10 basis points
        {
            Debug($"Wide spread: {quote.BidPrice:F2} x {quote.AskPrice:F2} ({spreadBps:F2} bps)");
        }

        // Example: Monitor quote imbalance
        if (quote.BidSize > 0 && quote.AskSize > 0)
        {
            var imbalance = (quote.BidSize - quote.AskSize) / (quote.BidSize + quote.AskSize);
            if (Math.Abs(imbalance) > 0.5m) // Significant imbalance
            {
                var side = imbalance > 0 ? "bid" : "ask";
                Debug($"Quote imbalance on {side} side: {imbalance:P2}");
            }
        }
    }

    /// <summary>
    /// End of algorithm execution.
    /// </summary>
    public override void OnEndOfAlgorithm()
    {
        Log($"Algorithm completed. Total trades processed: {_tradeCount}");
        Log($"Final portfolio value: {Portfolio.TotalPortfolioValue:C}");
    }
}
