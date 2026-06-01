# Provider Validation Matrix

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-05-20

**Last Updated:** 2026-05-20
**Scope:** Active Wave 1 provider confidence, checkpoint resumability, and Parquet Level 2 flush proof

This matrix is Meridian's active Wave 1 evidence gate. Every row must point to executable repo evidence, with bounded runtime evidence regenerated and attached from the validation run when a provider scenario cannot be closed from checked-in tests. The current signed DK1 evidence is the 2026-04-27 packet set under `artifacts/provider-validation/_automation/2026-04-27/`; future date-stamped packets are current only for the run that produced them and need matching packet-bound sign-off before they can replace that evidence. Deferred providers stay out of the active gate even when they remain in the broader provider strategy.


For the unified per-broker phase/blocker/evidence view, see [`provider-integration-status.md`](provider-integration-status.md).

## Legend

- ✅ Closed with executable repo evidence
- ⚠️ Bounded: meaningful evidence exists, but at least one vendor or runtime condition remains manual

## Wave 1 Matrix

| Scope | Offline / CI evidence | Manual / runtime evidence | Status | Bounded by |
| --- | --- | --- | --- | --- |
| Alpaca core provider confidence | `AlpacaBrokerageGatewayTests`, `AlpacaCorporateActionProviderTests`, `AlpacaCredentialAndReconnectTests`, `AlpacaHistoricalDataProviderTests` (capability surface, deterministic throttling counter, degraded response posture), `AlpacaMessageParsingTests`, `AlpacaQuotePipelineGoldenTests`, `AlpacaQuoteRoutingTests`, `ProviderFactoryCredentialContextTests` (APCA alias + DI/factory credential path), `ExecutionGovernanceEndpointsTests.AlpacaExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Not required for the active Wave 1 claim; DK1 packet-bound operator sign-off fields (`status`, `signedOwners`, `missingOwners`, `validForDk1Exit`) remain required when regenerating runtime evidence | ✅ | n/a |
| Robinhood supported surface | `RobinhoodBrokerageGatewayTests`, `RobinhoodMarketDataClientTests`, `RobinhoodHistoricalDataProviderTests`, `RobinhoodSymbolSearchProviderTests`, `ExecutionGovernanceEndpointsTests.RobinhoodExecutionPath_SubmitsOrderThroughStableExecutionSeam` | Bounded broker-session scenarios (`auth-session`, `quote-polling`, `order-submit-cancel`, `throttling-reconnect`) must be regenerated or attached for the review run; the old `artifacts/provider-validation/robinhood/2026-04-09/` packet is not retained in the current repo | ⚠️ | Unofficial API plus manual broker-session and runtime requirements |
| Yahoo historical and fallback confidence | `YahooFinanceHistoricalDataProviderTests`, `YahooFinanceIntradayContractTests` | Not required for the active Wave 1 claim; DK1 packet-bound operator sign-off fields (`status`, `signedOwners`, `missingOwners`, `validForDk1Exit`) remain required when regenerating runtime evidence; existing live Yahoo integration suites are optional developer reference only | ✅ | n/a |
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
- `docs/reference/provider-validation-matrix.md` (this update)

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

The DK1 sample-set contract is maintained in [`dk1-pilot-parity-runbook.md`](../status/evidence/dk1-pilot-parity-runbook.md)
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

## 2026-05-20 capability-claim parity + deterministic data-shape audit

The following audit reconciles current capability claims versus deterministic data-shape compatibility for the requested provider set:
Finnhub, NYSE, Edgar, OpenFigi, YahooFinance, Tradier, TwelveData, AlphaVantage, Tiingo, Stooq, Fred, and NasdaqDataLink.

| Provider | Capability-claim parity posture | Deterministic data-shape compatibility posture | Evidence anchor |
| --- | --- | --- | --- |
| Finnhub | Backfill + symbol-search supported through credential-gated adapters | JSON parse and contract fixtures enforce stable bar parsing and symbol-search projection | `FreeProviderContractTests`, `FreeHistoricalProviderParsingTests`, `ProviderFactoryCredentialContextTests` |
| NYSE | Deferred from active Wave 1 closure; retained as provider inventory/runtime lane | Message/csv parser and pipeline tests lock schema, exchange-code mapping, and publication flow | `NYSEMessageParsingTests`, `NyseNationalTradesCsvParserTests`, `NyseMessagePipelineTests` |
| Edgar | Security Master/symbol-search inventory support | Parser coverage validates ticker-entry extraction, malformed payload handling, and ingest projections | `EdgarSymbolSearchProviderTests` |
| OpenFigi | Symbol mapping/search support | Recorded-response contract tests validate mapping/search parse determinism | `OpenFigiClientTests` |
| YahooFinance | Active Wave 1 historical + fallback row | Historical/intraday contract suites lock fallback bar-shape expectations | `YahooFinanceHistoricalDataProviderTests`, `YahooFinanceIntradayContractTests` |
| Tradier | Execution reconciliation support path (not active Wave 1 provider row) | Deterministic execution evidence + out-of-order/partial-fill handling covered in focused execution suites | `TradierExecutionReconciliationTests` |
| TwelveData | Backfill inventory support via provider adapter | Stubbed-response parsing tests validate success/error/empty payload handling | `FreeHistoricalProviderParsingTests` |
| AlphaVantage | Backfill inventory support (explicitly credential/enable gated) | Contract + parser tests validate rate-limit and error-shape behavior | `FreeProviderContractTests`, `FreeHistoricalProviderParsingTests` |
| Tiingo | Backfill inventory support | Contract tests + factory credential-path tests keep adapter eligibility deterministic | `FreeProviderContractTests`, `ProviderFactoryCredentialContextTests` |
| Stooq | Backfill inventory support | CSV parser tests validate stable row-to-bar conversion and empty dataset handling | `FreeProviderContractTests`, `FreeHistoricalProviderParsingTests` |
| Fred | Backfill inventory support for economic-series lanes | Parser/credential tests validate deterministic failure surface when key/shape is invalid | `FreeHistoricalProviderParsingTests`, `ProviderFactoryCredentialContextTests` |
| NasdaqDataLink | Backfill inventory support | Provider-factory credential/context tests verify deterministic inclusion and credential wiring | `ProviderFactoryCredentialContextTests` |

### Unresolved items (owner + risk + defer rationale)

| Item | Owner | Risk | Defer rationale |
| --- | --- | --- | --- |
| NYSE runtime entitlement/session evidence remains outside active Wave 1 closure | `@provider-infra` + `@ops-readiness` | False-positive readiness claims if parser-only confidence is mistaken for runtime-feed confidence | Wave 1 closure is intentionally limited to Alpaca/Robinhood/Yahoo; NYSE remains explicit deferred inventory while entitlement and runtime evidence lanes mature |
| Tradier capability row is execution-focused and not yet normalized into an always-on provider matrix row | `@execution-reliability` | Claim drift between execution evidence and provider-row wording | Current closure requirement is execution reconciliation determinism; broader provider-row governance is deferred to next matrix expansion to avoid changing Wave 1 scope mid-gate |
| OpenFigi + Edgar runtime dependency characteristics (rate/uptime/vendor policy) are not represented in DK1 runtime packet | `@security-master` + `@provider-infra` | Symbol-search quality can degrade without appearing in provider runtime packet gating | Current DK1 packet contract is broker/paper-session centered; expanding to search-governance metrics is deferred until packet schema revision window |


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
