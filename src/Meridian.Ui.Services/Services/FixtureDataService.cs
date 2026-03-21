using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Provides canned fixture data for UI development without backend dependency.
/// Enables offline development and deterministic testing scenarios.
/// </summary>
public sealed class FixtureDataService
{
    private static readonly Lazy<FixtureDataService> _instance = new(() => new());
    public static FixtureDataService Instance => _instance.Value;

    private FixtureDataService() { }

    /// <summary>
    /// Gets a mock status response showing a running system.
    /// </summary>
    public StatusResponse GetMockStatusResponse() => new()
    {
        IsConnected = true,
        TimestampUtc = DateTimeOffset.UtcNow,
        Uptime = TimeSpan.FromHours(2).Add(TimeSpan.FromMinutes(15)),
        Metrics = new MetricsData
        {
            Published = 45678,
            Dropped = 12,
            Integrity = 3,
            HistoricalBars = 125000,
            EventsPerSecond = 1250.5f,
            DropRate = 0.026f,
            Trades = 23456,
            DepthUpdates = 15678,
            Quotes = 6544
        },
        Pipeline = new PipelineData
        {
            PublishedCount = 45678,
            DroppedCount = 12,
            ConsumedCount = 45666,
            CurrentQueueSize = 145,
            PeakQueueSize = 1024,
            QueueCapacity = 10000
        }
    };

    /// <summary>
    /// Gets a mock status response showing a disconnected system.
    /// </summary>
    public StatusResponse GetMockDisconnectedStatus() => new()
    {
        IsConnected = false,
        TimestampUtc = DateTimeOffset.UtcNow,
        Uptime = TimeSpan.Zero,
        Metrics = null,
        Pipeline = null
    };

    /// <summary>
    /// Gets mock trade data for a given symbol.
    /// </summary>
    public TradeDataResponse GetMockTradeData(string symbol) => new(
        Symbol: symbol,
        Timestamp: DateTimeOffset.UtcNow,
        Price: 450.25m + (symbol.GetHashCode() % 100),
        Size: 100,
        Aggressor: "Buy",
        SequenceNumber: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        StreamId: "fixture-stream",
        Venue: "NASDAQ"
    );

    /// <summary>
    /// Gets mock quote data for a given symbol.
    /// </summary>
    public QuoteDataResponse GetMockQuoteData(string symbol)
    {
        var basePrice = 450.00m + (symbol.GetHashCode() % 100);
        var spread = 0.10m;
        
        return new(
            Symbol: symbol,
            Timestamp: DateTimeOffset.UtcNow,
            BidPrice: basePrice,
            BidSize: 500,
            AskPrice: basePrice + spread,
            AskSize: 300,
            MidPrice: basePrice + (spread / 2),
            Spread: spread,
            SequenceNumber: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            StreamId: "fixture-stream",
            Venue: "NASDAQ"
        );
    }

    /// <summary>
    /// Gets a mock backfill health response showing healthy providers.
    /// </summary>
    public BackfillHealthResponse GetMockBackfillHealth() => new()
    {
        IsHealthy = true,
        Providers = new Dictionary<string, BackfillProviderHealth>
        {
            ["Alpaca"] = new()
            {
                IsAvailable = true,
                LatencyMs = 45.2f,
                ErrorMessage = null,
                LastChecked = DateTime.UtcNow
            },
            ["Polygon"] = new()
            {
                IsAvailable = true,
                LatencyMs = 67.8f,
                ErrorMessage = null,
                LastChecked = DateTime.UtcNow
            },
            ["Tiingo"] = new()
            {
                IsAvailable = false,
                LatencyMs = null,
                ErrorMessage = "Rate limit exceeded",
                LastChecked = DateTime.UtcNow.AddMinutes(-5)
            }
        }
    };

    /// <summary>
    /// Gets a collection of mock symbols for testing.
    /// </summary>
    public string[] GetMockSymbols() => new[] 
    { 
        "SPY", "AAPL", "MSFT", "TSLA", "GOOGL", 
        "AMZN", "META", "NVDA", "AMD", "NFLX" 
    };

    /// <summary>
    /// Gets mock trades response with multiple trades.
    /// </summary>
    public TradesResponse GetMockTradesResponse(string symbol, int count = 10)
    {
        var trades = new List<TradeDataResponse>();
        var baseTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        
        for (int i = 0; i < count; i++)
        {
            trades.Add(new TradeDataResponse(
                Symbol: symbol,
                Timestamp: baseTime.AddSeconds(i * 30),
                Price: 450.00m + (i * 0.25m),
                Size: 100 + (i * 50),
                Aggressor: i % 2 == 0 ? "Buy" : "Sell",
                SequenceNumber: 1000 + i,
                StreamId: "fixture-stream",
                Venue: "NASDAQ"
            ));
        }

        return new TradesResponse(
            Symbol: symbol,
            Trades: trades,
            Count: count,
            Timestamp: DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    /// Simulates network delay for realistic fixture behavior.
    /// </summary>
    public async Task SimulateNetworkDelayAsync(CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(50, 150));
    }
}
