using System.Text.Json;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Events;
using Meridian.Domain.Models;
using Serilog;

namespace Meridian.Application.Services;

/// <summary>
/// Service for generating sample market data for testing and development.
/// Implements QW-17: Sample Data Generator.
/// </summary>
public sealed class SampleDataGenerator
{
    private readonly ILogger _log = LoggingSetup.ForContext<SampleDataGenerator>();
    private readonly Random _random;

    // Realistic market parameters
    private static readonly string[] DefaultSymbols = { "SPY", "AAPL", "MSFT", "GOOGL", "AMZN", "META", "NVDA", "TSLA" };
    private static readonly string[] Venues = { "NYSE", "NASDAQ", "ARCA", "BATS", "IEX", "EDGX" };
    private static readonly string[] MarketMakers = { "GSCO", "MLCO", "MSCO", "JPMS", "CITI", "RBCM", "UBSS" };

    public SampleDataGenerator(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    /// <summary>
    /// Generates sample market data based on the provided options.
    /// </summary>
    public SampleDataResult Generate(SampleDataOptions options)
    {
        var result = new SampleDataResult
        {
            StartTime = DateTimeOffset.UtcNow
        };

        var events = new List<MarketEvent>();
        var symbols = options.Symbols?.Length > 0 ? options.Symbols : DefaultSymbols;
        var symbolPrices = InitializeSymbolPrices(symbols);

        var currentTime = options.StartTime ?? DateTimeOffset.UtcNow.AddHours(-1);
        var endTime = currentTime.AddMinutes(options.DurationMinutes);

        while (currentTime < endTime && events.Count < options.MaxEvents)
        {
            foreach (var symbol in symbols)
            {
                if (events.Count >= options.MaxEvents)
                    break;

                var basePrice = symbolPrices[symbol];

                // Generate trades
                if (options.IncludeTrades)
                {
                    var tradeCount = _random.Next(1, 4);
                    for (var i = 0; i < tradeCount && events.Count < options.MaxEvents; i++)
                    {
                        var trade = GenerateTrade(symbol, currentTime, ref basePrice);
                        events.Add(trade);
                        result.TradeCount++;
                    }
                }

                // Generate quotes
                if (options.IncludeQuotes)
                {
                    var quote = GenerateQuote(symbol, currentTime, basePrice);
                    events.Add(quote);
                    result.QuoteCount++;
                }

                // Generate depth updates
                if (options.IncludeDepth)
                {
                    var depthCount = _random.Next(2, 6);
                    for (var i = 0; i < depthCount && events.Count < options.MaxEvents; i++)
                    {
                        var depth = GenerateDepthUpdate(symbol, currentTime, basePrice, (ushort)i);
                        events.Add(depth);
                        result.DepthUpdateCount++;
                    }
                }

                // Generate bars (one per minute)
                if (options.IncludeBars && currentTime.Second == 0)
                {
                    var bar = GenerateBar(symbol, currentTime, ref basePrice);
                    events.Add(bar);
                    result.BarCount++;
                }

                symbolPrices[symbol] = basePrice;
            }

            currentTime = currentTime.AddMilliseconds(_random.Next(50, 500));
        }

        result.Events = events;
        result.TotalEvents = events.Count;
        result.EndTime = DateTimeOffset.UtcNow;
        result.Success = true;
        result.Message = $"Generated {events.Count} sample events for {symbols.Length} symbols";

        return result;
    }

    /// <summary>
    /// Generates sample data and writes it to JSONL files.
    /// </summary>
    public async Task<SampleDataResult> GenerateToFileAsync(
        SampleDataOptions options,
        string outputPath,
        CancellationToken ct = default)
    {
        var result = Generate(options);

        if (!result.Success || result.Events == null)
            return result;

        var groupedByType = result.Events.GroupBy(e => e.Type);
        var filesWritten = new List<string>();

        foreach (var group in groupedByType)
        {
            ct.ThrowIfCancellationRequested();

            var fileName = $"sample_{group.Key.ToString().ToLowerInvariant()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.jsonl";
            var filePath = Path.Combine(outputPath, fileName);

            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            await using var writer = new StreamWriter(filePath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            foreach (var evt in group)
            {
                var json = JsonSerializer.Serialize(evt, jsonOptions);
                await writer.WriteLineAsync(json);
            }

            filesWritten.Add(filePath);
        }

        result.FilesWritten = filesWritten;
        result.Message = $"Generated {result.TotalEvents} events to {filesWritten.Count} files";

        return result;
    }

    /// <summary>
    /// Generates a preview of sample data (limited events).
    /// </summary>
    public SampleDataPreview GeneratePreview(SampleDataOptions options)
    {
        var limitedOptions = options with { MaxEvents = Math.Min(options.MaxEvents, 20) };
        var result = Generate(limitedOptions);

        return new SampleDataPreview
        {
            Events = result.Events?.Take(10).ToList() ?? new List<MarketEvent>(),
            EstimatedTotalEvents = EstimateEventCount(options),
            SampleSymbols = options.Symbols?.Length > 0 ? options.Symbols : DefaultSymbols
        };
    }

    private Dictionary<string, decimal> InitializeSymbolPrices(string[] symbols)
    {
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        var basePrices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            ["SPY"] = 450m,
            ["AAPL"] = 175m,
            ["MSFT"] = 380m,
            ["GOOGL"] = 140m,
            ["AMZN"] = 180m,
            ["META"] = 350m,
            ["NVDA"] = 480m,
            ["TSLA"] = 250m
        };

        foreach (var symbol in symbols)
        {
            prices[symbol] = basePrices.TryGetValue(symbol, out var price)
                ? price
                : (decimal)(_random.NextDouble() * 200 + 50);
        }

        return prices;
    }

    private MarketEvent GenerateTrade(string symbol, DateTimeOffset timestamp, ref decimal basePrice)
    {
        // Random walk price movement
        var priceChange = (decimal)((_random.NextDouble() - 0.5) * 0.02) * basePrice;
        basePrice = Math.Max(0.01m, basePrice + priceChange);

        var size = _random.Next(1, 100) * 100;
        var aggressor = _random.NextDouble() > 0.5 ? AggressorSide.Buy : AggressorSide.Sell;

        var trade = new Trade(
            Timestamp: timestamp,
            Symbol: symbol,
            Price: Math.Round(basePrice, 2),
            Size: size,
            Aggressor: aggressor,
            SequenceNumber: _random.Next(1, 1000000),
            StreamId: "SAMPLE",
            Venue: Venues[_random.Next(Venues.Length)]
        );

        return MarketEvent.Trade(timestamp, symbol, trade, source: "SAMPLE");
    }

    private MarketEvent GenerateQuote(string symbol, DateTimeOffset timestamp, decimal basePrice)
    {
        var spread = Math.Max(0.01m, basePrice * 0.0001m * _random.Next(1, 10));
        var bidPrice = Math.Round(basePrice - spread / 2, 2);
        var askPrice = Math.Round(basePrice + spread / 2, 2);
        var bidSize = _random.Next(1, 50) * 100;
        var askSize = _random.Next(1, 50) * 100;

        var quote = new BboQuotePayload(
            Timestamp: timestamp,
            Symbol: symbol,
            BidPrice: bidPrice,
            BidSize: bidSize,
            AskPrice: askPrice,
            AskSize: askSize,
            MidPrice: Math.Round((bidPrice + askPrice) / 2, 4),
            Spread: askPrice - bidPrice,
            SequenceNumber: _random.Next(1, 1000000)
        );

        return MarketEvent.BboQuote(timestamp, symbol, quote, source: "SAMPLE");
    }

    private MarketEvent GenerateDepthUpdate(string symbol, DateTimeOffset timestamp, decimal basePrice, ushort level)
    {
        var side = _random.NextDouble() > 0.5 ? OrderBookSide.Bid : OrderBookSide.Ask;
        var priceOffset = level * 0.01m * (side == OrderBookSide.Bid ? -1 : 1);
        var price = Math.Round(basePrice + priceOffset, 2);
        var size = _random.Next(100, 10000);

        var update = new MarketDepthUpdate(
            Timestamp: timestamp,
            Symbol: symbol,
            Position: level,
            Operation: DepthOperation.Update,
            Side: side,
            Price: price,
            Size: size,
            MarketMaker: MarketMakers[_random.Next(MarketMakers.Length)]
        );

        // Build a minimal LOBSnapshot so the event carries a non-null payload.
        // MarketDepthUpdate (an incremental message) is not a MarketEventPayload, so we
        // materialise a one-level snapshot from the update for sample-data purposes only.
        var bookLevel = new OrderBookLevel(side, level, price, size);
        var bids = side == OrderBookSide.Bid ? (IReadOnlyList<OrderBookLevel>)[bookLevel] : [];
        var asks = side == OrderBookSide.Ask ? (IReadOnlyList<OrderBookLevel>)[bookLevel] : [];
        var snapshot = new LOBSnapshot(timestamp, symbol, bids, asks, SequenceNumber: 0);
        return MarketEvent.L2Snapshot(timestamp, symbol, snapshot, seq: 0, source: "SAMPLE");
    }

    private MarketEvent GenerateBar(string symbol, DateTimeOffset timestamp, ref decimal basePrice)
    {
        var open = basePrice;
        var high = basePrice * (1 + (decimal)(_random.NextDouble() * 0.005));
        var low = basePrice * (1 - (decimal)(_random.NextDouble() * 0.005));
        var close = low + (decimal)(_random.NextDouble()) * (high - low);
        var volume = _random.Next(10000, 1000000);

        basePrice = close;

        var bar = new HistoricalBar(
            Symbol: symbol,
            SessionDate: DateOnly.FromDateTime(timestamp.UtcDateTime),
            Open: Math.Round(open, 2),
            High: Math.Round(high, 2),
            Low: Math.Round(low, 2),
            Close: Math.Round(close, 2),
            Volume: volume,
            Source: "SAMPLE"
        );

        return MarketEvent.HistoricalBar(timestamp, symbol, bar, source: "SAMPLE");
    }

    private int EstimateEventCount(SampleDataOptions options)
    {
        var symbols = options.Symbols?.Length > 0 ? options.Symbols.Length : DefaultSymbols.Length;
        var eventsPerSecond = 0;

        if (options.IncludeTrades)
            eventsPerSecond += 2 * symbols;
        if (options.IncludeQuotes)
            eventsPerSecond += symbols;
        if (options.IncludeDepth)
            eventsPerSecond += 4 * symbols;
        if (options.IncludeBars)
            eventsPerSecond += symbols / 60;

        return Math.Min(options.MaxEvents, eventsPerSecond * options.DurationMinutes * 60);
    }
}

/// <summary>
/// Options for sample data generation.
/// </summary>
public sealed record SampleDataOptions(
    string[]? Symbols = null,
    DateTimeOffset? StartTime = null,
    int DurationMinutes = 60,
    int MaxEvents = 10000,
    bool IncludeTrades = true,
    bool IncludeQuotes = true,
    bool IncludeDepth = true,
    bool IncludeBars = true
);

/// <summary>
/// Result of sample data generation.
/// </summary>
public sealed class SampleDataResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset EndTime { get; set; }
    public int TotalEvents { get; set; }
    public int TradeCount { get; set; }
    public int QuoteCount { get; set; }
    public int DepthUpdateCount { get; set; }
    public int BarCount { get; set; }
    public List<MarketEvent>? Events { get; set; }
    public List<string>? FilesWritten { get; set; }
}

/// <summary>
/// Preview of sample data generation.
/// </summary>
public sealed class SampleDataPreview
{
    public List<MarketEvent> Events { get; set; } = new();
    public int EstimatedTotalEvents { get; set; }
    public string[] SampleSymbols { get; set; } = Array.Empty<string>();
}
