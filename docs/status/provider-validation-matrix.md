# Provider Validation Matrix

**Last Updated:** 2026-04-17  
**Scope:** Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof

This matrix is Meridian's active Wave 1 evidence gate. Every row must point to executable repo evidence or committed runtime artifacts. Deferred providers stay out of the active gate even when they remain in the broader provider strategy.

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor or runtime condition remains manual

## Wave 1 Matrix

| Scope | Offline / CI evidence | Manual / runtime evidence | Status | Bounded by |
|---|---|---|---|---|
| Alpaca core provider confidence | `AlpacaBrokerageGatewayTests`, `AlpacaCorporateActionProviderTests`, `AlpacaCredentialAndReconnectTests`, `AlpacaMessageParsingTests`, `AlpacaQuotePipelineGoldenTests`, `AlpacaQuoteRoutingTests`, `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Not required for the active Wave 1 claim | ✅ | n/a |
| Robinhood supported surface | `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests`, `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam` | `artifacts/provider-validation/robinhood/2026-04-09/` with `auth-session`, `quote-polling`, `order-submit-cancel`, and `throttling-reconnect` scenario folders | ⚠️ | Unofficial API plus manual broker-session and runtime requirements |
| Yahoo historical and fallback confidence | `YahooFinanceHistoricalDataProviderTests`, `YahooFinanceIntradayContractTests` | Not required for the active Wave 1 claim; existing live Yahoo integration suites are optional developer reference only | ✅ | n/a |
| Checkpoint reliability | `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
| Parquet L2 flush behavior | `ParquetStorageSinkTests`, `ParquetConversionServiceTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |

## Primary Validation Command

Run the committed Wave 1 command matrix with:

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```

The script writes:

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`

## Notes

- Robinhood remains polling-oriented and unofficial. Do not describe it as websocket-validated.
- Yahoo is active only as a historical and fallback provider row for Wave 1.
- `Polygon`, `Interactive Brokers`, `NYSE`, and `StockSharp` are deferred from the active Wave 1 gate.
