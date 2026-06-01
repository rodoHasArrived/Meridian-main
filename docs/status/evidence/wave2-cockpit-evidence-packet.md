# Wave 2 Cockpit Evidence Packet

**Last Updated:** 2026-05-21

## Overview

This document captures the dated Wave 2 paper-trading cockpit completion evidence. It satisfies the
definition-of-done for the paper-trading cockpit reliability sprint across all four acceptance gates.

---

## Evidence Snapshot — 2026-05-21

### Gate 1: Replay Confidence

**Status:** Complete  
**Acceptance criterion:** Cockpit-level test exercises `stale → re-verify → cleared` path; service-level test covers the same sequence with count assertions.

Evidence tests:
- `trading-screen.test.tsx > TradingScreen > clears stale replay work item after re-verify returns consistent state` — cockpit-driven workflow: renders with stale readiness, verifies replay, refreshes readiness, confirms stale work item cleared.
- `trading-screen.view-model.test.ts > trading readiness named operator states > replay-mismatch: inconsistent replay blocks cockpit and routes to replay panel` — view-model gate for replay mismatch state.
- `Wave2PaperTradingCockpitAcceptanceTests.cs` — service-level tests covering the verify→stale→re-verify sequence with fill/order/ledger count assertions.

### Gate 2: Session Persistence

**Status:** Complete  
**Acceptance criterion:** Cockpit-driven `create → restore → verify → close` flow test passes.

Evidence tests:
- `trading-screen.test.tsx > TradingScreen > create→verify→close session flow calls API in order and refreshes session list` — cockpit-level create→verify→close sequence validated against API mocks.
- `trading-screen.view-model.test.ts > trading readiness named operator states > context-required: missing session blocks cockpit with paper-session-missing work item` — named state test for missing-session context.
- `Wave2PaperTradingCockpitAcceptanceTests.SessionPersistenceGate_*` — service-level session persistence and continuity tests.

### Gate 3: Risk Auditability

**Status:** Complete  
**Acceptance criterion:** Circuit-breaker reason surfaces in readiness card; work item routes to risk/controls view.

Evidence tests:
- `trading-screen.view-model.test.ts > trading readiness named operator states > controls-blocked: open circuit breaker surfaces reason and routes to risk controls` — named state test verifying circuit-breaker reason is surfaced and work item routes to `/trading/risk`.
- `trading-screen.view-model.test.ts > trading readiness work-item action routing > routes ExecutionControl work items to the trading risk route` — routing unit test for ExecutionControl kind.
- Service-level: `WorkstationEndpointsTests.MapWorkstationEndpoints_TradingReadiness_*` — readiness endpoints enforce audit-controls gate posture.

### Gate 4: Promotion Traceability

**Status:** Complete  
**Acceptance criterion:** Promotion approval form submits `approvalChecklist`; validation blocks empty checklist.

Evidence tests:
- `trading-screen.view-model.test.ts > promotion approval checklist > validates that an empty checklist blocks the approval` — validation blocks empty checklist.
- `trading-screen.view-model.test.ts > promotion approval checklist > includes approvalChecklist in the serialized request when populated` — request serialization test.
- `trading-screen.view-model.test.ts > promotion approval checklist > auto-populates the checklist from the evaluation result when gate is eligible` — auto-population from eligible evaluation.
- `trading-screen.test.tsx > TradingScreen > handles promotion happy path` — end-to-end promotion with `approvalChecklist` in the API call.

---

## ReadyForPaperOperation = true Assertion Proof

**Test:** `trading-screen.view-model.test.ts > green cockpit: ReadyForPaperOperation proof > shows ReadyForPaperOperation=true when DK1 signed + healthy brokerage + active session + consistent replay`

This test proves that `readyForPaperOperation: true` is produced only when all four conditions are simultaneously satisfied:
- DK1 trust gate `operatorSignoffStatus = "signed"`
- Brokerage sync `health = "Healthy"`
- Active paper session exists
- Replay verification is consistent (`mismatchReasons: []`)

**Inverse proof:** `trading-screen.view-model.test.ts > green cockpit: ReadyForPaperOperation proof > keeps cockpit in ReviewRequired when DK1 operatorSignoffStatus=pending even with healthy brokerage`  
DK1 pending sign-off keeps `readyForPaperOperation: false` and surfaces `dk1-operator-signoff-pending` work item even when brokerage is healthy.

---

## Operator Inbox Work-Item Stability

**Non-brokerage action routing** (Milestone 2) — all five previously null-routed kinds now return non-null actions:

| Work-Item Kind | Routed Action Label | Route |
|---|---|---|
| `PaperReplay` | "Verify replay" | `/trading#session-replay-panel` |
| `ExecutionControl` | "Review risk controls" | `/trading/risk` |
| `PromotionReview` | "Open promotion gate" | `/trading#promotion-gate-panel` |
| `SecurityMasterCoverage` | "Open security master" | `/accounting/security-master` |
| `ReconciliationBreak` | "Open reconciliation" | `/accounting/reconciliation` |

Covered by five unit tests in `trading-screen.view-model.test.ts > trading readiness work-item action routing`.

---

## DK1 Trust Gate Sign-off Reference

DK1 operator sign-off baseline: `artifacts/provider-validation/_automation/2026-04-27/dk1-operator-signoff.json`  
Packet: `artifacts/provider-validation/_automation/2026-04-27/dk1-pilot-parity-packet.json`  
Sign-off status: `signed` by `RODO` for Data, Provider Reliability, and Trading.

The cockpit correctly reflects `ReadyForPaperOperation: false` until `operatorSignoffStatus = "signed"` is present in the trust gate, as proven by the green cockpit proof test and the DK1 pending inverse test.

---

## Account Context Threading

**Milestone 5** — `fundAccountId` threading to `getTradingReadiness`:

- `trading-screen.view-model.test.ts > trading readiness fund account threading > passes fundAccountId to getTradingReadiness on refresh` — asserts the `fundAccountId` query parameter is passed when an account context is active.
- `trading-screen.view-model.test.ts > trading readiness fund account threading > omits fundAccountId from the call when no account context is active` — asserts the parameter is absent without an account context.
- Endpoint-side coverage: `WorkstationEndpointsTests.MapWorkstationEndpoints_TradingReadiness` already covers the `fundAccountId` path parameter on the server.

---

## Test Summary

| Milestone | Test File | New Tests Added | Result |
|---|---|---|---|
| M1: Named operator states | `trading-screen.view-model.test.ts` | 4 (+1 todo) | ✅ Pass |
| M2: Work-item routing | `trading-screen.view-model.test.ts` | 6 | ✅ Pass |
| M3: Stale-replay recovery | `trading-screen.test.tsx` | 1 | ✅ Pass |
| M4: Approval checklist | `trading-screen.view-model.test.ts` | 5 | ✅ Pass |
| M5: Account context threading | `trading-screen.view-model.test.ts` | 2 | ✅ Pass |
| M6: Session persistence cockpit | `trading-screen.test.tsx` | 1 | ✅ Pass |
| M7: Green cockpit proof | `trading-screen.view-model.test.ts` | 2 | ✅ Pass |

**Total new tests: 21** (+ 1 pending/todo for live-oversight state)  
**Full suite result:** 1098 passed | 1 pre-existing failure (unrelated `aria-describedby` duplication bug in `surfaces VM-owned disabled reasons while creating paper sessions`) | 1 todo

---

## Production Code Changes

| File | Change |
|---|---|
| `trading-screen.view-model.ts` | Added routing for `PaperReplay`, `ExecutionControl`, `PromotionReview`, `SecurityMasterCoverage`, `ReconciliationBreak` in `buildTradingReadinessWorkItemAction` |
| `trading-screen.view-model.ts` | Added `approvalChecklist: string[]` to `PromotionGateForm`; `validatePromotionApproval` blocks empty checklist; `buildPromotionApprovalRequest` serializes checklist; `usePromotionGateViewModel` auto-populates checklist from evaluation and clears on `runId` change; exported `paperPromotionApprovalChecklist` |
| `trading-screen.view-model.ts` | `useTradingReadinessViewModel` accepts optional `fundAccountId` and threads it to `getTradingReadiness` |
| `trading-screen.tsx` | Passes `data.brokerage?.account` as `fundAccountId` to `useTradingReadinessViewModel` |
| `lib/api.ts` | `getTradingReadiness` accepts optional `{ fundAccountId?, signal? }` options object |
| `lib/workstation-endpoints.ts` | `workstationTradingReadinessEndpoint` helper threads `fundAccountId` as a query parameter |
