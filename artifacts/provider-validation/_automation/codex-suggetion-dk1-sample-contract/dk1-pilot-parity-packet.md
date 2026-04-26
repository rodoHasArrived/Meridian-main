# DK1 Pilot Parity Packet

- Generated: 2026-04-25T20:30:39.6627369Z
- Source summary: `artifacts/provider-validation/_automation/codex-suggetion-dk1-sample-contract/wave1-validation-summary.json`
- Source result: passed
- Packet status: ready-for-operator-review

## Pilot Sample Review

| Sample ID | Provider | Required step | Step status | Review status | Missing requirements | Evidence anchors |
| --- | --- | --- | --- | --- | --- | --- |
| DK1-ALPACA-QUOTE-GOLDEN | Alpaca | Alpaca core provider confidence | passed | ready | none | tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json<br>AlpacaQuotePipelineGoldenTests |
| DK1-ALPACA-PARSER-EDGE-CASES | Alpaca | Alpaca core provider confidence | passed | ready | none | AlpacaMessageParsingTests<br>AlpacaQuoteRoutingTests<br>AlpacaCredentialAndReconnectTests |
| DK1-ROBINHOOD-SUPPORTED-SURFACE | Robinhood | Robinhood supported surface | passed | ready | none | RobinhoodMarketDataClientTests<br>RobinhoodBrokerageGatewayTests<br>artifacts/provider-validation/robinhood/2026-04-09/manifest.json |
| DK1-YAHOO-HISTORICAL-FALLBACK | Yahoo | Yahoo historical-only core provider | passed | ready | none | YahooFinanceHistoricalDataProviderTests<br>YahooFinanceIntradayContractTests |

## Evidence Documents

| Document | Gate | Status | Missing requirements | Path |
| --- | --- | --- | --- | --- |
| DK1 pilot parity runbook | parity | validated | none | `docs/status/dk1-pilot-parity-runbook.md` |
| DK1 trust rationale mapping | explainability | validated | none | `docs/status/dk1-trust-rationale-mapping.md` |
| DK1 baseline trust thresholds | calibration | validated | none | `docs/status/dk1-baseline-trust-thresholds.md` |
| Provider validation matrix | parity | validated | none | `docs/status/provider-validation-matrix.md` |

## Explainability Contract

- Status: validated
- Required alert payload fields: `signalSource`, `reasonCode`, `recommendedAction`
- Required reason codes: `HEALTHY_BASELINE`; `PROVIDER_STREAM_DEGRADED`; `RECONNECT_INSTABILITY`; `ERROR_RATE_SPIKE`; `LATENCY_REGRESSION`; `PARITY_DRIFT_DETECTED`; `DATA_COMPLETENESS_GAP`; `CALIBRATION_STALE`

## Calibration Contract

- Status: validated
- Required metrics: `Composite trust score`; `Connection stability score`; `Error-rate score`; `Latency score`; `Reconnect score`
- FP/FN review required before DK1 calibration pass: True

## Operator Sign-off

- Required owners: Data Operations, Provider Reliability, Trading
- Status: pending

## Blockers

- none
