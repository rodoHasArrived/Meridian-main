# CLAUDE.storage.md - Storage System Guide

This document provides guidance for AI assistants working with the storage system in Meridian.

---

## Storage Architecture Overview

The system implements an **archival-first storage strategy** with Write-Ahead Logging (WAL) for crash-safe persistence.

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│   Ingest    │────►│     WAL     │────►│   Buffer    │────►│   Storage   │
│   Events    │     │  (Durable)  │     │  (Memory)   │     │  (JSONL/    │
│             │     │             │     │             │     │   Parquet)  │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
                           │                                       │
                           └───────────► Commit Point ◄────────────┘
```

---

## File Locations

The storage system comprises **61 files** across multiple subsystems.

### Core Storage Components
| File | Purpose |
|------|---------|
| `Storage/StorageOptions.cs` | Configuration options |
| `Storage/StorageProfiles.cs` | Pre-configured storage profiles |

### Sinks (4 files)
| File | Purpose |
|------|---------|
| `Storage/Sinks/JsonlStorageSink.cs` | JSONL file persistence |
| `Storage/Sinks/ParquetStorageSink.cs` | Parquet columnar storage |
| `Storage/Sinks/CompositeSink.cs` | Dual-write to multiple formats simultaneously |
| `Storage/Sinks/CatalogSyncSink.cs` | Catalog synchronization sink |

### Policies (1 file)
| File | Purpose |
|------|---------|
| `Storage/Policies/JsonlStoragePolicy.cs` | File naming and organization |

### Replay (2 files)
| File | Purpose |
|------|---------|
| `Storage/Replay/JsonlReplayer.cs` | Historical data replay |
| `Storage/Replay/MemoryMappedJsonlReader.cs` | High-performance memory-mapped reader |

### Archival System (5 files)
| File | Purpose |
|------|---------|
| `Storage/Archival/ArchivalStorageService.cs` | Archival-first pipeline |
| `Storage/Archival/WriteAheadLog.cs` | WAL implementation |
| `Storage/Archival/AtomicFileWriter.cs` | Safe file operations |
| `Storage/Archival/SchemaVersionManager.cs` | Schema versioning |
| `Storage/Archival/CompressionProfileManager.cs` | Compression profiles |

### Services (16 files)
| File | Purpose |
|------|---------|
| `Storage/Services/DataLineageService.cs` | Data lineage tracking |
| `Storage/Services/DataQualityScoringService.cs` | Data quality scoring |
| `Storage/Services/DataQualityService.cs` | Data quality checks |
| `Storage/Services/EventBuffer.cs` | Event buffering |
| `Storage/Services/FileMaintenanceService.cs` | File cleanup and maintenance |
| `Storage/Services/FilePermissionsService.cs` | File permission management |
| `Storage/Services/LifecyclePolicyEngine.cs` | Lifecycle policy enforcement |
| `Storage/Services/MaintenanceScheduler.cs` | Maintenance scheduling |
| `Storage/Services/MetadataTagService.cs` | Metadata tagging |
| `Storage/Services/QuotaEnforcementService.cs` | Quota enforcement |
| `Storage/Services/SourceRegistry.cs` | Data source registration |
| `Storage/Services/StorageCatalogService.cs` | Storage catalog management |
| `Storage/Services/StorageChecksumService.cs` | Checksum verification |
| `Storage/Services/StorageSearchService.cs` | File search and discovery |
| `Storage/Services/SymbolRegistryService.cs` | Symbol registry management |
| `Storage/Services/TierMigrationService.cs` | Hot/warm/cold tier migration |

### Export System (10 files)
| File | Purpose |
|------|---------|
| `Storage/Export/AnalysisExportService.cs` | Export orchestration |
| `Storage/Export/AnalysisExportService.Formats.cs` | Format selection logic |
| `Storage/Export/AnalysisExportService.Formats.Arrow.cs` | Apache Arrow export |
| `Storage/Export/AnalysisExportService.Formats.Parquet.cs` | Parquet export |
| `Storage/Export/AnalysisExportService.Formats.Xlsx.cs` | Excel export |
| `Storage/Export/AnalysisExportService.IO.cs` | I/O operations |
| `Storage/Export/AnalysisQualityReport.cs` | Quality report generation |
| `Storage/Export/ExportProfile.cs` | Pre-built export profiles |
| `Storage/Export/ExportRequest.cs` | Export request configuration |
| `Storage/Export/ExportResult.cs` | Export result information |

### Packaging System (9 files)
| File | Purpose |
|------|---------|
| `Storage/Packaging/PortableDataPackager.cs` | Portable data package creation |
| `Storage/Packaging/PortableDataPackager.Creation.cs` | Package creation logic |
| `Storage/Packaging/PortableDataPackager.Scripts.cs` | Script generation |
| `Storage/Packaging/PortableDataPackager.Scripts.Import.cs` | Import script generation |
| `Storage/Packaging/PortableDataPackager.Scripts.Sql.cs` | SQL script generation |
| `Storage/Packaging/PortableDataPackager.Validation.cs` | Package validation |
| `Storage/Packaging/PackageManifest.cs` | Package manifest definition |
| `Storage/Packaging/PackageOptions.cs` | Packaging configuration |
| `Storage/Packaging/PackageResult.cs` | Packaging result information |

### Maintenance System (6 files)
| File | Purpose |
|------|---------|
| `Storage/Maintenance/ScheduledArchiveMaintenanceService.cs` | Scheduled maintenance |
| `Storage/Maintenance/ArchiveMaintenanceScheduleManager.cs` | Schedule management |
| `Storage/Maintenance/IArchiveMaintenanceScheduleManager.cs` | Schedule manager interface |
| `Storage/Maintenance/IArchiveMaintenanceService.cs` | Maintenance interface |
| `Storage/Maintenance/IMaintenanceExecutionHistory.cs` | Execution history interface |
| `Storage/Maintenance/ArchiveMaintenanceModels.cs` | Maintenance models |

Maintenance execution history and schedule metadata are part of the execution completion contract.
`ScheduledArchiveMaintenanceService` should await `MaintenanceExecutionHistory` and `ArchiveMaintenanceScheduleManager` persistence before a run is treated as queued, started, or finished.
Avoid fire-and-forget persistence and avoid `CancellationToken.None` for shutdown-sensitive maintenance metadata writes.

### Interfaces (5 files)
| File | Purpose |
|------|---------|
| `Storage/Interfaces/IStorageSink.cs` | Storage sink interface |
| `Storage/Interfaces/IStoragePolicy.cs` | Storage policy interface |
| `Storage/Interfaces/ISourceRegistry.cs` | Source registry interface |
| `Storage/Interfaces/IStorageCatalogService.cs` | Storage catalog interface |
| `Storage/Interfaces/ISymbolRegistryService.cs` | Symbol registry interface |

---

## File Organization

### Naming Conventions

Four naming conventions are available (`FileNamingConvention` enum):

| Convention | Structure | Best For |
|------------|-----------|----------|
| **BySymbol** | `{root}/{symbol}/{type}/{date}.jsonl` | Analyzing individual symbols over time |
| **ByDate** | `{root}/{date}/{symbol}/{type}.jsonl` | Daily batch processing |
| **ByType** | `{root}/{type}/{symbol}/{date}.jsonl` | Analyzing event types across symbols |
| **Flat** | `{root}/{symbol}_{type}_{date}.jsonl` | Small datasets, simple structure |

### Date Partitioning

| Partition | Description | Use Case |
|-----------|-------------|----------|
| **None** | Single file per symbol/type | Append-only, small datasets |
| **Daily** | One file per day (default) | Standard usage |
| **Hourly** | One file per hour | High-volume scenarios |
| **Monthly** | One file per month | Long-term storage |

### Example Directory Structure

```
data/
├── live/                           # Real-time data (hot tier)
│   └── alpaca/
│       └── 2026-01-08/
│           ├── AAPL_trades.jsonl.gz
│           ├── AAPL_quotes.jsonl.gz
│           ├── SPY_trades.jsonl.gz
│           └── SPY_quotes.jsonl.gz
├── historical/                     # Backfill data
│   └── yahoo/
│       └── 2026-01-08/
│           ├── AAPL_bars.jsonl
│           └── SPY_bars.jsonl
├── _archive/                       # Compressed archives (cold tier)
│   └── parquet/
│       └── bars/
│           ├── AAPL_2025.parquet
│           └── SPY_2025.parquet
└── _wal/                           # Write-Ahead Log
    └── segment_00001.wal
```

---

## Configuration

### Host-Level Data Root

Meridian resolves storage roots in two stages:

1. `AppConfig.DataRoot` selects the logical storage root from `appsettings.json`.
2. The active host resolves that value against the active config file location, then passes the absolute result into `StorageOptions.RootPath`.

For the installed WPF desktop host, the active config file is `%LocalAppData%\Meridian\appsettings.json`, so relative `DataRoot` values land outside the install directory by default. The repository `config/appsettings.json` file remains the normal CLI, server, and local development config surface.

Legacy desktop configs that still carry `Storage.BaseDirectory` should be treated as migration input only. New guidance and new config writes should prefer top-level `DataRoot`.
Wizard review/save flows should serialize through `AppConfigJsonOptions.Write` and persist through `ConfigStore` so the preview JSON, the saved file, and the resolved active config path stay aligned.
Paper-session order history is part of the session continuity contract; await the durable order-history append before treating an update as committed.

### AppConfig Excerpt

```csharp
public sealed record AppConfig(
    string DataRoot = "data",
    bool? Compress = null,
    StorageConfig? Storage = null,
    ...
);
```

### StorageOptions Excerpt

```csharp
public sealed class StorageOptions
{
    public string RootPath { get; init; } = "data";
    public bool Compress { get; init; } = false;
    public CompressionCodec CompressionCodec { get; init; } = CompressionCodec.Gzip;
    public FileNamingConvention NamingConvention { get; init; } = FileNamingConvention.BySymbol;
    public DatePartition DatePartition { get; init; } = DatePartition.Daily;
    public bool IncludeProvider { get; init; } = false;
    public string? FilePrefix { get; init; }
    public int? RetentionDays { get; init; }
    public long? MaxTotalBytes { get; init; }
    public bool EnableParquetSink { get; init; } = false;
    public IReadOnlyList<string>? ActiveSinks { get; init; }
}
```

### appsettings.json Example

```json
{
  "DataRoot": "data",
  "Storage": {
    "NamingConvention": "BySymbol",
    "DatePartition": "Daily",
    "IncludeProvider": true,
    "RetentionDays": 30,
    "MaxTotalMegabytes": 10240,
    "EnableParquetSink": true,
    "Sinks": ["jsonl", "parquet"]
  }
}
```

### Desktop Persistence Note

On the WPF desktop host, retained local artifacts such as activity history, collection sessions, symbol-mapping overrides, schema dictionaries, watchlists, and workspace metadata follow the resolved external config and data roots instead of the install directory. When auditing upgrade safety, inspect `%LocalAppData%\Meridian\appsettings.json` and the resolved `DataRoot` first.

---

## Compression Profiles

### Pre-Built Profiles

| Profile | Algorithm | Level | Throughput | Ratio | Use Case |
|---------|-----------|-------|------------|-------|----------|
| RealTime | LZ4 | 1 | ~500 MB/s | 2.5x | Hot data collection |
| Standard | Gzip | 6 | ~80 MB/s | 3x | General purpose |
| WarmArchive | ZSTD | 6 | ~150 MB/s | 5x | Recent archives |
| ColdArchive | ZSTD | 19 | ~20 MB/s | 10x | Long-term storage |
| HighVolume | ZSTD | 3 | ~300 MB/s | 4x | SPY, QQQ, etc. |
| Portable | Gzip | 6 | ~80 MB/s | 3x | External sharing |

### Using Compression Profiles

```csharp
var compressionManager = new CompressionProfileManager();

// Get profile for a tier
var profile = compressionManager.GetProfile(StorageTier.Cold);

// Compress data
using var compressedStream = profile.CreateCompressionStream(outputStream);
await data.CopyToAsync(compressedStream);

// Decompress data
using var decompressedStream = profile.CreateDecompressionStream(inputStream);
```

---

## Write-Ahead Logging (WAL)

### Purpose
WAL ensures crash-safe persistence by writing events to a durable log before committing to storage files.

### WAL Sync Modes

| Mode | Description | Durability | Performance |
|------|-------------|------------|-------------|
| NoSync | OS-managed sync | Low | Highest |
| BatchedSync | Periodic flush | Medium | High |
| EveryWrite | Sync on each write | Highest | Lower |

### WAL Operations

```csharp
public sealed class WriteAheadLog : IAsyncDisposable
{
    // Append an event to the log
    public async Task<long> AppendAsync(
        MarketEvent evt,
        CancellationToken ct = default);

    // Commit events up to a sequence number
    public async Task CommitAsync(
        long sequenceNumber,
        CancellationToken ct = default);

    // Recover uncommitted events after crash
    public IAsyncEnumerable<MarketEvent> RecoverAsync(
        CancellationToken ct = default);

    // Truncate committed segments
    public async Task TruncateAsync(CancellationToken ct = default);
}
```

### WAL File Format

Each WAL record contains:
- 4 bytes: Record length
- 8 bytes: Sequence number
- 32 bytes: SHA256 checksum
- N bytes: JSON-serialized event
- 4 bytes: CRC32 trailer

---

## Schema Versioning

### Version Format
All event types use semantic versioning (e.g., `Trade v1.0.0`, `Quote v2.0.0`).

### Schema Registry

```csharp
public sealed class SchemaVersionManager
{
    // Register a new schema version
    public void RegisterSchema<T>(Version version, JsonSchema schema);

    // Get current schema for a type
    public JsonSchema GetCurrentSchema<T>();

    // Get schema for a specific version
    public JsonSchema GetSchema<T>(Version version);

    // Migrate data between versions
    public async Task MigrateAsync<T>(
        Version fromVersion,
        Version toVersion,
        Stream input,
        Stream output,
        CancellationToken ct = default);

    // Export schema as JSON Schema
    public string ExportJsonSchema<T>();
}
```

### Schema Evolution

```csharp
// Define a migration
schemaManager.RegisterMigration<Trade>(
    from: new Version(1, 0, 0),
    to: new Version(2, 0, 0),
    migrate: trade => new Trade_v2(
        trade.Symbol,
        trade.Price,
        trade.Size,
        trade.Timestamp,
        Venue: null,  // New field, default to null
        TradeCondition: null));
```

---

## Tiered Storage

### Storage Tiers

| Tier | Age | Location | Compression | Access Pattern |
|------|-----|----------|-------------|----------------|
| Hot | <24h | `live/` | LZ4 or none | Real-time writes |
| Warm | 1-30 days | `archive/` | ZSTD-6 | Occasional reads |
| Cold | >30 days | `cold/` | ZSTD-19 | Rare reads |

### Tier Migration

```csharp
public sealed class TierMigrationService
{
    // Migrate files from hot to warm tier
    public async Task MigrateToWarmAsync(
        DateTime cutoffDate,
        CancellationToken ct = default);

    // Migrate files from warm to cold tier
    public async Task MigrateToColdAsync(
        DateTime cutoffDate,
        CancellationToken ct = default);

    // Schedule automatic tier migration
    public void ScheduleMigration(
        TimeSpan hotToWarmAge,
        TimeSpan warmToColdAge);
}
```

---

## Data Retention

### Retention Policies

```csharp
public sealed class DataRetentionPolicy
{
    // Time-based retention
    public int? RetentionDays { get; set; }

    // Capacity-based retention
    public long? MaxTotalBytes { get; set; }

    // Per-symbol limits
    public long? MaxBytesPerSymbol { get; set; }

    // Excluded patterns (never delete)
    public List<string> ExcludePatterns { get; set; }
}
```

### Retention Enforcement

```csharp
// In JsonlStorageSink
private async Task EnforceRetentionAsync(CancellationToken ct)
{
    // Time-based cleanup
    if (_options.RetentionDays.HasValue)
    {
        var cutoff = DateTime.UtcNow.AddDays(-_options.RetentionDays.Value);
        await DeleteFilesOlderThanAsync(cutoff, ct);
    }

    // Capacity-based cleanup
    if (_options.MaxTotalBytes.HasValue)
    {
        while (await GetTotalSizeAsync(ct) > _options.MaxTotalBytes.Value)
        {
            await DeleteOldestFileAsync(ct);
        }
    }
}
```

---

## Data Replay

### JsonlReplayer

```csharp
public sealed class JsonlReplayer
{
    private readonly string _dataRoot;

    public JsonlReplayer(string dataRoot)
    {
        _dataRoot = dataRoot;
    }

    // Read all events chronologically
    public async IAsyncEnumerable<MarketEvent> ReadEventsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var files = GetJsonlFiles().OrderBy(f => f.Name);
        foreach (var file in files)
        {
            await foreach (var evt in ReadFileAsync(file, ct))
            {
                yield return evt;
            }
        }
    }

    // Read events for specific symbol
    public IAsyncEnumerable<MarketEvent> ReadSymbolAsync(
        string symbol,
        CancellationToken ct = default);

    // Read events in date range
    public IAsyncEnumerable<MarketEvent> ReadRangeAsync(
        DateTime start,
        DateTime end,
        CancellationToken ct = default);
}
```

### Usage Example

```csharp
var replayer = new JsonlReplayer("./data");

// Replay all events
await foreach (var evt in replayer.ReadEventsAsync(ct))
{
    switch (evt.Type)
    {
        case MarketEventType.Trade:
            ProcessTrade(evt.Payload as Trade);
            break;
        case MarketEventType.Quote:
            ProcessQuote(evt.Payload as BboQuotePayload);
            break;
    }
}

// Replay with filtering
await foreach (var evt in replayer.ReadSymbolAsync("AAPL", ct))
{
    // Process AAPL events only
}
```

---

## Export System

### Pre-Built Export Profiles

| Profile | Format | Target |
|---------|--------|--------|
| PythonPandas | CSV/Parquet | pandas DataFrame |
| RDataFrame | CSV | R data.frame |
| LeanEngine | Lean format | QuantConnect Lean |
| Excel | CSV | Microsoft Excel |
| PostgreSQL | SQL | PostgreSQL COPY |

### AnalysisExportService

```csharp
public sealed class AnalysisExportService
{
    // Export with profile
    public async Task<ExportResult> ExportAsync(
        ExportRequest request,
        CancellationToken ct = default);

    // Get available profiles
    public IReadOnlyList<ExportProfile> GetProfiles();

    // Create custom profile
    public ExportProfile CreateProfile(
        string name,
        ExportFormat format,
        Action<ExportOptions> configure);
}
```

### Export Request

```csharp
var request = new ExportRequest
{
    Profile = "PythonPandas",
    Symbols = new[] { "AAPL", "SPY" },
    StartDate = DateTime.Today.AddDays(-30),
    EndDate = DateTime.Today,
    EventTypes = new[] { MarketEventType.Trade },
    OutputPath = "./exports/aapl_spy_trades.parquet",
    IncludeQualityReport = true
};

var result = await exportService.ExportAsync(request, ct);
Console.WriteLine($"Exported {result.RecordCount} records to {result.OutputPath}");
```

### Quality Reports

```csharp
public sealed class AnalysisQualityReport
{
    public int TotalRecords { get; }
    public int ValidRecords { get; }
    public int InvalidRecords { get; }
    public IReadOnlyList<DataGap> Gaps { get; }
    public IReadOnlyList<Outlier> Outliers { get; }
    public double CompletenessScore { get; }
    public double AccuracyScore { get; }
    public string OverallGrade { get; }
    public IReadOnlyList<string> Recommendations { get; }
}
```

---

## Event Pipeline

### Bounded Channel

Use `EventPipelinePolicy` for consistent channel configuration:

```csharp
using Meridian.Application.Pipeline;

public sealed class EventPipeline
{
    private readonly Channel<MarketEvent> _channel;
    private readonly int _capacity;

    // Preferred: Use centralized policy
    public EventPipeline(EventPipelinePolicy? policy = null)
    {
        policy ??= EventPipelinePolicy.Default;
        _capacity = policy.Capacity;
        _channel = policy.CreateChannel<MarketEvent>();
    }

    public bool TryPublish(MarketEvent evt)
    {
        if (_channel.Writer.TryWrite(evt))
        {
            Metrics.Published++;
            return true;
        }

        Metrics.Dropped++;
        return false;
    }

    public IAsyncEnumerable<MarketEvent> ConsumeAsync(
        CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
```

---

## Best Practices

### 1. Always Use CancellationToken

```csharp
public async Task WriteAsync(
    MarketEvent evt,
    CancellationToken ct = default)
{
    ct.ThrowIfCancellationRequested();
    await _writer.WriteLineAsync(JsonSerializer.Serialize(evt));
}
```

### 2. Flush on Shutdown

```csharp
public async ValueTask DisposeAsync()
{
    // Flush pending writes
    await _writer.FlushAsync();

    // Commit WAL
    if (_wal != null)
    {
        await _wal.CommitAsync(_lastSequence);
        await _wal.DisposeAsync();
    }

    await _writer.DisposeAsync();
}
```

### 3. Use Atomic Writes

```csharp
// Write to temp file, then rename (atomic on most filesystems)
var tempPath = path + ".tmp";
await File.WriteAllTextAsync(tempPath, content, ct);
File.Move(tempPath, path, overwrite: true);
```

### 4. Handle Compression Transparently

```csharp
private Stream OpenFile(string path)
{
    var stream = File.OpenRead(path);

    if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
    {
        return new GZipStream(stream, CompressionMode.Decompress);
    }

    return stream;
}
```

### 5. Validate on Read

```csharp
private async Task<MarketEvent?> ParseLineAsync(string line)
{
    try
    {
        var evt = JsonSerializer.Deserialize<MarketEvent>(line);
        if (evt?.Timestamp == default)
        {
            _logger.LogWarning("Event missing timestamp: {Line}", line);
            return null;
        }
        return evt;
    }
    catch (JsonException ex)
    {
        _logger.LogWarning(ex, "Failed to parse event: {Line}", line);
        return null;
    }
}
```

---

## Monitoring

### Storage Metrics

| Metric | Description |
|--------|-------------|
| `mdc_storage_bytes_written` | Total bytes written |
| `mdc_storage_files_count` | Number of active files |
| `mdc_storage_oldest_file_age` | Age of oldest file |
| `mdc_wal_pending_events` | Uncommitted WAL events |
| `mdc_compression_ratio` | Current compression ratio |

### Health Checks

```csharp
public async Task<HealthCheckResult> CheckStorageHealthAsync()
{
    var issues = new List<string>();

    // Check disk space
    var freeSpace = GetFreeSpace(_options.DataRoot);
    if (freeSpace < 1_000_000_000)  // 1GB
    {
        issues.Add($"Low disk space: {freeSpace / 1_000_000}MB");
    }

    // Check WAL size
    if (_wal != null)
    {
        var walSize = await _wal.GetSizeAsync();
        if (walSize > 100_000_000)  // 100MB
        {
            issues.Add($"Large WAL: {walSize / 1_000_000}MB");
        }
    }

    return issues.Any()
        ? HealthCheckResult.Degraded(string.Join("; ", issues))
        : HealthCheckResult.Healthy();
}
```

---

## Related Documentation

- [docs/architecture/storage-design.md](../../architecture/storage-design.md) - Storage design document
- [docs/HELP.md#configuration](../../HELP.md#configuration) - Configuration guide
- [docs/integrations/lean-integration.md](../../integrations/lean-integration.md) - Lean export integration

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/docs/ai/README.md)
- **Root context:** [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) § Storage Architecture
- **Architecture docs:** [`docs/architecture/storage-design.md`](../../architecture/storage-design.md)

---

*Last Updated: 2026-03-16*
