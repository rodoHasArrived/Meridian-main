# Performance Tuning Guide

This guide covers the key configuration parameters that affect Meridian's throughput, latency, memory usage, and storage efficiency.

---

## Event Pipeline

The `EventPipeline` uses a `BoundedChannel` to buffer events between producers (market data providers) and consumers (storage sinks). Tuning these values affects ingest throughput and memory usage.

| Parameter | Default | Location |
|-----------|---------|----------|
| `capacity` | `100,000` | `EventPipeline` constructor |
| `batchSize` | `100` | Events batched before writing |
| `flushInterval` | `5 seconds` | Periodic flush interval |
| `FinalFlushTimeout` | `30 seconds` | Max wait during shutdown |
| High water mark | `80%` | Triggers backpressure warnings |

### Recommendations

- **High-throughput ingest** (many symbols, tick-by-tick): Increase `capacity` to `500,000` or `1,000,000`. Monitor memory usage — each event is ~200 bytes.
- **Low-latency requirements**: Reduce `batchSize` to `10-25` and `flushInterval` to `1 second`. This trades write throughput for lower end-to-end latency.
- **Memory-constrained environments**: Reduce `capacity` to `10,000-50,000`. Events arriving when the channel is full are dropped and logged via `DroppedEventAuditTrail`.

---

## Backfill Concurrency

Historical data backfill concurrency is controlled in `BackfillJobsConfig`.

| Parameter | Default | Range |
|-----------|---------|-------|
| `MaxConcurrentRequests` | `3` | 1-100 |
| `MaxConcurrentPerProvider` | `2` | per provider |
| `MaxRetries` | `3` | per request |
| `RetryDelaySeconds` | `5` | between retries |
| `BatchSizeDays` | `365` | max days per request |
| `MaxRateLimitWaitMinutes` | `5` | before pausing |

### Provider Rate Limits

| Provider | Limit | Window |
|----------|-------|--------|
| Yahoo Finance | 2,000 | per hour |
| Alpaca | 200 | per minute |
| Tiingo | 50 | per hour |
| Polygon | 5 | per minute (free) |
| Finnhub | 60 | per minute |
| Alpha Vantage | 5/min, 25/day | combined |

### Recommendations

- For **paid API tiers**, increase `MaxConcurrentRequests` to `5-10` and `MaxConcurrentPerProvider` to `3-5`.
- For **initial large backfills**, increase `BatchSizeDays` to handle multi-year ranges efficiently.
- Set `AutoPauseOnRateLimit` to `true` (default) to avoid wasting API quota on rejected requests.

---

## Compression Profiles

Six built-in profiles trade off between CPU usage and compression ratio.

| Profile | Codec | Level | Throughput | Use Case |
|---------|-------|-------|-----------|----------|
| `real-time-collection` | LZ4 | 1 | ~500 Mbps | Live streaming data |
| `warm-archive` | GZIP | 6 | ~150 Mbps | Warm tier storage |
| `cold-archive` | ZSTD | 19 | ~20 Mbps | Long-term cold storage |
| `high-volume-symbols` | ZSTD | 18 | ~80 Mbps | SPY, QQQ, AAPL, etc. |
| `portable-export` | GZIP | 9 | ~100 Mbps | Data package exports |
| `no-compression` | None | 0 | 1000+ Mbps | Raw uncompressed |

### Recommendations

- Use `real-time-collection` (LZ4) for live data to minimize CPU overhead on the ingest path.
- Use `cold-archive` (ZSTD-19) only for data that is rarely accessed — decompression is slow.
- For **SSD storage**, `no-compression` may be preferred if disk space is not a constraint, as it eliminates CPU overhead entirely.

---

## Storage Tiering

Data automatically migrates through tiers based on age.

| Tier | Default Retention | Format | Compression |
|------|-------------------|--------|-------------|
| Hot | 7 days | JSONL | None |
| Warm | 30 days | JSONL | Gzip |
| Cold | 180 days | Parquet | Zstd |
| Archive | Indefinite | Parquet | Zstd |

### Tuning

- **`HotTierDays`**: Increase if you frequently query recent data (reduces migration overhead).
- **`WarmTierDays`**: Increase for active research periods where you need fast access to weeks of data.
- **`ParallelMigrations`**: Default `4`. Increase on systems with fast storage (NVMe); decrease on spinning disks.
- **`MigrationSchedule`**: Set a cron expression to run migrations during off-hours (e.g., `0 2 * * *` for 2 AM daily).

---

## Write-Ahead Log (WAL)

The WAL provides crash-recovery durability for events in the pipeline.

| Parameter | Default | Notes |
|-----------|---------|-------|
| `MaxWalFileSizeBytes` | 100 MB | Triggers rotation |
| `MaxWalFileAge` | 1 hour | Max age before rotation |
| `SyncMode` | `BatchedSync` | Balance of durability and speed |
| `SyncBatchSize` | 1,000 | Records per sync batch |
| `MaxFlushDelay` | 1 second | Max delay between flushes |
| `ArchiveAfterTruncate` | `true` | Compress old WAL files |

### Sync Modes

| Mode | Description | Use Case |
|------|-------------|----------|
| `NoSync` | OS-buffered writes | Highest throughput, risk of data loss on crash |
| `BatchedSync` | Sync after N records | Default — good balance |
| `EveryWrite` | fsync per write | Maximum durability, ~10x slower |

### Recommendations

- **Production**: Use `BatchedSync` with `SyncBatchSize` of `500-1,000`.
- **Development**: `NoSync` is fine — faster startup and less disk I/O.
- If running on battery-backed RAID, `NoSync` is safe even in production since the controller handles write ordering.

---

## Storage Buffer Sizes

| Component | Default Buffer | Notes |
|-----------|---------------|-------|
| Parquet sink | 10,000 events | Standard preset |
| Parquet sink (high-volume) | 50,000 events | For high-throughput symbols |
| Parquet sink (low-latency) | 1,000 events | Faster flushes, more I/O |
| EventBuffer | 1,000 events | Initial capacity |
| AtomicFileWriter | 64 KB | FileStream buffer |
| WAL FileStream | 64 KB | Write buffer |
| WAL StreamWriter | 32 KB | Text encoding buffer |
| WebSocket receive | 64 KB | Per-connection buffer |

### Recommendations

- For **high-volume symbols** (SPY, QQQ), use the high-volume Parquet preset (`50,000` buffer).
- For **many symbols with low volume**, the standard preset (`10,000`) is sufficient.
- WebSocket receive buffer of 64 KB handles most Polygon/Alpaca messages in a single read. Increase to 128 KB only if you see frequent multi-fragment messages.

---

## Depth Levels

Market depth (L2) data volume scales with the number of depth levels requested.

| Provider | Max Levels | Default |
|----------|-----------|---------|
| Interactive Brokers | 10 | 10 |
| Polygon | 5 | 5 |
| StockSharp | varies | provider-dependent |

### Recommendations

- Each additional depth level roughly doubles the data rate for that symbol.
- For **spread monitoring only**, 1-3 levels is sufficient.
- For **order book analysis**, use the maximum available levels.
- Use `--depth-levels N` on the command line, or `DepthLevels` in `appsettings.json`.

---

## Scheduled Backfill

| Parameter | Default | Notes |
|-----------|---------|-------|
| `ScheduleCheckIntervalSeconds` | 60 | Polling interval |
| `MaxExecutionDurationHours` | 6 | Hard timeout per execution |
| `MaxConcurrentExecutions` | 1 | Parallel scheduled runs |
| `PauseDuringMarketHours` | `false` | Avoid API contention |

### Recommendations

- Set `PauseDuringMarketHours` to `true` if running backfill and live streaming on the same API credentials to avoid rate limit conflicts.
- Increase `MaxConcurrentExecutions` only if using multiple API keys or providers with independent rate limits.

---

## Monitoring Thresholds

Use `/api/backpressure` and `/api/quality/metrics` to monitor pipeline health. Key indicators:

- **Drop rate > 0.1%**: Pipeline capacity is insufficient — increase `capacity` or reduce subscription count.
- **Latency p99 > 500ms**: Storage write path is bottlenecked — check disk I/O, reduce compression level, or increase batch size.
- **WAL uncommitted > 50 MB**: WAL is not flushing fast enough — check disk I/O or reduce `MaxFlushDelay`.

---

*Last Updated: 2026-02-10*
