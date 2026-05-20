# Provider Validation Matrix

**Last Updated:** 2026-05-20
**Scope:** Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof

This matrix is Meridian's active Wave 1 evidence gate. Every row must point to executable repo evidence, with bounded runtime evidence regenerated and attached from the validation run when a provider scenario cannot be closed from checked-in tests. The current signed DK1 evidence is the 2026-04-27 packet set under `artifacts/provider-validation/_automation/2026-04-27/`; future date-stamped packets are current only for the run that produced them and need matching packet-bound sign-off before they can replace that evidence. Deferred providers stay out of the active gate even when they remain in the broader provider strategy.


For the unified per-broker phase/blocker/evidence view, see [`provider-integration-status.md`](./provider-integration-status.md).

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor or runtime condition remains manual

## Wave 1 Matrix

| Scope | Offline / CI evidence | Manual / runtime evidence | Latest packet path + row status | Status | Bounded by |
| --- | --- | --- | --- | --- | --- |
| Alpaca core provider confidence | `AlpacaBrokerageGatewayTests`, `AlpacaCorporateActionProviderTests`, `AlpacaCredentialAndReconnectTests`, `AlpacaMessageParsingTests`, `AlpacaQuotePipelineGoldenTests`, `AlpacaQuoteRoutingTests`, `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Not required for the active Wave 1 claim | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json` (`activeProviderRows[Alpaca].posture=repo-closed`) + `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` (`sampleReview.DK1-ALPACA-* = ready`) | ✅ | n/a |
| Robinhood supported surface | `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests`, `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Bounded broker-session scenarios (`auth-session`, `quote-polling`, `order-submit-cancel`, `throttling-reconnect`) must be regenerated or attached for the review run; the old `artifacts/provider-validation/robinhood/2026-04-09/` packet is not retained in the current repo | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json` (`activeProviderRows[Robinhood].posture=bounded`) + `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` (`sampleReview.DK1-ROBINHOOD-SUPPORTED-SURFACE = ready|blocked`) | ⚠️ | Unofficial API plus manual broker-session and runtime requirements |
| Yahoo historical and fallback confidence | `YahooFinanceHistoricalDataProviderTests`, `YahooFinanceIntradayContractTests` | Not required for the active Wave 1 claim; existing live Yahoo integration suites are optional developer reference only | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json` (`activeProviderRows[Yahoo].posture=repo-closed`) + `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` (`sampleReview.DK1-YAHOO-HISTORICAL-FALLBACK = ready`) | ✅ | n/a |
| Checkpoint reliability | `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
| TradeStation execution evidence reconciliation slice | `PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_CreateUpdateCancelAndFillReconciliation_ProducesDeterministicCanonicalEvidence`, `PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_DelayedOutOfOrderEvents_RemainsIdempotentAndDeterministic` | Not required; this row is repo-closed evidence for create/update/cancel plus delayed/out-of-order fill reconciliation determinism into canonical execution evidence | ✅ | n/a |
| Parquet L2 flush behavior | `ParquetStorageSinkTests`, `ParquetConversionServiceTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
| Execution/readiness parity slice (IBKR-focused contract stability) | `IBBrokerageGatewayTests.ConnectAsync_AfterReconnect_RehydratesSessionAndAllowsOrderLifecycleToContinue`, `IBBrokerageGatewayTests.GetPositionsAsync_MapsPositionCallbacks`, `TradingOperatorReadinessServiceTests.GetAsync_AfterRestart_ShouldPreserveReplayParityAndExecutionAuditEvidence` | Use run-date replay/session artifacts only when validating with a live broker gateway; CI closes the canonical projection stability contract for auth/session refresh, position snapshots, and replay-readiness reconstruction | ✅ | n/a |


## Readiness/Inbox regression evidence index (provider-impacting changes)

Use this compact index when a provider change can affect trading-readiness posture, operator-inbox projection, or endpoint dependency assumptions.
Each run must link the provider packet set to readiness/inbox verification results and operator sign-off state before promotion review.

| Evidence lane | Required artifact(s) | Verification outcome to record | Operator sign-off status |
| --- | --- | --- | --- |
| Provider validation baseline | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json` | Record `status` plus provider-row pass/bounded deltas for the changed adapter(s). | Mark `pending` until packet + endpoint checks are attached. |
| DK1 parity packet | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` | Record packet `status` (`ready-for-operator-review` or blocked) and any trust-gate blockers tied to provider changes. | Mark `review-ready` only when packet status is ready and blockers are explained. |
| Trading readiness projection compatibility | `GET /api/workstation/trading/readiness` snapshot (or endpoint test evidence) + contract packet (`artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.json`) | Confirm projection compatibility for shared readiness fields (`OverallStatus`, `SnapshotVersion`, `AcceptanceGates`, `EvidenceCompleteness`) and note pass/fail. | Mark `pending` if compatibility is unresolved or endpoint assumptions drift. |
| Operator inbox dependency assumptions | `GET /api/workstation/operator/inbox` snapshot (or endpoint test evidence) + linked readiness work-item mapping evidence | Confirm inbox blockers/severity/routing remain aligned with readiness acceptance gates for affected provider/account scopes. | Mark `pending` if inbox/readiness alignment is incomplete. |
| DK1 operator approval | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json` | Record `validForDk1Exit`, `signedOwners`, and missing owners tied to the same run-date packet set. | Mark `signed` only when `validForDk1Exit=true` and required owners are present. |

Regression index rule: a provider-impacting change is promotion-eligible only when all five rows are linked to the same run date and no row remains in `pending` status.

## 2026-05-20 focused Robinhood polling hardening

Focused validation on 2026-05-20 added offline coverage for the Robinhood quote-polling boundary:
crossed/invalid quotes are rejected before collector publication, unauthorized-token failures surface
through redacted diagnostics, and the polling adapter tracks lifecycle state, last successful API call,
last message time, consecutive poll failures, and data-quality rejection counts. This improves the
repo-closed part of the Robinhood row while preserving its bounded runtime status.

Evidence anchors for this run date:

- `src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodMarketDataClient.cs`
- `tests/Meridian.Tests/Infrastructure/Providers/RobinhoodMarketDataClientTests.cs`
- `docs/providers/provider-confidence-baseline.md`


## 2026-05-19 focused execution-provider validation artifacts

Focused validation on 2026-05-19 added executable coverage for Tradier-style account/position sync, options order placement lifecycle under partial-fill progression, and negative-path failover posture verification for rate-limit (`429`) plus transient broker failures. Evidence is now closed in-repo via `TradierExecutionReconciliationTests` instead of requiring standalone runtime packet notes for those scenarios.

Artifacts and evidence anchors for this run date:

- `tests/Meridian.Tests/Execution/TradierExecutionReconciliationTests.cs`
- `docs/status/provider-validation-matrix.md` (this update)

## Primary Validation Command

Run the Wave 1 command matrix with:

```powershell
./scripts/dev/run-wave1-provider-validation.ps1
```

The script writes:

- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.md`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`
- `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.md`

When operator owners approve DK1 exit, pass the signed review packet through
`-OperatorSignoffPath artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-operator-signoff.json`.
The regenerated DK1 packet records `operatorSignoff.status`, `signedOwners`, `missingOwners`,
approval timestamps, and owner rationales so the cockpit readiness lane can distinguish pending,
partial, and signed operator review.

Each generated summary now restates the active provider rows, the DK1 pilot replay/sample set,
the cross-cutting checkpoint and Parquet closures, and the deferred-provider inventory so the
automation output matches the authoritative Wave 1 posture described in this matrix.

The DK1 sample-set contract is maintained in [`dk1-pilot-parity-runbook.md`](./dk1-pilot-parity-runbook.md)
and emitted as `pilotReplaySampleSet` in the generated JSON summary. The DK1 packet generator
validates those required samples, links the trust-rationale mapping and baseline-threshold review
documents, checks those documents for the required DK1 reason codes, payload fields, threshold
metrics, FP/FN review markers, and provider-matrix anchors, then reports whether the packet is
`ready-for-operator-review` or blocked by missing or incomplete evidence. The current 2026-04-27
packet is signed and valid for DK1 exit. Future DK1 reviews must use a freshly generated
`artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json` packet plus
its bound operator sign-off file before replacing that signed evidence.


## Promotion Checklist Requirement (Paper + Live Enablement)

Before enabling any broker for **paper** or **live** workflows, all four checks must be green in workstation/provider readiness:

1. **Contract compatibility validation** (`artifacts/contract-review/<yyyy-mm-dd>/contract-review-packet.json` with `readyForCadenceReview=true`).
2. **Focused adapter tests** (provider row evidence captured in the active Wave 1/DK packet).
3. **Replay evidence generation** (consistent replay verification for the active paper session).
4. **Degradation calibration output** (`provider-validation-evidence-bundle.json` posture `candidate-approved`).

If any check fails, promotion enablement is blocked and operator surfaces must show explicit blockers.

## Pass/Fail Criteria

- **Pass:** all four checklist checks are true and no provider-promotion blockers are emitted.
- **Fail:** one or more checks are false; paper/live enablement must remain disabled until evidence is regenerated under `artifacts/provider-validation/_automation/<yyyy-mm-dd>/` (and `artifacts/contract-review/<yyyy-mm-dd>/` for contract compatibility).

## Notes

- Robinhood remains polling-oriented and unofficial. Do not describe it as websocket-validated.
- Yahoo is active only as a historical and fallback provider row for Wave 1.
- `Polygon`, `Interactive Brokers`, `NYSE`, and `StockSharp` are deferred from the active Wave 1 gate.


## Unified automation and promotion posture

Use `./scripts/dev/run-provider-validation-evidence-bundle.ps1` to generate:
- `wave1-validation-summary.json`
- `dk1-pilot-parity-packet.json`
- `dk1-operator-signoff.json`
- `provider-degradation-governance.json` (when `-CalibrationInput` is provided)
- `provider-validation-evidence-bundle.json`

The evidence bundle standardizes schema and emits promotion posture (`candidate-approved`, `candidate-rejected`, or `not-run`) with baseline-versus-candidate kernel metadata.
Bundle outputs are written under the same date-scoped automation root (`artifacts/provider-validation/_automation/<yyyy-mm-dd>/`) so provider-validation summaries, DK1 packet/sign-off outputs, and degradation governance evidence remain in one canonical artifact structure.

Promotion checklist and rollback triggers are authoritative in `docs/operations/provider-degradation-calibration.md`; this matrix requires those checks for any DK1 promotion decision.
