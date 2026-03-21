# ADR-002: Tiered Storage Architecture

**Status:** Accepted
**Date:** 2024-07-20
**Deciders:** Core Team

## Context

Market data collection generates high-volume, time-series data with varying access patterns:

- **Hot data**: Recent trades/quotes accessed frequently for real-time monitoring
- **Warm data**: Recent history accessed for intraday analysis
- **Cold data**: Historical archives accessed rarely for backtesting

A single storage approach cannot optimize for all access patterns while balancing:
- Write throughput (real-time ingestion)
- Read latency (dashboard queries)
- Storage costs (long-term retention)
- Data durability (no data loss)

## Decision

Implement a three-tier storage architecture with automatic data lifecycle management:

1. **Hot Tier**: Write-Ahead Log (WAL) + JSONL with LZ4 compression
2. **Warm Tier**: Compressed JSONL with Gzip
3. **Cold Tier**: Parquet with ZSTD compression

Data flows through tiers automatically based on age and access patterns.

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Storage Configuration | `src/Meridian.Storage/StorageOptions.cs` | Tier configuration |
| Event Pipeline | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Bounded channel routing |
| JSONL Sink | `src/Meridian.Storage/Sinks/JsonlStorageSink.cs` | Hot/warm tier writer |
| Parquet Sink | `src/Meridian.Storage/Sinks/ParquetStorageSink.cs` | Cold tier Parquet writer |
| WAL Implementation | `src/Meridian.Storage/Archival/WriteAheadLog.cs` | Durability layer |
| Compression Profiles | `src/Meridian.Storage/Archival/CompressionProfileManager.cs` | Compression strategies |
| Tier Migration | `src/Meridian.Storage/Services/TierMigrationService.cs` | Data lifecycle management |
| Archival Service | `src/Meridian.Storage/Archival/ArchivalStorageService.cs` | Archive management |
| Storage Tests | `tests/Meridian.Tests/Storage/` | Tier verification |

## Rationale

### Write-Ahead Logging
All events first hit the WAL before processing, ensuring zero data loss even during crashes. The WAL uses sequential writes for maximum throughput.

### Compression Selection
| Algorithm | Speed | Ratio | Use Case |
|-----------|-------|-------|----------|
| LZ4 | Very Fast | Lower | Real-time ingestion |
| Gzip | Medium | Medium | Compatibility, warm data |
| ZSTD-19 | Slow | Best | Archives (10:1+ ratio) |

### Format Selection
- **JSONL**: Human-readable, streamable, schema-flexible
- **Parquet**: Columnar, excellent compression, fast analytical queries

## Alternatives Considered

### Alternative 1: Single Database (PostgreSQL/TimescaleDB)

**Pros:**
- Single query interface
- ACID guarantees
- Rich query capabilities

**Cons:**
- Higher latency for writes
- Storage costs at scale
- Operational complexity

**Why rejected:** Write throughput requirements exceed DB capabilities.

### Alternative 2: Pure Parquet (Append-Only)

**Pros:**
- Single format
- Excellent compression
- Analytical performance

**Cons:**
- Cannot append to existing files
- Row-group overhead for small writes
- Complex real-time queries

**Why rejected:** Not suitable for real-time streaming ingestion.

## Consequences

### Positive

- Optimized for each access pattern
- Cost-effective long-term storage
- Zero data loss with WAL
- Flexible schema evolution with JSONL
- Fast analytical queries with Parquet

### Negative

- Multiple storage formats to maintain
- Background jobs for tier migration
- Query interface varies by tier

### Neutral

- Requires monitoring of tier sizes
- Backup strategy per tier

## Compliance

### Code Contracts

```csharp
// Storage sink contract
public interface IStorageSink : IAsyncDisposable
{
    Task WriteAsync<T>(T data, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

// Compression profile contract
public sealed record CompressionProfile(
    string Name,
    CompressionAlgorithm Algorithm,
    int Level);
```

### File Organization Rules

```
{DataRoot}/
├── live/                    # Hot tier (LZ4)
│   └── {provider}/{date}/{symbol}_trades.jsonl.lz4
├── historical/              # Warm tier (Gzip)
│   └── {provider}/{date}/{symbol}_bars.jsonl.gz
└── _archive/                # Cold tier (ZSTD/Parquet)
    └── parquet/{symbol}_{year}.parquet
```

### Runtime Verification

- `[ImplementsAdr("ADR-002")]` on storage components
- File naming conventions enforced by storage services
- Compression validation on read
- Tier migration scheduled via `TierMigrationService`

## References

- [Storage Design](../architecture/storage-design.md)
- [Compression Guide](../HELP.md#configuration)
- [Data Lifecycle](../operations/operator-runbook.md#storage-management)
- [ADR-007: Write-Ahead Log](007-write-ahead-log-durability.md) - WAL provides crash-safe durability for hot-tier writes
- [ADR-008: Multi-Format Composite Storage](008-multi-format-composite-storage.md) - CompositeSink fans out to JSONL and Parquet tiers simultaneously

---

*Last Updated: 2026-02-20*
