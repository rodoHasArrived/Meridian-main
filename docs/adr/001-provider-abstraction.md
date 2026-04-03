# ADR-001: Provider Abstraction Pattern

**Status:** Accepted
**Date:** 2024-06-15
**Deciders:** Core Team

## Context

The Meridian needs to support multiple data providers (Interactive Brokers, Alpaca, NYSE, Polygon, StockSharp, and 90+ others) for real-time streaming, historical data retrieval, and symbol search. Each provider has different:

- API protocols (REST, WebSocket, proprietary)
- Authentication mechanisms
- Data formats
- Rate limits and quotas
- Supported data types (trades, quotes, depth)
- Symbol universe and search capabilities

Without proper abstraction, the core application would become tightly coupled to specific providers, making it difficult to:
- Add new providers
- Switch between providers
- Run multiple providers concurrently
- Handle provider-specific failures
- Search symbols across heterogeneous catalogs

## Decision

Implement a provider abstraction layer using three core interfaces:

1. **`IMarketDataClient`** - For real-time streaming data
2. **`IHistoricalDataProvider`** - For historical data retrieval
3. **`ISymbolSearchProvider`** - For symbol lookup and autocomplete

All providers must implement the appropriate interface(s), enabling provider-agnostic data consumption throughout the application.

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Streaming Interface | `src/Meridian.ProviderSdk/IMarketDataClient.cs` | Real-time data contract |
| Historical Interface | `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs` | Historical data contract |
| Symbol Search Interface | `src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs` | Symbol lookup contract |
| Alpaca Streaming | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaMarketDataClient.cs` | Streaming provider |
| IB Streaming | `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBMarketDataClient.cs` | Streaming provider |
| Yahoo Finance Historical | `src/Meridian.Infrastructure/Adapters/YahooFinance/YahooFinanceHistoricalDataProvider.cs` | Historical provider |
| Stooq Historical | `src/Meridian.Infrastructure/Adapters/Stooq/StooqHistoricalDataProvider.cs` | Historical provider |
| Polygon Symbol Search | `src/Meridian.Infrastructure/Adapters/Polygon/PolygonSymbolSearchProvider.cs` | Symbol search provider |
| Alpaca Symbol Search | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaSymbolSearchProviderRefactored.cs` | Symbol search provider |
| Composite Provider | `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs` | Failover orchestration |
| Interface Tests | `tests/Meridian.Tests/Infrastructure/` | Contract verification |

## Rationale

### Provider Independence
By coding to interfaces rather than implementations, the core domain logic remains completely isolated from provider-specific concerns. This enables:

- **Hot-swapping**: Switch providers without code changes
- **Multi-provider**: Run IB and Alpaca simultaneously
- **Graceful failover**: `CompositeHistoricalDataProvider` tries providers in priority order
- **Symbol search**: Query multiple symbol catalogs through a unified interface

### Async-First Design
Both interfaces are designed for async operation with `CancellationToken` support, ensuring responsive and cancellable operations.

### Metadata-Driven Discovery
Combined with `DataSourceAttribute` (see ADR-005), providers are automatically discovered and registered, eliminating manual DI configuration.

## Alternatives Considered

### Alternative 1: Concrete Provider Classes

Direct use of provider classes without interfaces.

**Pros:**
- Simpler initial implementation
- No abstraction overhead

**Cons:**
- Tight coupling to providers
- Difficult to test
- No failover capability

**Why rejected:** Long-term maintainability outweighs short-term simplicity.

### Alternative 2: Event-Driven Architecture

Use event bus for all provider communication.

**Pros:**
- Maximum decoupling
- Natural async model

**Cons:**
- Higher complexity
- Debugging difficulties
- Overkill for single-process deployment

**Why rejected:** Added complexity without proportional benefit.

## Consequences

### Positive

- Clean separation of concerns
- Easy to add new providers (implement interface + add attribute)
- Testable with mock implementations
- Failover and redundancy support
- Provider-agnostic domain logic

### Negative

- Small abstraction overhead
- Must maintain interface compatibility across providers
- Some provider-specific features may not fit the interface

### Neutral

- Providers must adapt their native APIs to our interface
- Testing requires both unit tests and integration tests

## Compliance

### Code Contracts

```csharp
// All streaming providers must implement:
public interface IMarketDataClient : IAsyncDisposable
{
    bool IsEnabled { get; }
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync(CancellationToken ct = default);
    int SubscribeMarketDepth(SymbolConfig cfg);
    void UnsubscribeMarketDepth(int subscriptionId);
    int SubscribeTrades(SymbolConfig cfg);
    void UnsubscribeTrades(int subscriptionId);
}

// All historical providers must implement:
public interface IHistoricalDataProvider
{
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to,
        CancellationToken ct = default);
}

// All symbol search providers must implement:
public interface ISymbolSearchProvider : IProviderMetadata
{
    string Name { get; }
    Task<IReadOnlyList<SymbolSearchResult>> SearchAsync(
        string query, int maxResults = 20,
        CancellationToken ct = default);
}
```

### Runtime Verification

- `[ImplementsAdr("ADR-001")]` attribute on implementing classes
- Build verification: `make verify-adrs`
- Interface compliance checked at startup via DI registration

## References

- [Provider Implementation Guide](../development/provider-implementation.md)
- [Provider Comparison](../providers/provider-comparison.md)
- [Data Sources Documentation](../providers/data-sources.md)
- [ADR-005: Attribute-Based Discovery](005-attribute-based-discovery.md) - Automatic provider registration via `[DataSource]` attribute
- [ADR-010: HttpClientFactory](010-httpclient-factory.md) - HTTP client lifecycle for provider API calls
- [ADR-015: Strategy Execution Contract](015-strategy-execution-contract.md) - `IOrderGateway` extends the same interface pattern to brokerage adapters

---

*Last Updated: 2026-04-03*
