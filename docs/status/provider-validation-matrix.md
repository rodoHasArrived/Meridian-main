# Provider Validation Matrix

**Last Updated:** 2026-04-26
**Scope:** Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof

This matrix is Meridian's active Wave 1 evidence gate. Every row must point to executable repo evidence or committed runtime artifacts. Deferred providers stay out of the active gate even when they remain in the broader provider strategy.

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor or runtime condition remains manual

## Wave 1 Matrix

| Scope | Offline / CI evidence | Manual / runtime evidence | Status | Bounded by |
| --- | --- | --- | --- | --- |
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
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.md`

Each generated summary now restates the active provider rows, the DK1 pilot replay/sample set,
the cross-cutting checkpoint and Parquet closures, and the deferred-provider inventory so the
automation output matches the authoritative Wave 1 posture described in this matrix.

The DK1 sample-set contract is maintained in [`dk1-pilot-parity-runbook.md`](./dk1-pilot-parity-runbook.md)
and emitted as `pilotReplaySampleSet` in the generated JSON summary. The DK1 packet generator
validates those required samples, links the trust-rationale mapping and baseline-threshold review
documents, checks those documents for the required DK1 reason codes, payload fields, threshold
metrics, FP/FN review markers, and provider-matrix anchors, then reports whether the packet is
`ready-for-operator-review` or blocked by missing or incomplete evidence. The latest regenerated
packet at `artifacts/provider-validation/_automation/codex-dk1-packet-validation-final/dk1-pilot-parity-packet.json`
is `ready-for-operator-review` with no blockers; DK1 exit still requires operator sign-off.

## Notes

- Robinhood remains polling-oriented and unofficial. Do not describe it as websocket-validated.
- Yahoo is active only as a historical and fallback provider row for Wave 1.
- `Polygon`, `Interactive Brokers`, `NYSE`, and `StockSharp` are deferred from the active Wave 1 gate.
