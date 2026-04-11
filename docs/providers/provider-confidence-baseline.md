# Provider Confidence Baseline

**Last Updated:** 2026-04-09  
**Scope:** Wave 1 provider confidence and evidence discipline for Polygon, Robinhood, NYSE, Interactive Brokers, and StockSharp

Wave 1 is a trust-and-evidence program. This document separates executable repo evidence from manual/runtime evidence and keeps every bounded scenario tied to a concrete artifact folder or test suite.

Use this with:

- `docs/status/provider-validation-matrix.md`
- `artifacts/provider-validation/`
- `scripts/dev/run-wave1-provider-validation.ps1`

## Baseline Rules

- Offline/CI evidence is mandatory for every Wave 1 row.
- Manual/runtime evidence must use the committed artifact layout under `artifacts/provider-validation/<provider>/<yyyy-mm-dd>/`.
- Remaining gaps are allowed only as explicit `bounded` scenarios tied to a concrete vendor, entitlement, package, or session requirement.
- Do not broaden live-readiness language from this document.

## Polygon

**Offline / CI evidence**

- `PolygonRecordedSessionReplayTests`
- `PolygonMessageParsingTests`
- `PolygonSubscriptionTests`
- `PolygonMarketDataClientTests`
- committed replay fixtures under `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/`

**Manual / runtime evidence**

- None required for the replay baseline.
- Live reconnect and websocket throttling remain bounded follow-on work until a sanitized transcript is captured.

**Wave 1 posture**

- Replay confidence is strong and executable.
- Remaining runtime gaps are bounded to live websocket reconnect/throttling behavior.

## Robinhood

**Offline / CI evidence**

- `RobinhoodBrokerageGatewayTests`
- `RobinhoodMarketDataClientTests`
- `RobinhoodHistoricalDataProviderTests`
- `RobinhoodSymbolSearchProviderTests`
- `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam`

**Manual / runtime evidence**

- `artifacts/provider-validation/robinhood/2026-04-09/auth-session/summary.md`
- `artifacts/provider-validation/robinhood/2026-04-09/quote-polling/summary.md`
- `artifacts/provider-validation/robinhood/2026-04-09/order-submit-cancel/summary.md`
- `artifacts/provider-validation/robinhood/2026-04-09/throttling-reconnect/summary.md`

**Wave 1 posture**

- One supported Robinhood surface is in code across brokerage submit, quote polling, historical daily bars, and symbol search.
- Runtime confidence remains bounded by the unofficial broker API and the need for a real session outside CI.
- Quote confidence is polling-path confidence, not websocket confidence.

## NYSE

**Offline / CI evidence**

- `NyseSharedLifecycleTests`
- `NyseMarketDataClientTests`
- `NYSECredentialAndRateLimitTests`
- `NYSEMessageParsingTests`
- `NyseTaqCollectorIntegrationTests`

**Manual / runtime evidence**

- `artifacts/provider-validation/nyse/2026-04-09/auth-connectivity/summary.md`
- `artifacts/provider-validation/nyse/2026-04-09/l1-streaming-reconnect/summary.md`
- `artifacts/provider-validation/nyse/2026-04-09/rate-limit/summary.md`
- `artifacts/provider-validation/nyse/2026-04-09/premium-depth/summary.md`

**Wave 1 posture**

- The L1/shared-lifecycle gate is driven by repo evidence.
- Auth/connectivity and rate-limit behavior are explicitly bounded to real credentialed runtime.
- `premium-depth` is optional-but-bounded by entitlement and does not block Wave 1 closure.

## Interactive Brokers

**Offline / CI evidence**

- `IBRuntimeGuidanceTests`
- `IBOrderSampleTests`
- `IBApiVersionValidatorTests`
- `IBSimulationClientContractTests`
- `scripts/dev/build-ibapi-smoke.ps1`

**Manual / runtime evidence**

- `artifacts/provider-validation/interactive-brokers/2026-04-09/bootstrap/summary.md`
- `artifacts/provider-validation/interactive-brokers/2026-04-09/server-version/summary.md`
- `artifacts/provider-validation/interactive-brokers/2026-04-09/market-data-entitlements/summary.md`
- `artifacts/provider-validation/interactive-brokers/2026-04-09/disconnect-reconnect/summary.md`

**Wave 1 posture**

- Meridian keeps three IB modes separate:
  - non-`IBAPI` simulation/runtime-guidance
  - `EnableIbApiSmoke=true` compile-only smoke
  - official vendor runtime via the real `IBApi` surface
- Repo evidence now proves the version bounds and smoke-build path.
- Runtime confidence remains bounded by vendor DLL availability, TWS/Gateway setup, and entitlements.

## StockSharp

**Offline / CI evidence**

- `StockSharpSubscriptionTests`
- `StockSharpMessageConversionTests`
- `StockSharpConnectorFactoryTests`
- `StockSharpConnectorCapabilities.GetWave1ValidatedConnectors()`

**Wave 1 validated adapters**

- `Rithmic`
- `IQFeed`
- `CQG`
- `InteractiveBrokers`

**Optional/example adapters outside the Wave 1 gate**

- `Binance`
- `Coinbase`
- `Kraken`

**Manual / runtime evidence**

- `artifacts/provider-validation/stocksharp/2026-04-09/rithmic-bootstrap/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/rithmic-streaming/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/rithmic-historical/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/iqfeed-bootstrap/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/iqfeed-streaming/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/iqfeed-historical/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/cqg-bootstrap/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/cqg-streaming/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/cqg-historical/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/interactive-brokers-bootstrap/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/interactive-brokers-streaming/summary.md`
- `artifacts/provider-validation/stocksharp/2026-04-09/interactive-brokers-historical/summary.md`

**Wave 1 posture**

- Meridian still recognizes more named connectors than it validates in Wave 1.
- The Wave 1 gate is intentionally limited to the four adapters above.
- Every runtime scenario remains explicitly bounded by package surfaces, vendor software, and credentials until a sanitized local capture exists.

## Cross-Cutting Wave 1 Closures

These are not provider-runtime artifacts, but they are part of the same trust gate:

- Checkpoint reliability: `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests`
- Parquet L2 flush behavior: `ParquetStorageSinkTests`, `ParquetConversionServiceTests` (including retry-safe L2 flush retention after failure or cancellation)

## Primary Command

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```
