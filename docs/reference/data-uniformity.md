# Data Uniformity and Usability Plan

> **Related:** [Deterministic Canonicalization Design](../architecture/deterministic-canonicalization.md) — detailed design for cross-provider symbol resolution, condition code mapping, and venue normalization.

This note expands on the data-quality goals for the collector so downstream users receive a uniform, analysis-ready tape regardless of provider quirks.

## Implementation status summary

| Feature | Status | Key class |
|---------|--------|-----------|
| Canonical envelope fields | ✅ Done | `MarketEvent` (`CanonicalSymbol`, `CanonicalVenue`, `CanonicalizationVersion`) |
| Symbol mapping in ingestion | ✅ Done | `CanonicalSymbolRegistry` + `CanonicalizingPublisher` |
| Condition code normalization | ✅ Done | `ConditionCodeMapper` + `CanonicalTradeCondition` enum |
| Venue normalization (ISO MIC) | ✅ Done | `VenueMicMapper` with `config/venue-mapping.json` |
| Clock skew estimation | ✅ Done | `ClockSkewEstimator` (EWMA per provider) |
| Schema validation | ✅ Done | `EventSchemaValidator` (contract validation at ingestion) |
| Event canonicalization pipeline | ✅ Done | `EventCanonicalizer` (symbol + venue + condition enrichment) |
| Event filtering | ✅ Done | `MarketEventFilter` (Symbol / Type / Tier predicates) |
| Manifest files | ✅ Done | `PackageManifest` with per-file metadata |
| Downstream-readiness score | ✅ Done | `DataQualityScoringService` (multi-dimension, A–F grades) |
| Quarantine channel | ⚠️ Partial | Naming convention recognized; auto-routing not yet wired |
| Replay filtering API | ⚠️ Partial | `JsonlReplayer` streams raw events; consumer applies `MarketEventFilter` |

## Current implementation snapshot (2026-03-14)
* **Envelope fields:** `MarketEvent` includes `CanonicalSymbol`, `CanonicalVenue`, `CanonicalizationVersion`, `ExchangeTimestamp`, `ReceivedAtUtc`, and `ReceivedAtMonotonic` alongside the original `Symbol`, `Source`, `Timestamp`, `Type`, `Payload`, `Sequence`, `SchemaVersion`, and `Tier`. Computed properties `EffectiveSymbol` (returns `CanonicalSymbol ?? Symbol`) and `EstimatedLatencyMs` (wall-clock difference when both exchange and receive timestamps are present) are also available.
* **Event types:** Factory methods cover all event types currently in the domain:
  `Trade`, `L2Snapshot`, `BboQuote`, `OrderFlow`, `Integrity`,
  `HistoricalBar`, `AggregateBar`,
  `OptionQuote`, `OptionTrade`, `OptionGreeks`, `OptionChain`, `OpenInterest`,
  `OrderAdd`, `OrderModify`, `OrderCancel`, `OrderExecute`, `OrderReplace`, and `Heartbeat`.
* **Schema evolution:** Payload records are versioned via `schemaVersion` at the event level; per-payload version fields remain a future enhancement.
* **Canonicalization pipeline:** `EventCanonicalizer` enriches events with canonical symbol, ISO MIC venue, and (via payload extraction) condition codes. `CanonicalizingPublisher` wraps any `IMarketEventPublisher` to apply this transparently, with lock-free metrics (`CanonicalizationCount`, `UnresolvedCount`, `DualWriteCount`, `AverageDurationUs`).
* **Symbol mapping:** `CanonicalSymbolRegistry` resolves symbols by alias, ISIN, FIGI, SEDOL, CUSIP, or provider ticker and is wired into the `CanonicalizingPublisher` decorator so ingestion emits both raw and canonical symbols.
* **Retention and manifests:** Retention policies are active. `PackageManifest` writes per-file metadata (path, event count, checksum, timestamp range, quality score) alongside data exports. `DataQualityScoringService` computes composite readiness scores exposed in the status dashboard.

## Canonical JSONL schema
* **Single envelope:** Persist `MarketEvent` with consistent envelope fields for every row so downstream tools do not need per-provider readers.
* **Typed payloads:** Keep `Trade`, `BboQuote`, `LOBSnapshot`, and `OrderFlowStatistics` payloads stable with explicit version fields to support gradual schema evolution.
* **Nullable fields:** Represent missing provider values explicitly (`null` for sequence/venue/stream) instead of omitting fields; keeps JSON column order consistent for parquet conversion.

## Metadata and identifiers
* **Provider provenance** ✅ — `MarketEvent.Source` carries the provider name on every row; `CanonicalizingPublisher` preserves both raw and canonical values.
* **Symbol mapping registry** ✅ — `CanonicalSymbolRegistry` resolves provider symbols → canonical identifiers (ISIN/FIGI/alias). The [Deterministic Canonicalization](../architecture/deterministic-canonicalization.md) design describes how this populates `CanonicalSymbol` on the `MarketEvent` envelope while preserving the raw `Symbol`. Use `MarketEvent.EffectiveSymbol` in storage paths, dedup keys, and metrics labels to get `CanonicalSymbol` when available and fall back to `Symbol` otherwise.
* **Clock domains** ✅ — `ExchangeTimestamp`, `ReceivedAtUtc`, and `ReceivedAtMonotonic` fields on `MarketEvent` are populated. `ClockSkewEstimator` tracks per-provider drift using EWMA, exposing `ClockSkewSnapshot` records (EWMA skew, sample count, min/max). `MarketEvent.EstimatedLatencyMs` provides a best-effort wall-clock latency figure when both timestamps are present. A `ClockQuality` enum to qualify timestamp trustworthiness remains a future enhancement.
* **Condition code normalization** ✅ — `ConditionCodeMapper` maps provider-specific codes to `CanonicalTradeCondition` using `config/condition-codes.json`. The enum covers regular trading, halt and circuit-breaker levels (Level 1/2/3), LULD pauses, regulatory and IPO halts, and trading-resumed. See [condition code mapping](../architecture/deterministic-canonicalization.md#c-condition-code-mapping).
* **Venue normalization** ✅ — `VenueMicMapper` normalizes venue identifiers to ISO 10383 MIC codes using `config/venue-mapping.json`. See [venue normalization](../architecture/deterministic-canonicalization.md#d-venue-normalization).

## Precision, units, and currencies
* **Decimals for prices:** Store prices as `decimal` in code and stringified decimals in JSON to avoid floating-point drift during parquet/duckdb conversion.
* **Unit documentation:** Standardize on quote-currency prices and size in whole units (not lots); document exceptions per venue in metadata and integrity tags.
* **Currency context:** Currency is tracked at the symbol level via `CanonicalSymbolRegistry` (each `CanonicalSymbolDefinition` carries a `Currency` field). Events themselves do not carry a `quoteCurrency` field; downstream consumers should resolve currency from the symbol registry using `EffectiveSymbol`.

## Validation and integrity tagging
* **Schema validators** ✅ — `EventSchemaValidator` enforces contract validation (timestamp, symbol, type, schema version, payload presence) at ingestion before events reach the pipeline.
* **Integrity codes** ✅ — Machine-readable codes (`SEQ_GAP`, `SEQ_OOO`, `DEPTH_STALE`, `UNKNOWN_SYMBOL`, `CLOCK_DRIFT`) are emitted as `IntegrityEvent` payloads and tracked in dashboards.
* **Quarantine channel** ⚠️ Partial — The storage layer recognizes `_quarantine/` directory naming convention, but automatic routing of critically-failed events to a quarantine sink is not yet wired.

## File organization and retention
* **Folder conventions:** Eight naming conventions are available via `FileNamingConvention`:
  - `Flat` — `{root}/{symbol}_{type}_{date}.jsonl` (all files in root; good for small datasets)
  - `BySymbol` (default) — `{root}/{symbol}/{type}/{date}.jsonl` (best for single-symbol analysis)
  - `ByDate` — `{root}/{date}/{symbol}/{type}.jsonl` (best for daily batch processing)
  - `ByType` — `{root}/{type}/{symbol}/{date}.jsonl` (best for event-type analysis)
  - `BySource` — `{root}/{source}/{symbol}/{type}/{date}.jsonl` (multi-provider comparison)
  - `ByAssetClass` — `{root}/{asset_class}/{symbol}/{type}/{date}.jsonl` (multi-asset management)
  - `Hierarchical` — `{root}/{source}/{asset_class}/{symbol}/{type}/{date}.jsonl` (enterprise multi-source)
  - `Canonical` — `{root}/{year}/{month}/{day}/{source}/{symbol}/{type}.jsonl` (time-series archival)

  Set `IncludeProvider=true` to append the provider name to paths where the convention does not already include it. Record the active convention in metadata and the dashboard.
* **Date partitioning:** `DatePartition` supports `Daily` (default, `yyyy-MM-dd`), `Hourly` (`yyyy-MM-dd_HH`), `Monthly` (`yyyy-MM`), and `None` (single file per symbol/type).
* **Rotation and retention:** Rotate files hourly to bound file sizes; pair with retention policies per provider/symbol so storage stays predictable.
* **Manifest files:** Write a small manifest (`manifest.json`) alongside each partition listing file paths, counts, min/max timestamps, and integrity stats to speed up downstream discovery.

## Observability and quality metrics
* **Uniform counters** ✅ — `PrometheusMetrics` exports row counts, dropped rows, integrity events by code, and per-symbol arrival metrics. Mirrored in `/api/status` and the web dashboard.
* **Data freshness** ✅ — `DataFreshnessSlaMonitor` tracks max timestamp per symbol/provider and surfaces time-since-last-event for stalled feed detection.
* **Downstream-readiness score** ✅ — `DataQualityScoringService` computes a multi-dimension composite score (completeness, sequence integrity, freshness, format quality, source reliability) with A–F grades. Exposed in manifests via `PackageManifest.Quality` and in the status UI.

## Replay and interoperability
* **Replay filters** ✅ — `JsonlReplayer` and `MemoryMappedJsonlReader` stream JSONL events as `IAsyncEnumerable<MarketEvent>` with automatic gzip decompression and configurable chunk/batch sizes. Consumer-side filtering is handled by `MarketEventFilter` (filter by `Symbol`, `Type`, and/or `Tier`).
* **Columnar exports** ✅ — `ParquetStorageSink` and `AnalysisExportService` produce Parquet exports with stable column order matching the canonical schema. Arrow and XLSX export formats are also supported.
* **Compatibility adapters** — Portable data packages include SQL loader scripts and import helpers. Dedicated pandas/polars/duckdb adapters are a future enhancement.

## Governance and evolution
* **Versioned schemas** ✅ — `SchemaVersionManager` tracks schema versions and coordinates migrations. `MarketEvent.SchemaVersion` tags every event.
* **Contract tests** ✅ — `ContractVerificationService` validates provider implementations against canonical contracts. Integration endpoint tests in `tests/Meridian.Tests/Integration/` verify schema compliance.
* **Config-driven rollout** ✅ — Canonicalization is enabled via `CanonicalizationConfig` with `PilotSymbols` list and `EnableDualWrite` flag. Operators can gradually roll out new normalization rules without disrupting existing pipelines.
