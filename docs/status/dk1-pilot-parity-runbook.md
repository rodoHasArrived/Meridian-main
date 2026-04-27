# DK1 Pilot Parity Runbook (Alpaca, Robinhood, Yahoo)

**Last Updated:** 2026-04-26
**Owners:** Data Operations + Provider Reliability  
**Scope:** Evidence-backed parity execution for DK1 pilot provider set (Alpaca, Robinhood, Yahoo)

---

## Purpose

Provide a single reproducible runbook that proves DK1 pilot parity against the approved provider-validation evidence set.

## Canonical evidence set (exact links)

### Governing matrix and execution command

- Provider validation matrix (authoritative provider evidence table): [`provider-validation-matrix.md`](./provider-validation-matrix.md)
- Wave 1 validation command script (must be executed for every parity run): [`scripts/dev/run-wave1-provider-validation.ps1`](../../scripts/dev/run-wave1-provider-validation.ps1)
- Operator sign-off preflight helper: [`scripts/dev/prepare-dk1-operator-signoff.ps1`](../../scripts/dev/prepare-dk1-operator-signoff.ps1)
- Generated automation outputs (must be attached for the run date; these files are generated run evidence and are no longer retained in git):
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`
  - `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.md`

### Provider-specific evidence links

- **Alpaca row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) (Alpaca core provider confidence tests)
- **Robinhood row evidence:** [`provider-validation-matrix.md#wave-1-matrix`](./provider-validation-matrix.md#wave-1-matrix) + regenerated or attached runtime packet evidence for the review date
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
| `DK1-ROBINHOOD-SUPPORTED-SURFACE` | Robinhood | `AAPL`/`MSFT` offline polling fixtures plus regenerated or attached bounded runtime evidence for the review date | `RobinhoodMarketDataClientTests`, `RobinhoodBrokerageGatewayTests`, and the review-run Robinhood runtime packet when runtime confidence is claimed |
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
   - Attach or externally archive the generated Wave 1 summary and DK1 pilot parity packet files from `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`; do not assume a prior committed artifact path is current evidence.
4. **Assemble provider packet**
   - Confirm Alpaca, Robinhood, Yahoo evidence remains aligned with the matrix row expectations.
   - Confirm the generated `pilotReplaySampleSet` contains the four DK1 samples above.
   - Confirm `dk1-pilot-parity-packet.json` reports `ready-for-operator-review`; if it reports `blocked`, clear the listed blockers before sign-off.
   - Confirm pilot-sample rows are `ready`, not merely present. The packet generator now checks each sample's provider, automation step, sample universe/window, required evidence anchors, and acceptance check.
   - Confirm evidence-document rows are `validated`, not merely present. The packet generator now checks required DK1 sample IDs, explainability payload fields/reason codes, baseline threshold metrics, and FP/FN review markers inside the linked docs.
5. **Attach operator sign-off when approved**
   - Create the operator sign-off JSON template after Data Operations, Provider Reliability, and Trading have reviewed the packet:
     `./scripts/dev/prepare-dk1-operator-signoff.ps1 -OutputPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json -PacketPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`.
   - Keep the generated `packetReview` block in the sign-off file. It binds approvals to the reviewed parity packet path, generated timestamp, status, sample counts, evidence-document counts, and explainability/calibration contract status.
   - Fill each required approval row with `signedBy`, `signedAtUtc`, an approved/signed `decision`, and `rationale`.
   - Validate the completed sign-off before regenerating the packet:
     `./scripts/dev/prepare-dk1-operator-signoff.ps1 -OutputPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json -PacketPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json -Validate`.
   - Regenerate the packet with `./scripts/dev/generate-dk1-pilot-parity-packet.ps1 -SummaryJsonPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json -OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json`.
   - Alternatively pass the same sign-off file through the full validation wrapper: `./scripts/dev/run-wave1-provider-validation.ps1 -OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json`.
   - The packet generator also checks the sign-off `packetReview` against the already generated packet before accepting it as DK1-exit evidence. Copied, stale, or unbound sign-off files write `operatorSignoff.packetBindingStatus=invalid`, `operatorSignoff.validForDk1Exit=false`, and fail regeneration.
   - Confirm `operatorSignoff.status` is `signed`, `signedOwners` contains all three required owners, and `missingOwners` is empty before claiming DK1 exit.
6. **Publish parity packet**
   - Add links to output artifacts in the weekly DK1 review note and in the dashboard row.

### Operator sign-off file shape

Use this JSON shape for `-OperatorSignoffPath`:

```json
{
  "approvals": [
    {
      "owner": "Data Operations",
      "signedBy": "data.ops.owner",
      "signedAtUtc": "2026-04-26T15:58:00Z",
      "decision": "approved",
      "rationale": "Provider packet and pilot samples reviewed."
    },
    {
      "owner": "Provider Reliability",
      "signedBy": "provider.reliability.owner",
      "signedAtUtc": "2026-04-26T16:00:00Z",
      "decision": "approved",
      "rationale": "Threshold and evidence checks accepted."
    },
    {
      "owner": "Trading",
      "signedBy": "trading.owner",
      "signedAtUtc": "2026-04-26T16:02:00Z",
      "decision": "approved",
      "rationale": "Cockpit readiness gate accepted."
    }
  ]
}
```

Each approval must include `owner`, `signedBy`, `signedAtUtc`, an approved/signed `decision`, and
`rationale`. The packet generator records partial sign-off as `operatorSignoff.status=partial` and
lists the remaining owners in `operatorSignoff.missingOwners`.

When the sign-off template is created with `-PacketPath`, it also contains a `packetReview` block.
Validation with the same `-PacketPath` fails if the sign-off file was copied from another DK1 packet
or if the packet is not `ready-for-operator-review` with ready samples, validated evidence
documents, validated trust-rationale and baseline-threshold contracts, and no blockers.
The DK1 packet generator enforces the same binding during regeneration, so `operatorSignoff.status`
is not treated as `signed` for DK1 exit unless `packetBindingStatus` is `valid`.

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
- For DK1 exit, `operatorSignoff.status` is `signed` and `operatorSignoff.missingOwners` is empty.

### Fail

- Script failure.
- Missing run-date summary artifacts.
- Missing DK1 packet artifacts or packet status `blocked`.
- Evidence document exists but is reported as `incomplete` by the packet generator.
- Required pilot sample exists but is reported as `incomplete` by the packet generator.
- Missing or changed `pilotReplaySampleSet` entries without dashboard and matrix review.
- Matrix row evidence stale or contradictory to dashboard status.
- DK1 exit is claimed while `operatorSignoff.status` is `pending` or `partial`.

---

## Operator handoff checklist

- [ ] Run date recorded (UTC).
- [ ] `wave1-validation-summary.json` linked.
- [ ] `wave1-validation-summary.md` linked.
- [ ] `dk1-pilot-parity-packet.json` linked.
- [ ] `dk1-pilot-parity-packet.md` linked.
- [ ] Operator sign-off template generated with `-PacketPath` and packet binding retained.
- [ ] `pilotReplaySampleSet` reviewed against the four DK1 samples.
- [ ] Packet blockers reviewed and cleared, or explicitly carried into the DK1 weekly review.
- [ ] Operator sign-off JSON attached when DK1 exit is requested.
- [ ] `operatorSignoff.status=signed` and `operatorSignoff.missingOwners=[]` verified before DK1 exit.
- [ ] Robinhood manual runtime packet regenerated or attached for the review date (if applicable).
- [ ] Dashboard DK1 parity status updated with evidence references.
