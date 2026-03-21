# ADR-013: Bounded Channel Pipeline Policy with Backpressure

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

The application uses `System.Threading.Channels` for in-process producer-consumer pipelines. Channels handle:

1. **Event streaming** - Market data clients → EventPipeline → storage sinks
2. **Message buffering** - WebSocket messages → parser → event publisher
3. **Background tasks** - Maintenance queues, backfill coordinators
4. **Logging** - Structured log entries → file/console

Without consistent configuration, channels exhibit problems:
- **Unbounded channels** - Memory exhaustion under load
- **Ad-hoc capacity** - Arbitrary 1000, 5000, 10000 values scattered across code
- **Inconsistent overflow** - Some drop oldest, some block, some throw
- **Hidden backpressure** - Producers block unpredictably

A **centralized policy** is needed to ensure consistent behavior and prevent cascading failures.

## Decision

Implement **EventPipelinePolicy** with static presets for common scenarios:

```csharp
public sealed record EventPipelinePolicy(
    int Capacity,
    BoundedChannelFullMode FullMode,
    bool EnableMetrics
)
{
    // Static presets
    public static EventPipelinePolicy Default { get; } = new(100_000, DropOldest, true);
    public static EventPipelinePolicy HighThroughput { get; } = new(50_000, DropOldest, true);
    public static EventPipelinePolicy MessageBuffer { get; } = new(50_000, DropOldest, false);
    public static EventPipelinePolicy MaintenanceQueue { get; } = new(100, Wait, false);
    public static EventPipelinePolicy Logging { get; } = new(1_000, DropOldest, false);
    public static EventPipelinePolicy CompletionQueue { get; } = new(500, Wait, false);
    
    // Factory method
    public Channel<T> CreateChannel<T>(bool singleReader = true, bool singleWriter = false);
}
```

### Usage Example

```csharp
// Using preset
var marketDataChannel = EventPipelinePolicy.HighThroughput.CreateChannel<MarketEvent>();

// Custom policy for specific use case
var customPolicy = new EventPipelinePolicy(
    Capacity: 25_000,
    FullMode: BoundedChannelFullMode.Wait,
    EnableMetrics: true
);
var channel = customPolicy.CreateChannel<MyEvent>();
```

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Policy Class | `src/Meridian.Core/Pipeline/EventPipelinePolicy.cs:26` | Preset definitions |
| Constants | `src/Meridian.Contracts/Pipeline/PipelinePolicyConstants.cs` | Capacity constants |
| EventPipeline | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Main event pipeline |
| StockSharp Buffer | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` | MessageBuffer usage |
| Backfill Coordinator | `src/Meridian.Application/Backfill/BackfillCoordinator.cs` | CompletionQueue usage |
| Tests | `tests/Meridian.Tests/Application/Pipeline/EventPipelineTests.cs` | Policy verification |

## Rationale

### Consistent Backpressure Behavior

All channels in the application use one of six presets:

| Preset | Capacity | Full Mode | Use Case |
|--------|----------|-----------|----------|
| **Default** | 100k | DropOldest | General event pipelines |
| **HighThroughput** | 50k | DropOldest | Streaming data clients |
| **MessageBuffer** | 50k | DropOldest | Internal message parsing |
| **MaintenanceQueue** | 100 | Wait | Background tasks (no drops) |
| **Logging** | 1k | DropOldest | Log channels |
| **CompletionQueue** | 500 | Wait | Completion notifications |

This prevents:
- **Memory leaks** from unbounded channels
- **Deadlocks** from unpredictable blocking
- **Silent drops** from undocumented overflow behavior

### Drop-Oldest vs. Wait

Two overflow strategies address different needs:

**DropOldest** (for data streams):
```csharp
// Market data: drop old ticks to stay current
var channel = EventPipelinePolicy.HighThroughput.CreateChannel<MarketEvent>();

// If full, oldest event is discarded
// Metrics track dropped count for monitoring
```

**Wait** (for control messages):
```csharp
// Maintenance tasks: never drop tasks
var channel = EventPipelinePolicy.MaintenanceQueue.CreateChannel<MaintenanceTask>();

// If full, producer blocks until space available
// Backpressure propagates upstream
```

### Metrics Integration

Policies with `EnableMetrics: true` integrate with Prometheus:

```csharp
// Metrics tracked per policy type
pipeline_capacity{policy="HighThroughput"} 50000
pipeline_current_count{policy="HighThroughput"} 42387
pipeline_dropped_total{policy="HighThroughput"} 1523
```

This enables monitoring of backpressure and overflow across the system.

## Alternatives Considered

### Alternative 1: Unbounded Channels

Use `Channel.CreateUnbounded<T>()` everywhere.

**Pros:**
- No capacity configuration
- No overflow handling

**Cons:**
- **Memory exhaustion** under sustained load
- **GC pressure** from large queues
- **No backpressure signals** to slow producers

**Why rejected:** Unacceptable memory risk in production.

### Alternative 2: Ad-Hoc Configuration

Configure each channel at creation site:

```csharp
var channel = Channel.CreateBounded<T>(new BoundedChannelOptions(10000)
{
    FullMode = BoundedChannelFullMode.DropOldest,
    SingleReader = true
});
```

**Pros:**
- Full flexibility
- No abstraction overhead

**Cons:**
- **Inconsistent behavior** across codebase
- **Magic numbers** (why 10000?)
- **Hard to audit** backpressure strategy

**Why rejected:** Lack of standardization creates operational risk.

### Alternative 3: External Queue (RabbitMQ, Kafka)

Use external message broker for all pipelines.

**Pros:**
- Durable queues
- Distributed consumers
- Industry-standard tooling

**Cons:**
- **Overkill** for in-process pipelines
- **Network latency** (microseconds → milliseconds)
- **Operational complexity** (violates ADR-003)

**Why rejected:** Inappropriate for single-process architecture.

## Consequences

### Positive

- **Predictable overflow** - All channels use documented presets
- **Consistent monitoring** - Metrics tracked per policy type
- **Memory safety** - Bounded capacity prevents exhaustion
- **Auditability** - Easy to grep for policy usage
- **Self-documenting** - Preset names explain intent

### Negative

- **Fixed presets** - May not fit all use cases perfectly
- **Abstraction layer** - One more API to learn
- **Drop risk** - DropOldest can lose data if not monitored

### Neutral

- Requires monitoring dashboards to track dropped events
- Custom policies are allowed but discouraged
- Preset values tuned for typical workloads (may need adjustment)

## Compliance

### Code Contracts

```csharp
// All channel creation should use this policy
public sealed record EventPipelinePolicy(
    int Capacity,
    BoundedChannelFullMode FullMode,
    bool EnableMetrics
)
{
    // Static presets (6 total)
    public static EventPipelinePolicy Default { get; }
    public static EventPipelinePolicy HighThroughput { get; }
    // ... other presets
    
    // Factory method
    public Channel<T> CreateChannel<T>(bool singleReader = true, bool singleWriter = false);
    
    // Conversion method for advanced use cases
    public BoundedChannelOptions ToBoundedOptions(bool singleReader, bool singleWriter);
}
```

### Runtime Verification

- No `[ImplementsAdr]` attribute (pattern is usage-based)
- Code review: All `Channel.CreateBounded` should use EventPipelinePolicy
- Metrics: Monitor dropped events across all policies
- Integration tests: Verify overflow behavior per policy

## References

- [Event Pipeline Documentation](../architecture/storage-design.md#event-pipeline)
- [Monitoring Architecture](012-monitoring-and-alerting-pipeline.md)
- [BackpressureAlertService](../architecture/overview.md#backpressure-monitoring)
- [System.Threading.Channels Documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.channels)

---

*Last Updated: 2026-02-12*
