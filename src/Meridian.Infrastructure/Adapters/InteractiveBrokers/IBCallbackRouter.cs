using System.Collections.Concurrent;
using Meridian.Contracts.Domain.Enums;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Collectors;
using Meridian.Domain.Models;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Thin adapter that turns raw IB callbacks into domain updates.
/// This file is written to compile without the IBApi package; wire the real EWrapper implementation
/// behind conditional compilation if/when you add the official IB API reference.
/// </summary>
public sealed class IBCallbackRouter
{
    private readonly MarketDepthCollector _depthCollector;
    private readonly TradeDataCollector _tradeCollector;
    private readonly QuoteCollector? _quoteCollector;
    private readonly OptionDataCollector? _optionCollector;

    // requestId/tickerId -> symbol maps
    private readonly ConcurrentDictionary<int, string> _depthTickerMap = new();
    private readonly ConcurrentDictionary<int, string> _tradeTickerMap = new();
    private readonly ConcurrentDictionary<int, string> _quoteTickerMap = new();
    private readonly ConcurrentDictionary<int, OptionContractSpec> _optionTickerMap = new();

    // Per-ticker state for building quotes from separate price/size callbacks
    private readonly ConcurrentDictionary<int, QuoteState> _quoteStates = new();

    public IBCallbackRouter(MarketDepthCollector depthCollector, TradeDataCollector tradeCollector, QuoteCollector? quoteCollector = null, OptionDataCollector? optionCollector = null)
    {
        _depthCollector = depthCollector ?? throw new ArgumentNullException(nameof(depthCollector));
        _tradeCollector = tradeCollector ?? throw new ArgumentNullException(nameof(tradeCollector));
        _quoteCollector = quoteCollector;
        _optionCollector = optionCollector;
    }

    public void RegisterDepthTicker(int tickerId, string symbol) => _depthTickerMap[tickerId] = symbol;
    public void RegisterTradeTicker(int tickerId, string symbol) => _tradeTickerMap[tickerId] = symbol;
    public void RegisterQuoteTicker(int tickerId, string symbol)
    {
        _quoteTickerMap[tickerId] = symbol;
        _quoteStates[tickerId] = new QuoteState();
    }

    /// <summary>
    /// Registers an option contract ticker so that tickOptionComputation callbacks are routed
    /// to the <see cref="OptionDataCollector"/>.
    /// </summary>
    public void RegisterOptionTicker(int tickerId, OptionContractSpec contract) =>
        _optionTickerMap[tickerId] = contract;

    // ---------------------------
    // Depth callbacks (IB shape)
    // ---------------------------

    public void UpdateMktDepth(int tickerId, int position, int operation, int side, double price, double size)
    {
        if (!_depthTickerMap.TryGetValue(tickerId, out var symbol))
            return;

        var upd = new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Position: (ushort)position,
            Operation: (DepthOperation)operation,
            Side: side == 0 ? OrderBookSide.Bid : OrderBookSide.Ask,
            Price: (decimal)price,
            Size: (decimal)size,
            MarketMaker: null,
            SequenceNumber: 0,
            StreamId: "IB",
            Venue: null
        );

        _depthCollector.OnDepth(upd);
    }

    public void UpdateMktDepthL2(int tickerId, int position, string marketMaker, int operation, int side, double price, double size, bool isSmartDepth)
    {
        if (!_depthTickerMap.TryGetValue(tickerId, out var symbol))
            return;

        var upd = new MarketDepthUpdate(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: symbol,
            Position: (ushort)position,
            Operation: (DepthOperation)operation,
            Side: side == 0 ? OrderBookSide.Bid : OrderBookSide.Ask,
            Price: (decimal)price,
            Size: (decimal)size,
            MarketMaker: marketMaker,
            SequenceNumber: 0,
            StreamId: isSmartDepth ? "IB-SMARTDEPTH" : "IB-L2",
            Venue: marketMaker
        );

        _depthCollector.OnDepth(upd);
    }


    // ---------------------------
    // Tick-by-tick trades
    // ---------------------------
    public void OnTickByTickAllLast(int reqId, int tickType, long time, double price, double size, string exchange, string specialConditions)
    {
        if (!_tradeTickerMap.TryGetValue(reqId, out var symbol))
            return;

        // IB provides epoch seconds in 'time'
        var ts = DateTimeOffset.FromUnixTimeSeconds(time);

        // Aggressor inference from tickType isn't perfect; keep Unknown if not sure.
        var aggressor = AggressorSide.Unknown;

        var trade = new MarketTradeUpdate(
            Timestamp: ts,
            Symbol: symbol,
            Price: (decimal)price,
            Size: (long)Math.Round(size),
            Aggressor: aggressor,
            SequenceNumber: 0,
            StreamId: "IB-TBT",
            Venue: exchange
        );

        _tradeCollector.OnTrade(trade);
    }

    // ---------------------------
    // Level 1 tick callbacks
    // ---------------------------

    /// <summary>
    /// Handle tick price updates from reqMktData.
    /// </summary>
    public void OnTickPrice(int tickerId, int field, double price, bool canAutoExecute, bool pastLimit)
    {
        if (!_quoteTickerMap.TryGetValue(tickerId, out var symbol))
            return;
        if (!_quoteStates.TryGetValue(tickerId, out var state))
            return;

        var now = DateTimeOffset.UtcNow;

        switch (field)
        {
            case IBTickTypes.BidPrice:
                state.BidPrice = (decimal)price;
                state.BidTimestamp = now;
                TryEmitQuote(tickerId, symbol, state);
                break;
            case IBTickTypes.AskPrice:
                state.AskPrice = (decimal)price;
                state.AskTimestamp = now;
                TryEmitQuote(tickerId, symbol, state);
                break;
            case IBTickTypes.LastPrice:
                state.LastPrice = (decimal)price;
                state.LastTimestamp = now;
                break;
            case IBTickTypes.High:
                state.HighPrice = (decimal)price;
                break;
            case IBTickTypes.Low:
                state.LowPrice = (decimal)price;
                break;
            case IBTickTypes.ClosePrice:
                state.ClosePrice = (decimal)price;
                break;
            case IBTickTypes.Open:
                state.OpenPrice = (decimal)price;
                break;
        }
    }

    /// <summary>
    /// Handle tick size updates from reqMktData.
    /// </summary>
    public void OnTickSize(int tickerId, int field, long size)
    {
        if (!_quoteTickerMap.TryGetValue(tickerId, out var symbol))
            return;
        if (!_quoteStates.TryGetValue(tickerId, out var state))
            return;

        switch (field)
        {
            case IBTickTypes.BidSize:
                state.BidSize = size;
                TryEmitQuote(tickerId, symbol, state);
                break;
            case IBTickTypes.AskSize:
                state.AskSize = size;
                TryEmitQuote(tickerId, symbol, state);
                break;
            case IBTickTypes.LastSize:
                state.LastSize = size;
                break;
            case IBTickTypes.Volume:
                state.Volume = size;
                break;
        }
    }

    /// <summary>
    /// Handle tick string updates (e.g., last timestamp, RT Volume).
    /// </summary>
    public void OnTickString(int tickerId, int field, string value)
    {
        if (!_quoteTickerMap.TryGetValue(tickerId, out var symbol))
            return;
        if (!_quoteStates.TryGetValue(tickerId, out var state))
            return;

        switch (field)
        {
            case IBTickTypes.LastTimestamp:
                // IB sends epoch seconds as string
                if (long.TryParse(value, out var epochSeconds))
                {
                    state.LastTimestamp = DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
                }
                break;
            case IBTickTypes.RTVolume:
                // RT Volume format: price;size;time;total;vwap;single
                // Example: "100.50;200;1704067200000;50000;100.45;true"
                ParseRTVolume(tickerId, symbol, value);
                break;
        }
    }

    /// <summary>
    /// Handle generic tick updates (e.g., halted status, volatility).
    /// </summary>
    public void OnTickGeneric(int tickerId, int field, double value)
    {
        if (!_quoteTickerMap.TryGetValue(tickerId, out var symbol))
            return;
        if (!_quoteStates.TryGetValue(tickerId, out var state))
            return;

        switch (field)
        {
            case IBTickTypes.Halted:
                state.IsHalted = value != 0;
                break;
            case IBTickTypes.Shortable:
                state.ShortableIndicator = value;
                break;
            case IBTickTypes.HistoricalVolatility:
                state.HistoricalVolatility = value;
                break;
            case IBTickTypes.ImpliedVolatility:
                state.ImpliedVolatility = value;
                break;
        }
    }

    /// <summary>
    /// Handle snapshot end callback.
    /// </summary>
    public void OnTickSnapshotEnd(int tickerId)
    {
        // Snapshot complete - could emit final quote state here
        if (!_quoteTickerMap.TryGetValue(tickerId, out var symbol))
            return;
        if (!_quoteStates.TryGetValue(tickerId, out var state))
            return;

        // Force emit whatever state we have
        if (state.BidPrice.HasValue || state.AskPrice.HasValue)
        {
            EmitQuote(symbol, state);
        }
    }

    // ---------------------------
    // Options greeks callback
    // ---------------------------

    /// <summary>
    /// Handles an IB tickOptionComputation callback (tickType 10=Bid, 11=Ask, 12=Last, 13=Model).
    /// Only model ticks (tickType == 13) are used to populate greeks — bid/ask/last option computation
    /// ticks use the same IB callback but carry less reliable IV/greeks values.
    /// </summary>
    /// <param name="tickerId">The IB request id that identifies the option contract.</param>
    /// <param name="tickType">IB tick type: 10=BidOption, 11=AskOption, 12=LastOption, 13=ModelOption.</param>
    /// <param name="impliedVol">Implied volatility (IB transmits -1 when not available).</param>
    /// <param name="delta">Option delta (-1 to +1; IB transmits -2 when not available).</param>
    /// <param name="optPrice">Option price; IB transmits -1 when not available.</param>
    /// <param name="pvDividend">Present value of dividends expected before expiry.</param>
    /// <param name="gamma">Option gamma; IB transmits -2 when not available.</param>
    /// <param name="vega">Option vega; IB transmits -2 when not available.</param>
    /// <param name="theta">Option theta; IB transmits -2 when not available.</param>
    /// <param name="undPrice">Underlying price at time of computation; IB transmits -1 when not available.</param>
    public void OnTickOptionComputation(
        int tickerId,
        int tickType,
        double impliedVol,
        double delta,
        double optPrice,
        double pvDividend,
        double gamma,
        double vega,
        double theta,
        double undPrice)
    {
        // Only process the "model" tick (13) — it carries the authoritative IV and full greeks.
        // Other tickType values (10=BidOption, 11=AskOption, 12=LastOption) are informational.
        if (tickType != 13)
            return;

        if (_optionCollector is null)
            return;

        if (!_optionTickerMap.TryGetValue(tickerId, out var contract))
            return;

        // IB uses sentinel values of -1/-2 for "not available" — skip the update if
        // the two most critical values are missing.
        if (impliedVol < 0 || undPrice <= 0)
            return;

        var greeks = new GreeksSnapshot(
            Timestamp: DateTimeOffset.UtcNow,
            Symbol: contract.UnderlyingSymbol,
            Contract: contract,
            Delta: delta < -1.5 ? 0m : (decimal)delta,
            Gamma: gamma < -1.5 ? 0m : (decimal)gamma,
            Theta: theta < -1.5 ? 0m : (decimal)theta,
            Vega: vega < -1.5 ? 0m : (decimal)vega,
            Rho: 0m,
            ImpliedVolatility: (decimal)impliedVol,
            UnderlyingPrice: (decimal)undPrice,
            TheoreticalPrice: optPrice > 0 ? (decimal?)optPrice : null);

        _optionCollector.OnGreeksUpdate(greeks);
    }

    private void TryEmitQuote(int tickerId, string symbol, QuoteState state)
    {
        // Only emit when we have both bid and ask with recent timestamps
        if (!state.BidPrice.HasValue || !state.AskPrice.HasValue)
            return;
        if (!state.BidTimestamp.HasValue || !state.AskTimestamp.HasValue)
            return;

        // Emit quote
        EmitQuote(symbol, state);
    }

    private void EmitQuote(string symbol, QuoteState state)
    {
        if (_quoteCollector == null)
            return;

        var quote = new MarketQuoteUpdate(
            Timestamp: state.BidTimestamp ?? state.AskTimestamp ?? DateTimeOffset.UtcNow,
            Symbol: symbol,
            BidPrice: state.BidPrice ?? 0,
            BidSize: state.BidSize ?? 0,
            AskPrice: state.AskPrice ?? 0,
            AskSize: state.AskSize ?? 0,
            SequenceNumber: 0,
            StreamId: "IB-L1",
            Venue: null
        );

        _quoteCollector.OnQuote(quote);
    }

    /// <summary>
    /// Parse RT Volume tick string and emit as trade.
    /// Format: price;size;time;total;vwap;single
    /// </summary>
    private void ParseRTVolume(int tickerId, string symbol, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var parts = value.Split(';');
        if (parts.Length < 6)
            return;

        if (!double.TryParse(parts[0], out var price))
            return;
        if (!double.TryParse(parts[1], out var size))
            return;
        if (!long.TryParse(parts[2], out var timeMs))
            return;

        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(timeMs);

        // RT Volume represents a trade
        var trade = new MarketTradeUpdate(
            Timestamp: timestamp,
            Symbol: symbol,
            Price: (decimal)price,
            Size: (long)Math.Round(size),
            Aggressor: AggressorSide.Unknown,
            SequenceNumber: 0,
            StreamId: "IB-RTV",
            Venue: null
        );

        _tradeCollector.OnTrade(trade);
    }

    /// <summary>
    /// Internal state for building quotes from separate tick callbacks.
    /// </summary>
    private sealed class QuoteState
    {
        public decimal? BidPrice { get; set; }
        public long? BidSize { get; set; }
        public DateTimeOffset? BidTimestamp { get; set; }

        public decimal? AskPrice { get; set; }
        public long? AskSize { get; set; }
        public DateTimeOffset? AskTimestamp { get; set; }

        public decimal? LastPrice { get; set; }
        public long? LastSize { get; set; }
        public DateTimeOffset? LastTimestamp { get; set; }

        public decimal? HighPrice { get; set; }
        public decimal? LowPrice { get; set; }
        public decimal? OpenPrice { get; set; }
        public decimal? ClosePrice { get; set; }
        public long? Volume { get; set; }

        public bool IsHalted { get; set; }
        public double? ShortableIndicator { get; set; }
        public double? HistoricalVolatility { get; set; }
        public double? ImpliedVolatility { get; set; }
    }
}
