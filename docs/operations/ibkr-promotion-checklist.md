# IBKR Promotion Checklist

**Last Updated:** 2026-05-19  
**Owners:** Trading, Provider Reliability, Data Operations  
**Scope:** Phase-gated promotion of Interactive Brokers (IBKR) from read-only enablement to paper-trading enablement to production routing enablement.

---

## Purpose

Define explicit promotion gates and rollback rules for IBKR onboarding so no phase can advance unless:

1. contract compatibility passes,
2. adapter test slices pass,
3. replay parity evidence is current and accepted,
4. provider degradation calibration is approved,
5. operator sign-off is recorded and visible in readiness/inbox workflows.

This checklist is phase-authoritative for IBKR enablement decisions.

---

## Global prerequisites (required for every phase)

Before approving any phase promotion:

- [ ] **Readiness visibility is live:** `GET /api/workstation/trading/readiness` shows current IBKR posture/work items.
- [ ] **Inbox visibility is live:** `GET /api/workstation/operator/inbox` surfaces IBKR routing/reconciliation items with actionable navigation hints.
- [ ] **No hidden blockers:** all IBKR-critical warnings appear as readiness acceptance gates or operator inbox items.
- [ ] **Operator packet attached:** run-date evidence bundle and sign-off JSON are attached to the review record.

If visibility is missing or stale, promotion is automatically blocked.

---

## Phase 1 — Read-only enablement gate

### Promotion intent

Allow IBKR connectivity and read-only operational visibility (balances/positions/orders/health) with **no order routing**.

### Required passing conditions

- [ ] **Contract compatibility:** `docs/status/contract-compatibility-matrix.md` includes IBKR row status with no unresolved breaking-contract entries for shared DTOs/enums used by readiness and inbox projections.
- [ ] **Adapter test slices:** targeted IBKR adapter tests (connectivity, parsing, auth/session handling, throttling/retry, and snapshot projections) pass in CI/local evidence for the run date.
- [ ] **Replay parity evidence:** replay verification packet demonstrates parity for read-only event and state projections (counts and key identifiers match expected baselines; no unresolved mismatches).
- [ ] **Provider degradation calibration:** degradation kernel candidate for IBKR read-only posture is calibrated and approved with promotion posture `candidate-approved`.
- [ ] **Operator sign-off:** Trading + Provider Reliability + Data Operations sign `approved` with rationale that read-only posture is explainable and observable via readiness/inbox.

### Rollback / disable criteria

Immediately disable IBKR read-only exposure (or revert to previous known-good build/config) when any of the following occur:

- readiness/inbox cannot render IBKR status/work items,
- contract drift introduces incompatible readiness/inbox payload changes,
- replay parity changes from consistent to inconsistent without an accepted exception,
- degradation calibration status regresses to rejected/unknown,
- operator sign-off is revoked or expires due to stale packet binding.

---

## Phase 2 — Paper-trading enablement gate

### Promotion intent

Permit IBKR-backed paper workflow participation while keeping production/live routing disabled.

### Required passing conditions

- [ ] **Phase 1 remains green:** all read-only gate checks remain passing at promotion time.
- [ ] **Contract compatibility:** no unresolved IBKR compatibility breaks across execution-control/readiness, replay audit DTOs, and operator inbox projections used for paper workflow governance.
- [ ] **Adapter test slices:** IBKR paper-path slices pass (paper session lifecycle, order intent mapping, fill/cancel handling, reconciliation projections, and failure/retry paths).
- [ ] **Replay parity evidence:** paper-session replay verifies consistent counts and audit reconstruction for orders, fills, and ledger entries; mismatches are zero or formally accepted as bounded with owner approval.
- [ ] **Provider degradation calibration:** IBKR calibration for paper routing posture is approved (`candidate-approved`) with documented threshold and rationale.
- [ ] **Operator sign-off:** Trading + Provider Reliability + Data Operations approve paper enablement and confirm readiness acceptance gates + inbox work-item routing are sufficient for desk operations.

### Rollback / disable criteria

Disable IBKR paper-trading participation and return to read-only only when any of these occur:

- readiness gate or inbox shows blocked/critical posture for IBKR paper controls,
- replay verification fails or becomes stale relative to active paper session evidence,
- adapter slice regressions appear in required paper-path tests,
- calibration outcome is no longer approved,
- required owners withdraw sign-off.

---

## Phase 3 — Production routing enablement gate

### Promotion intent

Allow IBKR production/live routing after read-only + paper posture has remained stable and fully governed.

### Required passing conditions

- [ ] **Phase 1 and Phase 2 remain green:** no regressions in read-only/paper gate evidence.
- [ ] **Contract compatibility:** live-routing contract matrix entries are green for execution gateway payloads, control evidence, reconciliation detail, and readiness/inbox surfacing.
- [ ] **Adapter test slices:** live-path IBKR slices pass (routing handshake, reject/partial-fill handling, cancel/replace semantics, reconciliation and fail-safe controls).
- [ ] **Replay parity evidence:** latest replay packet confirms parity and audit continuity for the exact promotion baseline (no unresolved drift).
- [ ] **Provider degradation calibration:** production candidate calibration approved with explicit promotion posture `candidate-approved`, acceptable FP/FN review, and signed governance report.
- [ ] **Operator sign-off:** Trading + Provider Reliability + Data Operations sign production approval; sign-off references run-date packet path and confirms readiness/inbox visibility for production controls and incidents.

### Rollback / disable criteria

Immediately disable production routing (fall back to paper or read-only as appropriate) when:

- execution-control readiness gates move to blocked/critical,
- operator inbox surfaces unresolved critical IBKR routing/reconciliation items beyond allowed SLA,
- replay parity is inconsistent or missing for active production baseline,
- calibration confidence degrades below approved threshold,
- any mandatory owner revokes approval.

---

## Operator sign-off contract (all phases)

Each promotion decision must attach a sign-off record that includes:

- phase (`read-only`, `paper`, or `production`),
- reviewed packet path and generated timestamp,
- decision (`approved`, `rejected`, `conditional`),
- signed owners (Trading, Provider Reliability, Data Operations),
- rationale tied to readiness gate state and inbox work-item posture,
- expiry/revalidation trigger (for example: new adapter release, contract change, calibration rerun, or replay drift).

Promotions are invalid without a packet-bound sign-off from all required owners.

---

## Minimum evidence bundle for every promotion review

- Run-date contract compatibility output.
- Required adapter test-slice outputs for target phase.
- Replay parity packet (JSON/Markdown or equivalent) bound to the same run window.
- Provider degradation calibration governance output with promotion posture.
- Signed operator sign-off artifact showing readiness/inbox-based acceptance.

If any artifact is missing, promotion defaults to **blocked**.
