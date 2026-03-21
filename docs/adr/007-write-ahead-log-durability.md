# ADR-007: Write-Ahead Log (WAL) + Event Pipeline Durability

**Status:** Accepted
**Date:** 2026-02-12
**Deciders:** Core Team

## Context

Market data collection involves processing millions of events per day. Process crashes, OS failures, or power outages can result in:

1. **Data loss** - Events in-flight but not yet persisted to disk
2. **Incomplete batches** - Partial writes to JSONL/Parquet files
3. **Sequence gaps** - Missing events break downstream analytics
4. **Non-deterministic replay** - Cannot reliably reconstruct state

Traditional approaches have limitations:
- **Direct file writes** - No crash recovery, vulnerable to partial writes
- **Database transactions** - Too slow for 100k+ events/sec
- **In-memory buffering** - Lost on crash

Financial-grade systems require **at-least-once delivery** semantics with crash recovery.

## Decision

Implement a **Write-Ahead Log (WAL)** integrated into EventPipeline:

1. **Pre-commit to WAL** - All events written to WAL before primary storage
2. **Checksummed records** - SHA256 checksum per record detects corruption
3. **Sequence tracking** - Monotonic sequence numbers enable gap detection
4. **Automatic recovery** - On startup, replay uncommitted WAL records
5. **File rotation** - WAL rotates at 100MB to prevent unbounded growth

```
Event Flow:
MarketDataClient → EventPipeline → WAL (fsync) → StorageSink → Commit
                                  ↓
                            (crash recovery replays WAL)
```

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| WAL Implementation | `src/Meridian.Storage/Archival/WriteAheadLog.cs:17` | Core WAL class |
| WAL Options | `src/Meridian.Storage/Archival/WalOptions.cs` | Configuration |
| Pipeline Integration | `src/Meridian.Application/Pipeline/EventPipeline.cs:20-28` | WAL in event pipeline |
| Atomic File Writer | `src/Meridian.Storage/Archival/AtomicFileWriter.cs` | Crash-safe file writes |
| Recovery Tests | `tests/Meridian.Tests/Storage/WriteAheadLogTests.cs` | Verification |
| Flush on Shutdown | `src/Meridian.Application/Services/GracefulShutdownService.cs` | Coordinated shutdown |

## Rationale

### Crash-Safe Durability

WAL provides **durable persistence** before acknowledging events:

```csharp
// 1. Write to WAL with fsync
var walRecord = await _wal.AppendAsync(evt, "MarketEvent", ct);

// 2. Write to primary storage (JSONL/Parquet)
await _storageSink.AppendAsync(evt, ct);

// 3. Mark committed in WAL (async checkpoint)
await _wal.MarkCommittedAsync(walRecord.Sequence, ct);
```

On crash, uncommitted WAL records are replayed:

```csharp
// Startup: recover any uncommitted WAL files
foreach (var walFile in Directory.GetFiles(_walDirectory, "*.wal"))
{
    var records = await RecoverWalFileAsync(walFile, ct);
    foreach (var record in records)
    {
        // Re-apply to storage sink
        await _storageSink.AppendAsync(record.Payload, ct);
    }
}
```

### Sequence Integrity

Each WAL record contains:
- **Sequence number** (monotonic, per-symbol)
- **Timestamp** (event and write time)
- **Record type** (event discriminator)
- **Payload** (JSON-serialized event)
- **Checksum** (SHA256 hash)

Recovery validates checksums and detects missing sequences, triggering integrity alerts.

### Performance Trade-offs

WAL adds ~5-10% write overhead but prevents catastrophic data loss:

| Scenario | Without WAL | With WAL |
|----------|-------------|----------|
| Normal operation | 120k events/sec | 110k events/sec |
| Process crash | **Data loss** | Full recovery |
| Partial write | **Corrupted file** | Replayed from WAL |
| Disk full | Undefined | Graceful failure |

## Alternatives Considered

### Alternative 1: Database Transactions

Use SQLite or PostgreSQL for transactional writes.

**Pros:**
- ACID guarantees
- Mature tooling
- SQL query capability

**Cons:**
- **2-10x slower** than WAL (foreign key checks, indexes, query planner)
- Schema migrations complicate updates
- Increases deployment complexity

**Why rejected:** Performance overhead unacceptable for 100k+ events/sec.

### Alternative 2: Message Queue (Kafka, RabbitMQ)

External durable queue for event persistence.

**Pros:**
- Distributed durability
- Built-in replication
- Proven at scale

**Cons:**
- **Operational complexity** (external service required)
- Network latency for every event
- Overkill for single-process deployment

**Why rejected:** Violates "simplicity first" principle (ADR-003).

### Alternative 3: Periodic Snapshots

Checkpoint state every N seconds without WAL.

**Pros:**
- Simpler implementation
- Lower write overhead

**Cons:**
- **Lossy** - Events between checkpoints are lost
- Non-deterministic recovery
- Does not satisfy financial-grade requirements

**Why rejected:** Unacceptable data loss risk.

## Consequences

### Positive

- **Zero data loss** - Crash recovery replays uncommitted events
- **Sequence integrity** - Checksums and sequence numbers detect gaps
- **Fast recovery** - WAL replay completes in seconds
- **Graceful shutdown** - Coordinated flush before process exit
- **Low overhead** - ~5-10% performance impact

### Negative

- **Disk space** - WAL files consume 100MB per rotation (cleaned after commit)
- **Replay complexity** - Recovery logic must handle corrupted records
- **Write amplification** - Events written twice (WAL + primary)

### Neutral

- WAL directory must be on same filesystem as data directory (atomic rename)
- Requires periodic cleanup of old WAL files (automatic on commit)
- fsync overhead depends on filesystem and disk type (SSD recommended)

## Compliance

### Code Contracts

```csharp
// WAL interface contract
public sealed class WriteAheadLog : IAsyncDisposable
{
    // Initialize and recover from existing WAL files
    Task InitializeAsync(CancellationToken ct = default);
    
    // Append record with automatic checksum
    Task<WalRecord> AppendAsync<T>(T data, string recordType, CancellationToken ct);
    
    // Mark sequence as committed (can be garbage collected)
    Task MarkCommittedAsync(long sequence, CancellationToken ct);
    
    // Force flush to disk
    Task FlushAsync(CancellationToken ct);
}

// WAL record structure
public sealed record WalRecord
{
    public long Sequence { get; init; }
    public DateTime Timestamp { get; init; }
    public string RecordType { get; init; }
    public string Payload { get; init; }  // JSON
    public string Checksum { get; init; }  // SHA256
}
```

### Runtime Verification

- WAL automatically initialized on EventPipeline startup
- Checksum validation on every record read
- Sequence monotonicity enforced at write time
- Integration tests verify crash recovery (kill process, restart, verify)

## References

- [Tiered Storage Architecture](002-tiered-storage-architecture.md) (uses WAL)
- [Event Pipeline Documentation](../architecture/storage-design.md#event-pipeline)
- [GracefulShutdownService](../architecture/overview.md#graceful-shutdown)
- [PostgreSQL WAL Documentation](https://www.postgresql.org/docs/current/wal-intro.html) (inspiration)

---

*Last Updated: 2026-02-12*
