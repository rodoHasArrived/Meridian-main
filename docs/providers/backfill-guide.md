# Historical Data Backfill Guide

**Last Updated:** 2026-04-24
**Version:** 1.6.2

This document provides a comprehensive guide for backfilling historical market data using the Meridian.

---

## Overview

Historical backfill is the process of retrieving past market data to fill gaps in your data archive. The Meridian supports 8 core historical data providers through a composite fallback strategy, with optional IB support depending on build/runtime setup.

### Key Features

- **8 Core Data Providers**: Alpaca, Yahoo Finance, Stooq, Tiingo, Finnhub, Alpha Vantage, Polygon, Nasdaq Data Link
- **Optional Provider**: Interactive Brokers historical (when IB integration is enabled)
- **Composite Provider**: Automatic failover across providers
- **Rate Limiting**: Built-in throttling to respect API limits
- **Progress Reporting**: API endpoint for active backfill progress
- **Preview Mode**: API endpoint to validate requests before execution

---

## Quick Start

### 1. Configure Providers

Set API keys for your preferred providers:

```bash
# Required for Tiingo
export TIINGO_API_TOKEN="your-token"

# Required for Alpaca
export ALPACA_KEY_ID="your-key"
export ALPACA_SECRET_KEY="your-secret"

# Required for Finnhub
export FINNHUB_API_KEY="your-key"

# Optional providers (no auth required)
# Yahoo Finance - no key needed
# Stooq - no key needed
```

### 2. Run Backfill

**Via CLI:**
```bash
dotnet run -- --backfill \
  --backfill-provider stooq \
  --backfill-symbols AAPL,MSFT,GOOGL \
  --backfill-from 2025-01-01 \
  --backfill-to 2026-01-01
```

**Via Dashboard API/UI:**
1. Open the dashboard **Backfill** section
2. Select provider and symbols
3. Optionally run preview first
4. Start backfill and monitor progress

### 3. Monitor Progress

Check backfill status via:
- **HTTP API**: `GET /api/backfill/status`
- **HTTP API (active run)**: `GET /api/backfill/progress`
- **Dashboard UI**: Backfill status/progress panel
- **Logs**: Serilog structured logging

### Resume And Preview Semantics

- Resume checkpoints are scoped by requested granularity, so daily and intraday runs for the same symbol do not suppress each other.
- Preview existing-data checks now compare only files that match the requested storage lane, avoiding false positives when a symbol has daily files but no intraday archive yet.
- Status and desktop provider metadata should only advertise granularities that the runtime backfill contracts actually accept. In the current workflow, Alpaca remains daily-only while Yahoo exposes the supported intraday aggregate lanes.

---

## Backfill Architecture

### Component Overview

```
┌─────────────────┐     ┌──────────────────────┐     ┌─────────────────┐
│ BackfillRequest │────▶│HistoricalBackfillSvc │────▶│ CompositeProvider│
└─────────────────┘     └──────────────────────┘     └─────────────────┘
                                   │                         │
                                   ▼                         ▼
                        ┌──────────────────┐      ┌──────────────────┐
                        │  Priority Queue  │      │ Individual       │
                        │  (Symbol Jobs)   │      │ Providers        │
                        └──────────────────┘      │ ├─ Alpaca        │
                                   │              │ ├─ Yahoo         │
                                   ▼              │ ├─ Tiingo        │
                        ┌──────────────────┐      │ └─ etc.          │
                        │ EventPipeline    │      └──────────────────┘
                        └──────────────────┘
                                   │
                                   ▼
                        ┌──────────────────┐
                        │ Storage Layer    │
                        │ (JSONL/Parquet)  │
                        └──────────────────┘
```

### Key Classes

| Class | Location | Purpose |
|-------|----------|---------|
| `HistoricalBackfillService` | `Application/Subscriptions/` | Orchestrates backfill jobs |
| `CompositeHistoricalDataProvider` | `Infrastructure/Adapters/Core/` | Multi-provider failover |
| `BackfillJobQueue` | `Application/Subscriptions/` | Priority queue management |
| `DataQualityService` | `Storage/Services/` | Quality assessment |

---

## Provider Configuration

### Provider Priority

The `CompositeHistoricalDataProvider` tries providers in priority order:

```csharp
// Default priority (configurable)
1. Alpaca
2. Yahoo
3. Polygon
4. Tiingo
5. Finnhub
6. Stooq
7. AlphaVantage
8. NasdaqDataLink

> Actual execution order is controlled by each provider's configured `Priority` value.
```

### Configuring Priority

```json
{
  "Backfill": {
    "Providers": {
      "Alpaca": { "Enabled": true, "Priority": 1 },
      "Yahoo": { "Enabled": true, "Priority": 2 },
      "Polygon": { "Enabled": false, "Priority": 3 },
      "Tiingo": { "Enabled": true, "Priority": 4 },
      "Finnhub": { "Enabled": true, "Priority": 5 },
      "Stooq": { "Enabled": true, "Priority": 6 },
      "AlphaVantage": { "Enabled": false, "Priority": 7 },
      "Nasdaq": { "Enabled": false, "Priority": 8 }
    },
    "EnableFallback": true,
    "ProviderCooldownSeconds": 60
  }
}
```

---

## Rate Limiting Strategy

### Provider Rate Limits

| Provider | Rate Limit | Suggested Delay | Daily Limit |
|----------|------------|-----------------|-------------|
| Yahoo Finance | ~2000/hr | 2 seconds | Unlimited |
| Stooq | Respectful | 1 second | Unlimited |
| Alpaca | 200/min | 300ms | Unlimited |
| Tiingo | 50/hr | 72 seconds | 1,000 |
| Finnhub | 60/min | 1 second | Unlimited |
| Alpha Vantage | 5/min | 12 seconds | 25 |
| Polygon | 5/min | 12 seconds | Unlimited |

### Built-in Rate Limiting

Each provider has a built-in `RateLimiter`:

```csharp
// Rate limiter example (TiingoHistoricalDataProvider)
_rateLimiter = new RateLimiter(
    maxRequests: 50,
    window: TimeSpan.FromHours(1),
    minDelay: TimeSpan.FromSeconds(1.5)
);
```

### Rotating Across Providers

The `CompositeProvider` automatically rotates when rate limits are hit:

```
Request 1: Tiingo → Success
Request 2: Tiingo → Rate limited → Fallback to Alpaca
Request 3: Alpaca → Success
Request 4: Tiingo → (Recovered) → Success
```

---

## Backfill Modes

### Full Backfill

Retrieve all available history for a symbol:

```bash
dotnet run -- --backfill \
  --backfill-symbols AAPL \
  --backfill-from 2000-01-01 \
  --backfill-to 2026-01-01
```

### Incremental Backfill

Use your storage state and date bounds to run a targeted catch-up:

```bash
dotnet run -- --backfill \
  --backfill-symbols AAPL \
  --backfill-from 2025-01-01
```

### Date Range Backfill

Specific date range:

```bash
dotnet run -- --backfill \
  --backfill-symbols AAPL,MSFT \
  --backfill-from 2025-06-01 \
  --backfill-to 2025-12-31
```

### Bulk Backfill

Multiple symbols with priority:

```json
{
  "BackfillJobs": [
    { "Symbol": "SPY", "Priority": 1, "From": "2020-01-01" },
    { "Symbol": "QQQ", "Priority": 1, "From": "2020-01-01" },
    { "Symbol": "AAPL", "Priority": 2, "From": "2020-01-01" },
    { "Symbol": "MSFT", "Priority": 2, "From": "2020-01-01" }
  ]
}
```

---

## Gap Detection

### Automatic Gap Detection

The `DataQualityService` identifies gaps in your data:

```csharp
var gaps = await dataQualityService.DetectGapsAsync(
    symbol: "AAPL",
    from: DateTime.Parse("2025-01-01"),
    to: DateTime.Parse("2026-01-01"),
    expectedFrequency: TimeSpan.FromDays(1)
);

// Returns list of missing date ranges
foreach (var gap in gaps)
{
    Console.WriteLine($"Missing: {gap.Start} to {gap.End}");
}
```

### Auto Gap Remediation Coordinator

Meridian includes an `AutoGapRemediationService` (`src/Meridian.Application/Backfill/`) that consumes:

- real-time `DataQualityMonitoringService` gap events,
- `DataGapAnalyzer` scan results (scheduled gap-fill analysis),
- quality alert signals when API/workflow callers forward them.

Before dispatching remediation, it enforces:

- minimum gap duration / size,
- symbol/provider cooldown windows,
- idempotency (`symbol + provider + date-range`),
- max concurrent remediations.

Remediations are executed through `BackfillCoordinator`, and lineage is persisted in `BackfillExecutionHistory` with:

- `autoRemediationTriggerReason`
- `autoRemediationAttemptCount`
- `autoRemediationLastOutcome`
- `autoRemediationIdempotencyKey`

`/api/backfill/executions` and `/api/backfill/statistics` now expose these fields and an auto-remediation summary block.

### Gap Types

| Gap Type | Description | Action |
|----------|-------------|--------|
| **Weekend** | Expected (Sat-Sun) | Skip |
| **Holiday** | Market closed | Skip |
| **Unexpected** | Data missing | Backfill |
| **Delisted** | Symbol no longer trading | Mark as complete |

### Gap Detection Report

```bash
dotnet run -- --gap-report AAPL --from 2020-01-01
```

Output:
```
Gap Detection Report: AAPL
==========================
Period: 2020-01-01 to 2026-01-08
Expected Trading Days: 1,508
Actual Data Points: 1,495
Missing Days: 13

Gaps Found:
- 2020-03-15 to 2020-03-17 (3 days) - Unexpected
- 2024-12-26 to 2024-12-27 (2 days) - Holiday
...

Recommendation: Run incremental backfill
```

---

## Data Quality

### Quality Checks

Before exporting, run quality assessment:

```csharp
var report = await analysisQualityReport.GenerateAsync(
    symbol: "AAPL",
    from: DateTime.Parse("2020-01-01"),
    to: DateTime.Parse("2026-01-01")
);

Console.WriteLine($"Quality Grade: {report.Grade}");  // A+, A, B, C, D, F
Console.WriteLine($"Completeness: {report.Completeness:P1}");
Console.WriteLine($"Outliers Found: {report.OutlierCount}");
```

### Quality Metrics

| Metric | Description | Weight |
|--------|-------------|--------|
| **Completeness** | % of expected trading days | 40% |
| **Consistency** | No price jumps > 4σ | 25% |
| **Recency** | Data up to current date | 15% |
| **Accuracy** | Cross-provider validation | 20% |

### Quality Grades

| Grade | Score | Suitability |
|-------|-------|-------------|
| **A+** | 95-100% | Production backtesting |
| **A** | 90-94% | Research |
| **B** | 80-89% | Development |
| **C** | 70-79% | Limited use |
| **D** | 60-69% | Needs attention |
| **F** | < 60% | Not suitable |

---

## Storage Integration

### Storage Flow

```
Historical Data → Event Pipeline → WAL → JSONL → Parquet Archive
```

### File Organization

```
{DataRoot}/
├── historical/
│   ├── alpaca/
│   │   └── 2026-01-08/
│   │       ├── AAPL_bars.jsonl
│   │       └── MSFT_bars.jsonl
│   ├── yahoo/
│   │   └── 2026-01-08/
│   │       └── AAPL_bars.jsonl
│   └── composite/
│       └── 2026-01-08/
│           └── AAPL_bars.jsonl
└── _archive/
    └── parquet/
        └── bars/
            ├── AAPL_2020.parquet
            └── AAPL_2021.parquet
```

### Compression Options

| Tier | Format | Compression | Use Case |
|------|--------|-------------|----------|
| Hot | JSONL | Gzip | Recent data, active access |
| Warm | Parquet | Snappy | Historical, frequent queries |
| Cold | Parquet | ZSTD-19 | Archive, rare access |

---

## Dashboard Backfill Interface

### Backfill Page Features

1. **Symbol Input**: Comma-separated or file upload
2. **Date Range**: Calendar picker with presets
3. **Provider Selection**: Enable/disable specific providers
4. **Priority Setting**: High/Medium/Low per symbol
5. **Progress Tracking**: Real-time progress bars
6. **Quality Preview**: Quick quality check before archival

### Batch Import

Upload a CSV file with symbols:

```csv
Symbol,Priority,FromDate,ToDate
AAPL,1,2020-01-01,2026-01-01
MSFT,1,2020-01-01,2026-01-01
GOOGL,2,2020-01-01,2026-01-01
```

---

## Troubleshooting

### Common Issues

**Issue: Rate limit errors**
```
Error: 429 Too Many Requests
```
**Solution**: Increase delays between requests, or add more providers to rotation

**Issue: Missing data for certain dates**
```
Warning: No data returned for AAPL 2020-03-15
```
**Solution**: Check if market was closed (holiday/weekend), try alternative provider

**Issue: Data quality too low**
```
Quality Grade: D (65%)
```
**Solution**: Run incremental backfill, cross-validate with multiple providers

**Issue: Provider authentication failed**
```
Error: 401 Unauthorized for Tiingo
```
**Solution**: Verify API token in environment variables

### Debug Mode

Enable detailed logging:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Override": {
        "Meridian.Infrastructure.Providers.Backfill": "Debug"
      }
    }
  }
}
```

### Retry Configuration

```json
{
  "Backfill": {
    "RetryPolicy": {
      "MaxRetries": 3,
      "BaseDelayMs": 1000,
      "MaxDelayMs": 30000,
      "ExponentialBackoff": true
    }
  }
}
```

---

## Best Practices

### 1. Start Small

Test with a single symbol before bulk backfill:
```bash
dotnet run -- --backfill --backfill-symbols AAPL --backfill-from 2025-01-01 --dry-run
```

### 2. Prioritize Quality

Use providers with best data quality first:
- Tiingo for dividend-adjusted data
- Alpaca for recent/accurate data
- Yahoo as fallback for coverage

### 3. Monitor Rate Limits

Watch for rate limit warnings in logs:
```
[WRN] Rate limit approaching for Tiingo (45/50 in window)
[INF] Rotating to Yahoo Finance
```

### 4. Schedule Off-Hours

Run large backfills during off-market hours:
- Less API competition
- More stable connections
- Better rate limit availability

### 5. Validate Results

Always run quality checks after backfill:
```bash
dotnet run -- --quality-report AAPL
```

### 6. Archive Incrementally

Don't archive until quality is acceptable:
1. Backfill → JSONL (hot storage)
2. Quality check
3. Fix gaps if needed
4. Archive → Parquet (cold storage)

---

## API Reference

### CLI Commands

```bash
# Full/range backfill
dotnet run -- --backfill --backfill-symbols <symbols> --backfill-from <date> --backfill-to <date>

# Provider-specific backfill
dotnet run -- --backfill --backfill-provider <provider> --backfill-symbols <symbols>

# Dry run (no writes)
dotnet run -- --backfill --backfill-symbols <symbols> --dry-run

# Contextual help
dotnet run -- --help backfill
```

### HTTP API

```
GET  /api/backfill/providers
GET  /api/backfill/status
GET  /api/backfill/progress
POST /api/backfill/run/preview
POST /api/backfill/run
```

---

## Related Documentation

- [Data Sources Reference](data-sources.md)
- [Provider Comparison](provider-comparison.md)
- [Alpaca Setup](alpaca-setup.md)
- [Interactive Brokers Setup](interactive-brokers-setup.md)
- [Storage Architecture](../architecture/storage-design.md)
- [Operator Runbook](../operations/operator-runbook.md)

---

*Last Updated: 2026-02-17*
