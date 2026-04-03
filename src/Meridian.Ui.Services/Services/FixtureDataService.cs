using Meridian.Contracts.Api;

namespace Meridian.Ui.Services.Services;

/// <summary>
/// Provides canned fixture data for UI development without backend dependency.
/// Enables offline development and deterministic testing scenarios.
/// Supports runtime scenario switching via <see cref="SetScenario"/> so developers can
/// cycle through all visual states in a single session without restarting.
/// </summary>
public sealed class FixtureDataService
{
    private static readonly Lazy<FixtureDataService> _instance = new(() => new());
    public static FixtureDataService Instance => _instance.Value;

    private static readonly FixtureScenario[] _allScenarios =
        (FixtureScenario[])Enum.GetValues(typeof(FixtureScenario));

    private readonly object _scenarioLock = new();
    private FixtureScenario _activeScenario = FixtureScenario.Connected;

    private FixtureDataService() { }

    /// <summary>
    /// Gets the currently active fixture scenario.
    /// </summary>
    public FixtureScenario ActiveScenario
    {
        get { lock (_scenarioLock) { return _activeScenario; } }
    }

    /// <summary>
    /// Event raised whenever the active scenario is changed via <see cref="SetScenario"/>.
    /// Subscribers (e.g., ViewModels) should refresh their displayed data when this fires.
    /// </summary>
    public event EventHandler<FixtureScenario>? ScenarioChanged;

    /// <summary>
    /// Switches to the specified fixture scenario and raises <see cref="ScenarioChanged"/>.
    /// </summary>
    public void SetScenario(FixtureScenario scenario)
    {
        EventHandler<FixtureScenario>? handler;
        lock (_scenarioLock)
        {
            if (_activeScenario == scenario)
            {
                return;
            }

            _activeScenario = scenario;
            handler = ScenarioChanged;
        }

        handler?.Invoke(this, scenario);
    }

    /// <summary>
    /// Advances to the next scenario in the cycle and returns the new active scenario.
    /// Order: Connected → Disconnected → Degraded → Error → Loading → Connected → …
    /// </summary>
    public FixtureScenario CycleToNextScenario()
    {
        FixtureScenario next;
        lock (_scenarioLock)
        {
            var nextIndex = ((int)_activeScenario + 1) % _allScenarios.Length;
            next = _allScenarios[nextIndex];
        }

        SetScenario(next);
        return next;
    }

    /// <summary>
    /// Returns a human-readable label for the given scenario suitable for UI display.
    /// </summary>
    public static string GetScenarioLabel(FixtureScenario scenario) => scenario switch
    {
        FixtureScenario.Connected => "Connected (healthy)",
        FixtureScenario.Disconnected => "Disconnected",
        FixtureScenario.Degraded => "Degraded (partial)",
        FixtureScenario.Error => "Error state",
        FixtureScenario.Loading => "Loading / init",
        _ => scenario.ToString(),
    };

    /// <summary>
    /// Returns a status response appropriate for the currently active scenario.
    /// </summary>
    public StatusResponse GetStatusForActiveScenario() => _activeScenario switch
    {
        FixtureScenario.Connected => GetMockStatusResponse(),
        FixtureScenario.Disconnected => GetMockDisconnectedStatus(),
        FixtureScenario.Degraded => GetMockDegradedStatus(),
        FixtureScenario.Error => GetMockErrorStatus(),
        FixtureScenario.Loading => GetMockLoadingStatus(),
        _ => GetMockStatusResponse(),
    };

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
    /// Gets a mock status response showing a degraded system (partial provider connectivity).
    /// </summary>
    public StatusResponse GetMockDegradedStatus() => new()
    {
        IsConnected = true,
        TimestampUtc = DateTimeOffset.UtcNow,
        Uptime = TimeSpan.FromMinutes(18),
        Metrics = new MetricsData
        {
            Published = 8200,
            Dropped = 420,
            Integrity = 15,
            HistoricalBars = 4500,
            EventsPerSecond = 85.0f,
            DropRate = 0.049f,
            Trades = 3100,
            DepthUpdates = 2900,
            Quotes = 2200
        },
        Pipeline = new PipelineData
        {
            PublishedCount = 8200,
            DroppedCount = 420,
            ConsumedCount = 7780,
            CurrentQueueSize = 1850,
            PeakQueueSize = 4096,
            QueueCapacity = 10000
        }
    };

    /// <summary>
    /// Gets a mock status response showing a system in an error state.
    /// Useful for testing alert banners, error UI, and recovery flows.
    /// </summary>
    public StatusResponse GetMockErrorStatus() => new()
    {
        IsConnected = false,
        TimestampUtc = DateTimeOffset.UtcNow,
        Uptime = TimeSpan.FromSeconds(45),
        Metrics = new MetricsData
        {
            Published = 320,
            Dropped = 280,
            Integrity = 95,
            HistoricalBars = 0,
            EventsPerSecond = 2.1f,
            DropRate = 0.875f,
            Trades = 90,
            DepthUpdates = 50,
            Quotes = 180
        },
        Pipeline = new PipelineData
        {
            PublishedCount = 320,
            DroppedCount = 280,
            ConsumedCount = 40,
            CurrentQueueSize = 9800,
            PeakQueueSize = 10000,
            QueueCapacity = 10000
        }
    };

    /// <summary>
    /// Gets a mock status response showing a system that is still initializing.
    /// Useful for testing skeleton/loading states and progress indicators.
    /// </summary>
    public StatusResponse GetMockLoadingStatus() => new()
    {
        IsConnected = false,
        TimestampUtc = DateTimeOffset.UtcNow,
        Uptime = TimeSpan.Zero,
        Metrics = null,
        Pipeline = null
    };

    /// <summary>
    /// Simulates network delay for realistic fixture behavior.
    /// </summary>
    public async Task SimulateNetworkDelayAsync(CancellationToken ct = default)
    {
        await Task.Delay(Random.Shared.Next(50, 150));
    }
}
