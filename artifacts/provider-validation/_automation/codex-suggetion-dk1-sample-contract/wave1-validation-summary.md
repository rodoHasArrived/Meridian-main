# Wave 1 Validation Summary

- Generated: 2026-04-25T20:30:39.4314340Z
- Configuration: Release
- Scope: Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof
- Overall result: passed

## Active Provider Set

| Provider | Posture | Lane | Runtime evidence | Notes |
| --- | --- | --- | --- | --- |
| Alpaca | repo-closed | core provider confidence | Not required | Active Wave 1 core provider row. Closed by checked-in provider and stable execution seam tests. |
| Robinhood | bounded | supported surface | artifacts/provider-validation/robinhood/2026-04-09/auth-session/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/quote-polling/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/order-submit-cancel/summary.md<br>artifacts/provider-validation/robinhood/2026-04-09/throttling-reconnect/summary.md | Only active provider row that remains runtime-bounded. Confidence is polling-oriented and execution-adjacent, not websocket-validated. |
| Yahoo | repo-closed | historical and fallback confidence | Not required | Active historical-only core provider row. Not part of Meridian's live runtime-provider claim for Wave 1. |

## DK1 Pilot Replay / Sample Standard

| Sample ID | Provider | Lane | Sample universe | Replay / fixture window | Evidence anchor | Acceptance check |
| --- | --- | --- | --- | --- | --- | --- |
| DK1-ALPACA-QUOTE-GOLDEN | Alpaca | repo-closed quote pipeline parity | AAPL | 2026-03-19T14:30:00Z | tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json<br>AlpacaQuotePipelineGoldenTests | Parser, canonical publisher, and JSONL sink output match the committed golden subset. |
| DK1-ALPACA-PARSER-EDGE-CASES | Alpaca | repo-closed trade and quote parser coverage | AAPL<br>MSFT<br>QQQ<br>SPY | 2024-06-15 parser fixture window | AlpacaMessageParsingTests<br>AlpacaQuoteRoutingTests<br>AlpacaCredentialAndReconnectTests | Trade and quote edge cases preserve symbol separation, timestamp handling, duplicate suppression, routing, and reconnect behavior. |
| DK1-ROBINHOOD-SUPPORTED-SURFACE | Robinhood | bounded polling and brokerage surface | AAPL<br>MSFT | 2026-04-09 bounded runtime packet plus offline polling fixtures | RobinhoodMarketDataClientTests<br>RobinhoodBrokerageGatewayTests<br>artifacts/provider-validation/robinhood/2026-04-09/manifest.json | Offline polling, symbol search, historical bars, and execution seam tests pass; runtime evidence remains explicitly bounded. |
| DK1-YAHOO-HISTORICAL-FALLBACK | Yahoo | repo-closed historical-only fallback | AAPL<br>SPY | 2024-01-01 through 2024-01-02 daily fixtures and 2024-01-02 intraday session fixtures | YahooFinanceHistoricalDataProviderTests<br>YahooFinanceIntradayContractTests | Daily, adjusted daily, and intraday aggregate fixtures deserialize into stable historical/fallback bars without implying live-provider readiness. |

## Cross-Cutting Closures

| Closure | Posture | Evidence |
| --- | --- | --- |
| Checkpoint reliability | repo-closed | BackfillStatusStoreTests<br>ParallelBackfillServiceTests<br>GapBackfillServiceTests<br>CheckpointEndpointTests |
| Parquet L2 flush behavior | repo-closed | ParquetStorageSinkTests<br>ParquetConversionServiceTests |

| Step | Kind | Status | Duration (s) | Log |
| --- | --- | --- | ---: | --- |
| Meridian.Tests build | build | passed | 39.22 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/meridian-tests-build.log` |
| Alpaca core provider confidence | test | passed | 13.12 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/alpaca-core-provider-confidence.log` |
| Robinhood supported surface | test | passed | 7.98 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/robinhood-supported-surface.log` |
| Yahoo historical-only core provider | test | passed | 4.61 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/yahoo-historical-only-core-provider.log` |
| Checkpoint reliability and gap handling | test | passed | 7.85 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/checkpoint-reliability-and-gap-handling.log` |
| Parquet sink and conversion | test | passed | 6.04 | `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/parquet-sink-and-conversion.log` |

## Deferred Provider Inventory

- Polygon, Interactive Brokers, NYSE, StockSharp
