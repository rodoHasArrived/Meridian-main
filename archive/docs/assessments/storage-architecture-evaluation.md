# Storage Architecture Evaluation

## Meridian — Data Persistence Assessment

**Date:** 2026-03-15
**Status:** Evaluation Complete (Revised)
**Author:** Architecture Review
**Previous Revision:** 2026-02-22

---

## Executive Summary

This document evaluates the storage architecture of the Meridian system, including file formats, organization strategies, compression, tiered storage, the Write-Ahead Log (WAL) implementation, export system, quota enforcement, data lineage, and catalog management. The evaluation assesses current design decisions against alternatives and identifies remaining optimization opportunities.

**Key Finding:** The storage architecture has matured significantly since the initial evaluation. The core JSONL + Parquet dual-format approach remains well-designed for archival-first market data collection. Major additions since the last review include a comprehensive export pipeline (7 formats), granular quota enforcement with dynamic rebalancing, data lineage tracking, a lifecycle policy engine, a catalog sync system, expanded compression profiles, attribute-based sink plugin discovery, cross-platform file permission management, and retention compliance reporting. Several recommendations from the prior evaluation—parallel tier migration, retention policy automation, data validation pipelines, and native LZ4/ZSTD compression—have been implemented.

---

## A. Current Architecture Overview

### Storage Components

| Component | Location | Purpose |
|-----------|----------|---------|
| JsonlStorageSink | `Storage/Sinks/` | Real-time JSONL file writes |
| ParquetStorageSink | `Storage/Sinks/` | Columnar archive format |
| CompositeSink | `Storage/Sinks/` | Multi-format fan-out (JSONL + Parquet simultaneously) |
| CatalogSyncSink | `Storage/Sinks/` | Automatic catalog index updates on flush |
| WriteAheadLog | `Storage/Archival/` | Crash-safe durability with SHA-256 checksums |
| CompressionProfileManager | `Storage/Archival/` | Context-aware compression with 6 built-in profiles |
| AtomicFileWriter | `Storage/Archival/` | Atomic write operations for data integrity |
| SchemaVersionManager | `Storage/Archival/` | Schema evolution and compatibility |
| ArchivalStorageService | `Storage/Archival/` | Long-term archival coordination |
| TierMigrationService | `Storage/Services/` | Hot/warm/cold tier management with parallel migration |
| LifecyclePolicyEngine | `Storage/Services/` | Automated tier migration and retention enforcement |
| QuotaEnforcementService | `Storage/Services/` | Per-source, per-symbol, per-event-type quotas |
| DataQualityScoringService | `Storage/Services/` | Multi-dimensional quality scoring |
| DataLineageService | `Storage/Services/` | Provenance and transformation tracking |
| StorageCatalogService | `Storage/Services/` | Catalog indexing, manifest management |
| StorageChecksumService | `Storage/Services/` | Per-file checksum verification |
| StorageSearchService | `Storage/Services/` | File search and discovery |
| MetadataTagService | `Storage/Services/` | Metadata tagging for files |
| EventBuffer | `Storage/Services/` | Buffered event batching before writes |
| FileMaintenanceService | `Storage/Services/` | File cleanup and maintenance |
| MaintenanceScheduler | `Storage/Services/` | Cron-based maintenance scheduling |
| SourceRegistry | `Storage/Services/` | Multi-source data registry |
| SymbolRegistryService | `Storage/Services/` | Symbol catalog and alias management |
| PortableDataPackager | `Storage/Packaging/` | Self-contained data export/import packages |
| AnalysisExportService | `Storage/Export/` | 7-format export pipeline with built-in profiles |
| JsonlStoragePolicy | `Storage/Policies/` | Path resolution and naming conventions |
| JsonlReplayer | `Storage/Replay/` | Event replay from stored JSONL |
| MemoryMappedJsonlReader | `Storage/Replay/` | High-performance memory-mapped reads |
| ScheduledArchiveMaintenanceService | `Storage/Maintenance/` | Automated scheduled maintenance |
| FilePermissionsService | `Storage/Services/` | Cross-platform file/directory permission management (Unix 0755/0644, Windows NTFS ACLs) |
| RetentionComplianceReporter | `Storage/Services/` | Machine-readable retention compliance audit reports for regulatory review |
| StorageSinkRegistry | `Storage/` | Attribute-based storage sink plugin discovery and DI registration (ADR-005 pattern) |
| StorageSinkAttribute | `Storage/` | Marks a class as a discoverable `IStorageSink` plugin; controls `ActiveSinks` activation |

### Storage Profiles

Three preset profiles simplify configuration for common workloads:

| Profile | Compression | Manifests | Partitioning | Description |
|---------|-------------|-----------|--------------|-------------|
| **Research** (default) | Gzip | Yes | Date → Symbol | Balanced defaults for analysis workflows |
| **LowLatency** | None | No | Symbol → EventType (Hourly) | Minimum overhead for ingest speed |
| **Archival** | ZSTD | Yes (+ checksums) | Date → Source (Monthly) | Long-term retention with full tiering |

The Archival profile auto-configures a four-tier hierarchy (hot → warm → cold → archive) with appropriate compression per tier and a 10-year default retention.

### File Organization Strategies

The system supports eight naming conventions:

| Strategy | Pattern | Use Case |
|----------|---------|----------|
| **BySymbol** (default) | `{root}/{symbol}/{type}/{date}.jsonl` | Symbol-centric analysis |
| ByDate | `{root}/{date}/{symbol}/{type}.jsonl` | Date-centric queries |
| ByType | `{root}/{type}/{symbol}/{date}.jsonl` | Type-centric processing |
| Flat | `{root}/{symbol}_{type}_{date}.jsonl` | Simple deployments |
| BySource | `{root}/{source}/{symbol}/{type}/{date}.jsonl` | Multi-provider comparison |
| ByAssetClass | `{root}/{asset_class}/{symbol}/{type}/{date}.jsonl` | Multi-asset management |
| Hierarchical | `{root}/{source}/{asset_class}/{symbol}/{type}/{date}.jsonl` | Enterprise multi-source, multi-asset |
| Canonical | `{root}/{year}/{month}/{day}/{source}/{symbol}/{type}.jsonl` | Time-series archival |

Date partitioning options: None, Daily (default), Hourly, Monthly.

Multi-dimensional partitioning is also supported via `PartitionStrategy`, allowing primary/secondary/tertiary dimensions from: Date, Symbol, EventType, Source, AssetClass, Exchange.

### Directory Structure

```
data/
├── live/                    # Hot tier (real-time)
│   ├── {provider}/
│   │   └── {date}/
│   │       ├── {symbol}_trades.jsonl.gz
│   │       └── {symbol}_quotes.jsonl.gz
├── historical/              # Backfill data
│   └── {provider}/
│       └── {date}/
│           └── {symbol}_bars.jsonl
├── _wal/                    # Write-ahead log
│   ├── wal_{timestamp}_{seq}.wal
│   └── archive/             # Compressed archived WAL files
├── _archive/                # Cold tier
│   └── parquet/
│       └── {symbol}/
│           └── {year}/
│               └── {type}.parquet
└── _catalog/                # Catalog metadata
    ├── manifest.json
    ├── schemas/
    └── _index.json
```

---

## B. File Format Evaluation

---

### Format 1: JSONL (JSON Lines)

**Current Usage:** Primary format for real-time data ingestion and hot-tier storage

**Strengths:**

| Strength | Detail |
|----------|--------|
| Append-only writes | Perfect for streaming data |
| Human readable | Easy debugging and inspection |
| Schema flexible | Handles evolving event schemas |
| Line-oriented | Simple crash recovery (last complete line) |
| Compression friendly | Gzip/LZ4/ZSTD compress well |
| Universal tooling | Every language can parse JSON |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Query inefficiency | Full scan required for filters |
| Storage overhead | Text format larger than binary |
| Parse overhead | JSON parsing CPU-intensive |
| No columnar access | Cannot read single fields efficiently |
| Type coercion | Numbers as strings, precision issues |

**Performance Characteristics:**

| Metric | Typical Value |
|--------|---------------|
| Write throughput | 100K+ events/second |
| Read throughput | 10-50K events/second |
| Compression ratio (gzip) | 5-10x |
| Compression ratio (lz4) | 3-5x |
| Compression ratio (zstd) | 6-12x |
| Storage per trade event | ~200-500 bytes (uncompressed) |

**Best For:**
- Real-time data ingestion
- Short-term hot storage
- Data interchange
- Debugging and validation

---

### Format 2: Parquet

**Current Usage:** Archival format for cold storage and analytical export

**Strengths:**

| Strength | Detail |
|----------|--------|
| Columnar storage | Efficient single-column queries |
| Excellent compression | 10-50x typical for market data |
| Predicate pushdown | Filter at storage layer |
| Schema evolution | Add columns without rewrite |
| Industry standard | Spark, Pandas, DuckDB support |
| Statistics | Min/max/count in metadata |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Write complexity | Not append-friendly |
| Batch oriented | Requires buffering for writes |
| Memory overhead | Needs row group in memory |
| Small file overhead | Metadata dominates small files |
| Not streamable | Must write complete file |

**Performance Characteristics:**

| Metric | Typical Value |
|--------|---------------|
| Write throughput | Batch-dependent |
| Read throughput (full scan) | 1M+ rows/second |
| Read throughput (columnar) | 10M+ values/second |
| Compression ratio | 10-50x |
| Query speedup vs JSONL | 10-100x for filtered queries |

**Best For:**
- Long-term archival
- Analytical queries
- Data sharing/distribution
- Research and backtesting

---

### Format 3: Additional Export Formats

The AnalysisExportService supports seven output formats, each with pre-built profiles optimized for specific tools:

| Format | Built-in Profile | Target Tool | Key Features |
|--------|-----------------|-------------|-------------|
| **Parquet** | Python/Pandas | pandas.read_parquet() | Snappy compression, datetime64[ns] timestamps |
| **CSV** | R Statistics | R | Proper NA handling, ISO dates |
| **JSONL** | (general) | Any | Human-readable, streaming-compatible |
| **Lean** | QuantConnect Lean | Lean Engine | Native format, zip packaging, per-symbol/date split |
| **XLSX** | Microsoft Excel | Excel | Multi-sheet by symbol, 1M row limit per file |
| **SQL** | PostgreSQL | PostgreSQL/TimescaleDB | COPY commands with DDL scripts |
| **Arrow** | Apache Arrow (Feather) | PyArrow, R, Julia, Spark | Zero-copy IPC format, nanosecond timestamps |

Each profile includes configurable timestamp handling (ISO 8601, Unix seconds/milliseconds/nanoseconds, Excel serial), optional data dictionary generation, and optional loader script generation.

### Format 4: Alternatives Considered

#### Binary Formats

| Format | Pros | Cons | Verdict |
|--------|------|------|---------|
| **Protobuf** | Compact, fast, schema-enforced | Requires schema compilation | Good for internal, not archival |
| **MessagePack** | JSON-like but binary | Less tooling than JSON | Minor benefit for added complexity |
| **FlatBuffers** | Zero-copy access | Complex setup | Overkill for this use case |
| **Avro** | Schema evolution, Hadoop native | Less common outside Hadoop | Consider for Spark integration |

#### Time-Series Databases

| Database | Pros | Cons | Verdict |
|----------|------|------|---------|
| **TimescaleDB** | SQL interface, mature | Postgres dependency | Good alternative, more operational overhead |
| **InfluxDB** | Purpose-built for time-series | Proprietary query language | Vendor lock-in concern |
| **QuestDB** | Excellent performance | Younger ecosystem | Worth evaluating |
| **ClickHouse** | Exceptional query speed | Operational complexity | Consider for large-scale deployment |

**Current Decision Rationale:**

The JSONL + Parquet combination was chosen because:

1. **Simplicity** - File-based storage requires no database operations
2. **Portability** - Files can be copied, shared, backed up trivially
3. **Tooling** - Both formats have universal tool support
4. **Separation of concerns** - Write-optimized (JSONL) vs read-optimized (Parquet)
5. **Cost** - No database licensing or operational costs
6. **Export flexibility** - 7 export formats cover the major analysis ecosystems

---

## C. Compression Evaluation

### Current Compression Profiles

The `CompressionProfileManager` provides six built-in profiles with context-aware selection:

| Profile ID | Name | Codec | Level | Priority | Expected Ratio | Expected Throughput |
|------------|------|-------|-------|----------|----------------|---------------------|
| `real-time-collection` | Real-Time Collection | LZ4 | 1 | Speed | 2.5x | 500 MB/s |
| `warm-archive` | Warm Archive | ZSTD | 6 | Balanced | 5.0x | 150 MB/s |
| `cold-archive` | Cold Archive | ZSTD | 19 | Size | 10.0x | 20 MB/s |
| `high-volume-symbols` | High-Volume Symbols | ZSTD | 3 | Speed | 3.5x | 300 MB/s |
| `portable-export` | Portable Export | Gzip | 6 | Balanced | 6.0x | 100 MB/s |
| `no-compression` | No Compression | None | 0 | Speed | 1.0x | 1000 MB/s |

The `high-volume-symbols` profile automatically targets high-frequency symbols (SPY, QQQ, AAPL, MSFT, TSLA, NVDA, AMD) with ZSTD-3 for fast compression at a reasonable ratio.

### Supported Codecs

| Codec | Native .NET Support | Implementation Notes |
|-------|---------------------|---------------------|
| None | Yes | Pass-through |
| LZ4 | Via `K4os.Compression.LZ4.Streams` | Native LZ4 via K4os.Compression.LZ4.Streams package |
| Gzip | Yes | Standard System.IO.Compression |
| ZSTD | Via `ZstdSharp.Port` | Native ZSTD via ZstdSharp.Port package |
| Brotli | Yes | System.IO.Compression.BrotliStream |
| Deflate | Yes | System.IO.Compression.DeflateStream |

### Compression Comparison (Market Data)

| Algorithm | Ratio | Compress Speed | Decompress Speed | CPU Usage |
|-----------|-------|----------------|------------------|-----------|
| None | 1x | N/A | N/A | None |
| LZ4 | 3-5x | 500 MB/s | 1500 MB/s | Very Low |
| Gzip-6 | 5-10x | 50 MB/s | 200 MB/s | Medium |
| Gzip-9 | 6-12x | 20 MB/s | 200 MB/s | High |
| ZSTD-3 | 6-10x | 200 MB/s | 500 MB/s | Low |
| ZSTD-19 | 8-15x | 10 MB/s | 500 MB/s | Very High |
| Brotli-11 | 10-18x | 5 MB/s | 300 MB/s | Very High |

### Context-Aware Selection

The `CompressionProfileManager` selects profiles based on:

1. **Symbol-specific overrides** - Custom profiles for individual symbols
2. **Storage tier mapping** - Hot → `real-time-collection`, Warm → `warm-archive`, Cold → `cold-archive`
3. **Export context** - `portable-export` for data sharing

A benchmarking API (`BenchmarkAsync`) allows profiling all compression profiles against sample data to identify the optimal choice for a given dataset.

### Recommendations

| Tier | Current Profile | Assessment |
|------|----------------|------------|
| Hot (real-time) | LZ4 (native via K4os.Compression.LZ4.Streams) | Correct priority; native LZ4 performance now realized |
| Warm (recent) | ZSTD-6 (native via ZstdSharp.Port) | Correct balance; native ZSTD now realized |
| Cold (archive) | ZSTD-19 (native via ZstdSharp.Port) | Maximum compression; native ZSTD now realized |
| Export | Gzip-6 | Correct for universal compatibility |

---

## D. Tiered Storage Evaluation

### Current Tier Configuration

The system defines five storage tiers:

| Tier | Purpose | Typical Retention | Compression | Storage Media |
|------|---------|-------------------|-------------|---------------|
| Hot | Real-time access | 7 days | LZ4/None | NVMe SSD |
| Warm | Recent history | 30-90 days | Gzip/ZSTD-6 | SSD/HDD |
| Cold | Long-term archive | 180-365 days | ZSTD-19/Parquet | HDD/NAS |
| Archive | Rare bulk access | Indefinite | ZSTD-19/Parquet | Object storage |
| Glacier | Emergency only | Indefinite | Maximum | Tape/deep archive |

### Tier Configuration (Code)

```csharp
public sealed class TierConfig
{
    public string Name { get; init; }
    public string Path { get; init; }
    public int? MaxAgeDays { get; init; }
    public long? MaxSizeGb { get; init; }
    public string Format { get; init; }           // jsonl, jsonl.gz, parquet
    public CompressionCodec? Compression { get; init; }
    public string? StorageClass { get; init; }     // S3 storage class for cloud tiers
}
```

The `StorageClass` property supports cloud-tier configuration (e.g., S3 Standard, Infrequent Access, Glacier), addressing the prior recommendation for cloud storage support.

### Tier Migration Process

```
1. Data arrives → Hot tier (JSONL + LZ4)
   ↓ (configurable MaxAgeDays, default 7)
2. LifecyclePolicyEngine evaluates → Warm tier (recompress to Gzip/ZSTD-6)
   ↓ (configurable MaxAgeDays, default 30-90)
3. LifecyclePolicyEngine evaluates → Cold tier (convert to Parquet + ZSTD-19)
   ↓ (configurable MaxAgeDays, default 180-365)
4. ArchivalStorageService → Archive tier (Parquet + ZSTD-19, optional cloud)
```

### Migration Planning and Execution

The `TierMigrationService` provides:

| Capability | Method | Description |
|------------|--------|-------------|
| Plan migration | `PlanMigrationAsync` | Generate migration plan with estimated savings |
| Execute migration | `MigrateAsync` | Parallel file migration with progress callbacks |
| Determine tier | `DetermineTargetTier` | Classify file by current tier based on age |
| Tier statistics | `GetTierStatisticsAsync` | File count, total bytes, age range per tier |

Migration supports:
- **Parallel execution** via configurable `ParallelFiles` (default 4)
- **Checksum verification** during copy
- **Format conversion** (JSONL → Parquet)
- **Compression upgrade** per tier configuration
- **Source deletion** after successful migration
- **Progress callbacks** for UI/monitoring integration

### Lifecycle Policy Engine

The `LifecyclePolicyEngine` (`Storage/Services/LifecyclePolicyEngine.cs`) automates tier lifecycle decisions:

- Evaluates all data files against their `StoragePolicyConfig`
- Computes target tier based on file age vs configured thresholds
- Generates `LifecycleAction` list (tier migration, compression upgrade, retention enforcement)
- Supports per-event-type policies with different tier thresholds
- Handles data classification: Critical (never delete), Standard, Transient, Derived
- Archive policies support regulatory compliance (SEC Rule 17a-4, MiFID II) with immutability and encryption requirements

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Automatic lifecycle | LifecyclePolicyEngine evaluates and plans migrations |
| Parallel migration | Configurable parallelism (default 4 concurrent files) |
| Migration planning | Preview-before-execute with estimated savings |
| Cost optimization | Cold tier highly compressed; cloud storage class support |
| Configurable | Per-event-type tier thresholds |
| Regulatory support | Immutability, encryption, and compliance archive policies |
| Cron scheduling | Maintenance runs during off-hours via configurable cron |
| Progress tracking | Real-time progress callbacks for monitoring |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Storage spike | Temporary 2x storage during migration |
| Cloud tier not wired | `StorageClass` config property exists but no S3/Azure upload implementation |
| No incremental Parquet | JSONL → Parquet conversion requires full rewrite |

**Remaining Recommendations:**

1. **Wire cloud storage uploads** - S3/Azure Blob integration using the existing `StorageClass` configuration
2. **Implement incremental Parquet append** - Via row group appending for ongoing tier migration

---

## E. Write-Ahead Log (WAL) Evaluation

### Current Implementation

Location: `Storage/Archival/WriteAheadLog.cs`

**Purpose:** Ensure data durability by writing to WAL before primary storage.

**WAL Record Structure:**
```
{sequence}|{timestamp:ISO8601}|{recordType}|{sha256-checksum}|{json-payload}
```

**WAL File Header:** `MDCWAL01|1|{timestamp:ISO8601}`

**Process:**
```
1. Event received by pipeline
2. Serialize to JSON (high-performance options)
3. Compute SHA-256 checksum
4. Write record to WAL file (WriteThrough + Async)
5. Flush based on sync mode
6. Write to primary storage (async)
7. Write COMMIT record with committed-through sequence
8. Periodic WAL truncation (with optional gzip archival)
```

### WAL Configuration

```csharp
public sealed class WalOptions
{
    public long MaxWalFileSizeBytes { get; set; } = 100 * 1024 * 1024;  // 100MB rotation
    public TimeSpan? MaxWalFileAge { get; set; } = TimeSpan.FromHours(1);
    public WalSyncMode SyncMode { get; set; } = WalSyncMode.BatchedSync;
    public int SyncBatchSize { get; set; } = 1000;
    public TimeSpan MaxFlushDelay { get; set; } = TimeSpan.FromSeconds(1);
    public bool ArchiveAfterTruncate { get; set; } = true;
    public long UncommittedSizeWarningThreshold { get; set; } = 50 * 1024 * 1024;  // 50MB
}
```

### Sync Modes

| Mode | Behavior | Throughput | Durability |
|------|----------|------------|------------|
| NoSync | OS-buffered writes only | 200K+ events/sec | Low |
| **BatchedSync** (default) | Flush every 1000 records or 1 second | 100K+ events/sec | High |
| EveryWrite | Flush after every record | 10K events/sec | Maximum |

### Recovery

Recovery uses `IAsyncEnumerable<WalRecord>` for streaming reads, avoiding loading the entire WAL into memory:

1. **Two-pass recovery:** First pass finds last committed sequence; second pass streams uncommitted records
2. **Batched yielding:** Records are yielded in batches of 10,000 for memory efficiency
3. **Checksum verification:** Each record's SHA-256 checksum is validated during recovery
4. **Large WAL warning:** Logs a warning when uncommitted data exceeds 50MB threshold

### Evaluation

**Strengths:**

| Strength | Detail |
|----------|--------|
| Data durability | Survives process crash with checksummed records |
| Streaming recovery | IAsyncEnumerable avoids loading full WAL into memory |
| SHA-256 checksums | Detects corrupted records during recovery |
| Monotonic sequences | Gap detection between records |
| Configurable sync | Three sync modes balance durability vs performance |
| File rotation | Automatic rotation at 100MB or 1 hour |
| WAL archival | Truncated WAL files are gzip-compressed and archived |
| Uncommitted alerts | Warning logged when uncommitted data exceeds threshold |
| WriteThrough | Uses FileOptions.WriteThrough for direct disk writes |

**Weaknesses:**

| Weakness | Detail |
|----------|--------|
| Write amplification | 2x writes (WAL + primary) |
| Single-node only | No replication to secondary node |
| Two-pass recovery | Recovery reads WAL files twice (for commit marker then data) |
| Text-based records | Pipe-delimited text format; binary would be more compact |

**Performance Impact:**

| Configuration | Throughput | Durability |
|---------------|------------|------------|
| EveryWrite | ~10K events/sec | Maximum |
| BatchedSync (1000 records / 1s) | ~100K events/sec | High |
| NoSync | ~200K+ events/sec | Low |

**Assessment:** The default BatchedSync with 1000-record batches and 1-second max flush delay provides an appropriate balance for market data collection. The streaming recovery with batched yielding is a good design that avoids OOM scenarios with large WAL files.

---

## F. Quota and Capacity Management

### Quota Enforcement (New Since Last Review)

The `QuotaEnforcementService` provides granular storage capacity management:

| Quota Level | Configuration | Description |
|-------------|---------------|-------------|
| Global | `Quotas.Global` | Total storage limit across all data |
| Per-Source | `Quotas.PerSource` | Limits per data provider (e.g., Alpaca, Polygon) |
| Per-Symbol | `Quotas.PerSymbol` | Limits per symbol (e.g., SPY, AAPL) |
| Per-Event-Type | `Quotas.PerEventType` | Limits by event type (Trade, Quote, Depth) |

### Enforcement Policies

| Policy | Behavior |
|--------|----------|
| Warn | Log warning, continue writing |
| SoftLimit | Start cleanup, continue writing |
| HardLimit | Stop writing until cleanup completes |
| DropOldest | Delete oldest files to make room |

### Dynamic Quotas

```csharp
public sealed class DynamicQuotaConfig
{
    public bool Enabled { get; init; }
    public TimeSpan EvaluationPeriod { get; init; } = TimeSpan.FromHours(1);
    public double MinReservePct { get; init; } = 10;
    public double OverprovisionFactor { get; init; } = 1.1;
    public bool StealFromInactive { get; init; }
}
```

Dynamic quotas periodically rebalance capacity allocation, maintaining a configurable reserve, allowing burst above quota by an overprovision factor, and optionally reallocating from inactive sources.

### Storage Capacity Planning

#### Typical Data Volumes

| Data Type | Size per Event | Events per Day (active symbol) | Daily Size |
|-----------|----------------|--------------------------------|------------|
| Trade | 150-300 bytes | 50,000-500,000 | 15-150 MB |
| Quote | 200-400 bytes | 100,000-1,000,000 | 40-400 MB |
| L2 Depth | 500-2000 bytes | 10,000-100,000 | 10-200 MB |
| Daily Bar | 100-200 bytes | 1 | ~150 bytes |

#### Storage Projections (100 symbols)

| Timeframe | Raw JSONL | Compressed (ZSTD) | Parquet |
|-----------|-----------|-------------------|---------|
| 1 day | 5-50 GB | 0.5-5 GB | 0.3-3 GB |
| 1 month | 150-1500 GB | 15-150 GB | 9-90 GB |
| 1 year | 1.8-18 TB | 180-1800 GB | 108-1080 GB |

### Evaluation

**Assessment:** The quota enforcement system addresses the prior recommendation for retention policy automation. Granular per-source/per-symbol quotas with dynamic rebalancing provide production-ready capacity management. The DropOldest and SoftLimit policies enable autonomous operation without manual intervention.

---

## G. Data Catalog and Lineage

### Storage Catalog (New Since Last Review)

The `StorageCatalogService` (`Storage/Services/StorageCatalogService.cs`) maintains a persistent catalog of all stored data:

- **Directory indexes** (`_index.json`) track files per directory
- **Global manifest** (`_catalog/manifest.json`) aggregates the full catalog
- **File-level metadata:** file name, size, last modified, compression, format, event count, timestamp range, sequence range, symbol, event type, source
- **Schema tracking** via dedicated `schemas/` directory
- **Thread-safe** via `ConcurrentDictionary` for file index and `SemaphoreSlim` for catalog persistence

The `CatalogSyncSink` decorator automatically keeps the catalog in sync with live writes by tracking per-file metadata between flushes using lock-free `Interlocked` operations:

```
AppendAsync → Track metadata (InterlockedMin/Max for timestamps, sequences)
FlushAsync → Inner sink flush → Sync dirty paths to catalog → Persist catalog
```

### Data Lineage (New Since Last Review)

The `DataLineageService` (`Storage/Services/DataLineageService.cs`) tracks:

- **Ingestion records** - Where data came from (provider, timestamp, event counts)
- **Transformation records** - What processing was applied (tier migration, format conversion, compression)
- **Dependency graphs** - Upstream/downstream relationships between files
- **Persistent storage** - Lineage graphs saved to disk

### Evaluation

**Strengths:**
- Automatic catalog sync during writes eliminates need for periodic full rebuilds
- Lock-free metadata tracking minimizes impact on write throughput
- Lineage graphs enable data provenance auditing
- Schema tracking supports evolution monitoring

**Weaknesses:**
- Catalog persistence is best-effort (non-blocking for write path)
- Lineage storage is file-based (could be slow for large graphs)
- No query API for cross-referencing lineage and catalog

---

## H. Data Integrity Evaluation

### Current Integrity Measures

| Measure | Implementation | Location | Effectiveness |
|---------|----------------|----------|---------------|
| WAL | Write-ahead log with SHA-256 checksums | `WriteAheadLog` | High |
| Atomic writes | AtomicFileWriter for safe file operations | `AtomicFileWriter` | High |
| File checksums | StorageChecksumService per-file verification | `StorageChecksumService` | High |
| Sequence validation | Monotonic sequence tracking in WAL and CatalogSyncSink | Multiple | High |
| Schema validation | SchemaVersionManager tracks schema evolution | `SchemaVersionManager` | Medium |
| Quality scoring | Multi-dimensional quality assessment | `DataQualityScoringService` | High |
| Catalog sync | CatalogSyncSink keeps indexes current | `CatalogSyncSink` | Medium |
| Data lineage | Full provenance tracking | `DataLineageService` | Medium |

### Quality Scoring Dimensions

The `DataQualityScoringService` computes scores across:

- **Completeness** - Expected vs actual event counts
- **Sequence integrity** - Gap and out-of-order detection
- **Latency** - Ingestion freshness
- **Cross-source consistency** - Agreement between overlapping providers
- **Best-of-breed selection** - Pick highest-quality source for overlapping data

### Evaluation

**Assessment:** Data integrity has improved significantly since the initial evaluation. The combination of WAL checksums, atomic writes, per-file checksum verification, sequence tracking, and multi-dimensional quality scoring provides layered integrity guarantees. The addition of data lineage tracking enables auditing the full data lifecycle.

**Remaining gap:** End-to-end hash chains for tamper detection across file boundaries are not yet implemented.

---

## I. Query Performance Evaluation

### Current Query Patterns

| Query Type | Format | Performance |
|------------|--------|-------------|
| Recent data (< 7 days) | JSONL + LZ4 | Sequential scan, fast for small ranges |
| Historical analysis | Parquet | Columnar scan, excellent for aggregations |
| Specific event lookup | JSONL | Sequential scan, slow |
| Cross-symbol analysis | Parquet | Good with predicate pushdown |
| Memory-mapped replay | JSONL | MemoryMappedJsonlReader for high-throughput replay |

### Performance Benchmarks (Typical)

| Query | JSONL Time | Parquet Time | Improvement |
|-------|------------|--------------|-------------|
| Full day scan (1 symbol) | 2-5 seconds | 0.2-0.5 seconds | 10x |
| VWAP calculation | 5-10 seconds | 0.5-1 second | 10x |
| Price filter (< threshold) | 10-30 seconds | 0.1-0.3 seconds | 100x |
| Multi-symbol aggregation | Minutes | Seconds | 50-100x |

### Query Optimization Recommendations

1. **Add bloom filters** - For symbol/type filtering in Parquet files
2. **Consider DuckDB** - For ad-hoc analytical queries over Parquet
3. **Leverage MemoryMappedJsonlReader** - For hot-tier replay scenarios

---

## J. Portable Data Packaging

### Current Capabilities

The `PortableDataPackager` creates self-contained packages for data sharing:

| Feature | Detail |
|---------|--------|
| Formats | ZIP, TAR.GZ |
| Compression levels | None, Fast (Deflate L1), Balanced (Deflate L6), Maximum (Deflate L9) |
| Contents | Data files + manifest.json + README + data dictionary + loader scripts |
| Loader scripts | Python and R auto-generated loaders |
| Checksums | SHA-256 per file |
| Metadata | Schema definitions, event counts, sequence ranges, quality metrics |
| Import | Merge or replace semantics with optional validation skip |
| HTTP API | Create, import, validate, list, download endpoints |

### Package Structure

```
my-package_20260103_120000.zip
├── manifest.json          # Checksums, schemas, quality metrics
├── README.md
├── data/
│   └── 2026-01-03/AAPL/Trade/...
├── metadata/
│   └── data_dictionary.md
└── scripts/
    ├── load_data.py
    └── load_data.R
```

---

## K. Comparative Analysis: File-Based vs Database

### File-Based (Current Approach)

**Advantages:**

| Advantage | Detail |
|-----------|--------|
| Simplicity | No database to operate |
| Portability | Copy files to share data; 7 export formats |
| Cost | No licensing fees |
| Backup | Standard file backup tools |
| Performance | Excellent write throughput |
| Catalog | Auto-maintained catalog with lineage tracking |
| Quotas | Granular capacity management without DBA |

**Disadvantages:**

| Disadvantage | Detail |
|--------------|--------|
| Query flexibility | Limited ad-hoc queries (no SQL) |
| Indexing | Catalog provides metadata index but not field-level |
| Transactions | WAL provides durability but not full ACID |
| Concurrency | File locking via SemaphoreSlim, not multi-process |

### Verdict

The file-based approach remains appropriate for Meridian because:

1. **Primary use case is archival** - Write-heavy, read-occasional
2. **Portability matters** - Data sharing via packages and 7 export formats
3. **Team size** - No dedicated DBA required
4. **Query patterns** - Mostly sequential scans or batch analysis
5. **Cost sensitivity** - No database licensing costs
6. **Capacity management** - Quota enforcement and lifecycle automation now built in
7. **Data lineage** - Full provenance tracking without database dependency

**Consider database if:**
- Real-time analytical queries become the primary use case
- Multi-user concurrent access is required
- Sub-second query latency on arbitrary filters is critical
- Data volume exceeds local + cloud storage capacity

---

## L. Summary: Changes Since Last Review

### Implemented (Previously Recommended)

| Prior Recommendation | Status | Implementation |
|---------------------|--------|----------------|
| Parallelize tier migration | **Done** | `TierMigrationService` with configurable `ParallelFiles` |
| Implement retention policy automation | **Done** | `LifecyclePolicyEngine` + `QuotaEnforcementService` |
| Add data validation pipeline | **Done** | `DataQualityScoringService` with multi-dimensional scoring |
| Add migration scheduling | **Done** | Cron-based scheduling via `TieringOptions.MigrationSchedule` |
| Add native LZ4/ZSTD packages | **Done** | `K4os.Compression.LZ4.Streams` + `ZstdSharp.Port` integrated in `CompressionProfileManager` |

### New Capabilities (Not in Prior Review)

| Capability | Component |
|------------|-----------|
| 7-format export pipeline | `AnalysisExportService` |
| Granular quota enforcement | `QuotaEnforcementService` |
| Dynamic quota rebalancing | `DynamicQuotaConfig` |
| Data lineage tracking | `DataLineageService` |
| Catalog sync on write | `CatalogSyncSink` |
| 6 compression profiles | `CompressionProfileManager` |
| Compression benchmarking | `CompressionProfileManager.BenchmarkAsync` |
| Native LZ4 compression | `CompressionProfileManager` via `K4os.Compression.LZ4.Streams` |
| Native ZSTD compression | `CompressionProfileManager` via `ZstdSharp.Port` |
| 8 naming conventions | `FileNamingConvention` (was 4) |
| Multi-dimensional partitioning | `PartitionStrategy` |
| 5 storage tiers | `StorageTier` (was 3) |
| Storage profiles | `StorageProfilePresets` (Research, LowLatency, Archival) |
| Regulatory archive policies | `ArchivePolicyConfig` with compliance support |
| Memory-mapped JSONL reader | `MemoryMappedJsonlReader` |
| Symbol-specific compression | `CompressionProfileManager.SetSymbolOverride` |
| Cross-platform file permissions | `FilePermissionsService` (Unix 0755/0644 + Windows NTFS ACLs) |
| Retention compliance reporting | `RetentionComplianceReporter` (machine-readable audit reports) |
| Attribute-based sink discovery | `StorageSinkRegistry` + `StorageSinkAttribute` (ADR-005 pattern for storage plugins) |

---

## M. Remaining Recommendations

### Retain Current Architecture

The storage architecture is well-designed and has addressed most prior concerns. Retain:

1. **JSONL for hot tier** - Optimal for streaming ingestion
2. **Parquet for cold tier** - Optimal for analysis
3. **CompositeSink for multi-format writes** - Clean separation of concerns
4. **WAL for durability** - Appropriate checksummed crash recovery
5. **BySymbol organization as default** - Matches primary access patterns
6. **Lifecycle policy engine** - Automated tier management
7. **Quota enforcement** - Production-ready capacity management

### Recommended Improvements

| Priority | Improvement | Benefit | Complexity |
|----------|-------------|---------|------------|
| **High** | Wire cloud storage uploads (S3/Azure) | Use existing `StorageClass` config for cloud cold tier | Medium |
| Medium | Add DuckDB query layer | Ad-hoc analytical queries over Parquet | Medium |
| Medium | Add end-to-end hash chains | Tamper detection across file boundaries | Medium |
| Medium | Single-pass WAL recovery | Eliminate the two-pass recovery approach | Low |
| Low | Binary WAL record format | Reduce WAL write amplification | Medium |
| Low | Multi-node WAL replication | High-availability durability | High |
| Low | Incremental Parquet append | Row group appending for ongoing migration | Medium |

### Architecture Evolution Path

```
Current State (v1.7.0)
    ├── Wire cloud cold tier (S3/Azure using existing StorageClass config)
    ├── Add DuckDB query layer for analytics
    └── Add WAL replication for HA
        ↓
Future State: Hybrid local + cloud + optional query engine
```

---

## Key Insight

The storage architecture follows the principle of **separation of concerns**: write-optimized format (JSONL) for ingestion, read-optimized format (Parquet) for analysis, with tiered storage managing the lifecycle. This design has been extended with layered quality assurance (WAL checksums, file checksums, quality scoring, catalog sync) and operational automation (lifecycle policies, quota enforcement, scheduled maintenance).

The primary investment areas should be:

1. **Cloud tiering** - Wire the existing `StorageClass` configuration to actual cloud storage uploads
2. **Query optimization** - Add DuckDB or similar for research and backtesting workflows over the Parquet cold tier

The architecture is well-positioned for production use. Major structural changes (e.g., time-series database) remain unjustified given current requirements.

---

*Evaluation Date: 2026-03-15*
*Previous Evaluation: 2026-02-22*
