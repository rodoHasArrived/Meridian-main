<<<<<<< HEAD
# Provider Validation Matrix

**Last Updated:** 2026-04-09  
**Scope:** Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof
=======
# Provider Validation Matrix (Polygon, IB, StockSharp, NYSE)

**Last Updated:** 2026-04-01  
**Scope:** Replay scenarios, reconnect behavior, cancellation handling, auth failure behavior, and rate-limit handling.
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

This matrix is Meridian's Wave 1 evidence gate. Every row must point to either executable repo evidence or a committed artifact folder under `artifacts/provider-validation/`. No row is upgraded by prose alone.

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor/runtime/entitlement condition remains manual

## Wave 1 Matrix

<<<<<<< HEAD
| Scope | Offline / CI evidence | Manual / runtime evidence | Status | Bounded by |
|---|---|---|---|---|
| Polygon replay and parser coverage | `PolygonRecordedSessionReplayTests`, `PolygonMessageParsingTests`, `PolygonSubscriptionTests`, `PolygonMarketDataClientTests`, committed fixtures under `tests/Meridian.Tests/Infrastructure/Providers/Fixtures/Polygon/` | Not required for the replay baseline; live reconnect and websocket throttling remain separate runtime follow-on work | ⚠️ | No sanitized live reconnect or websocket throttling transcript is committed yet |
| Robinhood supported surface | `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests`, `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam` | `artifacts/provider-validation/robinhood/2026-04-09/` with `auth-session`, `quote-polling`, `order-submit-cancel`, and `throttling-reconnect` scenario folders | ⚠️ | Unofficial API plus manual broker-session/runtime requirements |
| Interactive Brokers mode separation and version bounds | `IBRuntimeGuidanceTests`, `IBOrderSampleTests`, `IBApiVersionValidatorTests`, `IBSimulationClientContractTests`, `scripts/dev/build-ibapi-smoke.ps1` | `artifacts/provider-validation/interactive-brokers/2026-04-09/` with `bootstrap`, `server-version`, `market-data-entitlements`, and `disconnect-reconnect` scenario folders | ⚠️ | Official `IBApi` DLL/project path, TWS/Gateway runtime, and entitlements are external to the default repo build |
| NYSE shared lifecycle and auth/rate-limit bounds | `NyseSharedLifecycleTests`, `NyseMarketDataClientTests`, `NYSECredentialAndRateLimitTests`, `NYSEMessageParsingTests`, `NyseTaqCollectorIntegrationTests` | `artifacts/provider-validation/nyse/2026-04-09/` with `auth-connectivity`, `l1-streaming-reconnect`, `rate-limit`, and `premium-depth` scenario folders | ⚠️ | Real credentials plus Premium/Professional entitlement for depth beyond the L1/shared-lifecycle gate |
| StockSharp Wave 1 validated adapter set | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests`, `StockSharpConnectorCapabilities.GetWave1ValidatedConnectors()` | `artifacts/provider-validation/stocksharp/2026-04-09/` with per-adapter `bootstrap`, `streaming`, and `historical` scenario folders for `Rithmic`, `IQFeed`, `CQG`, and `InteractiveBrokers` | ⚠️ | Package surfaces, locally running vendor software, and adapter-specific credentials remain manual runtime conditions |
| Checkpoint reliability | `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
<<<<<<< Updated upstream
| Parquet L2 flush behavior | `ParquetStorageSinkTests`, `ParquetConversionServiceTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
=======
| Provider | Replay Scenarios | Reconnect Behavior | Cancellation | Auth Failure | Rate-Limit Handling | Evidence |
|---|---|---|---|---|---|---|
| Polygon | ✅ | ⚠️ | ✅ | ✅ | ⚠️ | `PolygonRecordedSessionReplayTests`, `PolygonMarketDataClientTests`, fixtures under `Fixtures/Polygon` |
| Interactive Brokers (IB) | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `IBRuntimeGuidanceTests`, `IBSimulationClientContractTests`, `build-ibapi-smoke.ps1` |
| StockSharp | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ | `StockSharpSubscriptionTests`, `StockSharpMessageConversionTests`, `StockSharpConnectorFactoryTests` |
| NYSE | ⚠️ | ✅ | ⚠️ | ⚠️ | ❌ | `NyseMarketDataClientTests`, `NYSEMessageParsingTests`, `NyseTaqCollectorIntegrationTests` |
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe
=======
| Parquet L2 flush behavior | `ParquetStorageSinkTests`, `ParquetConversionServiceTests` (including failed/cancelled L2 flush retry retention) | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
>>>>>>> Stashed changes

## Primary Validation Command

<<<<<<< HEAD
Run the committed Wave 1 command matrix with:

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```

The script writes:
=======
### Polygon
>>>>>>> b39663640d8410b70232c5008f8860a1e82d5cbe

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`

## Notes

- Robinhood remains polling-oriented and unofficial. Do not describe it as websocket-validated.
- Interactive Brokers must keep simulation/guidance, compile-only smoke, and official vendor-runtime modes separate.
- StockSharp Wave 1 validation is intentionally narrower than the full recognized connector catalog.
- NYSE premium depth is optional-but-bounded and does not block closure of the L1/shared-lifecycle gate.
