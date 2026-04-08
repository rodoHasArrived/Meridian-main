# Crystallized Storage Format Specification

Version: 1.1
Status: Stable
Last Updated: 2026-03-14

## Overview

The Crystallized Storage Format is a standardized, intuitive data organization system designed for market data. It supports multiple data providers, various data types (bars, trades, quotes, order books), and different time granularities while remaining accessible to both casual Excel users and advanced ML practitioners.

### Design Goals

1. **Provider-agnostic**: Consistent structure regardless of data source
2. **Time granularity support**: From tick data to monthly bars
3. **Self-documenting**: File names and manifests explain the contents
4. **Excel-friendly**: CSV export with standard columns
5. **ML-optimized**: Efficient bulk access with consistent schemas
6. **Discoverable**: Catalog and manifest files for automated exploration
7. **Durable**: Write-Ahead Log (WAL) ensures crash-safe persistence

---

## Directory Structure

```
data/
├── _catalog/                                  # Catalog metadata directory
│   ├── manifest.json                          # Root catalog (all symbols/providers)
│   ├── sources.json                           # Registered data sources
│   └── schemas/                              # Schema definitions per version
│       ├── v1.json
│       └── v2.json
├── _wal/                                      # Write-Ahead Log files (crash recovery)
│   └── *.wal
├── {provider}/                                # Data source (alpaca, polygon, yahoo, etc.)
│   └── {symbol}/                              # Trading symbol (AAPL, SPY, etc.)
│       ├── _manifest.json                     # Symbol metadata and data summary
│       ├── bars/                              # OHLCV price bars
│       │   ├── tick/                          # Tick bars (rare)
│       │   ├── 1s/                            # 1-second bars
│       │   ├── 5s/                            # 5-second bars
│       │   ├── 1m/                            # 1-minute bars
│       │   ├── 5m/                            # 5-minute bars
│       │   ├── 15m/                           # 15-minute bars
│       │   ├── 30m/                           # 30-minute bars
│       │   ├── 1h/                            # 1-hour bars
│       │   ├── 4h/                            # 4-hour bars
│       │   ├── daily/                         # Daily bars (end-of-day)
│       │   ├── weekly/                        # Weekly bars
│       │   └── monthly/                       # Monthly bars
│       ├── trades/                            # Tick-by-tick trade prints
│       ├── quotes/                            # Best bid/offer snapshots
│       ├── orderbook/                         # Level 2 order book
│       ├── orderflow/                         # Pre-computed order flow stats
│       │   ├── 1m/
│       │   ├── 5m/
│       │   └── ...
│       ├── auctions/                          # Opening/closing auction data
│       └── corporate_actions/                 # Dividends, splits, etc.
└── _system/                                   # System events (global)
    └── events_{date}.jsonl
```

---

## Time Granularities

| Granularity | File Suffix | Use Case | Typical Partition |
|-------------|-------------|----------|-------------------|
| Tick | `tick` | Market microstructure, precise fills | Hourly |
| 1 Second | `1s` | High-frequency analysis | Hourly |
| 5 Seconds | `5s` | Scalping strategies | Hourly |
| 15 Seconds | `15s` | Short-term momentum | Hourly |
| 30 Seconds | `30s` | Short-term momentum | Hourly |
| 1 Minute | `1m` | Intraday trading | Daily |
| 5 Minutes | `5m` | Day trading | Daily |
| 15 Minutes | `15m` | Swing trading | Daily |
| 30 Minutes | `30m` | Swing trading | Daily |
| 1 Hour | `1h` | Position trading | Daily |
| 4 Hours | `4h` | Multi-day positions | Daily |
| Daily | `daily` | End-of-day analysis | Monthly |
| Weekly | `weekly` | Long-term trends | Single file |
| Monthly | `monthly` | Macro analysis | Single file |

---

## Data Categories

### Bars (OHLCV)
OHLCV price bars aggregated at various time intervals.

**Columns:**
| Column | Type | Description |
|--------|------|-------------|
| timestamp | datetime | Bar open time (UTC) |
| open | decimal | Opening price |
| high | decimal | Highest price |
| low | decimal | Lowest price |
| close | decimal | Closing price |
| volume | long | Total volume |
| vwap | decimal? | Volume-weighted average price |
| trades_count | int? | Number of trades in bar |

**Path Pattern:** `{root}/{provider}/{symbol}/bars/{granularity}/{date}.{ext}`

**Example Files:**
- `data/alpaca/AAPL/bars/daily/2024-01.jsonl`
- `data/polygon/SPY/bars/1m/2024-01-15.jsonl`

---

### Trades
Individual trade executions (tick data).

**Columns:**
| Column | Type | Description |
|--------|------|-------------|
| timestamp | datetime | Trade time (UTC, microsecond precision) |
| price | decimal | Execution price |
| size | long | Trade size |
| side | string | Aggressor side: "buy", "sell", "unknown" |
| sequence | long | Sequence number |
| venue | string? | Execution venue/exchange |
| conditions | string? | Trade condition codes |

**Path Pattern:** `{root}/{provider}/{symbol}/trades/{date}.{ext}`

**Example:** `data/alpaca/AAPL/trades/2024-01-15.jsonl`

---

### Quotes (BBO)
Best bid/offer snapshots.

**Columns:**
| Column | Type | Description |
|--------|------|-------------|
| timestamp | datetime | Quote time (UTC) |
| bid_price | decimal | Best bid price |
| bid_size | long | Size at best bid |
| ask_price | decimal | Best ask price |
| ask_size | long | Size at best ask |
| spread | decimal | Ask - Bid |
| mid_price | decimal | (Bid + Ask) / 2 |
| sequence | long | Sequence number |

**Path Pattern:** `{root}/{provider}/{symbol}/quotes/{date}.{ext}`

---

### Order Book (Level 2)
Full order book with multiple price levels.

**Columns:**
| Column | Type | Description |
|--------|------|-------------|
| timestamp | datetime | Snapshot time (UTC) |
| level | int | Price level (0 = best) |
| bid_price | decimal | Bid price at level |
| bid_size | long | Size at bid level |
| ask_price | decimal | Ask price at level |
| ask_size | long | Size at ask level |
| sequence | long | Sequence number |

**Path Pattern:** `{root}/{provider}/{symbol}/orderbook/{date}.{ext}`

---

### Order Flow
Pre-computed order flow statistics.

**Columns:**
| Column | Type | Description |
|--------|------|-------------|
| timestamp | datetime | Period timestamp |
| imbalance | decimal | Order flow imbalance |
| vwap | decimal | Volume-weighted average price |
| buy_volume | long | Buy-side volume |
| sell_volume | long | Sell-side volume |
| total_volume | long | Total volume |
| sequence | long | Sequence number |

**Path Pattern:** `{root}/{provider}/{symbol}/orderflow/{granularity}/{date}.{ext}`

---

## File Naming Conventions

### Standard Naming (within directory structure)
Files are named by their date partition:
```
2026-01-15.jsonl      # Daily partition
2026-01-15_14.jsonl   # Hourly partition (14:00 UTC)
2026-01.jsonl         # Monthly partition
all.jsonl             # No partition (weekly/monthly bars)
```

### Self-Documenting Naming (portable)
For files that may be moved or shared, the full context is embedded:
```
AAPL_alpaca_bars_daily_2026-01-15.csv
SPY_polygon_trades_2026-01-15.jsonl.gz
MSFT_yahoo_bars_1h_2026-01-15.jsonl
```

Format: `{symbol}_{provider}_{category}_{granularity}_{date}.{ext}`

### Directory Organization Conventions (`FileNamingConvention`)

Eight naming conventions are supported. Select the one that best fits your access pattern:

| Convention | Path Pattern | Best For |
|------------|-------------|----------|
| `Flat` | `{root}/{symbol}_{type}_{date}.jsonl` | Small datasets, quick inspection |
| `BySymbol` | `{root}/{symbol}/{type}/{date}.jsonl` | Analyzing individual symbols over time (**default**) |
| `ByDate` | `{root}/{date}/{symbol}/{type}.jsonl` | Daily batch processing and archival |
| `ByType` | `{root}/{type}/{symbol}/{date}.jsonl` | Analyzing a specific event type across many symbols |
| `BySource` | `{root}/{source}/{symbol}/{type}/{date}.jsonl` | Comparing data across providers |
| `ByAssetClass` | `{root}/{asset_class}/{symbol}/{type}/{date}.jsonl` | Managing multiple asset classes |
| `Hierarchical` | `{root}/{source}/{asset_class}/{symbol}/{type}/{date}.jsonl` | Enterprise multi-source deployments |
| `Canonical` | `{root}/{year}/{month}/{day}/{source}/{symbol}/{type}.jsonl` | Time-series archival |

---

## File Formats

### JSONL (JSON Lines)
- One JSON object per line
- Best for: Streaming writes, flexible schema, nested data
- Extension: `.jsonl` (compressed: `.jsonl.gz`, `.jsonl.zst`, `.jsonl.lz4`)

**Example (bar):**
```jsonl
{"timestamp":"2026-01-15T09:30:00Z","open":185.50,"high":186.20,"low":185.40,"close":186.00,"volume":1250000}
{"timestamp":"2026-01-15T09:31:00Z","open":186.00,"high":186.50,"low":185.90,"close":186.30,"volume":890000}
```

### CSV
- Standard comma-separated values
- Best for: Excel, simple analysis, pandas
- Extension: `.csv` (compressed: `.csv.gz`)

**Example (bar):**
```csv
timestamp,open,high,low,close,volume
2026-01-15,185.50,186.20,185.40,186.00,1250000
2026-01-16,186.00,187.10,185.80,186.90,1100000
```

### Parquet
- Columnar format for analytics
- Best for: ML workloads, large datasets, cloud storage
- Extension: `.parquet`

### Arrow (Feather)
- Apache Arrow IPC format
- Best for: Zero-copy interop with Python (PyArrow), R, Julia, and Spark
- Extension: `.arrow` / `.feather`

### Compression Codecs

| Codec | Extension Suffix | Use Case |
|-------|-----------------|---------|
| None | *(no suffix)* | Real-time hot tier, maximum read speed |
| LZ4 | `.lz4` | Fast compression with lower ratio; live streaming |
| Gzip | `.gz` | Balanced compression; general purpose |
| Zstd | `.zst` | High ratio; warm/cold archive tiers |
| Brotli | `.br` | High ratio, slower write; static archival |

---

## Manifest Files

### Symbol Manifest (`_manifest.json`)
Located in each symbol directory. Describes available data.

```json
{
  "schema_version": 1,
  "symbol": "AAPL",
  "provider": "alpaca",
  "description": "Apple Inc.",
  "asset_class": "equity",
  "exchange": "NASDAQ",
  "currency": "USD",
  "categories": {
    "bars": {
      "display_name": "Price Bars (OHLCV)",
      "granularities": ["1m", "5m", "15m", "1h", "daily"],
      "earliest_date": "2022-01-02",
      "latest_date": "2026-01-15",
      "file_count": 1248,
      "total_bytes": 524288000,
      "columns": ["timestamp", "open", "high", "low", "close", "volume", "vwap", "trades_count"]
    },
    "trades": {
      "display_name": "Trade Prints",
      "earliest_date": "2026-01-02",
      "latest_date": "2026-01-15",
      "file_count": 10,
      "total_bytes": 2147483648,
      "row_count": 45000000,
      "columns": ["timestamp", "price", "size", "side", "sequence", "venue", "conditions"]
    }
  },
  "earliest_date": "2022-01-02",
  "latest_date": "2026-01-15",
  "total_files": 1258,
  "total_bytes": 2671771648,
  "updated_at": "2026-01-15T23:59:59Z"
}
```

### Root Catalog (`_catalog/manifest.json`)
Located in the `_catalog/` directory under the root data path. Global index of all collected data.

```json
{
  "schema_version": 1,
  "title": "Market Data Collection",
  "description": "Historical market data for US equities",
  "providers": [
    {
      "name": "alpaca",
      "display_name": "Alpaca Markets",
      "symbol_count": 150,
      "categories": ["bars", "trades", "quotes"]
    },
    {
      "name": "yahoo",
      "display_name": "Yahoo Finance",
      "symbol_count": 500,
      "categories": ["bars"]
    }
  ],
  "symbols": [
    {
      "symbol": "AAPL",
      "provider": "alpaca",
      "asset_class": "equity",
      "categories": ["bars", "trades", "quotes"],
      "earliest_date": "2022-01-02",
      "latest_date": "2026-01-15",
      "manifest_path": "alpaca/AAPL/_manifest.json"
    }
  ],
  "date_range": {
    "earliest": "2022-01-02",
    "latest": "2026-01-15",
    "trading_days": 1005
  },
  "storage": {
    "total_files": 15000,
    "total_bytes": 107374182400,
    "total_bytes_human": "100 GB"
  },
  "updated_at": "2026-01-15T23:59:59Z",
  "format": {
    "version": "1.1",
    "file_format": "jsonl",
    "compression": "gzip",
    "self_documenting_names": true
  }
}
```

---

## Usage Examples

### Excel Users

1. **Finding data:**
   - Open `_catalog/manifest.json` to see available symbols
   - Navigate to `data/{provider}/{symbol}/_manifest.json` for details

2. **Opening daily bars:**
   ```
   data/yahoo/AAPL/bars/daily/2026-01.csv
   ```
   - Double-click to open in Excel
   - Columns: date, open, high, low, close, volume

3. **Combining files:**
   - Use Excel's Power Query to combine monthly files
   - Or use the CSV exporter to create a single file

### Python/Pandas Users

```python
import pandas as pd
from pathlib import Path

# Read daily bars
bars = pd.read_json(
    'data/alpaca/AAPL/bars/daily/2026-01.jsonl',
    lines=True
)

# Read all daily bars for a symbol
files = Path('data/alpaca/AAPL/bars/daily').glob('*.jsonl')
bars = pd.concat([pd.read_json(f, lines=True) for f in files])

# Read trades
trades = pd.read_json(
    'data/alpaca/AAPL/trades/2026-01-15.jsonl.gz',
    lines=True,
    compression='gzip'
)
```

### Machine Learning Pipeline

```python
import json
from pathlib import Path

# Discover available data
with open('data/_catalog/manifest.json') as f:
    catalog = json.load(f)

# Get all symbols with daily bars
symbols_with_bars = [
    s['symbol'] for s in catalog['symbols']
    if 'bars' in s['categories']
]

# Build feature matrix from multiple granularities
granularities = ['1m', '5m', '15m', '1h', 'daily']
for gran in granularities:
    path = Path(f'data/alpaca/AAPL/bars/{gran}')
    if path.exists():
        # Process files...
        pass
```

---

## Storage Profiles

Three built-in profiles are provided via `StorageProfilePresets`. Apply a profile using `StorageProfilePresets.ApplyProfile()` or `StorageProfilePresets.CreateFromProfile()`. The default profile when none is specified is **Research**.

| Profile | Compression | Manifests | Checksums | Partition Strategy | Date Granularity | Notes |
|---------|------------|-----------|-----------|-------------------|-----------------|-------|
| **Research** | Gzip | ✅ | — | Date + Symbol | Daily | Balanced defaults for analysis workflows |
| **LowLatency** | None | — | — | Symbol + EventType | Hourly | Prioritizes ingest speed with minimal processing |
| **Archival** | Zstd | ✅ | ✅ | Date + Source | Monthly | Long-term retention; 4-tier pipeline; 10 yr / 2 TB caps |

### For Research / Analysis
```csharp
var options = StorageProfilePresets.CreateFromProfile("Research", rootPath: "data");
// - Gzip compression
// - Manifests enabled
// - Partition: Date + Symbol, daily granularity
```

### For Low-Latency Collection
```csharp
var options = StorageProfilePresets.CreateFromProfile("LowLatency", rootPath: "data");
// - No compression (maximum write speed)
// - Manifests disabled
// - Partition: Symbol + EventType, hourly granularity
```

### For Long-Term Archival
```csharp
var options = StorageProfilePresets.CreateFromProfile("Archival", rootPath: "data");
// - Zstd compression
// - Manifests + checksums enabled
// - 4-tier storage pipeline: hot → warm → cold → archive
//   hot:     up to 7 days,   JSONL, no compression
//   warm:    up to 30 days,  JSONL, Gzip
//   cold:    up to 180 days, Parquet, Zstd
//   archive: indefinite,     Parquet, Zstd
// - Retention: 10 years / 2 TB hard cap
// - Partition: Date + Source, monthly granularity
```

---

## Tiered Storage Architecture

The Archival profile enables a four-tier storage pipeline managed by `TierMigrationService`:

| Tier | Purpose | Default Format | Default Compression | Default Max Age |
|------|---------|---------------|---------------------|----------------|
| **Hot** | Real-time access, NVMe SSD | JSONL | None | 7 days |
| **Warm** | Daily batch access, SSD/HDD | JSONL | Gzip | 30 days |
| **Cold** | Weekly/monthly access, HDD/NAS | Parquet | Zstd | 180 days |
| **Archive** | Rare bulk access, object storage | Parquet | Zstd | Indefinite |

Tier migration runs on a configurable cron schedule (`TieringOptions.MigrationSchedule`). Files are migrated in parallel (`TieringOptions.ParallelMigrations`).

---

## Write-Ahead Log (WAL)

All market events are written to a crash-safe WAL before being committed to primary storage. This guarantees **at-least-once delivery** semantics and enables full crash recovery.

### Event Flow
```
MarketDataClient → EventPipeline → WAL (fsync) → StorageSink → Commit
                                  ↓
                            (crash recovery replays WAL on restart)
```

### WAL Directory
WAL files are stored under `_wal/` (within the root data path) and rotated at 100 MB. Committed WAL files are cleaned up automatically.

### Key Properties
- **SHA-256 checksum** per record for corruption detection
- **Monotonic sequence numbers** for gap detection and recovery ordering
- **~5–10% write overhead** versus direct-to-storage writing
- **Recovery on startup** — `WriteAheadLog.InitializeAsync()` replays any uncommitted records

See [ADR-007](../adr/007-write-ahead-log-durability.md) for the full design rationale.

---

## Storage Sinks

The system supports pluggable storage sinks discovered via `StorageSinkRegistry` (mirrors the provider-layer `DataSourceRegistry` from ADR-005). Sinks are activated through the `Storage.ActiveSinks` configuration list.

### Built-in Sinks

| Sink ID | Class | Description |
|---------|-------|-------------|
| `jsonl` | `JsonlStorageSink` | JSONL file storage with batching and compression |
| `parquet` | `ParquetStorageSink` | Apache Parquet columnar storage (10–20× better compression than JSONL) |

### Composite Sink (Fan-out)
`CompositeSink` fans out events to multiple sinks simultaneously — for example writing JSONL and Parquet at the same time — without modifying the `EventPipeline`. It includes per-sink circuit-breaker health tracking to isolate failures in one sink from others.

```json
{
  "Storage": {
    "ActiveSinks": ["jsonl", "parquet"]
  }
}
```

### JSONL Batching Options
| Preset | Batch Size | Flush Interval | Use Case |
|--------|-----------|----------------|---------|
| `Default` | 1 000 events | 5 s | General purpose |
| `HighThroughput` | 5 000 events | 10 s | High-volume ingest |
| `LowLatency` | 100 events | 1 s | Latency-sensitive collection |
| `NoBatching` | — | — | Immediate per-event write |

### Custom Sinks
Decorate any `IStorageSink` implementation with `[StorageSink("my-sink", "My Sink")]` to register it automatically:

```csharp
[StorageSink("clickhouse", "ClickHouse Storage",
    Description = "Writes market events to ClickHouse for real-time analytics.")]
public sealed class ClickHouseSink : IStorageSink
{
    // Implementation
}
```

---

## Export Profiles

Six built-in export profiles are provided for different external analysis tools. Profiles are applied by `AnalysisExportService` and can be customised or extended.

| Profile ID | Target Tool | Format | Compression | Timestamp Format |
|------------|------------|--------|-------------|-----------------|
| `python-pandas` | Python/Pandas | Parquet | Snappy | Unix nanoseconds |
| `r-stats` | R Statistics | CSV | None | ISO 8601 |
| `quantconnect-lean` | QuantConnect Lean | Lean native | Zip | Unix milliseconds (ET) |
| `excel` | Microsoft Excel | XLSX | None | ISO 8601 |
| `postgresql` | PostgreSQL | CSV (COPY) | None | ISO 8601 |
| `arrow-feather` | PyArrow / R / Julia | Arrow IPC | None | Unix nanoseconds |

Each profile optionally generates a loader script and data dictionary alongside the exported files.

---

## Migration from Legacy Formats

### From Flat Structure
```bash
# Old: data/AAPL_Trade_2026-01-15.jsonl
# New: data/{provider}/AAPL/trades/2026-01-15.jsonl
```

### From BySymbol Structure
```bash
# Old: data/AAPL/Trade/2026-01-15.jsonl
# New: data/{provider}/AAPL/trades/2026-01-15.jsonl
```

The main changes:
1. Provider is now first-level directory
2. Category names are lowercase
3. Granularity subfolder for bars

---

## Best Practices

1. **Always include provider**: Different providers may have different data quality
2. **Use appropriate granularity**: Don't store tick data if you only need daily bars
3. **Compress older data**: Use Zstd for archived data, LZ4 for recent
4. **Update manifests**: Run manifest scan after bulk imports
5. **Validate on read**: Check schema_version in manifests for compatibility
6. **Use date partitions**: Easier to manage retention and backups
7. **Enable WAL for production**: Crash-safe durability prevents data loss during collection
8. **Use CompositeSink for dual format**: Write JSONL for streaming + Parquet for analytics simultaneously

---

## Schema Versioning

The format uses schema versioning for forward compatibility:

- `schema_version: 1` - Current stable version
- Files with unknown versions should be readable but may have extra fields
- Breaking changes require major version bump

---

## Related Documentation

- [Storage Architecture Overview](./storage-design.md)
- [Data Providers Guide](../providers/backfill-guide.md)
- [WAL Durability (ADR-007)](../adr/007-write-ahead-log-durability.md)
- [Multi-Format Storage (ADR-008)](../adr/008-multi-format-composite-storage.md)
- [AI Assistant Storage Guide](../ai/claude/CLAUDE.storage.md)
- [API Reference](../reference/api-reference.md)
