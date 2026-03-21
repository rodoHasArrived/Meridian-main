# ADR-005: Attribute-Based Provider Discovery

**Status:** Accepted
**Date:** 2024-10-15
**Deciders:** Core Team

## Context

As the number of data providers grows (currently 10+ historical providers, 5+ streaming providers), manual DI registration becomes:

1. **Error-prone**: Easy to forget registering a new provider
2. **Verbose**: Repetitive registration code
3. **Scattered**: Registration logic separate from implementation
4. **Hard to discover**: No single place to see all available providers

We need a mechanism to automatically discover and register providers while capturing their metadata.

## Decision

Use custom attributes to mark provider classes for automatic discovery and registration:

1. **`[DataSource]`** attribute captures provider metadata
2. **Assembly scanning** discovers all attributed types at startup
3. **Automatic DI registration** based on implemented interfaces
4. **Runtime metadata** available for UI display and priority ordering

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| DataSourceAttribute | `src/Meridian.ProviderSdk/DataSourceAttribute.cs` | Provider metadata and discovery helpers |
| DataSourceRegistry | `src/Meridian.ProviderSdk/DataSourceRegistry.cs` | DI registration and runtime discovery |
| IDataSource | `src/Meridian.ProviderSdk/IDataSource.cs` | Base interface |
| IRealtimeDataSource | `src/Meridian.ProviderSdk/IRealtimeDataSource.cs` | Streaming marker |
| IHistoricalDataSource | `src/Meridian.ProviderSdk/IHistoricalDataSource.cs` | Historical marker |
| IProviderMetadata | `src/Meridian.ProviderSdk/IProviderMetadata.cs` | Provider metadata contract |
| DataSourceConfiguration | `src/Meridian.Infrastructure/DataSources/DataSourceConfiguration.cs` | Runtime configuration |
| Attribute Tests | `tests/Meridian.Tests/ProviderSdk/` | Discovery and attribute tests |

## Rationale

### Attribute Design

```csharp
[DataSource("alpaca", "Alpaca Markets",
    DataSourceType.Hybrid,
    DataSourceCategory.Broker,
    Priority = 10,
    Description = "Commission-free trading with real-time data")]
public sealed class AlpacaDataSource : IRealtimeDataSource, IHistoricalDataSource
{
    // Implementation
}
```

### Automatic Registration

```csharp
// In Program.cs - single line registers all providers
services.AddDataSources(typeof(Program).Assembly);

// Behind the scenes:
// 1. Scans assembly for [DataSource] attributes
// 2. Extracts DataSourceMetadata
// 3. Registers with appropriate interfaces
// 4. Orders by Priority for failover
```

### Metadata Access

```csharp
// Runtime discovery for UI
var providers = serviceProvider.GetServices<IHistoricalDataSource>()
    .Select(p => p.GetType().GetDataSourceMetadata())
    .OrderBy(m => m.Priority);

// Display in dashboard
foreach (var meta in providers)
{
    Console.WriteLine($"{meta.DisplayName}: {meta.Description}");
}
```

## Alternatives Considered

### Alternative 1: Manual Registration

Explicit `services.AddSingleton<IDataSource, AlpacaDataSource>()` calls.

**Pros:**
- Explicit control
- No magic/reflection
- Easier debugging

**Cons:**
- Verbose, repetitive
- Easy to forget
- No metadata capture

**Why rejected:** Does not scale with provider count.

### Alternative 2: Convention-Based Discovery

Discover by naming convention (e.g., `*DataSource` suffix).

**Pros:**
- No attributes needed
- Simple rules

**Cons:**
- No metadata capture
- Naming constraints
- Accidental matches

**Why rejected:** Cannot capture rich metadata without attributes.

### Alternative 3: Configuration-Based

Register providers via appsettings.json.

**Pros:**
- Runtime configuration
- No code changes

**Cons:**
- Stringly-typed
- No compile-time validation
- Separate from implementation

**Why rejected:** Too error-prone for critical infrastructure.

## Consequences

### Positive

- Single source of truth (attribute on class)
- Automatic registration
- Rich metadata at runtime
- Priority-based ordering
- UI can display provider information
- Easy to add new providers

### Negative

- Reflection at startup (mitigated by caching)
- Magic discovery may surprise developers
- Attribute must be kept in sync with implementation

### Neutral

- Requires understanding attribute system
- Plugin assembly loading adds complexity

## Compliance

### Code Contracts

```csharp
// Required attribute structure
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class DataSourceAttribute : Attribute
{
    public string Id { get; }              // Required: unique identifier
    public string DisplayName { get; }     // Required: human-readable name
    public DataSourceType Type { get; }    // Required: Realtime/Historical/Hybrid
    public DataSourceCategory Category { } // Required: Broker/Exchange/Aggregator
    public int Priority { get; set; }      // Optional: failover order (default: 100)
    public string? Description { get; set; } // Optional: provider description
}

// All data sources must implement marker interface
public interface IDataSource { }
```

### Attribute Requirements

1. **Id**: Unique, lowercase, hyphen-separated (e.g., "alpha-vantage")
2. **DisplayName**: Human-readable, title case (e.g., "Alpha Vantage")
3. **Type**: Must match implemented interfaces
4. **Priority**: Lower = higher priority (tried first in failover)

### Runtime Verification

- `[ImplementsAdr("ADR-005")]` on DataSourceAttribute
- Build-time validation that attributed types implement IDataSource
- Startup validation for duplicate IDs

## References

- [DataSourceAttribute Source](../../src/Meridian.ProviderSdk/DataSourceAttribute.cs)
- [Provider Implementation Guide](../development/provider-implementation.md)
- [Microsoft Attribute Guidelines](https://docs.microsoft.com/en-us/dotnet/standard/design-guidelines/attributes)
- [ADR-001: Provider Abstraction](001-provider-abstraction.md) - Defines the interfaces that discovered providers implement

---

*Last Updated: 2026-02-20*
