# ADR-014: High-Performance JSON Serialization via Source Generators

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

Market data collection involves serializing millions of events per day:

1. **Storage persistence** - JSONL files at 100k+ events/sec
2. **Wire protocols** - WebSocket messages from providers
3. **HTTP APIs** - Dashboard endpoints, backfill responses
4. **Monitoring** - Prometheus metrics, health checks

Traditional `System.Text.Json` uses reflection at runtime, incurring:
- **Reflection overhead** - Type inspection per serialization call
- **JIT warmup** - Slow first serialization
- **Memory allocations** - Boxing, temporary objects
- **AOT incompatibility** - Reflection fails in Native AOT

For high-throughput scenarios (100k+ events/sec), reflection overhead is measurable (~15-25% performance loss).

## Decision

Use **System.Text.Json source generators** (introduced in .NET 7+) to eliminate reflection:

```csharp
[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(MarketEvent))]
[JsonSerializable(typeof(Trade))]
[JsonSerializable(typeof(AppConfig))]
// ... all serializable types
public partial class MarketDataJsonContext : JsonSerializerContext
{
    // Static options for reuse
    public static readonly JsonSerializerOptions HighPerformanceOptions = new()
    {
        TypeInfoResolver = Default,
        WriteIndented = false,
        // ... settings
    };
}
```

### Usage Guidelines

All JSON serialization in the codebase must use:

1. **MarketDataJsonContext.HighPerformanceOptions** - Storage, wire protocols
2. **MarketDataJsonContext.PrettyPrintOptions** - Debugging, config files
3. **HighPerformanceJson static methods** - Typed serialization for `MarketEvent`

```csharp
// ✅ CORRECT - Source-generated
var json = JsonSerializer.Serialize(evt, MarketDataJsonContext.HighPerformanceOptions);

// ❌ INCORRECT - Uses reflection
var json = JsonSerializer.Serialize(evt);
```

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| JSON Context | `src/Meridian.Core/Serialization/MarketDataJsonContext.cs:97` | Source-generated serializers |
| Alpaca Context | `src/Meridian.Core/Serialization/MarketDataJsonContext.cs:142` | Alpaca wire messages |
| Alpaca Corporate Actions | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaCorporateActionProvider.cs` | Dedicated source-generated context for Alpaca announcement DTOs |
| Audit Trail Contexts | `src/Meridian.Application/Pipeline/DeadLetterSink.cs`, `src/Meridian.Application/Pipeline/DroppedEventAuditTrail.cs` | Source-generated audit-record serializers for crash-safe JSONL append paths |
| HighPerformanceJson | `src/Meridian.Core/Serialization/MarketDataJsonContext.cs:243` | Typed utilities |
| WAL Integration | `src/Meridian.Storage/Archival/WriteAheadLog.cs:92` | Uses HighPerformanceOptions |
| JSONL Sink | `src/Meridian.Storage/Sinks/JsonlStorageSink.cs` | Bulk serialization |
| HTTP Endpoints | `src/Meridian.Application/Http/Endpoints/` | API responses |
| Benchmarks | `benchmarks/Meridian.Benchmarks/JsonSerializationBenchmarks.cs` | Performance validation |
| Tests | `tests/Meridian.Tests/Serialization/HighPerformanceJsonTests.cs` | Correctness tests |

## Rationale

### Performance Gains

Source generators eliminate reflection overhead:

| Scenario | Reflection | Source Generators | Speedup |
|----------|------------|-------------------|---------|
| Serialize MarketEvent | 850 ns | 520 ns | **1.6x** |
| Deserialize MarketEvent | 920 ns | 580 ns | **1.6x** |
| Bulk serialize (10k events) | 8.5 ms | 5.2 ms | **1.6x** |
| First call (cold start) | 12 ms | 0.6 ms | **20x** |

Benchmarks in `benchmarks/Meridian.Benchmarks/JsonSerializationBenchmarks.cs`.

### AOT Compatibility

Source generators enable **Native AOT** deployment:
- No reflection at runtime
- Smaller binary size (trimming)
- Faster startup (no JIT)

This unlocks deployment scenarios like AWS Lambda, edge devices, and microcontrollers.

### Type Safety

Source generators provide compile-time type registration:

```csharp
// Compile error if type not registered
var json = JsonSerializer.Serialize(unknownType, MarketDataJsonContext.Default);
```

This prevents runtime serialization failures.

### Wire Protocol Optimization

Separate context for Alpaca avoids case-sensitivity conflicts:

```csharp
// Alpaca uses both "T" (type) and "t" (timestamp)
// Must parse case-sensitively
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = false)]
public partial class AlpacaJsonContext : JsonSerializerContext { }

var tradeMsg = JsonSerializer.Deserialize<AlpacaTradeMessage>(
    utf8Json, AlpacaJsonContext.Default.AlpacaTradeMessage);
```

## Alternatives Considered

### Alternative 1: Reflection-Based Serialization

Use default `System.Text.Json` with reflection.

**Pros:**
- No code generation
- Automatically handles new types
- Simpler build process

**Cons:**
- **15-25% slower** for hot paths
- **12ms cold start** penalty
- **Incompatible with AOT** (reflection fails)
- **Higher memory** (boxing, temporary objects)

**Why rejected:** Performance critical for 100k+ events/sec.

### Alternative 2: MessagePack Binary Protocol

Use MessagePack for compact binary serialization.

**Pros:**
- **50-60% smaller** than JSON
- **2-3x faster** serialization
- Schema evolution support

**Cons:**
- **Not human-readable** (debugging harder)
- **Tooling gap** (fewer readers support MessagePack)
- **Breaking change** (existing JSONL archives)

**Why rejected:** Human readability and ecosystem compatibility prioritized.

### Alternative 3: Protocol Buffers (protobuf)

Use protobuf for strongly-typed serialization.

**Pros:**
- Compact binary format
- Schema-first design
- Cross-language support

**Cons:**
- **Requires .proto files** (adds build complexity)
- **Not human-readable**
- **Overkill** for single-language system

**Why rejected:** JSON suffices for current performance needs.

### Alternative 4: Manual StringBuilder Serialization

Hand-write JSON serialization for critical paths.

**Pros:**
- **Maximum performance** (no abstractions)
- Full control

**Cons:**
- **Error-prone** (escaping, formatting)
- **High maintenance** (every type needs custom code)
- **Incompatible with System.Text.Json ecosystem**

**Why rejected:** Source generators provide 90% of performance with 10% of effort.

## Consequences

### Positive

- **1.6x faster serialization** - Critical for 100k+ events/sec
- **20x faster cold start** - No reflection warmup
- **AOT compatible** - Enables Native AOT deployment
- **Type safety** - Compile-time type registration
- **Smaller binaries** - Trimming removes unused serializers
- **Lower memory** - No reflection metadata

### Negative

- **Manual registration** - Each type must be annotated with `[JsonSerializable]`
- **Build time cost** - Source generation adds ~100ms to compile time
- **Debugging complexity** - Generated code in `obj/` directory
- **Breaking changes** - Changing options requires recompilation

### Neutral

- Generated serializers are partial classes (can be extended)
- Options objects are reusable (static fields)
- Requires .NET 7+ (already on .NET 9)

## Compliance

### Code Contracts

```csharp
// All JSON contexts must derive from JsonSerializerContext
[JsonSourceGenerationOptions(...)]
[JsonSerializable(typeof(Type1))]
[JsonSerializable(typeof(Type2))]
public partial class MarketDataJsonContext : JsonSerializerContext
{
    // Provide static options for reuse
    public static readonly JsonSerializerOptions HighPerformanceOptions = ...;
    public static readonly JsonSerializerOptions PrettyPrintOptions = ...;
}

// Typed serialization utilities
public static class HighPerformanceJson
{
    public static byte[] SerializeToUtf8Bytes(MarketEvent evt);
    public static string Serialize(MarketEvent evt);
    public static MarketEvent? Deserialize(string json);
    // ... more methods
}
```

### Runtime Verification

- No `[ImplementsAdr]` attribute (pattern is usage-based)
- Code review: All `JsonSerializer` calls must use context options
- Build verification: Serialization tests validate all registered types
- Benchmarks: Performance regression tests track serialization speed

## References

- [Microsoft Docs: JSON source generation](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation)
- [Performance comparison blog post](https://devblogs.microsoft.com/dotnet/performance-improvements-in-dotnet-7/)
- [Open Source References](../reference/open-source-references.md) - System.Text.Json techniques
- [Storage Sink Implementations](../architecture/storage-design.md)
- [WAL Durability ADR](007-write-ahead-log-durability.md) (uses HighPerformanceOptions)

---

*Last Updated: 2026-02-12*
