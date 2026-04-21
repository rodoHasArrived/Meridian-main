# Wave 1 Validation Summary

- Generated: 2026-04-21T07:09:53.1866826Z
- Configuration: Release
- Scope: Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof
- Overall result: passed

## Active Provider Set

| Provider | Posture | Lane | Runtime evidence | Notes |
|---|---|---|---|---|
| Alpaca | repo-closed | core provider confidence | Not required | Active Wave 1 core provider row. Closed by checked-in provider and stable execution seam tests. |
| Robinhood | bounded | supported surface | artifacts/provider-validation/robinhood/2026-04-09/auth-session/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/quote-polling/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/order-submit-cancel/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/throttling-reconnect/summary.md | Only active provider row that remains runtime-bounded. Confidence is polling-oriented and execution-adjacent, not websocket-validated. |
| Yahoo | repo-closed | historical and fallback confidence | Not required | Active historical-only core provider row. Not part of Meridian's live runtime-provider claim for Wave 1. |

## Cross-Cutting Closures

| Closure | Posture | Evidence |
|---|---|---|
| Checkpoint reliability | repo-closed | BackfillStatusStoreTests<br>ParallelBackfillServiceTests<br>GapBackfillServiceTests<br>CheckpointEndpointTests |
| Parquet L2 flush behavior | repo-closed | ParquetStorageSinkTests<br>ParquetConversionServiceTests |

| Step | Kind | Status | Duration (s) | Log |
|---|---|---|---:|---|
| Meridian.Tests build | build | passed | 64.94 | `artifacts/provider-validation/_automation/2026-04-20/meridian-tests-build.log` |
| Alpaca core provider confidence | test | passed | 30.17 | `artifacts/provider-validation/_automation/2026-04-20/alpaca-core-provider-confidence.log` |
| Robinhood supported surface | test | passed | 20.44 | `artifacts/provider-validation/_automation/2026-04-20/robinhood-supported-surface.log` |
| Yahoo historical-only core provider | test | passed | 12.18 | `artifacts/provider-validation/_automation/2026-04-20/yahoo-historical-only-core-provider.log` |
| Checkpoint reliability and gap handling | test | passed | 23.52 | `artifacts/provider-validation/_automation/2026-04-20/checkpoint-reliability-and-gap-handling.log` |
| Parquet sink and conversion | test | passed | 14.55 | `artifacts/provider-validation/_automation/2026-04-20/parquet-sink-and-conversion.log` |

## Deferred Provider Inventory

- Polygon, Interactive Brokers, NYSE, StockSharp
