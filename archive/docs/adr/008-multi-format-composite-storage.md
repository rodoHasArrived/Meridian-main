# ADR-008: Multi-Format Composite Storage Sink Pattern

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

Market data has heterogeneous access patterns requiring different storage formats:

1. **Hot tier (JSONL)** - Recent data, fast sequential reads, human-readable
2. **Cold tier (Parquet)** - Historical data, columnar compression, analytics queries
3. **Research exports (CSV/HDF5)** - Third-party tool compatibility

Traditional approaches have trade-offs:
- **Single format** - Suboptimal for all use cases (JSONL slow for analytics, Parquet slow for streaming)
- **Pipeline duplication** - Separate pipelines per format increase complexity
- **Conversion jobs** - Batch conversion adds latency and operational burden

The system needs to support **multiple storage formats simultaneously** from a single event stream without modifying EventPipeline.

## Decision

Implement a **CompositeSink** that fans out events to multiple storage sinks:

```csharp
public sealed class CompositeSink : IStorageSink
{
    private readonly IReadOnlyList<IStorageSink> _sinks;
    
    public async ValueTask AppendAsync(MarketEvent evt, CancellationToken ct)
    {
        // Fan out to all sinks in parallel
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.AppendAsync(evt, ct);
            }
            catch (Exception ex)
            {
                // Log but don't fail other sinks
                _logger.LogWarning(ex, "Sink {Type} failed", sink.GetType().Name);
            }
        }
    }
}
```

### Configuration Example

```csharp
var compositeSink = new CompositeSink(new IStorageSink[]
{
    new JsonlStorageSink(hotTierPath, compression: CompressionLevel.Fastest),
    new ParquetStorageSink(coldTierPath, compression: CompressionLevel.Maximum),
    new MetricsReportingSink(prometheus)  // Optional: side effects
});

var pipeline = new EventPipeline(compositeSink, ...);
```

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Composite Sink | `src/Meridian.Storage/Sinks/CompositeSink.cs:12` | Fan-out implementation |
| Storage Sink Interface | `src/Meridian.Storage/Interfaces/IStorageSink.cs` | Sink contract |
| JSONL Sink | `src/Meridian.Storage/Sinks/JsonlStorageSink.cs` | Row-based storage |
| Parquet Sink | `src/Meridian.Storage/Sinks/ParquetStorageSink.cs` | Columnar storage |
| Pipeline Integration | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Sink injection |
| Composition Root | `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` | DI configuration |
| Tests | `tests/Meridian.Tests/Storage/CompositeSinkTests.cs` | Verification |

## Rationale

### Separation of Concerns

CompositeSink isolates format-specific logic from the event pipeline:

```
EventPipeline (format-agnostic)
    ↓
CompositeSink (fan-out)
    ├→ JsonlStorageSink (hot tier)
    ├→ ParquetStorageSink (cold tier)
    └→ MetricsReportingSink (monitoring)
```

Adding a new format requires:
1. Implement `IStorageSink`
2. Add to CompositeSink list
3. No changes to EventPipeline or upstream code

### Independent Failure Isolation

Each sink fails independently without affecting others:

```csharp
// Sink 1 succeeds
await jsonlSink.AppendAsync(evt, ct);  // ✓

// Sink 2 fails (e.g., disk full)
await parquetSink.AppendAsync(evt, ct);  // ✗ logs warning

// Sink 3 proceeds normally
await metricsSink.AppendAsync(evt, ct);  // ✓
```

This prevents cascading failures where one slow/broken sink blocks the entire pipeline.

### Write Skew Handling

CompositeSink allows temporary skew between formats:
- JSONL might have 1000 events
- Parquet might have 995 events (5 failed writes)

Recovery is idempotent: replaying WAL (ADR-007) re-applies missing events to Parquet without duplicating JSONL.

## Alternatives Considered

### Alternative 1: Single Unified Format

Use only JSONL or only Parquet for all data.

**Pros:**
- Simpler implementation
- No skew concerns
- Single query path

**Cons:**
- **JSONL-only**: Columnar analytics are 10-50x slower
- **Parquet-only**: Streaming writes are slower, human inspection harder
- Forces compromise on all use cases

**Why rejected:** No single format excels at both streaming and analytics.

### Alternative 2: Background Conversion Jobs

Write to JSONL only, batch-convert to Parquet nightly.

**Pros:**
- Simpler real-time path
- Conversion can be optimized separately

**Cons:**
- **Latency**: Analytics data is hours/days stale
- **Disk overhead**: Temporary storage for both formats
- **Operational complexity**: Cron jobs, failure monitoring

**Why rejected:** Adds latency and operational burden.

### Alternative 3: Storage Abstraction Layer

Unified interface that abstracts format details (like Apache Arrow).

**Pros:**
- Format-agnostic queries
- Industry-standard tools

**Cons:**
- **Complexity**: Abstraction layer adds overhead
- **Performance**: Conversion at query time
- **Dependency**: External library lock-in

**Why rejected:** Over-engineered for current needs.

## Consequences

### Positive

- **Format flexibility** - Easy to add CSV, HDF5, database sinks, etc.
- **Failure isolation** - One sink failure doesn't block others
- **Hot/cold separation** - JSONL for recent, Parquet for historical
- **Zero pipeline changes** - New formats added via DI configuration
- **Testable** - Each sink can be tested independently

### Negative

- **Write amplification** - Each event written N times (once per sink)
- **Skew management** - Formats can drift if writes fail
- **Flush complexity** - Must coordinate flush across all sinks
- **Disk I/O** - Multiple concurrent writes increase I/O contention

### Neutral

- Sink ordering is undefined (parallel writes)
- Idempotent sinks required for safe WAL replay
- Requires monitoring to detect skew between formats

## Compliance

### Code Contracts

```csharp
// All storage sinks must implement this interface
public interface IStorageSink : IAsyncDisposable
{
    // Append single event (idempotent for replay)
    ValueTask AppendAsync(MarketEvent evt, CancellationToken ct);
    
    // Flush buffered writes to disk
    Task FlushAsync(CancellationToken ct);
}

// Composite sink contract
public sealed class CompositeSink : IStorageSink
{
    public CompositeSink(IEnumerable<IStorageSink> sinks);
    
    public int SinkCount { get; }
    
    // Fans out to all sinks, logging failures
    public ValueTask AppendAsync(MarketEvent evt, CancellationToken ct);
    
    // Flushes all sinks (throws AggregateException on failure)
    public Task FlushAsync(CancellationToken ct);
}
```

### Runtime Verification

- No `[ImplementsAdr]` attribute required (pattern is structural)
- Integration tests verify multi-sink behavior:
  - All sinks receive events
  - Failure isolation (one sink fails, others succeed)
  - Flush coordination across sinks
- Storage tests verify idempotency for WAL replay

## References

- [Tiered Storage Architecture](002-tiered-storage-architecture.md) (uses CompositeSink)
- [WAL Durability](007-write-ahead-log-durability.md) (replay with CompositeSink)
- [Storage Design Documentation](../architecture/storage-design.md)
- [Storage Services Documentation](../architecture/storage-design.md)

---

*Last Updated: 2026-02-12*
