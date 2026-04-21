# Data Provider Implementation Guide

## Overview

This guide covers patterns for implementing market data providers in Meridian. The system has three provider categories, each with its own interface hierarchy:

| Category | Primary Interface | Base Class | Purpose |
| ---------- | ------------------- | ------------ | --------- |
| Streaming | `IMarketDataClient` | — | Real-time trade/quote/depth data via WebSocket or push |
| Historical (Backfill) | `IHistoricalDataProvider` | `BaseHistoricalDataProvider` | OHLCV bars, quotes, trades, auctions via REST |
| Symbol Search | `ISymbolSearchProvider` | — | Symbol lookup and autocomplete |

All three implement `IProviderMetadata` for unified discovery, routing, and UI presentation.

There is also a parallel **DataSource** hierarchy (`IDataSource` → `IRealtimeDataSource` / `IHistoricalDataSource`) with a rich `DataSourceBase` base class providing built-in health tracking, rate limiting, and retry logic. This hierarchy is the newer unified approach and sits in the `ProviderSdk` project.

---

## Provider SDK (`Meridian.ProviderSdk`)

The ProviderSdk project defines the core contracts that all providers share:

| File | Purpose |
| ------ | --------- |
| `IMarketDataClient.cs` | Streaming provider contract |
| `IDataSource.cs` | Unified base interface for all data sources |
| `IRealtimeDataSource.cs` | Real-time streaming data source |
| `IHistoricalDataSource.cs` | Historical data source (bars, dividends, splits) |
| `IProviderMetadata.cs` | Unified metadata for discovery and UI |
| `IProviderModule.cs` | DI registration module for providers |
| `DataSourceAttribute.cs` | Attribute-based provider discovery (ADR-005) |
| `DataSourceRegistry.cs` | Assembly scanning and auto-registration |
| `HistoricalDataCapabilities.cs` | Capability flags for backfill providers |
| `CredentialValidator.cs` | Shared credential validation utilities |
| `ProviderHttpUtilities.cs` | HTTP client helpers for providers |
| `ImplementsAdrAttribute.cs` | ADR traceability attribute |
| `IHistoricalBarWriter.cs` | Storage abstraction for backfill persistence |

---

## Architecture Decision Records

Provider implementations must comply with these ADRs:

| ADR | Requirement |
| ----- | ------------- |
| ADR-001 | All providers implement a standard interface (`IMarketDataClient`, `IHistoricalDataProvider`, or `ISymbolSearchProvider`) |
| ADR-004 | All async methods accept `CancellationToken` |
| ADR-005 | Use `[DataSource]` attribute for automatic discovery |
| ADR-010 | Use `IHttpClientFactory` for HTTP client lifecycle |
| ADR-013 | Use `EventPipelinePolicy` presets for bounded channels |
| ADR-014 | Use JSON source generators for high-performance serialization |

Mark your implementations with `[ImplementsAdr("ADR-XXX", "reason")]`.

---

## 1. Streaming Providers (`IMarketDataClient`)

### Interface

Location: `src/Meridian.ProviderSdk/IMarketDataClient.cs`

```csharp
[ImplementsAdr("ADR-001", "Core streaming data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IMarketDataClient : IProviderMetadata, IAsyncDisposable
{
    bool IsEnabled { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    int SubscribeMarketDepth(SymbolConfig cfg);
    void UnsubscribeMarketDepth(int subscriptionId);
    int SubscribeTrades(SymbolConfig cfg);
    void UnsubscribeTrades(int subscriptionId);
}
```

### Implementation Checklist

- [ ] Implement `IMarketDataClient` fully
- [ ] Add `[ImplementsAdr("ADR-001", "...")]` attribute
- [ ] Use `EventPipelinePolicy.HighThroughput.CreateChannel<T>()` for event buffering
- [ ] Handle reconnection with exponential backoff
- [ ] Emit `ConnectionStateChanged` events on all state transitions
- [ ] Support graceful shutdown via `CancellationToken`
- [ ] Log all connection events with structured logging
- [ ] Mark class as `sealed` unless designed for inheritance
- [ ] Override `IProviderMetadata` default implementations if needed

### Required Class Structure

```csharp
[ImplementsAdr("ADR-001", "Streaming data provider for {Provider}")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public sealed class {Provider}MarketDataClient : IMarketDataClient
{
    // 1. Dependencies via constructor injection
    private readonly {Provider}Options _options;
    private readonly ILogger<{Provider}MarketDataClient> _logger;
    private readonly TradeDataCollector _tradeCollector;
    private readonly MarketDepthCollector _depthCollector;

    // 2. Internal event channel using EventPipelinePolicy preset (ADR-013)
    private readonly Channel<MarketDataEvent> _eventChannel =
        EventPipelinePolicy.HighThroughput.CreateChannel<MarketDataEvent>();

    // 3. Connection state management
    private ConnectionState _state = ConnectionState.Disconnected;
    public ConnectionState State => _state;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    // 4. IProviderMetadata overrides (optional — defaults derive from class name)
    public string ProviderId => "myprovider";
    public string ProviderDisplayName => "My Provider Streaming";
    public string ProviderDescription => "Real-time streaming from My Provider";
    public int ProviderPriority => 50;
    public ProviderCapabilities ProviderCapabilities =>
        ProviderCapabilities.Streaming(trades: true, quotes: true, depth: true, maxDepthLevels: 10);

    // 5. Credential fields for UI form generation
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ApiKey", "MYPROVIDER__APIKEY", "API Key", Required: true),
        new ProviderCredentialField("SecretKey", "MYPROVIDER__SECRETKEY", "Secret Key", Required: true)
    };

    public {Provider}MarketDataClient(
        {Provider}Options options,
        ILogger<{Provider}MarketDataClient> logger,
        TradeDataCollector tradeCollector,
        MarketDepthCollector depthCollector)
    {
        _options = options;
        _logger = logger;
        _tradeCollector = tradeCollector;
        _depthCollector = depthCollector;
    }

    // Always update state through this method
    private void UpdateState(ConnectionState newState)
    {
        var oldState = _state;
        _state = newState;
        ConnectionStateChanged?.Invoke(this, new(oldState, newState));
    }
}
```

### Connection Pattern

```csharp
public async Task ConnectAsync(CancellationToken ct = default)
{
    UpdateState(ConnectionState.Connecting);
    try
    {
        // Provider-specific connection logic
        await EstablishConnectionAsync(ct);
        UpdateState(ConnectionState.Connected);
        _logger.LogInformation("Connected to {Provider}", "MyProvider");
    }
    catch (Exception ex)
    {
        UpdateState(ConnectionState.Error);
        _logger.LogError(ex, "Failed to connect to {Provider}", "MyProvider");
        throw;
    }
}
```

### Data Flow Pattern

`IMarketDataClient` does **not** expose a `GetEventsAsync` method. Providers push events into dependency-injected collectors:

```csharp
// In WebSocket message handler or data receive loop
private void HandleTradeMessage(JsonElement msg)
{
    var update = new MarketTradeUpdate(
        Timestamp: ParseTimestamp(msg),
        Symbol: msg.GetProperty("symbol").GetString()!,
        Price: msg.GetProperty("price").GetDecimal(),
        Size: msg.GetProperty("size").GetInt64(),
        Aggressor: ParseAggressor(msg),
        SequenceNumber: msg.GetProperty("seq").GetInt64(),
        StreamId: _streamId,
        Venue: _options.Venue);

    // Push to collector — collector handles event publishing internally
    _tradeCollector.OnTrade(update);
}

private void HandleDepthMessage(JsonElement msg)
{
    var update = new MarketDepthUpdate(
        Timestamp: ParseTimestamp(msg),
        Symbol: msg.GetProperty("symbol").GetString()!,
        Bids: ParseLevels(msg, "bids"),
        Asks: ParseLevels(msg, "asks"),
        SequenceNumber: msg.GetProperty("seq").GetInt64(),
        StreamId: _streamId);

    // Push to collector — collector handles event publishing internally
    _depthCollector.OnDepth(update);
}
```

The internal channel can still be used for buffering within the provider, but data flows outward through the collectors, not via an exposed `IAsyncEnumerable`.

### Reconnection Strategy

```csharp
public async Task ConnectWithRetryAsync(CancellationToken ct)
{
    var delay = TimeSpan.FromSeconds(1);
    const int maxRetries = 5;

    for (int attempt = 1; attempt <= maxRetries && !ct.IsCancellationRequested; attempt++)
    {
        try
        {
            await ConnectAsync(ct);
            return;
        }
        catch (Exception ex) when (attempt < maxRetries)
        {
            _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed, retry in {Delay}s",
                attempt, maxRetries, delay.TotalSeconds);
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
        }
    }
}
```

---

## 2. Historical (Backfill) Providers (`IHistoricalDataProvider`)

### Interface

Location: `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs`

```csharp
[ImplementsAdr("ADR-001", "Core historical data provider contract")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
public interface IHistoricalDataProvider : IProviderMetadata, IDisposable
{
    // Identity
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }

    // Priority and rate limiting (default implementations provided)
    int Priority => 100;
    TimeSpan RateLimitDelay => TimeSpan.Zero;
    int MaxRequestsPerWindow => int.MaxValue;
    TimeSpan RateLimitWindow => TimeSpan.FromHours(1);

    // Capabilities
    HistoricalDataCapabilities Capabilities => HistoricalDataCapabilities.None;

    // Core data methods
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default);

    // Extended methods with default no-op implementations
    Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(...);
    Task<HistoricalQuotesResult> GetHistoricalQuotesAsync(...);
    Task<HistoricalTradesResult> GetHistoricalTradesAsync(...);
    Task<HistoricalAuctionsResult> GetHistoricalAuctionsAsync(...);

    // Health check
    Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);
}
```

### Base Class: `BaseHistoricalDataProvider`

Location: `src/Meridian.Infrastructure/Adapters/Core/BaseHistoricalDataProvider.cs`

All historical providers should extend this base class. It provides:

- **Rate limiting** — automatic slot tracking, window-based rate limiting, `WaitForRateLimitSlotAsync`
- **HTTP resilience** — retry with exponential backoff, circuit breaker, timeout via Polly
- **Centralized HTTP error handling** — `ExecuteGetAndReadAsync` handles 200, 401/403, 404, 429, 5xx
- **Credential validation** — `ValidateSymbol`, `ValidateSymbols`
- **Symbol normalization** — `NormalizeSymbol` via `SymbolNormalization.Normalize`
- **JSON deserialization** — `DeserializeResponse<T>` with error handling
- **`IRateLimitAwareProvider`** — event-based rate limit notifications
- **OHLC validation** — `IsValidOhlc`, `ValidateOhlc`

### Implementation Checklist

- [ ] Extend `BaseHistoricalDataProvider`
- [ ] Add `[ImplementsAdr("ADR-001", "...")]` attribute
- [ ] Override `Name`, `DisplayName`, `Description`, `HttpClientName`
- [ ] Override `Capabilities` with the correct `HistoricalDataCapabilities` preset
- [ ] Override `Priority`, `RateLimitDelay`, `MaxRequestsPerWindow` as needed
- [ ] Implement `GetDailyBarsAsync` (required)
- [ ] Implement extended methods (`GetHistoricalQuotesAsync`, etc.) if supported
- [ ] Use `ExecuteGetAndReadAsync` for HTTP requests
- [ ] Use `NormalizeSymbol` for symbol input
- [ ] Override `IProviderMetadata` credential fields for UI

### Minimal Example

```csharp
[ImplementsAdr("ADR-001", "Historical data provider for ExampleSource")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-010", "Uses HttpClientFactory for HTTP lifecycle")]
public sealed class ExampleHistoricalDataProvider : BaseHistoricalDataProvider
{
    public override string Name => "example";
    public override string DisplayName => "Example Data";
    public override string Description => "Free daily bar data from Example API";
    protected override string HttpClientName => "Example";

    // Capabilities
    public override int Priority => 90;
    public override TimeSpan RateLimitDelay => TimeSpan.FromMilliseconds(200);
    public override int MaxRequestsPerWindow => 300;
    public override TimeSpan RateLimitWindow => TimeSpan.FromMinutes(1);

    public override HistoricalDataCapabilities Capabilities =>
        HistoricalDataCapabilities.BarsOnly;

    // Credential metadata for UI
    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("ApiKey", "EXAMPLE__APIKEY", "API Key", Required: true)
    };

    public ExampleHistoricalDataProvider(HttpClient? httpClient = null, ILogger? log = null)
        : base(httpClient, log) { }

    public override async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        ValidateSymbol(symbol);
        var normalized = NormalizeSymbol(symbol);

        var url = BuildUrl(normalized, from, to);
        var json = await ExecuteGetAndReadAsync(url, normalized, "daily-bars", ct)
            .ConfigureAwait(false);

        if (json is null) return Array.Empty<HistoricalBar>();

        var response = DeserializeResponse<ExampleApiResponse>(json, normalized);
        return ParseBars(response, normalized);
    }

    private static string BuildUrl(string symbol, DateOnly? from, DateOnly? to)
    {
        var url = $"https://api.example.com/v1/bars/{symbol}?interval=daily";
        if (from.HasValue) url += $"&from={from.Value:yyyy-MM-dd}";
        if (to.HasValue) url += $"&to={to.Value:yyyy-MM-dd}";
        return url;
    }

    private static List<HistoricalBar> ParseBars(ExampleApiResponse? response, string symbol)
    {
        if (response?.Data is null) return new();

        return response.Data
            .Where(d => IsValidOhlc(d.Open, d.High, d.Low, d.Close))
            .Select(d => new HistoricalBar(
                Symbol: symbol,
                SessionDate: d.Date,
                Open: d.Open,
                High: d.High,
                Low: d.Low,
                Close: d.Close,
                Volume: d.Volume,
                Source: "example"))
            .OrderBy(b => b.SessionDate)
            .ToList();
    }
}
```

### Capability Presets

Use `HistoricalDataCapabilities` factory methods:

```csharp
// Basic daily bar provider
public override HistoricalDataCapabilities Capabilities =>
    HistoricalDataCapabilities.BarsOnly;

// Full-featured provider (bars, intraday, quotes, trades, auctions)
public override HistoricalDataCapabilities Capabilities =>
    HistoricalDataCapabilities.FullFeatured;

// Custom capabilities
public override HistoricalDataCapabilities Capabilities => new()
{
    AdjustedPrices = true,
    Intraday = true,
    Dividends = true,
    Splits = true,
    Quotes = false,
    Trades = false,
    Auctions = false,
    SupportedMarkets = new[] { "US", "UK" }
};
```

### Registration in CompositeHistoricalDataProvider

Historical providers are routed through `CompositeHistoricalDataProvider`, which provides:

- Priority-based fallback chain
- Rate limit tracking per provider
- Provider health monitoring
- Automatic failover when a provider is unavailable

Register your provider in the DI container and the composite provider picks it up automatically.

---

## 3. Symbol Search Providers (`ISymbolSearchProvider`)

### Interface

Location: `src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs`

```csharp
public interface ISymbolSearchProvider : IProviderMetadata
{
    string Name { get; }
    string DisplayName { get; }
    int Priority { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query, int limit = 10, CancellationToken ct = default);

    Task<SymbolDetails?> GetDetailsAsync(
        string symbol, CancellationToken ct = default);
}
```

For providers supporting filters, implement `IFilterableSymbolSearchProvider`:

```csharp
public interface IFilterableSymbolSearchProvider : ISymbolSearchProvider
{
    IReadOnlyList<string> SupportedAssetTypes { get; }
    IReadOnlyList<string> SupportedExchanges { get; }

    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query, int limit = 10,
        string? assetType = null, string? exchange = null,
        CancellationToken ct = default);
}
```

---

## 4. Unified DataSource Hierarchy (`IDataSource`)

The `ProviderSdk` project defines a unified interface hierarchy for newer provider implementations:

```text
IDataSource (base)
├── IRealtimeDataSource (streaming with IObservable<T> event streams)
└── IHistoricalDataSource (bars, dividends, splits, intraday)
```

### `IDataSource`

Location: `src/Meridian.ProviderSdk/IDataSource.cs`

Provides identity, classification, capabilities, health/status, rate limit state, and lifecycle management (`InitializeAsync`, `ValidateCredentialsAsync`, `TestConnectivityAsync`).

Key types:

- `DataSourceType` — `Realtime`, `Historical`, `Hybrid`
- `DataSourceCategory` — `Exchange`, `Broker`, `Aggregator`, `Free`, `Premium`
- `DataSourceCapabilities` — Bitwise flags for fine-grained capability checking
- `DataSourceHealth` — Health score with recent errors
- `RateLimitState` — Current rate limit status

### `DataSourceBase`

Location: `src/Meridian.Infrastructure/DataSources/DataSourceBase.cs`

Abstract base class providing:

- **Health tracking** — automatic health score calculation, consecutive failure tracking, health change notifications via `IObservable<DataSourceHealthChanged>`
- **Rate limiting** — window-based rate limit enforcement, concurrent request limiting via semaphore
- **Retry logic** — exponential backoff for `HttpRequestException` and `TimeoutException`
- **Lifecycle** — `InitializeAsync` validates credentials and tests connectivity
- **Dispose pattern** — `DisposeAsync` with `OnDisposeAsync` override point

### `[DataSource]` Attribute (ADR-005)

Location: `src/Meridian.ProviderSdk/DataSourceAttribute.cs`

```csharp
[DataSource("example", "Example Markets", DataSourceType.Hybrid, DataSourceCategory.Broker, Priority = 10)]
public sealed class ExampleDataSource : DataSourceBase, IRealtimeDataSource, IHistoricalDataSource
{
    // Implementation
}
```

Parameters:

- `id` — Unique identifier (e.g., `"alpaca"`, `"yahoo"`, `"ib"`)
- `displayName` — Human-readable name
- `type` — `DataSourceType.Realtime`, `Historical`, or `Hybrid`
- `category` — `DataSourceCategory.Exchange`, `Broker`, `Aggregator`, `Free`, `Premium`
- `Priority` — Lower = higher priority (default: 100)
- `EnabledByDefault` — Whether enabled without explicit config (default: true)
- `Description` — Optional description
- `ConfigSection` — Configuration section name (defaults to id)

### `DataSourceRegistry`

Location: `src/Meridian.ProviderSdk/DataSourceRegistry.cs`

Scans assemblies for types decorated with `[DataSource]` and registers them in DI:

```csharp
var registry = new DataSourceRegistry();
registry.DiscoverFromAssemblies(typeof(AlpacaDataSource).Assembly);
registry.RegisterServices(services);
registry.RegisterModules(services, typeof(AlpacaProviderModule).Assembly);
```

### `IProviderModule`

Location: `src/Meridian.ProviderSdk/IProviderModule.cs`

For providers requiring custom DI registration:

```csharp
public sealed class ExampleProviderModule : IProviderModule
{
    public void Register(IServiceCollection services, DataSourceRegistry registry)
    {
        services.AddSingleton<ExampleOptions>(sp =>
            sp.GetRequiredService<IConfiguration>().GetSection("Example").Get<ExampleOptions>()!);
        services.AddSingleton<ExampleDataSource>();
    }
}
```

---

## 5. `IProviderMetadata` — Unified Provider Discovery

Location: `src/Meridian.ProviderSdk/IProviderMetadata.cs`

All three provider interfaces (`IMarketDataClient`, `IHistoricalDataProvider`, `ISymbolSearchProvider`) extend `IProviderMetadata`, enabling:

- Consistent discovery across all provider types
- Unified UI presentation via `ProviderTemplateFactory`
- Automatic catalog entry generation via `ProviderTemplateFactory.ToCatalogEntry()`
- Capability-based routing

```csharp
public interface IProviderMetadata
{
    string ProviderId { get; }
    string ProviderDisplayName { get; }
    string ProviderDescription { get; }
    int ProviderPriority { get; }
    ProviderCapabilities ProviderCapabilities { get; }

    // UI-specific (default implementations returning empty)
    bool RequiresCredentials => ProviderCredentialFields.Length > 0;
    ProviderCredentialField[] ProviderCredentialFields => Array.Empty<ProviderCredentialField>();
    string[] ProviderNotes => Array.Empty<string>();
    string[] ProviderWarnings => Array.Empty<string>();
    string[] SupportedDataTypes => /* derived from capabilities */;
}
```

### `ProviderCapabilities`

Unified capability record with factory methods for common configurations:

```csharp
// Streaming-only
ProviderCapabilities.Streaming(trades: true, quotes: true, depth: true, maxDepthLevels: 10)

// Backfill bars-only
ProviderCapabilities.BackfillBarsOnly

// Full-featured backfill
ProviderCapabilities.BackfillFullFeatured

// Symbol search
ProviderCapabilities.SymbolSearch

// Filterable symbol search
ProviderCapabilities.SymbolSearchFilterable(
    assetTypes: new[] { "stock", "etf", "crypto" },
    exchanges: new[] { "NYSE", "NASDAQ" })

// Hybrid (streaming + backfill)
ProviderCapabilities.Hybrid(trades: true, quotes: true, depth: false, adjustedPrices: true, intraday: true)

// Convert from legacy HistoricalDataCapabilities
ProviderCapabilities.FromHistoricalCapabilities(caps, maxRequestsPerWindow: 200, ...)
```

### `ProviderCredentialField`

Metadata for UI form generation and pre-flight validation:

```csharp
new ProviderCredentialField(
    Name: "ApiKey",
    EnvironmentVariable: "MYPROVIDER__APIKEY",
    DisplayName: "API Key",
    Required: true,
    DefaultValue: null)
```

---

## 6. Credential Management

### `CredentialValidator`

Location: `src/Meridian.ProviderSdk/CredentialValidator.cs`

Centralized utilities to eliminate duplicate validation:

```csharp
// Validate single API key
CredentialValidator.ValidateApiKey(apiKey, "MyProvider", _logger);

// Validate key/secret pair
CredentialValidator.ValidateKeySecretPair(keyId, secretKey, "MyProvider", _logger);

// Throw if missing
CredentialValidator.ThrowIfApiKeyMissing(apiKey, "MyProvider", "MYPROVIDER__APIKEY");
CredentialValidator.ThrowIfCredentialsMissing(keyId, secretKey, "MyProvider",
    "MYPROVIDER__KEYID", "MYPROVIDER__SECRETKEY");

// Resolve from env var with fallback
var key = CredentialValidator.GetCredential(configValue, "MYPROVIDER__APIKEY");
var key = CredentialValidator.GetCredential(configValue, "MYPROVIDER__APIKEY", "MYPROVIDER_API_KEY");
```

### Credential Resolution Order

The `CredentialConfig` class supports multiple credential sources with the following priority:

1. **Vault** — AWS Secrets Manager or Azure Key Vault via `ISecretProvider`
2. **File** — JSON file at `CredentialsPath`
3. **Environment** — Environment variables (recommended for development)
4. **Config** — Direct values in configuration (not recommended for production)

---

## 7. Channel Buffering (`EventPipelinePolicy`)

Location: `src/Meridian.Core/Pipeline/EventPipelinePolicy.cs`

All providers must use `EventPipelinePolicy` presets for bounded channels (ADR-013):

```csharp
// For streaming market data (50k capacity, DropOldest)
private readonly Channel<T> _channel =
    EventPipelinePolicy.HighThroughput.CreateChannel<T>();

// For general event pipelines (100k capacity, DropOldest)
EventPipelinePolicy.Default.CreateChannel<T>();

// For internal message buffering (50k capacity, no metrics)
EventPipelinePolicy.MessageBuffer.CreateChannel<T>();

// For background task queues (100 capacity, Wait/backpressure)
EventPipelinePolicy.MaintenanceQueue.CreateChannel<T>();

// For logging channels (1k capacity, DropOldest)
EventPipelinePolicy.Logging.CreateChannel<T>();

// For completion notifications (500 capacity, Wait/backpressure)
EventPipelinePolicy.CompletionQueue.CreateChannel<T>();
```

---

## 8. Configuration

### Options Pattern

Create a configuration model for your provider:

```csharp
public sealed record ExampleProviderOptions
{
    public string? ApiKey { get; init; }
    public string? SecretKey { get; init; }
    public bool UsePaper { get; init; }
    public string BaseUrl { get; init; } = "https://api.example.com";
    public int TimeoutSeconds { get; init; } = 30;
}
```

### Unified DataSource Configuration

For providers using the `IDataSource` hierarchy, configuration goes into `UnifiedDataSourcesConfig`:

```json
{
  "DataSources": {
    "Sources": {
      "example": {
        "Enabled": true,
        "Priority": 50,
        "Type": "Hybrid",
        "Category": "Broker",
        "Credentials": {
          "Source": "Environment",
          "ApiKeyVar": "EXAMPLE__APIKEY",
          "SecretKeyVar": "EXAMPLE__SECRETKEY"
        },
        "RateLimits": {
          "MaxRequestsPerWindow": 200,
          "WindowSeconds": 60,
          "MinDelayBetweenRequestsMs": 100
        },
        "Connection": {
          "BaseUrl": "https://api.example.com",
          "EnableWebSocket": true,
          "TimeoutSeconds": 30
        }
      }
    }
  }
}
```

### Environment Variables

Use double underscore (`__`) for nested configuration:

```bash
export EXAMPLE__APIKEY=your-api-key
export EXAMPLE__SECRETKEY=your-secret-key
```

---

## 9. Directory Structure

Provider scaffolding templates now live outside the production assembly at `docs/examples/provider-template/`. Copy the files you need from there into `src/Meridian.Infrastructure/Adapters/{ProviderName}/` before customizing them.

Place provider implementations in the appropriate subdirectory:

```text
src/Meridian.Infrastructure/Adapters/
├── Backfill/                          # Historical provider base + interface
│   ├── IHistoricalDataProvider.cs     # Interface
│   ├── BaseHistoricalDataProvider.cs  # Base class
│   └── {Provider}/                    # Provider-specific implementations
│       └── {Provider}HistoricalDataProvider.cs
├── Streaming/
│   └── {Provider}/
│       └── {Provider}MarketDataClient.cs
├── Historical/                        # (alias for Backfill/)
│   ├── Alpaca/
│   ├── AlphaVantage/
│   ├── Finnhub/
│   ├── InteractiveBrokers/
│   ├── NasdaqDataLink/
│   ├── Polygon/
│   ├── StockSharp/
│   ├── Stooq/
│   ├── Tiingo/
│   └── YahooFinance/
├── SymbolSearch/
│   ├── ISymbolSearchProvider.cs
│   └── {Provider}SymbolSearchProvider.cs
└── Core/
    ├── ProviderRegistry.cs
    ├── ProviderFactory.cs
    ├── ProviderTemplate.cs
    └── ProviderServiceExtensions.cs
```

---

## 10. Testing Providers

### Unit Test Structure

```csharp
public sealed class ExampleHistoricalDataProviderTests : IDisposable
{
    private readonly MockHttpMessageHandler _mockHandler;
    private readonly ExampleHistoricalDataProvider _sut;

    public ExampleHistoricalDataProviderTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(_mockHandler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };
        _sut = new ExampleHistoricalDataProvider(httpClient);
    }

    [Fact]
    public async Task GetDailyBarsAsync_ReturnsOrderedBars()
    {
        _mockHandler.SetResponse("""{"data":[...]}""");

        var bars = await _sut.GetDailyBarsAsync("AAPL",
            new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31));

        bars.Should().BeInAscendingOrder(b => b.SessionDate);
        bars.Should().AllSatisfy(b => b.Source.Should().Be("example"));
    }

    [Fact]
    public async Task GetDailyBarsAsync_Returns_Empty_On_404()
    {
        _mockHandler.SetResponse(HttpStatusCode.NotFound);

        var bars = await _sut.GetDailyBarsAsync("INVALID", null, null);

        bars.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDailyBarsAsync_Throws_On_AuthError()
    {
        _mockHandler.SetResponse(HttpStatusCode.Unauthorized);

        var act = () => _sut.GetDailyBarsAsync("AAPL", null, null);

        await act.Should().ThrowAsync<ConnectionException>();
    }

    public void Dispose() => _sut.Dispose();
}
```

### Mock Streaming Client for Integration Tests

```csharp
public sealed class MockExampleClient : IMarketDataClient
{
    private readonly Channel<MarketDataEvent> _channel =
        EventPipelinePolicy.HighThroughput.CreateChannel<MarketDataEvent>();

    public bool IsEnabled => true;
    public ConnectionState State { get; private set; }
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    public void SimulateTrade(string symbol, decimal price, long size)
        => _channel.Writer.TryWrite(new TradeEvent(symbol, price, size, DateTimeOffset.UtcNow));

    public void SimulateDisconnection()
    {
        var old = State;
        State = ConnectionState.Disconnected;
        ConnectionStateChanged?.Invoke(this, new(old, ConnectionState.Disconnected));
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Connected;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        State = ConnectionState.Disconnected;
        return Task.CompletedTask;
    }

    public int SubscribeMarketDepth(SymbolConfig cfg) => 1;
    public void UnsubscribeMarketDepth(int subscriptionId) { }
    public int SubscribeTrades(SymbolConfig cfg) => 1;
    public void UnsubscribeTrades(int subscriptionId) { }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

---

## 11. Step-by-Step: Adding a New Provider

### Streaming Provider

1. Create `src/Meridian.Infrastructure/Adapters/Streaming/{Provider}/{Provider}MarketDataClient.cs`
2. Implement `IMarketDataClient`
3. Add `[ImplementsAdr("ADR-001", "...")]` and `[ImplementsAdr("ADR-004", "...")]`
4. Create `{Provider}Options` configuration model
5. Register in DI (`ServiceCompositionRoot.cs` or via `IProviderModule`)
6. Add config section in `config/appsettings.sample.json`
7. Add tests in `tests/Meridian.Tests/Infrastructure/Adapters/`
8. Document in `docs/providers/`

### Historical Provider

1. Create `src/Meridian.Infrastructure/Adapters/{Provider}/{Provider}HistoricalDataProvider.cs`
2. Extend `BaseHistoricalDataProvider`
3. Add `[ImplementsAdr]` attributes
4. Override `Name`, `DisplayName`, `Description`, `HttpClientName`, `Capabilities`
5. Implement `GetDailyBarsAsync` (and extended methods if supported)
6. The `CompositeHistoricalDataProvider` picks it up automatically via DI
7. Add tests, config, and documentation

### Symbol Search Provider

1. Create `src/Meridian.Infrastructure/Adapters/Core/{Provider}SymbolSearchProvider.cs`
2. Implement `ISymbolSearchProvider` (or `IFilterableSymbolSearchProvider`)
3. Override `IProviderMetadata` defaults
4. Register in DI
5. Add tests and documentation

---

## Performance Considerations

| Technique | When to Use |
| ----------- | ------------- |
| `EventPipelinePolicy` presets | All bounded channel creation |
| `BoundedChannelFullMode.DropOldest` | High-frequency data where latest matters |
| `IHttpClientFactory` | All HTTP-based providers (ADR-010) |
| `ValueTask` | Operations that often complete synchronously |
| `Span<T>` and `ArrayPool<T>` | Hot paths with frequent allocations |
| `BaseHistoricalDataProvider` | All backfill providers (built-in resilience) |
| `ExecuteGetAndReadAsync` | HTTP requests with centralized error handling |
| `ResiliencePipeline` | Transient fault handling via Polly |

---

## Common Mistakes to Avoid

| Mistake | Consequence |
| --------- | ------------- |
| Forgetting `[ImplementsAdr]` attributes | ADR traceability is lost; audit tools flag it |
| Not using `CancellationToken` throughout async chain | Graceful shutdown fails |
| Creating unbounded channels | Memory exhaustion under load |
| Not using `EventPipelinePolicy` presets | Inconsistent backpressure behavior (ADR-013) |
| Creating `new HttpClient()` directly | Socket exhaustion, DNS issues (ADR-010) |
| Forgetting to call `UpdateState()` on connection failure | Consumers don't know connection died |
| Swallowing exceptions in event handlers | Silent data loss |
| Not disposing resources in `DisposeAsync()` / `Dispose()` | Resource leaks |
| Using string interpolation in logger calls | Loses structured logging benefits |
| Not sealing provider classes | Violates project convention |
| Adding `Version` to `PackageReference` | NU1008 error — use Central Package Management |

---

## Related Documentation

- **Architecture and Design:**
  - [ADR-001: Provider Abstraction](../adr/001-provider-abstraction.md) — Interface contracts for data providers
  - [ADR-004: Async Streaming Patterns](../adr/004-async-streaming-patterns.md) — CancellationToken, IAsyncEnumerable
  - [ADR-005: Attribute-Based Discovery](../adr/005-attribute-based-discovery.md) — `[DataSource]`, `[ImplementsAdr]`
  - [ADR-010: HttpClient Factory](../adr/010-httpclient-factory.md) — HTTP client lifecycle management
  - [ADR-013: Bounded Channel Policy](../adr/013-bounded-channel-policy.md) — Consistent backpressure presets
  - [Provider Comparison](../providers/provider-comparison.md) — Feature comparison matrix
  - [Data Sources Overview](../providers/data-sources.md) — Complete provider catalog

- **Implementation Guides:**
  - [Refactor Map](./refactor-map.md) — Safe refactoring procedures
  - [Repository Organization Guide](./repository-organization-guide.md) — Code structure conventions
  - [WPF Implementation Notes](./wpf-implementation-notes.md) — Desktop integration patterns

- **Operations and Testing:**
  - [Backfill Guide](../providers/backfill-guide.md) — Historical data procedures
  - [Performance Tuning](../operations/performance-tuning.md) — Optimization strategies
- [Provider-Specific Setup Guides](../providers/README.md) — Interactive Brokers, Alpaca, etc.

- **AI Guides:**
  - [CLAUDE.providers.md](../ai/claude/CLAUDE.providers.md) — AI-focused provider reference
  - [CLAUDE.testing.md](../ai/claude/CLAUDE.testing.md) — Testing patterns

---

**Version:** 1.6.2
**Last Updated:** 2026-03-16
**Audience:** Contributors implementing new data providers and AI assistants working on provider integration.
