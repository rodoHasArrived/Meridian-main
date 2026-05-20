# Provider Validation Matrix

**Last Updated:** 2026-05-20
**Scope:** Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof

This matrix is Meridian's active Wave 1 evidence gate. Every row must point to executable repo evidence, with bounded runtime evidence regenerated and attached from the validation run when a provider scenario cannot be closed from checked-in tests. The current signed DK1 evidence is the 2026-04-27 packet set under `artifacts/provider-validation/_automation/2026-04-27/`; future date-stamped packets are current only for the run that produced them and need matching packet-bound sign-off before they can replace that evidence. Deferred providers stay out of the active gate even when they remain in the broader provider strategy.


For the unified per-broker phase/blocker/evidence view, see [`provider-integration-status.md`](./provider-integration-status.md).

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor or runtime condition remains manual

## Wave 1 Matrix

| Scope | Offline / CI evidence | Manual / runtime evidence | Status | Bounded by |
| --- | --- | --- | --- | --- |
| Alpaca core provider confidence | `AlpacaBrokerageGatewayTests`, `AlpacaCorporateActionProviderTests`, `AlpacaCredentialAndReconnectTests`, `AlpacaMessageParsingTests`, `AlpacaQuotePipelineGoldenTests`, `AlpacaQuoteRoutingTests`, `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Not required for the active Wave 1 claim | ✅ | n/a |
| Robinhood supported surface | `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests`, `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Bounded broker-session scenarios (`auth-session`, `quote-polling`, `order-submit-cancel`, `throttling-reconnect`) must be regenerated or attached for the review run; the old `artifacts/provider-validation/robinhood/2026-04-09/` packet is not retained in the current repo | ⚠️ | Unofficial API plus manual broker-session and runtime requirements |
| Yahoo historical and fallback confidence | `YahooFinanceHistoricalDataProviderTests`, `YahooFinanceIntradayContractTests` | Not required for the active Wave 1 claim; existing live Yahoo integration suites are optional developer reference only | ✅ | n/a |
| Checkpoint reliability | `BackfillStatusStoreTests`, `ParallelBackfillServiceTests`, `GapBackfillServiceTests`, `CheckpointEndpointTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
| TradeStation execution evidence reconciliation slice | `PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_CreateUpdateCancelAndFillReconciliation_ProducesDeterministicCanonicalEvidence`, `PaperSessionPersistenceServiceTests.TradeStationExecutionSlice_DelayedOutOfOrderEvents_RemainsIdempotentAndDeterministic` | Not required; this row is repo-closed evidence for create/update/cancel plus delayed/out-of-order fill reconciliation determinism into canonical execution evidence | ✅ | n/a |
| Parquet L2 flush behavior | `ParquetStorageSinkTests`, `ParquetConversionServiceTests` | Not required; the Wave 1 claim is closed in repo tests | ✅ | n/a |
| Execution/readiness parity slice (IBKR-focused contract stability) | `IBBrokerageGatewayTests.ConnectAsync_AfterReconnect_RehydratesSessionAndAllowsOrderLifecycleToContinue`, `IBBrokerageGatewayTests.GetPositionsAsync_MapsPositionCallbacks`, `TradingOperatorReadinessServiceTests.GetAsync_AfterRestart_ShouldPreserveReplayParityAndExecutionAuditEvidence` | Use run-date replay/session artifacts only when validating with a live broker gateway; CI closes the canonical projection stability contract for auth/session refresh, position snapshots, and replay-readiness reconstruction | ✅ | n/a |


## Deferred provider inventory (outside active Wave 1 gate)

| Provider | Owner | Deferral reason | Revisit sprint |
| --- | --- | --- | --- |
| Polygon | Data Operations & Provider Reliability | Wave 1 scope is intentionally limited to Alpaca, Robinhood, and Yahoo closure plus shared reliability slices; Polygon evidence is not required for the active DK1 packet. | Sprint 2026.13 (June 2026 planning window) |
| Interactive Brokers | Shared Platform Interop | IBKR remains outside the active Wave 1 provider confidence claim while contract-compatibility and replay-readiness evidence stabilize around the closed execution/readiness parity slice. | Sprint 2026.14 (late June 2026 integration planning) |
| NYSE | Market Structure & Reference Data | Exchange-direct NYSE adapter work is deferred to preserve focus on current Wave 1 provider closure and paper-trading cockpit reliability gates. | Sprint 2026.15 (July 2026 roadmap revalidation) |
| StockSharp | Shared Platform Interop | StockSharp adapter promotion is deferred pending post-Wave-1 prioritization and governance capacity after DK1 sign-off maintenance. | Sprint 2026.15 (July 2026 roadmap revalidation) |

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
- Deferred providers are tracked in the **Deferred provider inventory** table above; every deferred row must retain explicit owner, rationale, and revisit sprint.


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
