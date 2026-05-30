# ADR-002: Tiered Storage Architecture

**Status:** Accepted
**Date:** 2026-05-30
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
- Governance controls (retention, deletion, archive hold)

## Decision

Implement a policy-driven tiered storage architecture with governed lifecycle management:

1. **Write path is durable first**
   - Market events are written through `EventPipeline` into `StorageSink` implementations.
   - WAL is used for crash recovery (`WriteAheadLog`) and remains the canonical durability layer (ADR-007).
2. **Tiering is configured, not hard-coded**
   - `StorageOptions.Tiering` defines named tiers (`hot`, `warm`, `cold`, `archive`) and their target paths.
   - Tiering is optional; if disabled, files remain in the default configured sink layout.
3. **Tiering is policy-driven**
   - `StoragePolicyConfig` (`HotTierDays`, `WarmTierDays`, `ColdTierDays`, optional `ArchiveTier`) drives target tier decisions.
   - `LifecyclePolicyEngine` continuously evaluates files by age, classification, and policy, then emits tier migration, compression, archive, and delete actions.
4. **Lifecycle execution is scheduled and idempotent**
   - Migration actions are executed by `TierMigrationService` with configurable parallelism and checksum verification.
   - `MaintenanceScheduler` and `ScheduledArchiveMaintenanceService` handle periodic scheduling.
5. **Formats are per-tier configurable**
   - `StorageTierConfig.Format` chooses sink format (`jsonl`, `jsonl.gz`, `parquet`).
   - Sink conversion is performed when migrating between tiers as needed.

Data flows through tiers by policy evaluation and scheduled maintenance, not by direct ingestion path alone.

## Implementation Links

<!-- These links are verified by the build process -->

| Component | Location | Purpose |
|-----------|----------|---------|
| Storage Configuration | `src/Meridian.Storage/StorageOptions.cs` | Tiering and policy contracts |
| Tier presets | `src/Meridian.Storage/StorageProfiles.cs` | `Research`/`Archival` profiles and default tier paths |
| Event Pipeline | `src/Meridian.Application/Pipeline/EventPipeline.cs` | Ingestion + WAL integration |
| JSONL Sink | `src/Meridian.Storage/Sinks/JsonlStorageSink.cs` | Primary hot-tier writer |
| Parquet Sink | `src/Meridian.Storage/Sinks/ParquetStorageSink.cs` | Columnar archive-oriented writer |
| Composite Sink | `src/Meridian.Storage/Sinks/CompositeSink.cs` | Multi-sink fan-out |
| Lifecycle Engine | `src/Meridian.Storage/Services/LifecyclePolicyEngine.cs` | Policy-based tier decisions |
| Tier Migration | `src/Meridian.Storage/Services/TierMigrationService.cs` | Movement between tiers |
| Compression Profiles | `src/Meridian.Storage/Archival/CompressionProfileManager.cs` | Tier-aware profile/catalog policy |
| Lifecycle Scheduler | `src/Meridian.Storage/Services/MaintenanceScheduler.cs` | Execution of recurring tiering tasks |
| WAL Implementation | `src/Meridian.Storage/Archival/WriteAheadLog.cs` | Crash-safe durability |
| Storage Tests | `tests/Meridian.Tests/Storage/` | Storage and lifecycle coverage |
| Tiering API | `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs` | Tier stats, planning, and manual migration actions |

## Rationale

### Write-Ahead Logging
All events first hit the WAL before processing, ensuring zero data loss even during crashes. The WAL uses sequential writes for maximum throughput.

### Compression Selection
| Codec | Speed | Ratio | Use Case |
|-----------|-------|-------|----------|
| LZ4 | Very Fast | Low | Real-time ingestion |
| Zstd | Medium/Fast | Very Good | Cold/Archive compression |
| Gzip | Medium | Medium | Compatibility and portable exchange |
| Brotli | Medium/Slow | Good | Non-time-critical payloads |
| None | Very Fast | None | Debug/diagnostic workloads |

### Format Selection
- **JSONL**: Human-readable, streamable, schema-flexible
- **Parquet**: Columnar, excellent compression, fast analytical queries

The tier profile is configurable; the built-in `Archival` profile currently maps:

- hot → `jsonl`, no compression
- warm → `jsonl.gz`
- cold → `parquet` (`zstd` target)
- archive → `parquet` (`zstd` target)

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

- Enforces retention, compression, and migration policies across storage tiers
- Keeps hot-path ingestion stable while enabling long-term analytics
- Supports archival and compliance retention paths (`archive` tier + perpetual policy)
- Zero data loss with WAL
- Flexible governance via `StoragePolicyConfig` and `StorageProfiles`
- Fast analytical queries with Parquet

### Negative

- Additional asynchronous background jobs to keep lifecycle up-to-date
- Query strategy differs across tiers and formats unless downstream read layer normalizes it
- Admin-facing configuration complexity increases with more active tiers

### Neutral

- Requires monitoring of tier sizes
- Requires explicit governance rules to avoid unintended deletion of `Critical` data
- Backup strategy per tier

## Compliance

### Code Contracts

```csharp
// Storage sink contract
public interface IStorageSink : IAsyncDisposable
{
    ValueTask AppendAsync(MarketEvent evt, CancellationToken ct = default);
    Task FlushAsync(CancellationToken ct = default);
}

// Compression profile contract
public sealed record CompressionProfile(
    string Id,
    string Name,
    string Description,
    CompressionCodec Codec,
    int Level,
    CompressionPriority Priority);
```

### File Organization Rules

```
{DataRoot}/
├── hot/                         # Hot tier (ingest path)
│   └── {optional provider}/{optional date}/{symbol}_{event}.jsonl[.gz]
├── warm/                        # Warm tier (compressed JSONL)
│   └── {optional provider}/{optional date}/{symbol}_{event}.jsonl.gz
├── cold/                        # Cold tier (often Parquet)
│   └── {optional provider}/{optional date}/{symbol}_{event}.parquet
└── archive/                     # Archive tier (cold+perpetual)
    └── {optional provider}/{optional date}/{symbol}_{event}.parquet
```

Tier paths are configurable in `StorageOptions.Tiering.Tiers`; only the `hot/warm/cold/archive` conventions are required for built-in policy routing, with additional tiers possible via configuration.

### Runtime Verification

- `[ImplementsAdr("ADR-002")]` on storage components
- `[ImplementsAdr("ADR-002")]` on `LifecyclePolicyEngine`
- File naming conventions enforced by storage services
- Compression validation on read
- Tier evaluation and migration jobs surfaced via `LifecyclePolicyEngine`, `MaintenanceScheduler`, and `/api/storage/tiers/*`

## References

- [Storage Design](../architecture/storage-design.md)
- [Compression Guide](../HELP.md#configuration)
- [Data Lifecycle](../operations/operator-runbook.md)
- [ADR-007: Write-Ahead Log](007-write-ahead-log-durability.md) - WAL provides crash-safe durability for hot-tier writes
- [ADR-008: Multi-Format Composite Storage](008-multi-format-composite-storage.md) - CompositeSink fans out to JSONL and Parquet tiers simultaneously

---

*Last Updated: 2026-05-30*
