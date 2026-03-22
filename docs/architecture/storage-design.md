# Storage Organization Design: Improvements & Best Practices

This document outlines storage organization improvements for the Meridian, covering naming conventions, date partitioning, policies, capacity limits, and perpetual data management strategies.


> **Document status:** Living architecture reference — reflects the implemented storage layer.
>
> **Last updated:** 2026-03-14
>
> **Audience:** Platform engineers, storage/infrastructure owners, and data operations.

> ## Primary Mission: Data Collection & Archival
>
> The Meridian is designed as a **collection and archival system**. Its primary purpose is:
>
> 1. **Reliable Data Collection**: Capture market data from multiple sources with minimal gaps
> 2. **Long-Term Archival**: Store data securely with integrity verification for years or indefinitely
> 3. **Export for External Analysis**: Provide clean, well-organized data for analysis in external tools
>
> **Analysis is performed externally** using specialized tools such as Python, R, QuantConnect Lean, databases, or custom applications. This focus shapes all storage design decisions—prioritizing archival durability, data integrity, and export flexibility over real-time analytics capabilities.
>
> ### Design Principles
>
> | Principle | Description |
> |-----------|-------------|
> | **Archival First** | Optimize for long-term storage, verification, and retrieval |
> | **Collection Integrity** | Ensure gap-free, fault-tolerant data capture |
> | **Export Excellence** | Make data extraction seamless for external tools |
> | **Future Flexibility** | Maintain compatibility for cloud/hybrid storage when needed |
> | **Self-Describing Data** | Include manifests, schemas, and metadata with all stored data |
>
> While the system maintains flexibility for future integration with cloud storage, real-time streaming, and online databases, the current priority is building a robust, self-contained offline archive that serves as the authoritative source for all collected market data.

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Naming Convention Improvements](#naming-convention-improvements)
3. [Date Partitioning Strategies](#date-partitioning-strategies)
4. [Storage Policies](#storage-policies)
5. [Capacity Limits & Quotas](#capacity-limits--quotas)
6. [Perpetual Data Management](#perpetual-data-management)
7. [Multi-Source Data Organization](#multi-source-data-organization)
8. [Tiered Storage Architecture](#tiered-storage-architecture)
9. [File Maintenance & Health Monitoring](#file-maintenance--health-monitoring)
10. [Data Robustness & Quality Scoring](#data-robustness--quality-scoring)
11. [Search & Discovery Infrastructure](#search--discovery-infrastructure)
12. [Actionable Metadata & Insights](#actionable-metadata--insights)
13. [Operational Scheduling & Off-Hours Maintenance](#operational-scheduling--off-hours-maintenance)
14. [External Analysis Export Architecture](#external-analysis-export-architecture)
15. [Collection Session Management](#collection-session-management)
16. [Implementation Roadmap](#implementation-roadmap)
17. [Decision Log](#decision-log)
18. [Open Questions](#open-questions)

---

## Executive Summary

### Implemented Capabilities
The storage layer currently delivers:
- 8 file naming conventions (Flat, BySymbol, ByDate, ByType, BySource, ByAssetClass, Hierarchical, Canonical)
- 4 date partition granularities (None, Daily, Hourly, Monthly)
- Multi-dimensional `PartitionStrategy` (primary + secondary + tertiary dimensions)
- 5 compression codecs (None, LZ4, Gzip, Zstd, Brotli)
- Time-based retention (`RetentionDays`) and capacity-based retention (`MaxTotalBytes`)
- 3 built-in storage profiles: **Research**, **LowLatency**, **Archival** (via `StorageProfilePresets`)
- Tiered storage with hot/warm/cold/archive tiers (`TieringOptions` + `TierMigrationService`)
- Per-source, per-symbol, and per-event-type quota management (`QuotaOptions`)
- Per-event-type lifecycle policies with archive support (`StoragePolicyConfig`)
- Write-Ahead Log for crash-safe durability (`WriteAheadLog`)
- Dual-sink fan-out: JSONL + Parquet simultaneously (`CompositeSink` with circuit breaker)
- Plugin-based sink discovery (`StorageSinkRegistry`)
- Storage catalog with manifest generation and integrity verification (`StorageCatalogService`)
- Data quality scoring across 6 dimensions (`DataQualityScoringService`)
- Data lineage tracking (`DataLineageService`)
- File metadata tagging (`MetadataTagService`)
- Archive maintenance scheduling with cron support (`ScheduledArchiveMaintenanceService`)
- Portable data packaging with import/export scripts (`PortableDataPackager`)
- 6 built-in export profiles for Python, R, QuantConnect Lean, Excel, PostgreSQL, Arrow (`ExportProfile`)
- File maintenance and health checking (`FileMaintenanceService`)
- Storage search service (`StorageSearchService`)
- Parquet conversion pipeline (`ParquetConversionService`)
- Checksum-based integrity verification (`StorageChecksumService`)

### Implementation Status Overview
| Area | Status | Key Classes |
|------|--------|-------------|
| Naming conventions (8 patterns) | ✅ Implemented | `FileNamingConvention`, `JsonlStoragePolicy` |
| Multi-dimensional partitioning | ✅ Implemented | `PartitionStrategy`, `PartitionDimension` |
| Tier-based lifecycle policies | ✅ Implemented | `StoragePolicyConfig`, `TierMigrationService` |
| Per-source/symbol quotas | ✅ Implemented | `QuotaOptions`, `QuotaEnforcementService` |
| Archive tier with cold storage | ✅ Implemented | `ArchivePolicyConfig`, `ArchivalStorageService` |
| Source registries | ✅ Implemented | `SourceRegistry`, `ISourceRegistry` |
| File health checks | ✅ Implemented | `FileMaintenanceService` |
| Quality scoring | ✅ Implemented | `DataQualityScoringService`, `DataQualityService` |
| Storage search & discovery | ✅ Implemented | `StorageSearchService`, `StorageCatalogService` |
| Rich metadata & lineage | ✅ Implemented | `MetadataTagService`, `DataLineageService` |
| Maintenance scheduling | ✅ Implemented | `ScheduledArchiveMaintenanceService` |
| Cross-source reconciliation | 🔄 Partial | `DataQualityService`, cross-provider comparisons |
| Natural language query parser | ⏳ Planned | — |
| Capacity forecasting | ⏳ Planned | — |
| Adaptive partitioning | ⏳ Planned | — |

### Storage Profiles (Presets)
Storage profiles are optional presets that map to existing storage options without removing advanced configuration.

| Profile | Compression | Manifests | Checksums | Partition Strategy | Granularity | Retention / Size |
|---------|------------|-----------|-----------|-------------------|-------------|-----------------|
| **Research** | Gzip | ✅ | — | Date + Symbol | Daily | Caller-defined |
| **LowLatency** | None | — | — | Symbol + EventType | Hourly | Caller-defined |
| **Archival** | Zstd | ✅ | ✅ | Date + Source | Monthly | 10 yr / 2 TB cap + 4-tier pipeline |

Profiles are applied by `StorageProfilePresets.ApplyProfile` and only adjust the options shown above unless the caller explicitly overrides them. The default profile when none is specified is **Research**.

## Event pipeline

Events emitted by each provider travel through a deterministic event pipeline that handles canonicalization, metadata tagging, and sink fan-out. The pipeline wires `MarketEvent` envelopes through the `EventPipelinePolicy`, `WriteAheadLog`, and the `CompositeSink` so storage tiers (JSONL, Parquet, catalog, and export services) all see the same canonicalized data.

## Storage services

Storage responsibilities are split into services that keep data organized, searchable, and healthy. `StorageCatalogService` maintains manifests and metadata, `StorageSearchService` powers lookups, `FileMaintenanceService` enforces retention/quotas, and `TierMigrationService` coordinates hot/warm/cold transitions.

## Storage sinks

Storage sinks describe the destinations the pipeline writes to. `StorageSinkRegistry` discovers available sinks, `CompositeSink` fans each event to JSONL/Parquet writers, and `ParquetConversionService` keeps the Parquet tier consistent with the JSONL master copy. Additional sinks (archives, exports, diagnostics) plug into the same registry so new storage targets can be added without touching the core pipeline.

---

## Naming Convention Improvements

### 1. Hierarchical Taxonomy Structure

**Proposed Directory Hierarchy:**
```
{root}/
├── _catalog/                    # Metadata & indices
│   ├── manifest.json            # Global catalog of all data
│   ├── sources.json             # Registered data sources
│   └── schemas/                 # Schema definitions per version
│       ├── v1.json
│       └── v2.json
├── _archive/                    # Cold storage tier
│   └── {year}/
│       └── {month}/
├── live/                        # Hot tier (current trading data)
│   └── {source}/
│       └── {asset_class}/
│           └── {symbol}/
│               └── {event_type}/
│                   └── {date}.jsonl.gz
└── historical/                  # Warm tier (backfill data)
    └── {provider}/
        └── {symbol}/
            └── {granularity}/
                └── {date_range}.parquet
```

### 2. Enhanced Naming Patterns

**Add new naming conventions:**

```csharp
enum FileNamingConvention
{
    // Existing
    Flat,           // {root}/{symbol}_{type}_{date}.jsonl
    BySymbol,       // {root}/{symbol}/{type}/{date}.jsonl
    ByDate,         // {root}/{date}/{symbol}/{type}.jsonl
    ByType,         // {root}/{type}/{symbol}/{date}.jsonl

    // NEW: Extended patterns
    BySource,       // {root}/{source}/{symbol}/{type}/{date}.jsonl
    ByAssetClass,   // {root}/{asset_class}/{symbol}/{type}/{date}.jsonl
    Hierarchical,   // {root}/{source}/{asset_class}/{symbol}/{type}/{date}.jsonl
    Canonical       // {root}/{year}/{month}/{day}/{source}/{symbol}/{type}.jsonl
}
```

### 3. Symbol Naming Standardization

**Implement canonical symbol registry:**

```json
{
  "symbols": {
    "AAPL": {
      "canonical": "AAPL",
      "aliases": ["AAPL.US", "AAPL.O", "US0378331005"],
      "asset_class": "equity",
      "exchange": "NASDAQ",
      "currency": "USD",
      "sedol": "2046251",
      "isin": "US0378331005",
      "figi": "BBG000B9XRY4"
    }
  }
}
```

**Benefits:**
- Unified symbol lookup across data sources
- Automatic alias resolution during queries
- Cross-reference with industry identifiers (ISIN, FIGI, SEDOL)

### 4. File Naming Metadata Encoding

**Embed queryable metadata in filenames:**

```
Format: {symbol}_{type}_{date}_{source}_{checksum}.jsonl.gz

Example: AAPL_Trade_2024-01-15_alpaca_a3f2b1.jsonl.gz
                                  │       │
                                  │       └── First 6 chars of SHA256
                                  └── Data source identifier
```

**Metadata index file (per directory):**
```json
{
  "files": [
    {
      "name": "AAPL_Trade_2024-01-15_alpaca_a3f2b1.jsonl.gz",
      "symbol": "AAPL",
      "type": "Trade",
      "date": "2024-01-15",
      "source": "alpaca",
      "checksum": "a3f2b1c4d5e6f7...",
      "size_bytes": 1048576,
      "event_count": 50000,
      "first_seq": 1000000,
      "last_seq": 1050000,
      "created_at": "2024-01-15T16:00:00Z"
    }
  ]
}
```

---

## Date Partitioning Strategies

### 1. Multi-Dimensional Partitioning

**Current:** Single partition dimension (date OR symbol OR type)

**Proposed:** Composite partitioning with configurable priority

```csharp
record PartitionStrategy(
    PartitionDimension Primary,      // e.g., Date
    PartitionDimension? Secondary,   // e.g., Symbol
    PartitionDimension? Tertiary,    // e.g., EventType
    DateGranularity DateFormat       // Daily, Hourly, Monthly
);

enum PartitionDimension
{
    Date,
    Symbol,
    EventType,
    Source,
    AssetClass,
    Exchange
}
```

**Example configurations:**

```json
{
  "Partitioning": {
    "Strategy": "composite",
    "Dimensions": ["Date", "Source", "Symbol"],
    "DateGranularity": "Daily"
  }
}
```

### 2. Adaptive Partitioning by Volume

**Auto-adjust partition granularity based on data volume:**

```csharp
record AdaptivePartitionConfig(
    long EventsPerHourThreshold = 100_000,  // Switch to hourly
    long EventsPerDayThreshold = 50_000,    // Stay at daily
    long EventsPerMonthThreshold = 10_000   // Switch to monthly
);
```

**Implementation logic:**
```
IF events_per_hour > 100,000 THEN use Hourly partitions
ELSE IF events_per_day > 50,000 THEN use Daily partitions
ELSE IF events_per_month < 10,000 THEN use Monthly partitions
```

### 3. Trading Calendar Awareness

**Align partitions with market calendars:**

```csharp
record TradingCalendarPartition(
    string Exchange,           // "NYSE", "NASDAQ", "CME"
    bool SkipNonTradingDays,   // Don't create files for holidays
    bool SeparatePreMarket,    // Pre-market in separate partition
    bool SeparateAfterHours,   // After-hours in separate partition
    TimeZoneInfo MarketTimeZone
);
```

**Directory structure with sessions:**
```
AAPL/Trade/
├── 2024-01-15_pre.jsonl.gz      # 04:00-09:30 ET
├── 2024-01-15_regular.jsonl.gz  # 09:30-16:00 ET
└── 2024-01-15_after.jsonl.gz    # 16:00-20:00 ET
```

### 4. Rolling Window Partitions

**For real-time analytics, maintain rolling windows:**

```csharp
record RollingPartitionConfig(
    TimeSpan WindowSize,        // e.g., 1 hour
    int WindowCount,            // Keep last N windows
    bool CompactOnRotation      // Merge into daily on rotation
);
```

**Example: 1-hour rolling windows, keep last 24:**
```
AAPL/Trade/
├── current.jsonl               # Active window (0-60 min old)
├── window_23.jsonl             # 1-2 hours old
├── window_22.jsonl             # 2-3 hours old
└── ...
└── window_00.jsonl             # 23-24 hours old → compact to daily
```

---

## Storage Policies

### 1. Lifecycle Policy Framework

**Define policies per data classification:**

```csharp
record StoragePolicy(
    string Name,
    DataClassification Classification,
    RetentionPolicy Retention,
    CompressionPolicy Compression,
    TieringPolicy Tiering,
    ReplicationPolicy? Replication
);

enum DataClassification
{
    Critical,       // Never delete (regulatory/compliance)
    Standard,       // Normal retention policies apply
    Transient,      // Short-lived, deletable quickly
    Derived         // Can be regenerated, aggressive cleanup
}
```

### 2. Tiered Retention Policies

```json
{
  "Policies": {
    "Trade": {
      "classification": "Critical",
      "hot_tier_days": 7,
      "warm_tier_days": 90,
      "cold_tier_days": 365,
      "archive_tier": "perpetual",
      "compression": {
        "hot": "none",
        "warm": "gzip",
        "cold": "zstd",
        "archive": "zstd-max"
      }
    },
    "L2Snapshot": {
      "classification": "Standard",
      "hot_tier_days": 3,
      "warm_tier_days": 30,
      "cold_tier_days": 180,
      "archive_tier": null,
      "compression": {
        "hot": "gzip",
        "warm": "zstd",
        "cold": "zstd-max"
      }
    },
    "Heartbeat": {
      "classification": "Transient",
      "hot_tier_days": 1,
      "warm_tier_days": 0,
      "cold_tier_days": 0,
      "archive_tier": null
    }
  }
}
```

### 3. Compression Policy Matrix

| Data Age | Compression | Ratio | CPU Cost | Use Case |
|----------|-------------|-------|----------|----------|
| Hot (< 7d) | None/LZ4 | 1-2x | Minimal | Real-time access |
| Warm (7-90d) | Gzip-6 | 5-8x | Low | Daily analytics |
| Cold (90-365d) | Zstd-12 | 10-15x | Medium | Monthly reports |
| Archive (> 1y) | Zstd-19 | 15-20x | High | Compliance/audit |

### 4. Integrity Policy

**Automatic data validation rules:**

```csharp
record IntegrityPolicy(
    bool ValidateOnWrite,           // Schema validation
    bool ChecksumOnWrite,           // SHA256 per file
    bool VerifyOnRead,              // Checksum verification
    int MaxSequenceGap,             // Alert threshold
    TimeSpan StaleDataThreshold,    // No data for N minutes = alert
    bool EnforceMonotonicity        // Reject out-of-order events
);
```

### 5. Backup Policy

```json
{
  "Backup": {
    "enabled": true,
    "schedule": "0 0 * * *",
    "retention_backups": 7,
    "targets": [
      {
        "type": "local",
        "path": "/backup/meridian"
      },
      {
        "type": "s3",
        "bucket": "meridian-backups",
        "region": "us-east-1",
        "storage_class": "GLACIER_IR"
      }
    ],
    "include_patterns": ["*.jsonl", "*.jsonl.gz", "*.parquet"],
    "exclude_patterns": ["**/current.jsonl", "**/_temp/*"]
  }
}
```

---

## Capacity Limits & Quotas

### 1. Hierarchical Quota System

**Multi-level capacity management:**

```csharp
record QuotaConfig(
    StorageQuota Global,
    Dictionary<string, StorageQuota> PerSource,
    Dictionary<string, StorageQuota> PerAssetClass,
    Dictionary<string, StorageQuota> PerSymbol,
    Dictionary<MarketEventType, StorageQuota> PerEventType
);

record StorageQuota(
    long MaxBytes,
    long? MaxFiles,
    long? MaxEventsPerDay,
    QuotaEnforcementPolicy Enforcement
);

enum QuotaEnforcementPolicy
{
    Warn,           // Log warning, continue writing
    SoftLimit,      // Start cleanup, continue writing
    HardLimit,      // Stop writing until cleanup completes
    DropOldest      // Delete oldest to make room
}
```

### 2. Per-Source Quotas

```json
{
  "Quotas": {
    "global": {
      "max_bytes": 107374182400,
      "enforcement": "SoftLimit"
    },
    "per_source": {
      "alpaca": {
        "max_bytes": 53687091200,
        "max_files": 100000,
        "enforcement": "DropOldest"
      },
      "ib": {
        "max_bytes": 53687091200,
        "enforcement": "SoftLimit"
      }
    },
    "per_symbol": {
      "default": {
        "max_bytes": 1073741824,
        "max_events_per_day": 10000000
      },
      "SPY": {
        "max_bytes": 10737418240,
        "max_events_per_day": 50000000
      }
    }
  }
}
```

### 3. Dynamic Quota Allocation

**Auto-adjust quotas based on usage patterns:**

```csharp
record DynamicQuotaConfig(
    bool Enabled,
    TimeSpan EvaluationPeriod,      // How often to rebalance
    double MinReservePct,            // Always keep N% free
    double OverprovisionFactor,      // Allow N% burst above quota
    bool StealFromInactive          // Reallocate from unused quotas
);
```

### 4. Capacity Forecasting

**Predict storage needs based on historical patterns:**

```csharp
interface ICapacityForecaster
{
    StorageForecast Forecast(TimeSpan horizon);
    Alert[] GetCapacityAlerts(double thresholdPct);
}

record StorageForecast(
    DateTimeOffset ForecastDate,
    long CurrentUsageBytes,
    long ProjectedUsageBytes,
    double GrowthRatePerDay,
    DateTimeOffset? EstimatedFullDate,
    Dictionary<string, long> BreakdownBySource
);
```

---

## Perpetual Data Management

### 1. Archive Tier Definition

**Data that must be kept indefinitely:**

```csharp
enum ArchiveReason
{
    Regulatory,         // SEC Rule 17a-4, MiFID II
    Compliance,         // Internal audit requirements
    Research,           // ML training datasets
    Legal,              // Litigation hold
    Historical          // Reference data
}

record ArchivePolicy(
    ArchiveReason Reason,
    string Description,
    bool Immutable,              // Write-once, never modify
    bool RequiresEncryption,
    TimeSpan? MinRetention,      // Minimum before deletion allowed
    string[] ApproversForDelete  // Required approvals
);
```

### 2. Perpetual Storage Organization

```
_archive/
├── regulatory/                  # SEC 17a-4 compliant
│   ├── manifest.json            # Cryptographically signed
│   └── 2024/
│       └── Q1/
│           ├── trades.parquet.zst
│           ├── trades.manifest.json
│           └── trades.sha256
├── research/                    # ML training datasets
│   ├── labeled/
│   │   └── market_regimes/
│   └── raw/
│       └── tick_data/
└── legal_holds/                 # Litigation preservation
    └── case_12345/
        ├── hold_notice.json
        └── preserved_data/
```

### 3. Write-Once Append-Only (WORM) Support

```csharp
record WormConfig(
    bool Enabled,
    TimeSpan LockDelay,          // Time before lock engages
    bool AllowExtend,            // Can extend retention, not shorten
    string ComplianceMode        // "governance" | "compliance"
);
```

### 4. Archive Format Optimization

**Convert to columnar format for long-term storage:**

```csharp
record ArchiveFormat(
    string Format,                // "parquet" | "orc" | "avro"
    string Compression,           // "zstd" | "snappy" | "brotli"
    int CompressionLevel,
    bool EnableBloomFilters,      // Fast existence checks
    bool EnableStatistics,        // Min/max per column
    string[] PartitionColumns,    // e.g., ["date", "symbol"]
    int RowGroupSize              // Rows per group (default 100K)
);
```

**Parquet conversion pipeline:**
```
Daily JSONL files (hot tier)
    ↓ (after 7 days)
Weekly Parquet files (warm tier)
    ↓ (after 90 days)
Monthly Parquet files (cold tier)
    ↓ (after 365 days)
Yearly Parquet files (archive tier, perpetual)
```

### 5. Data Catalog for Perpetual Data

```json
{
  "catalog": {
    "version": "1.0",
    "entries": [
      {
        "id": "uuid-v4",
        "path": "_archive/regulatory/2024/Q1/trades.parquet.zst",
        "type": "Trade",
        "symbols": ["AAPL", "GOOGL", "MSFT"],
        "date_range": {
          "start": "2024-01-01",
          "end": "2024-03-31"
        },
        "event_count": 150000000,
        "size_bytes": 2147483648,
        "checksum": "sha256:abc123...",
        "created_at": "2024-04-01T00:00:00Z",
        "archive_reason": "Regulatory",
        "retention_until": null,
        "immutable": true,
        "schema_version": 1
      }
    ]
  }
}
```

---

## Multi-Source Data Organization

### 1. Source Registry

**Centralized source management:**

```json
{
  "sources": {
    "alpaca": {
      "id": "alpaca",
      "name": "Alpaca Markets",
      "type": "live",
      "priority": 1,
      "asset_classes": ["equity"],
      "data_types": ["Trade", "BboQuote", "L2Snapshot"],
      "latency_ms": 10,
      "reliability": 0.999,
      "cost_per_event": 0.0001,
      "enabled": true
    },
    "ib": {
      "id": "ib",
      "name": "Interactive Brokers",
      "type": "live",
      "priority": 2,
      "asset_classes": ["equity", "options", "futures", "forex"],
      "data_types": ["Trade", "BboQuote", "L2Snapshot", "OrderFlow"],
      "latency_ms": 5,
      "reliability": 0.9999,
      "enabled": true
    },
    "polygon": {
      "id": "polygon",
      "name": "Polygon.io",
      "type": "live",
      "priority": 3,
      "asset_classes": ["equity", "crypto"],
      "enabled": false
    },
    "stooq": {
      "id": "stooq",
      "name": "Stooq Historical",
      "type": "historical",
      "asset_classes": ["equity"],
      "data_types": ["HistoricalBar"],
      "enabled": true
    }
  }
}
```

### 2. Source-Aware Directory Structure

```
data/
├── live/
│   ├── alpaca/
│   │   ├── equity/
│   │   │   ├── AAPL/
│   │   │   └── SPY/
│   │   └── _source_meta.json
│   └── ib/
│       ├── equity/
│       ├── options/
│       └── futures/
├── historical/
│   ├── stooq/
│   ├── yahoo/
│   └── nasdaq/
└── consolidated/               # Merged view across sources
    └── AAPL/
        └── Trade/
            └── 2024-01-15.parquet  # Best-of-breed merged
```

### 3. Source Conflict Resolution

**When multiple sources have overlapping data:**

```csharp
record ConflictResolutionPolicy(
    ConflictStrategy Strategy,
    string[] PriorityOrder,          // ["ib", "alpaca", "polygon"]
    bool KeepAll,                    // Store all versions
    bool CreateConsolidated          // Merge into golden record
);

enum ConflictStrategy
{
    FirstWins,          // Keep first source's data
    LastWins,           // Keep latest source's data
    HighestPriority,    // Use priority order
    LowestLatency,      // Prefer lowest-latency source
    MostComplete,       // Source with most fields populated
    Merge               // Combine and deduplicate
}
```

### 4. Cross-Source Reconciliation

```csharp
record ReconciliationConfig(
    bool Enabled,
    TimeSpan Window,                 // Compare events within window
    decimal PriceTolerance,          // 0.01 = 1 cent
    long VolumeTolerance,            // Acceptable volume difference
    bool GenerateDiscrepancyReport,
    string ReportPath
);
```

**Reconciliation report:**
```json
{
  "date": "2024-01-15",
  "symbol": "AAPL",
  "sources_compared": ["alpaca", "ib"],
  "total_events": {
    "alpaca": 50000,
    "ib": 50250
  },
  "matched": 49800,
  "discrepancies": [
    {
      "type": "missing_in_source",
      "source": "alpaca",
      "count": 250
    },
    {
      "type": "price_mismatch",
      "count": 50,
      "avg_difference": 0.005
    }
  ]
}
```

---

## Tiered Storage Architecture

### 1. Storage Tier Definitions

| Tier | Age | Storage Type | Access Pattern | Format | Compression |
|------|-----|--------------|----------------|--------|-------------|
| Hot | 0-7d | NVMe SSD | Real-time, random | JSONL | None/LZ4 |
| Warm | 7-90d | SSD/HDD | Daily batch | JSONL.gz | Gzip |
| Cold | 90-365d | HDD/NAS | Weekly/monthly | Parquet | Zstd |
| Archive | >1y | Object Storage | Rare, bulk | Parquet | Zstd-max |
| Glacier | >3y | Tape/Glacier | Emergency only | Parquet | Zstd-max |

### 2. Tiering Configuration

```json
{
  "Tiering": {
    "enabled": true,
    "tiers": [
      {
        "name": "hot",
        "path": "/fast-ssd/meridian/hot",
        "max_age_days": 7,
        "max_size_gb": 100,
        "format": "jsonl",
        "compression": null
      },
      {
        "name": "warm",
        "path": "/ssd/meridian/warm",
        "max_age_days": 90,
        "max_size_gb": 500,
        "format": "jsonl.gz",
        "compression": "gzip"
      },
      {
        "name": "cold",
        "path": "/hdd/meridian/cold",
        "max_age_days": 365,
        "max_size_gb": 2000,
        "format": "parquet",
        "compression": "zstd"
      },
      {
        "name": "archive",
        "path": "s3://meridian-archive",
        "max_age_days": null,
        "format": "parquet",
        "compression": "zstd",
        "storage_class": "GLACIER_IR"
      }
    ],
    "migration_schedule": "0 2 * * *",
    "parallel_migrations": 4
  }
}
```

### 3. Tier Migration Service

```csharp
interface ITierMigrationService
{
    Task<MigrationResult> MigrateAsync(
        string sourcePath,
        StorageTier targetTier,
        MigrationOptions options,
        CancellationToken ct
    );

    Task<MigrationPlan> PlanMigrationAsync(
        TimeSpan horizon,
        CancellationToken ct
    );
}

record MigrationOptions(
    bool DeleteSource,
    bool VerifyChecksum,
    bool ConvertFormat,
    int ParallelFiles,
    Action<MigrationProgress>? OnProgress
);
```

### 4. Unified Query Layer

**Query across all tiers transparently:**

```csharp
interface IUnifiedStorageReader
{
    IAsyncEnumerable<MarketEvent> ReadAsync(
        StorageQuery query,
        CancellationToken ct
    );
}

record StorageQuery(
    string[] Symbols,
    MarketEventType[] Types,
    DateTimeOffset Start,
    DateTimeOffset End,
    string[]? Sources,
    StorageTier[]? PreferredTiers,  // Hint for optimization
    int? MaxEvents
);
```

---

## File Maintenance & Health Monitoring

### 1. Automated File Health Service

**Continuous monitoring and self-healing for storage integrity:**

```csharp
interface IFileMaintenanceService
{
    Task<HealthReport> RunHealthCheckAsync(HealthCheckOptions options, CancellationToken ct);
    Task<RepairResult> RepairAsync(RepairOptions options, CancellationToken ct);
    Task<DefragResult> DefragmentAsync(DefragOptions options, CancellationToken ct);
    Task<OrphanReport> FindOrphansAsync(CancellationToken ct);
}

record HealthCheckOptions(
    bool ValidateChecksums,          // Verify file integrity
    bool CheckSequenceContinuity,    // Detect gaps in sequences
    bool ValidateSchemas,            // Ensure JSON/Parquet valid
    bool CheckFilePermissions,       // Verify read/write access
    bool IdentifyCorruption,         // Detect partial writes
    string[] Paths,                  // Specific paths or "*" for all
    int ParallelChecks               // Concurrent validation threads
);
```

### 2. File Health Report Structure

```json
{
  "report_id": "uuid-v4",
  "generated_at": "2024-01-15T12:00:00Z",
  "scan_duration_ms": 45000,
  "summary": {
    "total_files": 15000,
    "total_bytes": 107374182400,
    "healthy_files": 14950,
    "warning_files": 35,
    "corrupted_files": 15,
    "orphaned_files": 12
  },
  "issues": [
    {
      "severity": "critical",
      "type": "checksum_mismatch",
      "path": "live/alpaca/AAPL/Trade/2024-01-10.jsonl.gz",
      "expected_checksum": "sha256:abc123...",
      "actual_checksum": "sha256:def456...",
      "recommended_action": "restore_from_backup",
      "auto_repairable": true
    },
    {
      "severity": "warning",
      "type": "sequence_gap",
      "path": "live/ib/SPY/Trade/2024-01-12.jsonl",
      "details": {
        "gap_start": 1000500,
        "gap_end": 1000750,
        "missing_events": 250
      },
      "recommended_action": "backfill_from_source",
      "auto_repairable": false
    },
    {
      "severity": "info",
      "type": "orphaned_file",
      "path": "live/alpaca/DELETED_SYMBOL/Trade/2024-01-05.jsonl",
      "reason": "symbol_not_in_registry",
      "recommended_action": "archive_or_delete"
    }
  ],
  "statistics": {
    "avg_file_size_bytes": 7158278,
    "oldest_file": "2023-01-01",
    "newest_file": "2024-01-15",
    "compression_ratio": 8.5,
    "fragmentation_pct": 12.3
  }
}
```

### 3. Self-Healing Capabilities

```csharp
record RepairOptions(
    RepairStrategy Strategy,
    bool DryRun,                     // Preview changes only
    bool BackupBeforeRepair,         // Create backup first
    string BackupPath,
    RepairScope Scope
);

enum RepairStrategy
{
    RestoreFromBackup,       // Use backup copy
    BackfillFromSource,      // Re-fetch from data provider
    TruncateCorrupted,       // Remove corrupted tail
    RebuildIndex,            // Regenerate metadata index
    MergeFragments,          // Combine small files
    RecompressOptimal        // Apply better compression
}

enum RepairScope
{
    SingleFile,
    Directory,
    Symbol,
    DateRange,
    EventType,
    All
}
```

### 4. Scheduled Maintenance Tasks

```json
{
  "Maintenance": {
    "enabled": true,
    "schedule": {
      "health_check": "0 3 * * *",
      "defragmentation": "0 4 * * 0",
      "orphan_cleanup": "0 5 1 * *",
      "index_rebuild": "0 2 * * 0",
      "backup_verification": "0 6 * * 0"
    },
    "auto_repair": {
      "enabled": true,
      "max_auto_repairs_per_run": 100,
      "require_backup": true,
      "notify_on_repair": ["admin@example.com"]
    },
    "thresholds": {
      "fragmentation_trigger_pct": 20,
      "min_file_size_for_merge_bytes": 1048576,
      "max_file_age_for_hot_tier_days": 7
    }
  }
}
```

### 5. File Compaction & Defragmentation

**Merge small files and optimize storage layout:**

```csharp
record DefragOptions(
    long MinFileSizeBytes,           // Files smaller than this get merged
    int MaxFilesPerMerge,            // Batch size for merging
    bool PreserveOriginals,          // Keep originals until verified
    CompressionLevel TargetCompression,
    TimeSpan MaxFileAge              // Only defrag files older than
);

interface IFileCompactor
{
    // Merge multiple small files into optimized larger files
    Task<CompactionResult> CompactAsync(
        string[] sourcePaths,
        string targetPath,
        CompactionOptions options,
        CancellationToken ct
    );

    // Split oversized files into manageable chunks
    Task<SplitResult> SplitAsync(
        string sourcePath,
        long maxChunkBytes,
        CancellationToken ct
    );
}

record CompactionResult(
    int FilesProcessed,
    int FilesCreated,
    long BytesBefore,
    long BytesAfter,
    double CompressionImprovement,
    TimeSpan Duration
);
```

### 6. Orphan Detection & Cleanup

```csharp
record OrphanDetectionConfig(
    bool CheckSymbolRegistry,        // Files for unknown symbols
    bool CheckSourceRegistry,        // Files from unknown sources
    bool CheckDateRanges,            // Files outside expected dates
    bool CheckManifestConsistency,   // Files not in manifest
    OrphanAction DefaultAction
);

enum OrphanAction
{
    Report,              // Just log, take no action
    Quarantine,          // Move to _quarantine folder
    Archive,             // Move to archive tier
    Delete               // Remove permanently
}
```

---

## Data Robustness & Quality Scoring

### 1. Data Quality Dimensions

**Evaluate data across multiple quality dimensions:**

```csharp
record DataQualityScore(
    string Path,
    DateTimeOffset EvaluatedAt,
    double OverallScore,             // 0.0 - 1.0
    QualityDimension[] Dimensions
);

record QualityDimension(
    string Name,
    double Score,                    // 0.0 - 1.0
    double Weight,                   // Importance factor
    string[] Issues                  // Specific problems found
);
```

**Quality Dimensions:**

| Dimension | Description | Scoring Criteria |
|-----------|-------------|------------------|
| Completeness | No missing data | % of expected events present |
| Accuracy | Data matches source | Cross-source validation |
| Timeliness | Data is current | Lag from event to storage |
| Consistency | No conflicts | Schema compliance, no duplicates |
| Integrity | Data uncorrupted | Checksum validation |
| Continuity | No sequence gaps | Sequence number analysis |

### 2. Quality Scoring Engine

```csharp
interface IDataQualityService
{
    Task<DataQualityScore> ScoreAsync(string path, CancellationToken ct);
    Task<QualityReport> GenerateReportAsync(QualityReportOptions options, CancellationToken ct);
    Task<DataQualityScore[]> GetHistoricalScoresAsync(string path, TimeSpan window, CancellationToken ct);
}

record QualityReportOptions(
    string[] Paths,
    DateTimeOffset? From,
    DateTimeOffset? To,
    double MinScoreThreshold,        // Only include if score < threshold
    bool IncludeRecommendations,
    bool CompareAcrossSources
);
```

### 3. Quality Score Calculation

```csharp
// Completeness Score
completeness = actual_events / expected_events;

// Expected events derived from:
// - Historical average for symbol/date
// - Trading hours × average events/minute
// - Cross-source comparison

// Accuracy Score (when multiple sources available)
accuracy = matching_events / total_comparable_events;

// Timeliness Score
timeliness = 1.0 - (avg_latency_ms / max_acceptable_latency_ms);

// Consistency Score
consistency = valid_schema_events / total_events
            × unique_events / total_events  // penalize duplicates
            × events_in_sequence / total_events;

// Integrity Score
integrity = verified_checksums / total_files
          × uncorrupted_files / total_files;

// Continuity Score
continuity = 1.0 - (gap_count × gap_penalty);
```

### 4. Best-of-Breed Data Selection

**When multiple sources exist, select the most robust data:**

```csharp
interface IBestOfBreedSelector
{
    Task<SourceRanking[]> RankSourcesAsync(
        string symbol,
        DateTimeOffset date,
        MarketEventType type,
        CancellationToken ct
    );

    Task<ConsolidatedDataset> CreateGoldenRecordAsync(
        string symbol,
        DateTimeOffset date,
        ConsolidationOptions options,
        CancellationToken ct
    );
}

record SourceRanking(
    string Source,
    double QualityScore,
    long EventCount,
    int GapCount,
    double Latency,
    bool IsRecommended
);

record ConsolidationOptions(
    SourceSelectionStrategy Strategy,
    bool FillGapsFromAlternates,     // Use secondary sources for missing data
    bool ValidateCrossSource,        // Cross-check prices/volumes
    decimal PriceTolerancePct,       // Max price diff before flagging
    long VolumeTolerancePct          // Max volume diff before flagging
);

enum SourceSelectionStrategy
{
    HighestQualityScore,     // Best overall quality
    MostComplete,            // Highest event count
    LowestLatency,           // Fastest data
    MostConsistent,          // Fewest anomalies
    Merge                    // Combine best of each
}
```

### 5. Quality-Aware Storage Decisions

```json
{
  "QualityPolicies": {
    "minimum_score_for_archive": 0.95,
    "minimum_score_for_research": 0.90,
    "quarantine_below_score": 0.70,
    "auto_backfill_below_score": 0.85,
    "prefer_source_above_score": 0.98,
    "consolidation": {
      "enabled": true,
      "schedule": "0 6 * * *",
      "target_directory": "consolidated",
      "strategy": "Merge",
      "min_sources_for_consolidation": 2
    }
  }
}
```

### 6. Quality Trend Monitoring

```csharp
interface IQualityTrendMonitor
{
    Task<QualityTrend> GetTrendAsync(
        string symbol,
        TimeSpan window,
        CancellationToken ct
    );

    Task<Alert[]> GetQualityAlertsAsync(CancellationToken ct);
}

record QualityTrend(
    string Symbol,
    double CurrentScore,
    double PreviousScore,
    double TrendDirection,           // -1.0 to 1.0
    string[] DegradingDimensions,
    string[] ImprovingDimensions,
    DateTimeOffset[] ScoreHistory,
    double[] ScoreValues
);
```

**Quality Dashboard Metrics:**

```json
{
  "quality_dashboard": {
    "overall_score": 0.94,
    "by_source": {
      "alpaca": 0.96,
      "ib": 0.93,
      "polygon": 0.91
    },
    "by_event_type": {
      "Trade": 0.97,
      "L2Snapshot": 0.92,
      "BboQuote": 0.95
    },
    "by_symbol": {
      "SPY": 0.98,
      "AAPL": 0.96,
      "TSLA": 0.89
    },
    "alerts": [
      {
        "symbol": "TSLA",
        "issue": "quality_degradation",
        "current_score": 0.89,
        "previous_score": 0.95,
        "recommendation": "investigate_source_ib"
      }
    ]
  }
}
```

---

## Search & Discovery Infrastructure

### 1. Multi-Level Index Architecture

**Hierarchical indexing for fast discovery:**

```
_index/
├── global/
│   ├── symbols.idx              # All symbols with metadata
│   ├── date_range.idx           # Date coverage per symbol
│   ├── sources.idx              # Source availability matrix
│   └── statistics.idx           # Aggregated stats
├── by_symbol/
│   └── {symbol}/
│       ├── files.idx            # All files for symbol
│       ├── sequences.idx        # Sequence ranges per file
│       └── quality.idx          # Quality scores history
├── by_date/
│   └── {yyyy-mm-dd}/
│       ├── symbols.idx          # Symbols with data on date
│       └── summary.idx          # Daily statistics
└── full_text/
    └── events.idx               # Full-text search index
```

### 2. Index Schema

```csharp
record GlobalSymbolIndex(
    Dictionary<string, SymbolIndexEntry> Symbols,
    DateTimeOffset LastUpdated,
    int Version
);

record SymbolIndexEntry(
    string Symbol,
    string CanonicalName,
    string[] Aliases,
    string AssetClass,
    string Exchange,
    DateTimeOffset FirstDataDate,
    DateTimeOffset LastDataDate,
    long TotalEvents,
    long TotalBytes,
    string[] AvailableSources,
    MarketEventType[] AvailableTypes,
    double QualityScore,
    Dictionary<string, SourceCoverage> SourceCoverage
);

record SourceCoverage(
    string Source,
    DateTimeOffset FirstDate,
    DateTimeOffset LastDate,
    long EventCount,
    double CoveragePct              // % of trading days with data
);
```

### 3. Search Query API

```csharp
interface IStorageSearchService
{
    // Find files matching criteria
    Task<SearchResult<FileInfo>> SearchFilesAsync(
        FileSearchQuery query,
        CancellationToken ct
    );

    // Find events within files
    Task<SearchResult<MarketEvent>> SearchEventsAsync(
        EventSearchQuery query,
        CancellationToken ct
    );

    // Discover available data
    Task<DataCatalog> DiscoverAsync(
        DiscoveryQuery query,
        CancellationToken ct
    );
}

record FileSearchQuery(
    string[]? Symbols,
    MarketEventType[]? Types,
    string[]? Sources,
    DateTimeOffset? From,
    DateTimeOffset? To,
    long? MinSize,
    long? MaxSize,
    double? MinQualityScore,
    string? PathPattern,             // Glob pattern
    SortField SortBy,
    bool Descending,
    int Skip,
    int Take
);

record EventSearchQuery(
    string Symbol,
    MarketEventType Type,
    DateTimeOffset From,
    DateTimeOffset To,
    decimal? MinPrice,
    decimal? MaxPrice,
    long? MinVolume,
    AggressorSide? Side,
    long? SequenceFrom,
    long? SequenceTo,
    int Limit
);
```

### 4. Faceted Search Support

```json
{
  "search": {
    "query": "AAPL",
    "filters": {
      "date_range": ["2024-01-01", "2024-01-31"],
      "event_types": ["Trade", "BboQuote"],
      "sources": ["alpaca"]
    }
  },
  "results": {
    "total_matches": 1250000,
    "files": 31,
    "facets": {
      "by_date": {
        "2024-01-02": 45000,
        "2024-01-03": 42000,
        "...": "..."
      },
      "by_event_type": {
        "Trade": 800000,
        "BboQuote": 450000
      },
      "by_source": {
        "alpaca": 1250000
      },
      "by_hour": {
        "09": 150000,
        "10": 180000,
        "...": "..."
      }
    }
  }
}
```

### 5. Natural Language Query Support

**Parse human-readable queries into structured searches:**

```csharp
interface INaturalLanguageQueryParser
{
    StorageQuery Parse(string naturalQuery);
}

// Example queries:
// "AAPL trades from last week"
//   → Symbol: AAPL, Type: Trade, From: -7d
//
// "all L2 snapshots for SPY on January 15th"
//   → Symbol: SPY, Type: L2Snapshot, Date: 2024-01-15
//
// "high volume trades over 1M shares"
//   → Type: Trade, MinVolume: 1000000
//
// "data gaps in TSLA for December"
//   → Symbol: TSLA, Month: 2024-12, Query: gaps
```

### 6. Real-Time Index Updates

```csharp
interface IIndexMaintainer
{
    // Called after each file write
    Task UpdateIndexAsync(
        string filePath,
        IndexUpdateType updateType,
        CancellationToken ct
    );

    // Rebuild indexes from scratch
    Task RebuildIndexAsync(
        string[] paths,
        RebuildOptions options,
        CancellationToken ct
    );
}

enum IndexUpdateType
{
    FileCreated,
    FileAppended,
    FileDeleted,
    FileMoved,
    MetadataChanged
}
```

### 7. Search Performance Optimization

```json
{
  "SearchOptimization": {
    "index_in_memory": true,
    "max_index_memory_mb": 512,
    "cache_recent_queries": true,
    "query_cache_size": 1000,
    "query_cache_ttl_seconds": 300,
    "parallel_search_threads": 4,
    "bloom_filters": {
      "enabled": true,
      "false_positive_rate": 0.01
    },
    "partitioned_indexes": {
      "by_date": true,
      "by_symbol": true
    }
  }
}
```

---

## Actionable Metadata & Insights

### 1. Rich Metadata Schema

**Comprehensive metadata for every data file:**

```csharp
record FileMetadata(
    // Identity
    string FilePath,
    string FileId,                   // UUID
    string Checksum,

    // Content Description
    string Symbol,
    MarketEventType EventType,
    string Source,
    DateTimeOffset Date,

    // Statistics
    long EventCount,
    long SizeBytes,
    long SizeCompressed,
    double CompressionRatio,

    // Temporal Coverage
    DateTimeOffset FirstEventTime,
    DateTimeOffset LastEventTime,
    TimeSpan Duration,
    double EventsPerSecond,

    // Sequence Info
    long FirstSequence,
    long LastSequence,
    int SequenceGaps,
    long[] GapRanges,

    // Quality Metrics
    double QualityScore,
    int WarningCount,
    int ErrorCount,
    string[] ValidationIssues,

    // Price Statistics (for Trade events)
    decimal? PriceMin,
    decimal? PriceMax,
    decimal? PriceOpen,
    decimal? PriceClose,
    decimal? VWAP,

    // Volume Statistics
    long? TotalVolume,
    long? BuyVolume,
    long? SellVolume,
    double? BuySellRatio,

    // Lifecycle
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt,
    DateTimeOffset? ArchivedAt,
    string CurrentTier,
    string[] TierHistory,

    // Lineage
    string? ParentFileId,            // If derived/compacted
    string[] ChildFileIds,
    string? SourceProvider,
    string? BackfillProvider
);
```

### 2. Automated Insights Generation

```csharp
interface IInsightGenerator
{
    Task<Insight[]> GenerateInsightsAsync(
        InsightScope scope,
        CancellationToken ct
    );
}

record Insight(
    InsightType Type,
    InsightSeverity Severity,
    string Title,
    string Description,
    string[] AffectedPaths,
    string[] RecommendedActions,
    Dictionary<string, object> Context,
    DateTimeOffset GeneratedAt
);

enum InsightType
{
    // Storage Insights
    StorageGrowthAnomaly,
    UnusualCompressionRatio,
    HighFragmentation,
    QuotaNearLimit,

    // Data Quality Insights
    QualityDegradation,
    SourceReliabilityIssue,
    SequenceGapPattern,
    DataLatencyIncrease,

    // Optimization Insights
    CompressionOpportunity,
    ArchivalCandidate,
    ConsolidationOpportunity,
    UnusedDataPattern,

    // Anomaly Detection
    VolumeSpike,
    PriceAnomaly,
    MissingExpectedData,
    DuplicateDataDetected
}
```

### 3. Insight Examples

```json
{
  "insights": [
    {
      "type": "CompressionOpportunity",
      "severity": "info",
      "title": "Compression savings available",
      "description": "150 files in warm tier using gzip could save 40% space with zstd",
      "affected_paths": ["warm/alpaca/equity/**/*.jsonl.gz"],
      "recommended_actions": [
        "Run: migrate-compression --from gzip --to zstd --tier warm"
      ],
      "context": {
        "current_size_gb": 50,
        "projected_size_gb": 30,
        "savings_gb": 20
      }
    },
    {
      "type": "QualityDegradation",
      "severity": "warning",
      "title": "TSLA data quality declining",
      "description": "Quality score dropped from 0.95 to 0.87 over past 7 days",
      "affected_paths": ["live/ib/equity/TSLA/**"],
      "recommended_actions": [
        "Check IB connection for TSLA subscription",
        "Compare with Alpaca data for gaps",
        "Consider switching primary source"
      ],
      "context": {
        "score_history": [0.95, 0.93, 0.91, 0.89, 0.88, 0.87, 0.87],
        "main_issues": ["sequence_gaps", "increased_latency"]
      }
    },
    {
      "type": "ArchivalCandidate",
      "severity": "info",
      "title": "30 days of data ready for archival",
      "description": "Cold tier data from November 2023 meets archival criteria",
      "affected_paths": ["cold/*/2023/11/**"],
      "recommended_actions": [
        "Review archival policy",
        "Run: archive --month 2023-11 --verify"
      ],
      "context": {
        "file_count": 2500,
        "total_size_gb": 150,
        "avg_quality_score": 0.97
      }
    }
  ]
}
```

### 4. Usage Analytics

**Track how data is accessed and used:**

```csharp
record UsageMetrics(
    string Path,
    int ReadCount,
    int QueryCount,
    DateTimeOffset LastAccessed,
    DateTimeOffset[] AccessHistory,
    string[] AccessPatterns,         // "bulk_read", "random_access", "streaming"
    Dictionary<string, int> AccessByUser,
    double HotDataScore              // How frequently accessed (0-1)
);

interface IUsageAnalytics
{
    Task RecordAccessAsync(string path, AccessType type, CancellationToken ct);
    Task<UsageReport> GetUsageReportAsync(TimeSpan window, CancellationToken ct);
    Task<string[]> GetColdDataAsync(TimeSpan threshold, CancellationToken ct);
    Task<string[]> GetHotDataAsync(int topN, CancellationToken ct);
}
```

### 5. Data Lineage Tracking

```csharp
record DataLineage(
    string FileId,
    LineageNode[] Ancestors,
    LineageNode[] Descendants,
    TransformationStep[] Transformations
);

record LineageNode(
    string FileId,
    string Path,
    string Type,                     // "raw", "processed", "consolidated", "archived"
    DateTimeOffset CreatedAt
);

record TransformationStep(
    string Operation,                // "compress", "convert", "merge", "filter"
    DateTimeOffset PerformedAt,
    string[] InputFiles,
    string[] OutputFiles,
    Dictionary<string, string> Parameters
);
```

**Lineage visualization:**
```
raw/alpaca/AAPL/Trade/2024-01-15.jsonl
    │
    ├─[compress]─→ warm/alpaca/AAPL/Trade/2024-01-15.jsonl.gz
    │                  │
    │                  └─[convert]─→ cold/parquet/AAPL/Trade/2024-01.parquet
    │                                    │
    └─[merge]────────────────────────────┴─→ consolidated/AAPL/Trade/2024-01.parquet
                                                  │
                                                  └─[archive]─→ archive/2024/Q1/AAPL_Trade.parquet.zst
```

### 6. Actionable Dashboards

```json
{
  "Dashboard": {
    "sections": [
      {
        "name": "Storage Overview",
        "widgets": [
          {"type": "gauge", "metric": "total_usage_pct", "thresholds": [70, 90]},
          {"type": "trend", "metric": "daily_growth_gb", "window": "30d"},
          {"type": "breakdown", "metric": "usage_by_tier"}
        ]
      },
      {
        "name": "Data Quality",
        "widgets": [
          {"type": "score", "metric": "overall_quality", "target": 0.95},
          {"type": "heatmap", "metric": "quality_by_symbol_date"},
          {"type": "list", "metric": "quality_alerts", "limit": 10}
        ]
      },
      {
        "name": "Insights & Actions",
        "widgets": [
          {"type": "feed", "source": "insights", "filter": "actionable"},
          {"type": "checklist", "source": "pending_maintenance"},
          {"type": "timeline", "source": "scheduled_tasks"}
        ]
      },
      {
        "name": "Search & Discovery",
        "widgets": [
          {"type": "search_bar", "scope": "global"},
          {"type": "facets", "dimensions": ["symbol", "type", "source", "date"]},
          {"type": "recent_searches", "limit": 5}
        ]
      }
    ]
  }
}
```

### 7. Metadata-Driven Automation

```csharp
record AutomationRule(
    string Name,
    MetadataCondition[] Conditions,
    AutomationAction[] Actions,
    bool Enabled,
    string Schedule                  // Cron expression or "realtime"
);

record MetadataCondition(
    string Field,                    // e.g., "QualityScore", "SizeBytes", "Age"
    ConditionOperator Operator,      // Lt, Gt, Eq, Contains, etc.
    object Value
);

record AutomationAction(
    ActionType Type,
    Dictionary<string, string> Parameters
);

enum ActionType
{
    Notify,
    MoveToTier,
    Compress,
    Archive,
    Delete,
    Backfill,
    RunRepair,
    GenerateReport
}
```

**Example automation rules:**

```json
{
  "AutomationRules": [
    {
      "name": "auto_archive_old_high_quality",
      "conditions": [
        {"field": "Age", "operator": "Gt", "value": "365d"},
        {"field": "QualityScore", "operator": "Gte", "value": 0.95},
        {"field": "CurrentTier", "operator": "Eq", "value": "cold"}
      ],
      "actions": [
        {"type": "Archive", "parameters": {"target": "glacier", "verify": "true"}}
      ],
      "schedule": "0 3 1 * *"
    },
    {
      "name": "alert_quality_drop",
      "conditions": [
        {"field": "QualityScore", "operator": "Lt", "value": 0.85}
      ],
      "actions": [
        {"type": "Notify", "parameters": {"channel": "slack", "severity": "warning"}}
      ],
      "schedule": "realtime"
    },
    {
      "name": "auto_backfill_gaps",
      "conditions": [
        {"field": "SequenceGaps", "operator": "Gt", "value": 0},
        {"field": "Age", "operator": "Lt", "value": "7d"}
      ],
      "actions": [
        {"type": "Backfill", "parameters": {"source": "alternate", "max_gaps": "10"}}
      ],
      "schedule": "0 */4 * * *"
    }
  ]
}
```

---

## Operational Scheduling & Off-Hours Maintenance

### 1. Trading Hours Awareness

**Define operational windows based on market schedules:**

```csharp
record OperationalSchedule(
    string Name,
    TradingSession[] TradingSessions,
    MaintenanceWindow[] MaintenanceWindows,
    TimeZoneInfo PrimaryTimeZone,
    string[] Holidays                    // ISO dates or calendar reference
);

record TradingSession(
    string Name,                         // "US_Equities", "Crypto_24x7", "Futures"
    DayOfWeek[] ActiveDays,
    TimeOnly PreMarketStart,             // 04:00 ET
    TimeOnly RegularStart,               // 09:30 ET
    TimeOnly RegularEnd,                 // 16:00 ET
    TimeOnly AfterHoursEnd,              // 20:00 ET
    TimeZoneInfo TimeZone,
    bool IncludesPreMarket,
    bool IncludesAfterHours
);

record MaintenanceWindow(
    string Name,
    TimeOnly Start,
    TimeOnly End,
    DayOfWeek[] Days,
    MaintenanceType[] AllowedOperations,
    int MaxConcurrentJobs,
    ResourceLimits Limits
);

enum MaintenanceType
{
    HealthCheck,
    IntegrityValidation,
    Backfill,
    Compaction,
    TierMigration,
    IndexRebuild,
    Archival,
    Backup,
    Reconciliation,
    QualityScoring
}
```

### 2. Maintenance Window Configuration

```json
{
  "OperationalSchedule": {
    "name": "US_Equities_Schedule",
    "timezone": "America/New_York",
    "trading_sessions": [
      {
        "name": "US_Equities",
        "active_days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
        "pre_market_start": "04:00",
        "regular_start": "09:30",
        "regular_end": "16:00",
        "after_hours_end": "20:00",
        "includes_pre_market": true,
        "includes_after_hours": true
      }
    ],
    "real_time_collection": {
      "active_window": {
        "start": "03:30",
        "end": "20:30",
        "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"]
      },
      "buffer_minutes": 30
    },
    "maintenance_windows": [
      {
        "name": "overnight_maintenance",
        "start": "21:00",
        "end": "03:00",
        "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
        "allowed_operations": ["*"],
        "max_concurrent_jobs": 8,
        "resource_limits": {
          "max_cpu_pct": 80,
          "max_memory_pct": 70,
          "max_disk_io_mbps": 500
        }
      },
      {
        "name": "weekend_maintenance",
        "start": "00:00",
        "end": "23:59",
        "days": ["Saturday", "Sunday"],
        "allowed_operations": ["*"],
        "max_concurrent_jobs": 16,
        "resource_limits": {
          "max_cpu_pct": 100,
          "max_memory_pct": 90,
          "max_disk_io_mbps": 1000
        }
      },
      {
        "name": "intraday_light",
        "start": "12:00",
        "end": "13:00",
        "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
        "allowed_operations": ["HealthCheck", "QualityScoring"],
        "max_concurrent_jobs": 2,
        "resource_limits": {
          "max_cpu_pct": 20,
          "max_memory_pct": 10,
          "max_disk_io_mbps": 50
        }
      }
    ],
    "holidays": [
      "2024-01-01",
      "2024-01-15",
      "2024-02-19",
      "2024-03-29",
      "2024-05-27",
      "2024-06-19",
      "2024-07-04",
      "2024-09-02",
      "2024-11-28",
      "2024-12-25"
    ],
    "holiday_calendar": "NYSE"
  }
}
```

### 3. Scheduler Service

```csharp
interface IMaintenanceScheduler
{
    // Check if operation can run now
    Task<ScheduleDecision> CanRunNowAsync(
        MaintenanceType operation,
        ResourceRequirements requirements,
        CancellationToken ct
    );

    // Find next available window for operation
    Task<ScheduleSlot?> FindNextWindowAsync(
        MaintenanceType operation,
        TimeSpan estimatedDuration,
        ResourceRequirements requirements,
        CancellationToken ct
    );

    // Schedule operation for next available window
    Task<ScheduledJob> ScheduleAsync(
        MaintenanceJob job,
        ScheduleOptions options,
        CancellationToken ct
    );

    // Get current operational state
    Task<OperationalState> GetStateAsync(CancellationToken ct);
}

record ScheduleDecision(
    bool Allowed,
    string Reason,
    MaintenanceWindow? CurrentWindow,
    TimeSpan? WaitTime,
    ResourceLimits? ApplicableLimits
);

record ScheduleSlot(
    DateTimeOffset Start,
    DateTimeOffset End,
    MaintenanceWindow Window,
    ResourceLimits Limits,
    int AvailableConcurrencySlots
);

record OperationalState(
    bool IsRealTimeCollectionActive,
    TradingSession? CurrentSession,
    MaintenanceWindow? CurrentMaintenanceWindow,
    int RunningMaintenanceJobs,
    ScheduledJob[] PendingJobs,
    DateTimeOffset NextMaintenanceWindowStart
);
```

### 4. Job Priority & Queuing

```csharp
enum JobPriority
{
    Critical,        // Run ASAP, even during light maintenance windows
    High,            // Schedule for next overnight window
    Normal,          // Schedule for next available window
    Low,             // Weekend only, background tasks
    Deferred         // Run only when system is idle
}

record MaintenanceJob(
    string Id,
    MaintenanceType Type,
    JobPriority Priority,
    string Description,
    ResourceRequirements Requirements,
    TimeSpan EstimatedDuration,
    string[] TargetPaths,
    Dictionary<string, object> Parameters,
    bool Interruptible,              // Can pause if trading resumes
    int MaxRetries,
    Action<JobProgress>? OnProgress
);

record ResourceRequirements(
    int CpuCores,
    long MemoryBytes,
    long DiskIoMbps,
    long NetworkIoMbps,
    bool RequiresExclusiveLock,      // No other jobs on same paths
    string[]? ExclusivePaths
);
```

### 5. Task Scheduling Matrix

| Operation | Priority | Typical Duration | Preferred Window | Interruptible |
|-----------|----------|------------------|------------------|---------------|
| Health Check | Normal | 5-30 min | Overnight/Intraday | Yes |
| Integrity Validation | Normal | 1-4 hours | Overnight | Yes |
| Historical Backfill | High | 2-8 hours | Weekend | Yes |
| File Compaction | Normal | 30-120 min | Overnight | Yes |
| Tier Migration | Normal | 1-6 hours | Weekend | Yes |
| Index Rebuild | High | 30-90 min | Overnight | No |
| Parquet Conversion | Normal | 2-6 hours | Weekend | Yes |
| Cross-Source Reconciliation | Normal | 1-3 hours | Weekend | Yes |
| Quality Scoring | Low | 15-60 min | Overnight/Intraday | Yes |
| Backup | Critical | 30-120 min | Overnight | No |
| Archival | Low | 1-4 hours | Weekend | Yes |
| Capacity Cleanup | Normal | 15-60 min | Overnight | Yes |

### 6. Real-Time Collection Coordination

```csharp
interface ICollectionCoordinator
{
    // Check if real-time collection is active
    Task<bool> IsCollectionActiveAsync(CancellationToken ct);

    // Get estimated time until collection stops
    Task<TimeSpan?> GetTimeUntilCollectionEndsAsync(CancellationToken ct);

    // Request graceful pause for maintenance (with timeout)
    Task<PauseResult> RequestPauseAsync(
        TimeSpan duration,
        string reason,
        CancellationToken ct
    );

    // Resume collection after maintenance
    Task ResumeAsync(CancellationToken ct);

    // Subscribe to collection state changes
    IAsyncEnumerable<CollectionState> WatchStateAsync(CancellationToken ct);
}

record CollectionState(
    bool IsActive,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExpectedEnd,
    string[] ActiveSymbols,
    long EventsCollectedToday,
    bool IsPausedForMaintenance,
    string? PauseReason
);

record PauseResult(
    bool Success,
    string? FailureReason,
    DateTimeOffset? ResumeAt,
    int EventsBuffered              // Events queued during pause
);
```

### 7. Scheduled Task Configuration

```json
{
  "ScheduledMaintenance": {
    "tasks": [
      {
        "name": "nightly_health_check",
        "type": "HealthCheck",
        "schedule": "0 21 * * 1-5",
        "priority": "Normal",
        "window_required": "overnight_maintenance",
        "config": {
          "validate_checksums": true,
          "check_sequences": true,
          "parallel_checks": 4
        },
        "enabled": true
      },
      {
        "name": "nightly_quality_scoring",
        "type": "QualityScoring",
        "schedule": "0 22 * * 1-5",
        "priority": "Normal",
        "window_required": "overnight_maintenance",
        "config": {
          "score_all_symbols": true,
          "generate_report": true
        },
        "enabled": true
      },
      {
        "name": "weekly_backfill",
        "type": "Backfill",
        "schedule": "0 2 * * 6",
        "priority": "High",
        "window_required": "weekend_maintenance",
        "config": {
          "lookback_days": 7,
          "fill_gaps_only": true,
          "sources": ["stooq", "yahoo"]
        },
        "enabled": true
      },
      {
        "name": "weekly_compaction",
        "type": "Compaction",
        "schedule": "0 4 * * 0",
        "priority": "Normal",
        "window_required": "weekend_maintenance",
        "config": {
          "min_file_size_bytes": 1048576,
          "target_tier": "warm",
          "compress": true
        },
        "enabled": true
      },
      {
        "name": "weekly_tier_migration",
        "type": "TierMigration",
        "schedule": "0 6 * * 0",
        "priority": "Normal",
        "window_required": "weekend_maintenance",
        "config": {
          "migrate_hot_to_warm_after_days": 7,
          "migrate_warm_to_cold_after_days": 90,
          "convert_to_parquet": true
        },
        "enabled": true
      },
      {
        "name": "monthly_reconciliation",
        "type": "Reconciliation",
        "schedule": "0 8 1 * *",
        "priority": "Normal",
        "window_required": "weekend_maintenance",
        "config": {
          "compare_sources": ["alpaca", "ib"],
          "generate_discrepancy_report": true
        },
        "enabled": true
      },
      {
        "name": "monthly_archival",
        "type": "Archival",
        "schedule": "0 10 1 * *",
        "priority": "Low",
        "window_required": "weekend_maintenance",
        "config": {
          "archive_older_than_days": 365,
          "min_quality_score": 0.95,
          "target": "glacier"
        },
        "enabled": true
      }
    ],
    "conflict_resolution": {
      "strategy": "priority_then_fifo",
      "max_queue_size": 100,
      "max_wait_hours": 168
    },
    "notifications": {
      "on_job_start": false,
      "on_job_complete": true,
      "on_job_failure": true,
      "on_window_missed": true,
      "channels": ["slack", "email"]
    }
  }
}
```

### 8. Adaptive Scheduling

```csharp
interface IAdaptiveScheduler
{
    // Analyze historical job performance
    Task<JobAnalytics> AnalyzeJobHistoryAsync(
        MaintenanceType type,
        TimeSpan window,
        CancellationToken ct
    );

    // Predict duration based on data volume
    Task<TimeSpan> PredictDurationAsync(
        MaintenanceJob job,
        CancellationToken ct
    );

    // Optimize schedule based on patterns
    Task<OptimizedSchedule> OptimizeScheduleAsync(
        ScheduledJob[] jobs,
        CancellationToken ct
    );
}

record JobAnalytics(
    MaintenanceType Type,
    int ExecutionCount,
    TimeSpan AverageDuration,
    TimeSpan MinDuration,
    TimeSpan MaxDuration,
    double SuccessRate,
    Dictionary<DayOfWeek, TimeSpan> DurationByDay,
    double[] DurationTrend               // Is it getting slower/faster?
);

record OptimizedSchedule(
    ScheduleSlot[] RecommendedSlots,
    string[] Optimizations,              // "Moved X to earlier slot", etc.
    double EstimatedEfficiencyGain,
    ConflictResolution[] ResolvedConflicts
);
```

### 9. Monitoring & Alerting

```json
{
  "ScheduleMonitoring": {
    "alerts": [
      {
        "name": "maintenance_window_missed",
        "condition": "job.wait_time > 24h",
        "severity": "warning",
        "action": "notify"
      },
      {
        "name": "job_running_into_trading",
        "condition": "job.end_time > trading_start - 30m",
        "severity": "critical",
        "action": "interrupt_and_notify"
      },
      {
        "name": "backfill_incomplete",
        "condition": "backfill.gaps_remaining > 0 AND no_window_available_48h",
        "severity": "warning",
        "action": "escalate"
      },
      {
        "name": "resource_contention",
        "condition": "queued_jobs > 10 AND avg_wait > 4h",
        "severity": "info",
        "action": "recommend_window_expansion"
      }
    ],
    "dashboard": {
      "widgets": [
        {"type": "calendar", "view": "maintenance_schedule"},
        {"type": "timeline", "view": "job_execution_history"},
        {"type": "queue", "view": "pending_jobs"},
        {"type": "gauge", "metric": "window_utilization_pct"}
      ]
    }
  }
}
```

### 10. Emergency Override

```csharp
record EmergencyOverride(
    bool Enabled,
    string Reason,
    MaintenanceType[] AllowedOperations,
    TimeSpan MaxDuration,
    string AuthorizedBy,
    DateTimeOffset ExpiresAt,
    bool PauseRealTimeCollection,
    NotificationSettings Notifications
);

interface IEmergencyScheduler
{
    // Force run maintenance during trading hours (requires approval)
    Task<EmergencyResult> ForceRunAsync(
        MaintenanceJob job,
        EmergencyOverride @override,
        CancellationToken ct
    );

    // Extend current maintenance window
    Task<ExtensionResult> ExtendWindowAsync(
        TimeSpan additionalTime,
        string reason,
        CancellationToken ct
    );
}
```

**Emergency scenarios:**
- Critical data corruption requiring immediate repair
- Storage capacity emergency (quota exceeded)
- Compliance deadline requiring urgent archival
- Security incident requiring data validation

---

## External Analysis Export Architecture

> **Purpose**: Design data export pipelines optimized for external analysis tools. The collector's role is to provide clean, well-organized data that external tools can consume efficiently.

### 1. Export Pipeline Design

**Layered export architecture:**

```
┌─────────────────────────────────────────────────────────────────┐
│                    External Analysis Tools                       │
│  (Python, R, QuantConnect Lean, Databases, Custom Apps)         │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │
┌─────────────────────────────────────────────────────────────────┐
│                     Export Layer                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  Format     │  │  Transform  │  │  Package    │             │
│  │  Converters │  │  Pipeline   │  │  Builder    │             │
│  └─────────────┘  └─────────────┘  └─────────────┘             │
└─────────────────────────────────────────────────────────────────┘
                              ▲
                              │
┌─────────────────────────────────────────────────────────────────┐
│                     Storage Layer                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐             │
│  │  Hot Tier   │  │  Warm Tier  │  │  Cold/      │             │
│  │  (JSONL)    │  │  (JSONL.gz) │  │  Archive    │             │
│  └─────────────┘  └─────────────┘  └─────────────┘             │
└─────────────────────────────────────────────────────────────────┘
```

### 2. Export Format Specifications

```csharp
interface IExportFormatProvider
{
    Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken ct
    );

    ExportCapabilities GetCapabilities();
}

record ExportRequest(
    string[] Symbols,
    MarketEventType[] Types,
    DateTimeOffset From,
    DateTimeOffset To,
    ExportFormat Format,
    ExportOptions Options,
    string DestinationPath
);

enum ExportFormat
{
    // Raw formats
    JsonLines,           // Original JSONL
    JsonLinesCompressed, // JSONL.gz

    // Columnar formats (analysis-optimized)
    Parquet,            // Apache Parquet
    ParquetPartitioned, // Partitioned Parquet (by date/symbol)
    Arrow,              // Apache Arrow IPC

    // Interchange formats
    Csv,                // Comma-separated values
    CsvCompressed,      // CSV.gz

    // Tool-specific formats
    LeanZip,            // QuantConnect Lean format
    FeatherV2,          // Arrow Feather v2

    // Database formats
    SqlInsert,          // SQL INSERT statements
    PostgresCopy,       // PostgreSQL COPY format
    ParquetDelta        // Delta Lake format
}
```

### 3. Analysis-Ready Export Profiles

Six built-in profiles are registered in `ExportProfile.GetBuiltInProfiles()`:

| Profile ID | Target Tool | Format | Notes |
|------------|-------------|--------|-------|
| `python-pandas` | Python / pandas | Parquet (snappy) | Includes loader script, data dictionary |
| `r-stats` | R / arrow | CSV | Includes R loader script |
| `quantconnect-lean` | QuantConnect Lean | Lean ZIP | Tick resolution, equity security type |
| `excel` | Microsoft Excel | XLSX | Row limit enforced, human-readable |
| `postgresql` | PostgreSQL | CSV | Includes DDL + COPY statements |
| `arrow-feather` | Apache Arrow | Arrow Feather v2 | Zero-copy IPC format |

```json
{
  "ExportProfiles": {
    "python-pandas": {
      "format": "parquet",
      "options": {
        "engine": "pyarrow",
        "compression": "snappy",
        "timestamp_as": "datetime64[ns]",
        "include_index": false,
        "partition_by": ["date"],
        "row_group_size": 100000
      },
      "post_export": {
        "generate_loader_script": true,
        "generate_requirements": true,
        "include_sample_notebook": true
      }
    },
    "quantconnect-lean": {
      "format": "lean_zip",
      "options": {
        "resolution": "tick",
        "market": "usa",
        "security_type": "equity",
        "data_type": ["trade", "quote"],
        "include_auxiliary": true
      }
    },
    "postgresql": {
      "format": "csv",
      "options": {
        "include_ddl": true,
        "include_hypertable": true,
        "chunk_interval": "1 day",
        "compression_policy": true,
        "include_indexes": true
      }
    },
    "arrow-feather": {
      "format": "arrow",
      "options": {
        "partition_by": ["symbol", "date"],
        "include_statistics": true,
        "include_metadata": true,
        "generate_catalog": true
      }
    }
  }
}
```

### 4. Data Transformation Pipeline

```csharp
interface IExportTransformPipeline
{
    // Register transformation steps
    IExportTransformPipeline AddStep(ITransformStep step);

    // Execute pipeline
    Task<TransformResult> ExecuteAsync(
        IAsyncEnumerable<MarketEvent> source,
        CancellationToken ct
    );
}

// Available transformations
interface ITransformStep
{
    Task<IAsyncEnumerable<object>> TransformAsync(
        IAsyncEnumerable<object> input,
        CancellationToken ct
    );
}

// Built-in transformations
class TimeAlignmentStep : ITransformStep { }       // Align to regular intervals
class OhlcvAggregationStep : ITransformStep { }    // Create OHLCV bars
class FeatureEngineeringStep : ITransformStep { }  // Compute derived features
class QualityFilterStep : ITransformStep { }       // Filter by quality score
class DeduplicationStep : ITransformStep { }       // Remove duplicates
class NormalizationStep : ITransformStep { }       // Normalize/scale values
```

### 5. Export Metadata & Documentation

**Auto-generated export documentation:**

```json
{
  "export_metadata": {
    "export_id": "uuid-v4",
    "created_at": "2026-01-03T06:00:00Z",
    "source_archive_version": "v2026.01.03",

    "data_coverage": {
      "symbols": ["AAPL", "MSFT", "SPY"],
      "date_range": {"start": "2025-01-01", "end": "2025-12-31"},
      "event_types": ["Trade", "BboQuote"],
      "total_events": 150000000,
      "total_bytes": 12884901888
    },

    "quality_summary": {
      "overall_score": 0.987,
      "completeness": 0.995,
      "gaps_filled": 12,
      "outliers_present": 45
    },

    "format_details": {
      "format": "parquet",
      "compression": "snappy",
      "partitioning": ["date", "symbol"],
      "schema_version": "2.0"
    },

    "checksums": {
      "manifest": "sha256:abc123...",
      "files": [
        {"path": "AAPL/2025-01.parquet", "sha256": "def456..."}
      ]
    },

    "loader_code": {
      "python": "import pandas as pd\ndf = pd.read_parquet('data/')",
      "r": "library(arrow)\ndf <- read_parquet('data/')"
    }
  }
}
```

### 6. Batch Export Scheduling

```csharp
record ExportSchedule(
    string Name,
    string CronExpression,          // e.g., "0 6 * * *" for daily at 6 AM
    ExportRequest Template,
    DateRangeType DateRange,        // LastDay, LastWeek, LastMonth, Custom
    bool Incremental,               // Only export new/changed data
    string[] Destinations,          // Multiple output locations
    NotificationConfig Notifications
);

interface IExportScheduler
{
    Task<ScheduledExport> ScheduleAsync(ExportSchedule schedule, CancellationToken ct);
    Task<ExportJobResult> RunNowAsync(string scheduleId, CancellationToken ct);
    Task<ExportHistory[]> GetHistoryAsync(string scheduleId, int limit, CancellationToken ct);
}
```

### 7. Export Verification

```csharp
interface IExportVerifier
{
    // Verify export completeness
    Task<VerificationResult> VerifyCompletenessAsync(
        string exportPath,
        ExportRequest originalRequest,
        CancellationToken ct
    );

    // Verify checksums
    Task<ChecksumResult> VerifyChecksumsAsync(
        string exportPath,
        CancellationToken ct
    );

    // Compare with source
    Task<ComparisonResult> CompareWithSourceAsync(
        string exportPath,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct
    );
}

record VerificationResult(
    bool IsValid,
    long ExpectedEvents,
    long ActualEvents,
    string[] MissingSymbols,
    DateTimeOffset[] MissingDates,
    string[] Issues
);
```

---

## Collection Session Management

> **Purpose**: Organize data collection into discrete, trackable sessions for better organization, verification, and export.

### 1. Session Definition

```csharp
record CollectionSession(
    string SessionId,               // Unique identifier
    string Name,                    // Human-readable name
    SessionType Type,               // Daily, Weekly, Custom, Continuous
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    SessionState State,             // Active, Completed, Failed
    string[] Symbols,
    SessionConfiguration Config,
    SessionStatistics Stats,
    string[] Tags                   // For organization: ["Q1-2026", "Earnings"]
);

enum SessionType
{
    Daily,          // One trading day
    Weekly,         // Trading week (Mon-Fri)
    Monthly,        // Calendar month
    Custom,         // User-defined range
    Continuous      // Ongoing until stopped
}

enum SessionState
{
    Scheduled,      // Waiting to start
    Active,         // Currently collecting
    Paused,         // Temporarily stopped
    Completed,      // Successfully finished
    Failed,         // Ended with errors
    Cancelled       // Manually stopped
}
```

### 2. Session Statistics

```csharp
record SessionStatistics(
    long TotalEvents,
    long TotalBytes,
    long TotalBytesCompressed,
    double CompressionRatio,

    Dictionary<string, long> EventsBySymbol,
    Dictionary<MarketEventType, long> EventsByType,
    Dictionary<string, long> EventsBySource,

    int GapsDetected,
    int GapsFilled,
    int SequenceErrors,
    double QualityScore,

    TimeSpan Duration,
    double EventsPerSecond,
    double BytesPerSecond
);
```

### 3. Session Report Generation

```json
{
  "session_report": {
    "session_id": "2026-01-03-regular",
    "name": "Regular Trading Session - 2026-01-03",
    "type": "Daily",

    "timing": {
      "started": "2026-01-03T09:30:00-05:00",
      "ended": "2026-01-03T16:00:00-05:00",
      "duration": "6h 30m 00s"
    },

    "coverage": {
      "symbols": 50,
      "trading_days": 1,
      "market_hours": "09:30-16:00 ET"
    },

    "volume": {
      "total_events": 12500000,
      "trades": 8000000,
      "quotes": 4000000,
      "l2_snapshots": 500000,
      "raw_bytes": "2.5 GB",
      "compressed_bytes": "320 MB",
      "compression_ratio": "7.8x"
    },

    "quality": {
      "overall_score": 0.998,
      "gaps_detected": 0,
      "gaps_auto_filled": 0,
      "sequence_errors": 0,
      "symbols_complete": 50,
      "symbols_with_issues": 0
    },

    "files": {
      "count": 150,
      "all_verified": true,
      "manifest_path": "sessions/2026-01-03/manifest.json"
    },

    "notes": "Clean session, no issues detected"
  }
}
```

### 4. Session-Based Operations

```csharp
interface ISessionManager
{
    // Session lifecycle
    Task<CollectionSession> CreateSessionAsync(SessionConfiguration config, CancellationToken ct);
    Task<CollectionSession> StartSessionAsync(string sessionId, CancellationToken ct);
    Task<CollectionSession> EndSessionAsync(string sessionId, CancellationToken ct);
    Task<CollectionSession> PauseSessionAsync(string sessionId, CancellationToken ct);
    Task<CollectionSession> ResumeSessionAsync(string sessionId, CancellationToken ct);

    // Session queries
    Task<CollectionSession> GetSessionAsync(string sessionId, CancellationToken ct);
    Task<CollectionSession[]> ListSessionsAsync(SessionQuery query, CancellationToken ct);

    // Session operations
    Task<SessionReport> GenerateReportAsync(string sessionId, CancellationToken ct);
    Task<ExportResult> ExportSessionAsync(string sessionId, ExportOptions options, CancellationToken ct);
    Task<VerificationResult> VerifySessionAsync(string sessionId, CancellationToken ct);

    // Bulk operations
    Task<SessionComparison> CompareSessionsAsync(string[] sessionIds, CancellationToken ct);
    Task<MergeResult> MergeSessionsAsync(string[] sessionIds, string newSessionName, CancellationToken ct);
}
```

### 5. Session Storage Organization

```
sessions/
├── 2026-01-03-regular/
│   ├── manifest.json           # Session metadata and file list
│   ├── report.json             # Session summary report
│   ├── quality.json            # Quality metrics
│   ├── data/
│   │   ├── AAPL/
│   │   │   ├── Trade.jsonl.gz
│   │   │   └── BboQuote.jsonl.gz
│   │   ├── MSFT/
│   │   └── SPY/
│   └── checksums.sha256        # Verification checksums
├── 2026-01-02-regular/
└── index.json                  # Session index for quick lookup
```

### 6. Session Tags & Organization

```csharp
record SessionTagging(
    string[] Tags,               // ["Q1-2026", "Pre-Earnings", "High-Volume"]
    string[] Categories,         // ["Regular", "Extended", "Special"]
    Dictionary<string, string> CustomMetadata
);

interface ISessionOrganizer
{
    Task TagSessionAsync(string sessionId, string[] tags, CancellationToken ct);
    Task<CollectionSession[]> FindByTagsAsync(string[] tags, CancellationToken ct);
    Task<SessionGroup[]> GroupByTagAsync(string tag, CancellationToken ct);
}
```

---

## Implementation Roadmap

The roadmap is intentionally sequenced to preserve ingestion reliability while layering in stronger lifecycle controls, observability, and archival capabilities.

### Phase 1: Foundation & Core Infrastructure ✅ Complete
**Objective:** Establish canonical layout and metadata primitives without disrupting current collectors.

- [x] Implement source registry configuration
- [x] Add hierarchical naming convention option
- [x] Create file manifest generation
- [x] Implement per-source quota tracking
- [x] Build basic file health check service

**Exit criteria:**
- At least one production collection path writes manifests and source identifiers consistently
- Existing naming modes continue to function with zero migration requirement

### Phase 2: Storage Policies & Lifecycle ✅ Complete
**Objective:** Move from static retention settings to policy-driven lifecycle behavior.

- [x] Define policy configuration schema
- [x] Implement policy evaluation engine
- [x] Add compression policy matrix
- [ ] Create backup policy executor
- [x] Implement scheduled maintenance tasks

**Exit criteria:**
- Dry-run mode can explain every policy decision for a representative 30-day data set
- Operators can apply policy updates without restart

### Phase 3: Tiered Storage ✅ Complete
**Objective:** Introduce deterministic hot/warm/cold movement while preserving queryability and auditability.

- [x] Define tier configuration schema
- [x] Implement tier migration service
- [x] Add Parquet conversion pipeline
- [x] Create unified query interface
- [x] Build file compaction service

**Exit criteria:**
- Tier transitions are idempotent and resumable
- Query interface can discover files regardless of active tier

### Phase 4: Perpetual Storage & Compliance ✅ Complete
**Objective:** Provide compliance-grade archival guarantees and lineage transparency.

- [x] Implement archive tier with WORM support
- [x] Add data catalog service
- [ ] Create compliance reporting
- [x] Implement immutability guarantees
- [x] Build data lineage tracking

**Exit criteria:**
- Retention lock and legal-hold workflows are testable in non-production
- End-to-end lineage from ingest source to archive object is queryable

### Phase 5: Data Quality & Robustness ✅ Complete
**Objective:** Quantify data trust and automate remediation of common defects.

- [x] Implement quality scoring engine
- [x] Add quality dimension evaluators (completeness, accuracy, etc.)
- [ ] Build best-of-breed data selector
- [ ] Create quality trend monitoring
- [ ] Implement auto-backfill for gaps

**Exit criteria:**
- Quality score computation is reproducible for a fixed input corpus
- Gap detection and backfill produce a measurable reduction in missing intervals

### Phase 6: Search & Discovery ✅ Complete
**Objective:** Reduce time-to-discovery for datasets and support analyst self-service workflows.

- [x] Build multi-level index architecture
- [x] Implement file and event search APIs
- [ ] Add faceted search support
- [ ] Create natural language query parser
- [x] Build real-time index maintenance

**Exit criteria:**
- Common metadata queries return within agreed SLO bounds
- Index rebuild procedures are documented and recoverable

### Phase 7: Metadata & Insights ✅ Complete
**Objective:** Turn passive metadata into actionable operational insight.

- [x] Implement rich file metadata schema
- [ ] Build automated insight generator
- [x] Create usage analytics tracking
- [ ] Implement metadata-driven automation rules
- [ ] Build actionable dashboards

**Exit criteria:**
- Metadata lineage supports both engineering and audit reporting needs
- At least one automated optimization action can be safely replayed from metadata

### Phase 8: Operational Scheduling ✅ Complete
**Objective:** Align maintenance and heavy processing with market-aware idle windows.

- [x] Implement trading hours awareness service
- [x] Build maintenance window configuration
- [x] Create maintenance scheduler with job queuing
- [x] Implement real-time collection coordinator
- [ ] Add job priority and resource management
- [ ] Build adaptive scheduling with duration prediction

**Exit criteria:**
- Maintenance workload consistently avoids peak ingestion windows
- Scheduler can preempt non-critical jobs under collection pressure

### Phase 9: Self-Healing & Advanced Features 🔄 In Progress
**Objective:** Improve resilience with autonomous correction and forecasting.

- [x] Implement self-healing repair capabilities
- [x] Add orphan detection and cleanup
- [ ] Build cross-source reconciliation
- [ ] Create capacity forecasting
- [ ] Add adaptive partitioning
- [ ] Implement emergency override system

**Exit criteria:**
- Self-healing actions are logged, reversible where possible, and policy-governed
- Forecasting models provide capacity early-warning signals with operational lead time

## Decision Log

| ID | Decision | Rationale | Review Trigger |
|----|----------|-----------|----------------|
| D-001 | Keep JSONL as the default hot-tier ingest format | Append-only writes are simple, robust, and easy to validate | Sustained ingest bottlenecks or downstream compatibility constraints |
| D-002 | Treat Parquet as a derived archival/export representation | Separates ingest reliability concerns from analytics optimization | Need for direct-query SLA on archival data |
| D-003 | Prefer metadata sidecars/manifests over filename-only semantics | Improves schema evolution and machine-readable discovery | Significant storage overhead from sidecar proliferation |
| D-004 | Enforce policy-driven lifecycle before introducing autonomous self-healing | Prevents opaque automation and improves operator trust | Proven low-risk automation and rollback maturity |

## Open Questions

1. Should per-source quotas support hard-stop mode, soft warning mode, or both?
2. What is the authoritative clock for partition boundaries in multi-region deployments?
3. Which compliance profiles require immutable retention locks at write time versus post-ingest sealing?
4. Should the first unified query interface be file-centric (metadata + paths) or record-centric (predicate pushdown)?
5. What minimum recovery point objective (RPO) is required for manifest/catalog data versus raw files?

---

## Configuration Examples

### Minimal Configuration (Development)

```json
{
  "Storage": {
    "NamingConvention": "BySymbol",
    "DatePartition": "Daily",
    "RetentionDays": 7,
    "Compress": false
  }
}
```

### Standard Configuration (Production)

```json
{
  "Storage": {
    "NamingConvention": "Hierarchical",
    "DatePartition": "Daily",
    "Compress": true,
    "CompressionCodec": "gzip",
    "RetentionDays": 90,
    "MaxTotalBytes": 107374182400,
    "Quotas": {
      "PerSource": {
        "alpaca": { "MaxBytes": 53687091200 }
      }
    },
    "Policies": {
      "Trade": {
        "Classification": "Critical",
        "WarmTierDays": 30,
        "ColdTierDays": 180
      }
    }
  }
}
```

### Enterprise Configuration (Compliance)

```json
{
  "Storage": {
    "NamingConvention": "Canonical",
    "DatePartition": "Daily",
    "Compress": true,
    "CompressionCodec": "zstd",
    "Tiering": {
      "Enabled": true,
      "Tiers": ["hot", "warm", "cold", "archive"]
    },
    "Archive": {
      "Enabled": true,
      "Worm": true,
      "Reason": "Regulatory",
      "MinRetentionYears": 7
    },
    "Catalog": {
      "Enabled": true,
      "SignManifests": true
    },
    "Reconciliation": {
      "Enabled": true,
      "Sources": ["alpaca", "ib"]
    }
  }
}
```

---

## Summary

This design provides a comprehensive framework for storage organization that supports **data collection and archival as the primary mission**, with analysis performed externally.

### Core Archival Capabilities

1. **Collection Excellence**: Reliable, gap-free data capture from multiple sources
2. **Archival Integrity**: Long-term storage with verification, checksums, and format preservation
3. **Export Flexibility**: Easy extraction in formats suitable for external analysis tools
4. **Storage Efficiency**: Optimal compression, tiering, and organization for archival workloads

### Full Feature Set

1. **Scales** from development to enterprise compliance requirements
2. **Optimizes** storage costs through intelligent tiering and compression
3. **Ensures** data integrity through checksums, validation, and reconciliation
4. **Supports** perpetual data retention for regulatory compliance
5. **Enables** flexible querying across multiple sources and time ranges
6. **Manages** capacity proactively through quotas and forecasting
7. **Maintains** file health with automated checks, self-healing, and defragmentation
8. **Guarantees** data robustness through quality scoring and best-of-breed selection
9. **Discovers** data efficiently with multi-level indexes and faceted search
10. **Provides** actionable insights through rich metadata and automated recommendations
11. **Schedules** maintenance intelligently during off-hours to avoid impacting real-time collection
12. **Exports** data efficiently with tool-specific profiles for Python, R, QuantConnect, and databases
13. **Organizes** collection into discrete sessions for better tracking and verification

### Key Capabilities Matrix

| Capability | Description | Business Value |
|------------|-------------|----------------|
| **Archival Pipeline** | Write-ahead logging, crash-safe persistence | Zero data loss on failures |
| **Session Management** | Discrete collection sessions with reports | Organized, verifiable archives |
| **Export Architecture** | Tool-specific export profiles | Seamless external analysis |
| **File Maintenance** | Health checks, self-healing, compaction | Reduced manual ops, fewer outages |
| **Quality Scoring** | 6-dimension quality evaluation | Trust in data, better decisions |
| **Best-of-Breed** | Auto-select highest quality source | Always use best available data |
| **Search Infrastructure** | Indexes, faceted search, NL queries | Find any data in seconds |
| **Metadata & Lineage** | Full lifecycle tracking | Audit trail, reproducibility |
| **Automated Insights** | Proactive recommendations | Prevent issues before they occur |
| **Usage Analytics** | Access pattern tracking | Optimize for actual workloads |
| **Off-Hours Scheduling** | Trading-hours-aware maintenance | Zero impact on live data collection |

### Architecture Philosophy

The modular design allows incremental adoption—start with basic naming conventions and progressively add policies, tiering, quality management, scheduling, and advanced search as needs grow.

**Archival-First Approach**: While the system maintains flexibility for future integration with cloud storage, real-time streaming, and online databases, the current priority is building a robust, self-contained offline archive. This focus ensures:

- Data is always safely persisted before any other processing
- Archives are self-describing with embedded manifests and schemas
- Export to external analysis tools is optimized and well-documented
- All existing cloud/online storage features remain available for future use

---

**Version:** 2.1.0
**Last Updated:** 2026-03-14
**Focus:** Data Collection, Archival & External Analysis Export
**See Also:** [Meridian README](https://github.com/rodoHasArrived/Meridian/blob/main/README.md) | [Architecture Overview](overview.md) | [Configuration Guide](../HELP.md#configuration) | [ADR-002: Tiered Storage](../adr/002-tiered-storage-architecture.md)
