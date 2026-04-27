# Provider Confidence Baseline

**Last Updated:** 2026-04-27
**Scope:** Active Wave 1 provider confidence for Alpaca, Robinhood, and Yahoo

Wave 1 is an evidence gate, not a coverage-inventory exercise. This baseline separates repo-closed evidence from intentionally bounded runtime evidence and keeps deferred providers out of the active closure target.

Use this with:

- `docs/status/provider-validation-matrix.md`
- generated `artifacts/provider-validation/` run outputs
- `scripts/dev/run-wave1-provider-validation.ps1`

## Baseline Rules

- Offline and CI evidence is mandatory for every active Wave 1 row.
- Manual or runtime evidence is only required when the claim cannot be closed from checked-in tests; generated runtime packets are review attachments and are no longer retained in git.
- Deferred providers must stay labeled as deferred, future-wave, or reference inventory and must not drift back into the active Wave 1 gate by prose alone.
- Do not broaden live-readiness language from this document.

## Alpaca

**Offline / CI evidence**

- `AlpacaBrokerageGatewayTests`
- `AlpacaCorporateActionProviderTests`
- `AlpacaCredentialAndReconnectTests`
- `AlpacaMessageParsingTests`
- `AlpacaQuotePipelineGoldenTests`
- `AlpacaQuoteRoutingTests`
- `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam`

**Manual / runtime evidence**

- Not required for the active Wave 1 claim.

**Wave 1 posture**

- Alpaca is an active core provider row.
- The current Wave 1 claim is repo-closed through checked-in provider and stable execution seam tests.

## Robinhood

**Offline / CI evidence**

- `RobinhoodBrokerageGatewayTests`
- `RobinhoodMarketDataClientTests`
- `RobinhoodHistoricalDataProviderTests`
- `RobinhoodSymbolSearchProviderTests`
- `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`

**Manual / runtime evidence**

- Bounded broker-session scenarios remain required for review when claiming Robinhood runtime confidence: `auth-session`, `quote-polling`, `order-submit-cancel`, and `throttling-reconnect`.
- The old `artifacts/provider-validation/robinhood/2026-04-09/` summaries are not retained in the current repo; regenerate or attach the runtime packet for the DK1 review run.

**Wave 1 posture**

- Robinhood remains one supported, execution-adjacent surface in Meridian across brokerage submit, quote polling, historical daily bars, and symbol search.
- Runtime confidence remains intentionally bounded by the unofficial API and the need for a real broker session outside CI.
- Quote confidence is polling-path confidence, not websocket confidence.

## Yahoo

**Offline / CI evidence**

- `YahooFinanceHistoricalDataProviderTests`
- `YahooFinanceIntradayContractTests`

**Manual / runtime evidence**

- Not required for the active Wave 1 claim.
- Existing live Yahoo integration suites remain optional developer reference and do not define the gate.

**Wave 1 posture**

- Yahoo is an active core provider row for historical and fallback confidence.
- Yahoo is not part of Meridian's live runtime-provider claim for Wave 1.

## Deferred Provider Inventory

- `Polygon` is deferred from the active Wave 1 gate. Replay coverage remains useful, but live reconnect and websocket throttling are not current blockers.
- `Interactive Brokers` is deferred from the active Wave 1 gate and remains a future runtime-validation track.
- `NYSE` is deferred from the active Wave 1 gate and remains a future entitlement and runtime-validation track.
- `StockSharp` remains outside the active Wave 1 gate as reference and future validation inventory only.

## Cross-Cutting Wave 1 Closures

These are not provider-runtime artifacts, but they are part of the same trust gate:

- Checkpoint reliability: `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests`
- Parquet L2 flush behavior: `ParquetStorageSinkTests`, `ParquetConversionServiceTests`

## Primary Command

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```
