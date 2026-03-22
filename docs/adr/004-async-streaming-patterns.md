# ADR-004: Async Streaming Patterns

**Status:** Accepted
**Date:** 2024-08-05
**Deciders:** Core Team

## Context

Market data streams are continuous and potentially unbounded. Traditional collection-based APIs have problems:

1. **Memory pressure**: Buffering large datasets exhausts memory
2. **Latency**: Must wait for entire collection before processing
3. **Cancellation**: Difficult to cancel mid-stream
4. **Backpressure**: No mechanism to slow producers

We need a streaming pattern that handles these challenges while maintaining clean async/await semantics.

## Decision

Adopt `IAsyncEnumerable<T>` as the primary pattern for streaming data throughout the codebase:

1. **Data sources** yield events as `IAsyncEnumerable<T>`
2. **Pipelines** transform streams using LINQ-style operators
3. **Sinks** consume streams asynchronously
4. **All async methods** accept `CancellationToken`

Use `System.Threading.Channels` for producer-consumer scenarios with backpressure.

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Streaming Interface | `src/Meridian.ProviderSdk/IMarketDataClient.cs` | Event streaming |
| Event Pipeline | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Channel-based routing |
| Trade Collector | `src/Meridian.Domain/Collectors/TradeDataCollector.cs` | Stream consumer |
| Quote Collector | `src/Meridian.Domain/Collectors/QuoteCollector.cs` | Stream consumer |
| Event Buffer | `src/Meridian.Storage/Services/EventBuffer.cs` | Bounded buffering |
| Backfill Streaming | `src/Meridian.Infrastructure/Adapters/Core/` | Historical streaming |
| Async Tests | `tests/Meridian.Tests/Application/Pipeline/` | Pattern verification |

## Rationale

### IAsyncEnumerable Benefits

```csharp
// Memory-efficient streaming
await foreach (var trade in client.GetTradesAsync(ct))
{
    await ProcessTradeAsync(trade, ct);
}

// Composable transformations
var highValueTrades = client.GetTradesAsync(ct)
    .Where(t => t.Volume > 1000)
    .Select(t => new TradeEvent(t));
```

### Channel Benefits

Use `EventPipelinePolicy` for consistent channel configuration across the codebase:

```csharp
using Meridian.Application.Pipeline;

// Preferred: Use centralized policy with factory method
var channel = EventPipelinePolicy.Default.CreateChannel<MarketEvent>();

// Or use a specific preset for your use case:
// - EventPipelinePolicy.HighThroughput  // 50k capacity, DropOldest
// - EventPipelinePolicy.MessageBuffer   // 50k capacity, no metrics
// - EventPipelinePolicy.MaintenanceQueue // 100 capacity, Wait/backpressure
// - EventPipelinePolicy.Logging         // 1k capacity, DropOldest

// Producer
await channel.Writer.WriteAsync(event, ct);

// Consumer (streaming)
await foreach (var item in channel.Reader.ReadAllAsync(ct))
{
    await ProcessAsync(item, ct);
}
```

### CancellationToken Propagation

All async methods must accept and respect `CancellationToken`:

```csharp
public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
    string symbol,
    DateOnly? from,
    DateOnly? to,
    CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    // ... implementation
}
```

## Alternatives Considered

### Alternative 1: Rx.NET (System.Reactive)

Push-based observable streams.

**Pros:**
- Rich operator library
- Time-based operators
- Multicasting built-in

**Cons:**
- Learning curve
- Push vs pull semantics
- Harder to debug

**Why rejected:** Team familiarity with async/await; simpler debugging.

### Alternative 2: DataFlow (TPL)

Block-based parallel processing.

**Pros:**
- Parallel processing
- Block composition
- Bounded capacity

**Cons:**
- Complex configuration
- Heavyweight for simple streams
- Less flexible than channels

**Why rejected:** Channels provide sufficient capability with less complexity.

## Consequences

### Positive

- Memory-efficient streaming
- Natural async/await integration
- Clean cancellation semantics
- Backpressure support via channels
- LINQ-style composition

### Negative

- Debugging async streams can be challenging
- Must remember to pass `CancellationToken` everywhere
- Channel capacity tuning required

### Neutral

- Performance characteristics depend on buffer sizes
- Exception handling requires careful consideration

## Compliance

### Code Contracts

```csharp
// Streaming data sources must use IAsyncEnumerable
public interface IStreamingDataSource
{
    IAsyncEnumerable<MarketEvent> GetEventsAsync(CancellationToken ct = default);
}

// All async methods must accept CancellationToken
public interface IAsyncOperation
{
    Task ExecuteAsync(CancellationToken ct = default);
}
```

### Naming Conventions

- Async methods end with `Async` suffix
- Streaming methods may use `Stream` prefix: `StreamHistoricalBarsAsync`
- CancellationToken parameter named `ct` or `cancellationToken`

### Anti-Patterns to Avoid

```csharp
// BAD: Blocking async
var result = GetDataAsync().Result;  // Deadlock risk

// BAD: Missing cancellation
await foreach (var item in stream)  // No cancellation

// GOOD: Proper cancellation
await foreach (var item in stream.WithCancellation(ct))
{
    ct.ThrowIfCancellationRequested();
    await ProcessAsync(item, ct);
}
```

### Runtime Verification

- `[ImplementsAdr("ADR-004")]` on streaming implementations
- Analyzer rules for missing `CancellationToken`
- Channel capacity monitoring

## References

- [CLAUDE.md Critical Rules](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md)
- [Configuration Reference](../HELP.md#configuration)
- [Microsoft IAsyncEnumerable Docs](https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/generate-consume-asynchronous-stream)
- [ADR-013: Bounded Channel Policy](013-bounded-channel-policy.md) - Defines the `EventPipelinePolicy` presets referenced in the Channel Benefits section above

---

*Last Updated: 2026-02-20*
