# Contract & Endpoint Audit — Meridian.Ui.Shared

**Date:** 2026-07-05 · **Source:** local mount `Meridian.Ui.Shared/` (Contracts + Endpoints)
· **Audited against:** DS 1.14.0 (149→154 exports, 19 templates)

Contracts read in full: `WorkstationOperationsContracts.cs`, `CoveredCallContracts.cs`,
`FamilyOfficeContracts.cs`, `Reconciliation/StatementImportContracts.cs`,
`Reconciliation/IReconciliationApiService.cs`, `Integrations/OmsIntegrationContracts.cs`,
`Simulation/ExecutionSimulationContracts.cs`. Endpoint surface enumerated from
`Endpoints/` (route groups + `UiApiRoutes.*` usage).

---

## A. Domain coverage — templates vs. backend

### Covered (template ↔ endpoint domain)

| Template | Backend domain |
| --- | --- |
| accounting-workstation, journaling-workstation | Ledger / AccountingSystem endpoints |
| security-master-registry | SecurityMaster + SecurityMasterWorkbench |
| ingestion-operations | IngestionJob / Backfill* / provider freshness |
| trading-desk | Execution / OptionChain (partial — see B5) |
| strategy-builder, strategy-runs, backtest-builder, field-formula, strategy-onboarding, amx-governance, basket-builder | StrategyLifecycle / QuantLab / Lean |
| report-library, report-scheduler | Export / Packaging / reporting templates |
| charting-workstation | WorkstationEndpoints.PlotTool |
| settings-admin | Admin / Config / FeatureCapabilities |
| dashboard-workstation | Status / Health / Diagnostics |
| alerting-workstation | DataQuality / Diagnostics (loose fit — no 1:1 endpoint file) |

### Not covered (backend domain with **no** template)

1. **Statement reconciliation & case triage** — `WorkstationEndpoints.Reconciliation.cs`
   (21 routes: statement runs, validation, breaks, open cases, queue status, break
   review/resolve/bulk, calibration, audit) + `StatementImportContracts.cs`. The single
   largest uncovered surface. The DS had **no SLA or case-triage primitives** until this
   audit (see C).
2. **Operations continuity close workflow** — `WorkstationEndpoints.OperationsContinuity.cs`
   (28 routes: gates, timeline, break cases, approvals submit/approve/reject, close
   calendar, close/reopen). `GateRail`, `EventTimeline`, `ReadinessPanel` cover the read
   side; approval decisions and break-case triage had no components.
3. **Family office** — `WorkstationEndpoints.FamilyOffice.cs` (overview, balance-sheet,
   entities, ownership-graph). No template; before this audit no component could render
   an ownership graph, a capital commitment, or the per-row provenance tuple.
4. **Covered-call strategy lab** — `CoveredCallEndpoints.cs` + 177-line contract.
   backtest-builder is generic; nothing renders the chain-preview candidate table
   (`MeetsAllFilters` / `RejectReason`) or the 21-field metrics block.
5. **OMS integration diagnostics** — `/api/oms` (adapter diagnostics, retry schedule,
   audit trail, request signing, key rotation).
6. Smaller gaps: Direct lending (`/api/loans`), Banking (`/api/banking`), Fund structure
   (`/api/fund-structure`), Brokerage connections, Evidence explorer
   (`/api/workstation/evidence` — report-library only references packs), Risk
   (`/api/risk` + `/api/v1/risk`).

---

## B. Data-shape findings (template mock data vs. contract)

1. **strategy-runs** — run rows `{id, strat, env, started, dur, status, ret, sharpe,
   trades}` vs. `CoveredCallRunSummary { RunId, UnderlyingSymbol, From, To, Label,
   Status, StartedAt, EndedAt?, Cagr?, SharpeRatio?, WinRate? }`. Missing: `WinRate`,
   `EndedAt` (nullable while running — the mock has no running run). Mock stores
   **pre-formatted strings** (`"+23.74%"`, `"1.42"`); the contract sends numbers
   (`Cagr: 0.2374`). Templates should keep numerics and format at render (`Delta`,
   `AmountCell`) so sorting works.
2. **Status vocabulary drift** — contract enums not previously in the normalizer:
   `OperationsGateStatus.NotReady/Skipped`, `ReconciliationState.Matched/BreaksDetected/
   Resolved`, `OperationsWorkflowStatus.AwaitingApproval`, SLA `OnTrack/Breached`.
   These fell through `normalizeSeverity` to muted "info". **Fixed** — `status.js` now
   maps all of them (BreaksDetected → action, Breached → blocked, Matched/Resolved/
   OnTrack → ready, AwaitingApproval → review, NotReady/Skipped/Paused → info).
3. **ReconciliationPanel scope** — it models line-level statement↔ledger matching. The
   server model is **case-centric**: `ReconciliationCaseSummaryDto` carries confidence,
   rationale, priority, assignee, a 6-field SLA block, sign-off and reopen trails, and a
   `Version` for optimistic concurrency. Nothing rendered that lifecycle. **Fixed** —
   `CaseQueue` + `SlaChip` (see C).
4. **Provenance tuple** — every family-office read model repeats `SourceSystem,
   SourceDocumentId, AsOfDate, ValuationDate, EvidenceCompleteness,
   ReconciliationStatus, LastReviewedBy/AtUtc` (7 DTOs). No primitive existed.
   **Fixed** — `ProvenanceChip`.
5. **Covered-call chain preview** — `CoveredCallChainRow` (bid/ask, delta, IV, OI,
   volume, `MeetsAllFilters`, `RejectReason`) fits `FilteredDataTable` with a
   `SeverityBadge` reject column; no new component needed, but no template exercises it.
6. **Timestamps** — contracts send ISO UTC strings; several templates hand-format
   (`"06-30 14:12:04Z"`). House rule: render through `Timestamp` / `FreshnessIndicator`.
7. **Optimistic concurrency** — `Version` appears on cases, approvals, transitions, and
   `OmsOutboundContract`. Consuming screens should hold `version` per row and disable
   mutating actions on staleness; no component change needed, documented here.
8. **Evidence routes line up** — `EvidenceLink`'s `route` prop matches
   `StatementRunEvidenceLinkDto.EvidenceRoute` and `FamilyOfficeEvidenceLinkDto.Route`. ✓
9. **GateRail ↔ gates** — `OperationsGateDto` fields (`DisplayName`, `Status`,
   `BlockingReason`) map cleanly to `GateRail` props (`label`, `status`,
   `statusLabel`); `IsRequired=false` gates should pass `statusLabel="Optional"`. ✓
   (with the B2 vocabulary fix).

---

## C. Actions taken in this audit (DS 1.14.0)

- **`status.js`** — vocabulary extended with the contract enums above.
- **`SlaChip`** (operations) — SLA posture chip; fields mirror the case SLA block.
- **`CaseQueue`** (operations) — triage list for `ReconciliationCaseSummaryDto` /
  `OperationsBreakCaseDto` rows: priority rail, status, SLA, assignee.
- **`ProvenanceChip`** (operations) — the recurring evidence tuple, cell-sized.
- **`OwnershipGraph`** (charts) — layered entity/control diagram for
  `FamilyOwnershipGraphDto`; accepts DTO field names directly.
- **`CommitmentBar`** (accounting) — called/unfunded/distributed/NAV funding state
  with derived DPI · TVPI for `CapitalCommitmentDto`.
- Cards: *Case triage & SLA* (Operations), *Ownership graph* (Charts), *Capital
  commitments* (Accounting), plus this audit's summary card (Documentation).

## D. Recommended next

Items 1, 2, and 4 were **built in 1.15.0** as `templates/reconciliation-workstation`,
`templates/family-office`, and `templates/covered-call-lab`. Remaining:

1. **operations-continuity template** — `GateRail` + approvals + close calendar.
2. OMS adapter diagnostics panel (retry/next-retry/last-error per adapter).
3. Smaller domains from §A: direct lending, banking, fund structure, risk.
