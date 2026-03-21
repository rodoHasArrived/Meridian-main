# CLAUDE.providers.md - Data Provider Implementation Guide

This document provides guidance for AI assistants working with data providers in Meridian. Data providers form the foundation of the data collection pillar, feeding real-time and historical market data into backtesting, paper trading, and live execution pipelines.

---

## Provider Architecture Overview

The system uses a unified abstraction layer supporting both **real-time streaming** and **historical data** providers.

```
┌──────────────────────────────────────────────────────────────┐
│                    Provider Abstraction                       │
│  IDataSource (base) → IRealtimeDataSource / IHistoricalDataSource │
└──────────────────────────────────────────────────────────────┘
           │                                    │
           ▼                                    ▼
┌─────────────────────┐              ┌─────────────────────────┐
│ Streaming Providers │              │ Historical Providers    │
│ ├─ Alpaca           │              │ ├─ Alpaca               │
│ ├─ Interactive Brkrs│              │ ├─ Yahoo Finance        │
│ ├─ StockSharp       │              │ ├─ Stooq                │
│ ├─ NYSE Direct      │              │ ├─ Tiingo               │
│ └─ Polygon          │              │ ├─ Finnhub              │
└─────────────────────┘              │ ├─ Alpha Vantage        │
                                     │ ├─ Nasdaq Data Link     │
┌─────────────────────┐              │ ├─ Polygon              │
│ Failover            │              │ └─ StockSharp           │
│ └─ FailoverAware    │              └─────────────────────────┘
│    MarketDataClient │
└─────────────────────┘
┌─────────────────────┐
│ Symbol Search       │
│ ├─ Alpaca           │
│ ├─ Polygon          │
│ ├─ Finnhub          │
│ ├─ OpenFIGI         │
│ └─ StockSharp       │
└─────────────────────┘
```

---

## Current Provider Inventory (2026-03-20)

The repository currently includes **20 concrete provider implementations** plus shared base and registry components.

### Streaming / Hybrid Providers
| Provider | Class | Path |
|----------|-------|------|
| Alpaca | `AlpacaMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs` |
| Interactive Brokers | `IBMarketDataClient` | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs` |
| Interactive Brokers (simulation) | `IBSimulationClient` | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBSimulationClient.cs` |
| NYSE | `NYSEDataSource` | `src/Meridian.Infrastructure/Adapters/NYSE/NYSEDataSource.cs` |
| Polygon | `PolygonMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonMarketDataClient.cs` |
| StockSharp | `StockSharpMarketDataClient` | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` |
| Streaming failover | `FailoverAwareMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs` |

### Historical Providers
| Provider | Class | Path |
|----------|-------|------|
| Alpaca | `AlpacaHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaHistoricalDataProvider.cs` |
| Alpha Vantage | `AlphaVantageHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` |
| Composite failover | `CompositeHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs` |
| Finnhub | `FinnhubHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Finnhub/FinnhubHistoricalDataProvider.cs` |
| Interactive Brokers | `IBHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs` |
| Nasdaq Data Link | `NasdaqDataLinkHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/NasdaqDataLink/NasdaqDataLinkHistoricalDataProvider.cs` |
| Polygon | `PolygonHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonHistoricalDataProvider.cs` |
| StockSharp | `StockSharpHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpHistoricalDataProvider.cs` |
| Stooq | `StooqHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Stooq/StooqHistoricalDataProvider.cs` |
| Tiingo | `TiingoHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Tiingo/TiingoHistoricalDataProvider.cs` |
| Twelve Data | `TwelveDataHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/TwelveData/TwelveDataHistoricalDataProvider.cs` |
| Yahoo Finance | `YahooFinanceHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/YahooFinance/YahooFinanceHistoricalDataProvider.cs` |

### Symbol Search Providers
| Provider | Class | Path |
|----------|-------|------|
| Alpaca | `AlpacaSymbolSearchProviderRefactored` | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProviderRefactored.cs` |
| Finnhub | `FinnhubSymbolSearchProviderRefactored` | `src/Meridian.Infrastructure/Adapters/Finnhub/FinnhubSymbolSearchProviderRefactored.cs` |
| OpenFIGI | `OpenFigiClient` | `src/Meridian.Infrastructure/Adapters/OpenFigi/OpenFigiClient.cs` |
| Polygon | `PolygonSymbolSearchProvider` | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonSymbolSearchProvider.cs` |
| StockSharp | `StockSharpSymbolSearchProvider` | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpSymbolSearchProvider.cs` |

## File Locations

### Core Abstractions (ProviderSdk)
| File | Purpose |
|------|---------|
| `ProviderSdk/IDataSource.cs` | Base interface for all data sources |
| `ProviderSdk/IRealtimeDataSource.cs` | Real-time streaming extension |
| `ProviderSdk/IHistoricalDataSource.cs` | Historical data retrieval |
| `ProviderSdk/IMarketDataClient.cs` | Market data client interface |
| `ProviderSdk/DataSourceAttribute.cs` | Attribute for auto-discovery |
| `ProviderSdk/DataSourceRegistry.cs` | Registry for data source discovery |
| `ProviderSdk/IProviderMetadata.cs` | Provider metadata interface |
| `ProviderSdk/IProviderModule.cs` | Provider module interface |
| `ProviderSdk/IHistoricalBarWriter.cs` | Historical bar data writer |
| `ProviderSdk/CredentialValidator.cs` | Credential validation |
| `ProviderSdk/HistoricalDataCapabilities.cs` | Historical capability flags |
| `ProviderSdk/ImplementsAdrAttribute.cs` | ADR implementation tracking |
| `ProviderSdk/ProviderHttpUtilities.cs` | HTTP utility methods |

### Infrastructure Base Classes
| File | Purpose |
|------|---------|
| `Infrastructure/DataSources/DataSourceBase.cs` | Base class for data sources |
| `Infrastructure/DataSources/DataSourceConfiguration.cs` | Data source configuration |

### Provider Core Framework
| File | Purpose |
|------|---------|
| `Infrastructure/Adapters/Core/ProviderFactory.cs` | Factory for creating providers |
| `Infrastructure/Adapters/Core/ProviderRegistry.cs` | Registry of available providers |
| `Infrastructure/Adapters/Core/ProviderServiceExtensions.cs` | DI service extensions |
| `Infrastructure/Adapters/Core/ProviderSubscriptionRanges.cs` | Subscription range management |
| `Infrastructure/Adapters/Core/ProviderTemplate.cs` | Provider template/base |

### Streaming Providers
| Provider | Location | Files |
|----------|----------|-------|
| Alpaca | `Infrastructure/Adapters/Alpaca/` | 1 |
| Interactive Brokers | `Infrastructure/Adapters/InteractiveBrokers/` | 8 |
| StockSharp | `Infrastructure/Adapters/StockSharp/` | 6 |
| NYSE | `Infrastructure/Adapters/NYSE/` | 3 |
| Polygon | `Infrastructure/Adapters/Polygon/` | 1 |
| Failover | `Infrastructure/Adapters/Failover/` | 3 |

**Interactive Brokers files:**
- `IBMarketDataClient.cs` - Main streaming client
- `IBConnectionManager.cs` - Connection lifecycle
- `IBCallbackRouter.cs` - Callback handling
- `ContractFactory.cs` - IB contract creation
- `EnhancedIBConnectionManager.cs` - Enhanced connection management
- `EnhancedIBConnectionManager.IBApi.cs` - IB API partial class
- `IBApiLimits.cs` - API rate limiting
- `IBSimulationClient.cs` - IB testing without live connection

**StockSharp files:**
- `StockSharpMarketDataClient.cs` - Main streaming client
- `StockSharpConnectorFactory.cs` - Connector creation
- `StockSharpConnectorCapabilities.cs` - Capability flags
- `StockSharpSymbolSearchProvider.cs` - Symbol search
- `Converters/MessageConverter.cs` - Message conversion
- `Converters/SecurityConverter.cs` - Security conversion

**Failover files:**
- `FailoverAwareMarketDataClient.cs` - Automatic provider failover wrapper
- `StreamingFailoverRegistry.cs` - Failover routing registry
- `StreamingFailoverService.cs` - Failover orchestration

### Historical Providers
| Provider | Location |
|----------|----------|
| Base class | `Infrastructure/Adapters/BaseHistoricalDataProvider.cs` |
| Interface | `Infrastructure/Adapters/IHistoricalDataProvider.cs` |
| Composite (failover) | `Infrastructure/Adapters/CompositeHistoricalDataProvider.cs` |
| Alpaca | `Infrastructure/Adapters/Alpaca/AlpacaHistoricalDataProvider.cs` |
| Yahoo Finance | `Infrastructure/Adapters/YahooFinance/YahooFinanceHistoricalDataProvider.cs` |
| Stooq | `Infrastructure/Adapters/Stooq/StooqHistoricalDataProvider.cs` |
| Tiingo | `Infrastructure/Adapters/Tiingo/TiingoHistoricalDataProvider.cs` |
| Finnhub | `Infrastructure/Adapters/Finnhub/FinnhubHistoricalDataProvider.cs` |
| Alpha Vantage | `Infrastructure/Adapters/AlphaVantage/AlphaVantageHistoricalDataProvider.cs` |
| Nasdaq Data Link | `Infrastructure/Adapters/NasdaqDataLink/NasdaqDataLinkHistoricalDataProvider.cs` |
| Polygon | `Infrastructure/Adapters/Polygon/PolygonHistoricalDataProvider.cs` |
| StockSharp | `Infrastructure/Adapters/StockSharp/StockSharpHistoricalDataProvider.cs` |
| Interactive Brokers | `Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs` |

### Historical Support Modules
| Module | Location | Purpose |
|--------|----------|---------|
| Gap Analysis | `Infrastructure/Adapters/GapAnalysis/` | Data gap detection and repair |
| Rate Limiting | `Infrastructure/Adapters/RateLimiting/` | Per-provider rate limit tracking |
| Queue | `Infrastructure/Adapters/Queue/` | Backfill job queue and workers |
| Symbol Resolution | `Infrastructure/Adapters/SymbolResolution/` | Cross-provider symbol resolution |
| Core | `Infrastructure/Adapters/Core/` | Response handling utilities |

**Gap Analysis files:**
- `DataGapAnalyzer.cs` - Gap analysis and reporting
- `DataGapRepair.cs` - Automatic gap detection/repair
- `DataQualityMonitor.cs` - Multi-dimensional quality scoring

**Rate Limiting files:**
- `RateLimiter.cs` - Per-provider rate limiting
- `ProviderRateLimitTracker.cs` - Rate limit tracking across providers

**Queue files:**
- `BackfillJob.cs` - Backfill job model
- `BackfillJobManager.cs` - Job lifecycle management
- `BackfillRequestQueue.cs` - Request queue implementation
- `BackfillWorkerService.cs` - Background worker service
- `PriorityBackfillQueue.cs` - Priority-based queue

**Symbol Resolution files:**
- `ISymbolResolver.cs` - Symbol resolution interface
- `OpenFigiSymbolResolver.cs` - OpenFIGI implementation

### Symbol Search Providers
| Provider | Location |
|----------|----------|
| Alpaca | `Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProviderRefactored.cs` |
| Polygon | `Infrastructure/Adapters/Polygon/PolygonSymbolSearchProvider.cs` |
| Finnhub | `Infrastructure/Adapters/Finnhub/FinnhubSymbolSearchProviderRefactored.cs` |
| OpenFIGI | `Infrastructure/Adapters/OpenFigi/OpenFigiClient.cs` |

Supporting files:
- `ISymbolSearchProvider.cs` - Base interface
- `BaseSymbolSearchProvider.cs` - Common implementation
- `SymbolSearchUtility.cs` - Search utilities

### Resilience Infrastructure
| File | Purpose |
|------|---------|
| `Infrastructure/Resilience/HttpResiliencePolicy.cs` | HTTP resilience with Polly |
| `Infrastructure/Resilience/WebSocketResiliencePolicy.cs` | WebSocket resilience |
| `Infrastructure/Resilience/WebSocketConnectionManager.cs` | WebSocket connection lifecycle |
| `Infrastructure/Resilience/WebSocketConnectionConfig.cs` | WebSocket configuration |
| `Infrastructure/Http/SharedResiliencePolicies.cs` | Shared Polly policies |
| `Infrastructure/Adapters/Core/BackfillProgressTracker.cs` | Backfill progress tracking |

---

## Key Interfaces

### IDataSource (Base Interface)

All providers implement this base interface:

```csharp
public interface IDataSource : IAsyncDisposable
{
    // Identity
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    // Classification
    DataSourceType Type { get; }        // Realtime, Historical, Hybrid
    DataSourceCategory Category { get; } // Exchange, Broker, Aggregator, Free, Premium
    int Priority { get; }                // Lower = higher priority

    // Capabilities
    DataSourceCapabilities Capabilities { get; }
    IReadOnlySet<string> SupportedMarkets { get; }
    IReadOnlySet<AssetClass> SupportedAssetClasses { get; }

    // Health & Status
    DataSourceHealth Health { get; }
    DataSourceStatus Status { get; }
    RateLimitState RateLimitState { get; }

    // Lifecycle
    Task InitializeAsync(CancellationToken ct = default);
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);
    Task<bool> TestConnectivityAsync(CancellationToken ct = default);
}
```

### DataSourceCapabilities (Bitwise Flags)

```csharp
[Flags]
public enum DataSourceCapabilities : long
{
    // Real-time (bits 0-9)
    RealtimeTrades = 1L << 0,
    RealtimeQuotes = 1L << 1,
    RealtimeDepthL1 = 1L << 2,
    RealtimeDepthL2 = 1L << 3,
    RealtimeDepthL3 = 1L << 4,

    // Historical (bits 10-19)
    HistoricalDailyBars = 1L << 10,
    HistoricalIntradayBars = 1L << 11,
    HistoricalTicks = 1L << 12,
    HistoricalAdjustedPrices = 1L << 13,
    HistoricalDividends = 1L << 14,
    HistoricalSplits = 1L << 15,

    // Operational (bits 20-29)
    SupportsBackfill = 1L << 20,
    SupportsStreaming = 1L << 21,
    SupportsWebSocket = 1L << 23,
    SupportsBatchRequests = 1L << 24,

    // Quality (bits 30-39)
    ExchangeTimestamps = 1L << 30,
    SequenceNumbers = 1L << 31,
    TradeConditions = 1L << 32,
}
```

### IHistoricalDataProvider

```csharp
public interface IHistoricalDataProvider
{
    string Name { get; }
    int Priority { get; }
    bool SupportsSymbol(string symbol);

    Task<IReadOnlyList<HistoricalBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime start,
        DateTime end,
        BarTimeframe timeframe,
        CancellationToken ct = default);

    IAsyncEnumerable<HistoricalBar> StreamHistoricalBarsAsync(
        string symbol,
        DateTime start,
        DateTime end,
        BarTimeframe timeframe,
        CancellationToken ct = default);
}
```

---

## Adding a New Streaming Provider

### Step 1: Create the Provider Class

```csharp
// Location: Infrastructure/Adapters/{ProviderName}/{ProviderName}DataSource.cs

[DataSource(
    "myprovider",
    "My Provider",
    DataSourceType.Realtime,
    DataSourceCategory.Broker,
    Priority = 50,
    Description = "My custom data provider")]
public sealed class MyProviderDataSource : IRealtimeDataSource
{
    private readonly ILogger<MyProviderDataSource> _logger;
    private readonly MyProviderOptions _options;
    private readonly Channel<MarketDataEvent> _events;

    public MyProviderDataSource(
        ILogger<MyProviderDataSource> logger,
        IOptions<MyProviderOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        // Use EventPipelinePolicy for consistent channel configuration
        _events = EventPipelinePolicy.HighThroughput.CreateChannel<MarketDataEvent>();
    }

    // Implement IDataSource properties...

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Connecting to {Provider}", DisplayName);
        // Connection logic...
    }

    public async IAsyncEnumerable<MarketDataEvent> GetEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in _events.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }

    // Implement remaining interface methods...
}
```

### Step 2: Create Options Class

```csharp
// Location: Infrastructure/Adapters/{ProviderName}/{ProviderName}Options.cs

public sealed class MyProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public string Endpoint { get; set; } = "wss://api.myprovider.com/stream";
    public int ReconnectDelayMs { get; set; } = 5000;
    public int MaxReconnectAttempts { get; set; } = 10;
}
```

### Step 3: Add Configuration Section

In `appsettings.sample.json`:

```json
{
  "MyProvider": {
    "ApiKey": "",
    "ApiSecret": "",
    "Endpoint": "wss://api.myprovider.com/stream",
    "ReconnectDelayMs": 5000
  }
}
```

### Step 4: Register in DI

```csharp
// In Program.cs or a ServiceExtensions file
services.Configure<MyProviderOptions>(
    configuration.GetSection("MyProvider"));
services.AddSingleton<IRealtimeDataSource, MyProviderDataSource>();
```

### Step 5: Add Tests

```csharp
// Location: tests/Meridian.Tests/Providers/MyProviderTests.cs

public class MyProviderDataSourceTests
{
    [Fact]
    public async Task ConnectAsync_WithValidCredentials_Succeeds()
    {
        // Arrange
        var options = Options.Create(new MyProviderOptions { ApiKey = "test" });
        var logger = NullLogger<MyProviderDataSource>.Instance;
        var sut = new MyProviderDataSource(logger, options);

        // Act
        await sut.ConnectAsync();

        // Assert
        sut.Status.Should().Be(DataSourceStatus.Connected);
    }
}
```

---

## Adding a New Historical Provider

### Step 1: Create the Provider Class

```csharp
// Location: Infrastructure/Adapters/Core/{ProviderName}HistoricalDataProvider.cs

public sealed class MyHistoricalProvider : IHistoricalDataProvider
{
    private readonly ILogger<MyHistoricalProvider> _logger;
    private readonly HttpClient _httpClient;

    public string Name => "myprovider";
    public int Priority => 50; // Lower = tried first

    public MyHistoricalProvider(
        ILogger<MyHistoricalProvider> logger,
        HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
    }

    public bool SupportsSymbol(string symbol)
    {
        // Return true if this provider can handle the symbol
        return !symbol.Contains(":");  // e.g., exclude forex pairs
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetHistoricalBarsAsync(
        string symbol,
        DateTime start,
        DateTime end,
        BarTimeframe timeframe,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Fetching {Symbol} bars from {Start} to {End}",
            symbol, start, end);

        // API call logic...
        var url = BuildUrl(symbol, start, end, timeframe);
        var response = await _httpClient.GetFromJsonAsync<ApiResponse>(url, ct);

        return response.Data
            .Select(bar => new HistoricalBar(
                Timestamp: bar.Date,
                Symbol: symbol,
                Open: bar.Open,
                High: bar.High,
                Low: bar.Low,
                Close: bar.Close,
                Volume: bar.Volume,
                Provider: Name))
            .ToList();
    }

    public async IAsyncEnumerable<HistoricalBar> StreamHistoricalBarsAsync(
        string symbol,
        DateTime start,
        DateTime end,
        BarTimeframe timeframe,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // For providers that support streaming, implement here
        // Otherwise, fetch all and yield
        var bars = await GetHistoricalBarsAsync(symbol, start, end, timeframe, ct);
        foreach (var bar in bars)
        {
            yield return bar;
        }
    }
}
```

### Step 2: Register with Composite Provider

```csharp
// In CompositeHistoricalDataProvider or DI setup
services.AddSingleton<IHistoricalDataProvider, MyHistoricalProvider>();
```

---

## Provider Priority System

Providers are tried in priority order (lower number = higher priority):

| Priority | Provider | Notes |
|----------|----------|-------|
| 5 | NYSE Direct | Exchange-direct, highest quality |
| 5 | Alpaca | High quality, unlimited free |
| 10 | Interactive Brokers | Requires subscription |
| 20 | Yahoo Finance | Free, 50K+ securities |
| 30 | Stooq | US equities EOD |
| 40 | Tiingo | Best for dividend-adjusted |
| 50 | Finnhub | Includes fundamentals |
| 60 | Alpha Vantage | Limited free tier |
| 70 | Nasdaq Data Link | Alternative datasets |
| 80 | Polygon | High-quality tick data |

---

## Circuit Breaker Pattern

All providers use circuit breakers for resilience:

```csharp
public sealed class CircuitBreaker
{
    public CircuitState State { get; }  // Closed, Open, HalfOpen

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken ct = default)
    {
        if (State == CircuitState.Open)
        {
            if (!ShouldAttemptReset())
                throw new CircuitBreakerOpenException();

            State = CircuitState.HalfOpen;
        }

        try
        {
            var result = await action(ct);
            OnSuccess();
            return result;
        }
        catch (Exception ex)
        {
            OnFailure(ex);
            throw;
        }
    }
}
```

### Circuit Breaker States

| State | Description |
|-------|-------------|
| **Closed** | Normal operation, requests flow through |
| **Open** | Circuit tripped, requests fail fast |
| **HalfOpen** | Testing if service recovered |

---

## Rate Limiting

### Per-Provider Rate Limits

```csharp
public sealed class RateLimiter
{
    private readonly int _maxRequestsPerMinute;
    private readonly int _maxRequestsPerHour;
    private readonly int _maxRequestsPerDay;

    public async Task<bool> TryAcquireAsync(CancellationToken ct = default)
    {
        // Check all rate limit windows
        if (await _minuteWindow.TryAcquireAsync(ct) &&
            await _hourWindow.TryAcquireAsync(ct) &&
            await _dayWindow.TryAcquireAsync(ct))
        {
            return true;
        }
        return false;
    }

    public TimeSpan? GetRetryAfter()
    {
        // Return time until next available request
    }
}
```

### Rate Limit Configuration

```json
{
  "Providers": {
    "AlphaVantage": {
      "MaxRequestsPerMinute": 5,
      "MaxRequestsPerDay": 500
    },
    "YahooFinance": {
      "MaxRequestsPerMinute": 100,
      "MaxRequestsPerHour": 2000
    }
  }
}
```

---

## Data Quality Monitoring

### Quality Dimensions

| Dimension | Weight | Description |
|-----------|--------|-------------|
| Completeness | 30% | No missing data points |
| Accuracy | 25% | Prices within expected range |
| Timeliness | 20% | Data delivered on time |
| Consistency | 15% | No sequence gaps |
| Validity | 10% | Schema conformance |

### Quality Grades

| Grade | Score | Action |
|-------|-------|--------|
| A+ | 95-100 | Optimal |
| A | 90-94 | Good |
| B | 80-89 | Acceptable |
| C | 70-79 | Warning |
| D | 60-69 | Alert |
| F | <60 | Switch provider |

---

## Common Patterns

### Credential Validation

```csharp
public async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
{
    if (string.IsNullOrEmpty(_options.ApiKey))
    {
        _logger.LogWarning("API key not configured for {Provider}", DisplayName);
        return false;
    }

    try
    {
        // Make a lightweight API call to validate
        await _httpClient.GetAsync("/v1/account", ct);
        return true;
    }
    catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
    {
        _logger.LogError("Invalid credentials for {Provider}", DisplayName);
        return false;
    }
}
```

### Reconnection with Exponential Backoff

```csharp
private async Task ReconnectWithBackoffAsync(CancellationToken ct)
{
    var attempt = 0;
    var maxAttempts = _options.MaxReconnectAttempts;

    while (attempt < maxAttempts)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
        _logger.LogInformation(
            "Reconnecting to {Provider} in {Delay}s (attempt {Attempt}/{Max})",
            DisplayName, delay.TotalSeconds, attempt + 1, maxAttempts);

        await Task.Delay(delay, ct);

        try
        {
            await ConnectAsync(ct);
            _logger.LogInformation("Reconnected to {Provider}", DisplayName);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reconnection attempt {Attempt} failed", attempt + 1);
            attempt++;
        }
    }

    throw new ReconnectionFailedException(DisplayName, maxAttempts);
}
```

---

## Testing Providers

### Unit Test Template

```csharp
public class MyProviderTests
{
    private readonly Mock<ILogger<MyProvider>> _logger;
    private readonly Mock<HttpMessageHandler> _httpHandler;
    private readonly MyProvider _sut;

    public MyProviderTests()
    {
        _logger = new Mock<ILogger<MyProvider>>();
        _httpHandler = new Mock<HttpMessageHandler>();
        var httpClient = new HttpClient(_httpHandler.Object);
        _sut = new MyProvider(_logger.Object, httpClient);
    }

    [Fact]
    public async Task GetHistoricalBarsAsync_ReturnsData()
    {
        // Arrange
        SetupHttpResponse(HttpStatusCode.OK, SampleBarsJson);

        // Act
        var bars = await _sut.GetHistoricalBarsAsync(
            "AAPL",
            DateTime.Today.AddDays(-7),
            DateTime.Today,
            BarTimeframe.Daily);

        // Assert
        bars.Should().NotBeEmpty();
        bars.Should().AllSatisfy(b => b.Provider.Should().Be("myprovider"));
    }
}
```

---

## Environment Variables

Provider credentials should be set via environment variables:

```bash
# Alpaca
export ALPACA__KEYID=your-key-id
export ALPACA__SECRETKEY=your-secret-key

# Interactive Brokers
export IB__HOST=127.0.0.1
export IB__PORT=7496
export IB__CLIENTID=1

# NYSE
export NYSE__APIKEY=your-api-key

# Alpha Vantage
export ALPHAVANTAGE__APIKEY=your-key

# Tiingo
export TIINGO__APIKEY=your-key
```

---

## Canonicalization Requirements for Providers

A planned [Deterministic Canonicalization](../../architecture/deterministic-canonicalization.md) stage will run between provider adapters and `EventPipeline` to normalize cross-provider differences. When implementing or modifying a provider, keep these requirements in mind:

### What providers must do

1. **Populate `ExchangeTimestamp`** — Call `StampReceiveTime(exchangeTs)` with the exchange timestamp when the provider feed includes it. Alpaca provides ISO 8601 timestamps in `t`, Polygon provides epoch-millisecond `t`, IB provides epoch-seconds for tick-by-tick and epoch-milliseconds in RTVolume.

2. **Pass raw symbol unchanged** — Do not attempt to resolve canonical symbols in the provider adapter. Use the raw symbol string from the provider feed. Canonicalization handles identity resolution.

3. **Preserve raw condition codes** — Store provider-specific condition codes in `TradeDto.Conditions` (or equivalent payload fields) without mapping. The `ConditionCodeMapper` handles translation to canonical codes.

4. **Set `Source` accurately** — The `Source` field on `MarketEvent` must match the provider identifier used in `CanonicalSymbolRegistry.ProviderMappings` (e.g., `"ALPACA"`, `"POLYGON"`, `"IB"`, `"STOCKSHARP"`).

### Provider-specific timestamp hazards

| Provider | Hazard | Mitigation |
|----------|--------|------------|
| IB | Uses **seconds** for `tickByTickAllLast` but **milliseconds** in RTVolume | Provider adapter must normalize before calling `StampReceiveTime()` |
| StockSharp | `msg.ServerTime` source varies by connector (exchange time vs. server time) | Document clock quality per connector |
| Polygon | Timestamps are Unix epoch **milliseconds** | Standard conversion |
| Alpaca | ISO 8601 strings | Standard parsing |

### Venue identifiers by provider

Each provider uses a different format for exchange/venue identifiers. The `VenueMicMapper` normalizes these to ISO 10383 MIC codes. When adding a new provider, document the venue format in the [canonicalization design](../../architecture/deterministic-canonicalization.md) and add mappings to `config/venue-mapping.json`.

See the [provider field audit](../../architecture/deterministic-canonicalization.md#provider-field-audit) for a comprehensive comparison of field formats across providers.

---

## Related Documentation

- [docs/providers/provider-comparison.md](../../providers/provider-comparison.md) - Provider feature matrix
- [docs/providers/backfill-guide.md](../../providers/backfill-guide.md) - Historical backfill guide
- [docs/providers/interactive-brokers-setup.md](../../providers/interactive-brokers-setup.md) - IB setup
- [docs/providers/alpaca-setup.md](../../providers/alpaca-setup.md) - Alpaca setup
- [docs/architecture/provider-management.md](../../architecture/provider-management.md) - Provider architecture
- [docs/architecture/deterministic-canonicalization.md](../../architecture/deterministic-canonicalization.md) - Canonicalization design

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](../README.md)
- **Root context:** [`CLAUDE.md`](../../../CLAUDE.md) § Data Providers
- **Code review (Lens 5 - Provider Compliance):** [`.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md)
- **Development guide:** [`docs/development/provider-implementation.md`](../../development/provider-implementation.md)

---

*Last Updated: 2026-03-16*
