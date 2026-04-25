# DK1 Pilot Parity Runbook (Alpaca, Robinhood, Yahoo)

**Last Updated:** 2026-04-25
**Owners:** Data Operations + Provider Reliability  
**Scope:** Evidence-backed parity execution for DK1 pilot provider set (Alpaca, Robinhood, Yahoo)

---

## Purpose

Provide a single reproducible runbook that proves DK1 pilot parity against the approved provider-validation evidence set.

## Canonical evidence set (exact links)

### Governing matrix and execution command

- Provider validation matrix (authoritative provider evidence table): [`provider-validation-matrix.md`](./provider-validation-matrix.md)
- Wave 1 validation command script (must be executed for every parity run): [`scripts/dev/run-wave1-provider-validation.ps1`](../../scripts/dev/run-wave1-provider-validation.ps1)
- Generated automation outputs (must be attached for the run date):
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.md`

### Provider-specific evidence links

- **Alpaca row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) (Alpaca core provider confidence tests)
- **Robinhood row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) + runtime packet path `artifacts/provider-validation/robinhood/2026-04-09/`
- **Yahoo row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) (historical/fallback test suites)

---

## DK1 replay / sample standard

Every parity run uses the sample ledger emitted by
[`scripts/dev/run-wave1-provider-validation.ps1`](../../scripts/dev/run-wave1-provider-validation.ps1)
into `wave1-validation-summary.json` and `wave1-validation-summary.md`. The same command now invokes
[`scripts/dev/generate-dk1-pilot-parity-packet.ps1`](../../scripts/dev/generate-dk1-pilot-parity-packet.ps1)
to assemble the DK1 operator-review packet from the generated summary, trust rationale mapping,
baseline thresholds, and evidence documents.

The generated `pilotReplaySampleSet` is the review contract for DK1 pilot parity:

| Sample ID | Provider | Sample scope | Required evidence |
| --- | --- | --- | --- |
| `DK1-ALPACA-QUOTE-GOLDEN` | Alpaca | `AAPL` quote pipeline fixture at `2026-03-19T14:30:00Z` | `tests/Meridian.Tests/TestData/Golden/alpaca-quote-pipeline.json` plus `AlpacaQuotePipelineGoldenTests` |
| `DK1-ALPACA-PARSER-EDGE-CASES` | Alpaca | `AAPL`, `MSFT`, `QQQ`, `SPY` parser/routing edge cases from the 2024-06-15 fixture window | `AlpacaMessageParsingTests`, `AlpacaQuoteRoutingTests`, `AlpacaCredentialAndReconnectTests` |
| `DK1-ROBINHOOD-SUPPORTED-SURFACE` | Robinhood | `AAPL`/`MSFT` offline polling fixtures plus the 2026-04-09 bounded runtime packet | `RobinhoodMarketDataClientTests`, `RobinhoodBrokerageGatewayTests`, and `artifacts/provider-validation/robinhood/2026-04-09/manifest.json` |
| `DK1-YAHOO-HISTORICAL-FALLBACK` | Yahoo | `AAPL`/`SPY` daily, adjusted daily, and intraday historical fixtures | `YahooFinanceHistoricalDataProviderTests` and `YahooFinanceIntradayContractTests` |

Reviewers should treat a run as incomplete if the generated summary omits this sample ledger, even
when all test steps pass.

---

## Run procedure

1. **Sync evidence baseline**
   - Verify `docs/status/provider-validation-matrix.md` is current for the pilot window.
2. **Execute Wave 1 command matrix**
   - Run `./scripts/dev/run-wave1-provider-validation.ps1` from repo root.
3. **Capture output artifacts**
   - Archive the generated Wave 1 summary and DK1 pilot parity packet files from `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
4. **Assemble provider packet**
   - Confirm Alpaca, Robinhood, Yahoo evidence remains aligned with the matrix row expectations.
   - Confirm the generated `pilotReplaySampleSet` contains the four DK1 samples above.
   - Confirm `dk1-pilot-parity-packet.json` reports `ready-for-operator-review`; if it reports `blocked`, clear the listed blockers before sign-off.
   - Confirm pilot-sample rows are `ready`, not merely present. The packet generator now checks each sample's provider, automation step, sample universe/window, required evidence anchors, and acceptance check.
   - Confirm evidence-document rows are `validated`, not merely present. The packet generator now checks required DK1 sample IDs, explainability payload fields/reason codes, baseline threshold metrics, and FP/FN review markers inside the linked docs.
5. **Publish parity packet**
   - Add links to output artifacts in the weekly DK1 review note and in the dashboard row.

---

## Pass / fail criteria

### Pass

- Wave 1 script exits successfully.
- Generated JSON/Markdown summaries exist for the run date.
- Generated DK1 packet JSON/Markdown artifacts exist for the run date.
- Generated summaries include the DK1 replay/sample standard for Alpaca, Robinhood, and Yahoo.
- Generated DK1 packet status is `ready-for-operator-review`.
- Generated DK1 packet sample rows are `ready` with no missing sample-contract requirements.
- Generated DK1 packet evidence-document rows are `validated` with no missing content requirements.
- No unresolved parity drift between dashboard claim and matrix evidence rows.

### Fail

- Script failure.
- Missing run-date summary artifacts.
- Missing DK1 packet artifacts or packet status `blocked`.
- Evidence document exists but is reported as `incomplete` by the packet generator.
- Required pilot sample exists but is reported as `incomplete` by the packet generator.
- Missing or changed `pilotReplaySampleSet` entries without dashboard and matrix review.
- Matrix row evidence stale or contradictory to dashboard status.

---

## Operator handoff checklist

- [ ] Run date recorded (UTC).
- [ ] `wave1-validation-summary.json` linked.
- [ ] `wave1-validation-summary.md` linked.
- [ ] `dk1-pilot-parity-packet.json` linked.
- [ ] `dk1-pilot-parity-packet.md` linked.
- [ ] `pilotReplaySampleSet` reviewed against the four DK1 samples.
- [ ] Packet blockers reviewed and cleared, or explicitly carried into the DK1 weekly review.
- [ ] Robinhood manual runtime packet linked (if applicable).
- [ ] Dashboard DK1 parity status updated with evidence references.
