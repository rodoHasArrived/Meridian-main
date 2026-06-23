# Meridian Implementation TODO List

**Status:** active execution tracker  
**Owner:** core-team  
**Reviewed:** 2026-06-22
**Source:** [Meridian Design Document (Version 0.18)](meridian-design-document.md) and [Roadmap Registry](../roadmap/data/roadmap-items.yml)

This file is the single planning-tooling tracker for implemented design-document items and remaining TODOs. Keep detailed status changes here, then leave the design document to explain the product rationale and link back to this tracker.

## Completion Rules

- A checked item must map to a roadmap row with `status: done` and `evidence_posture: complete`, or to an explicitly listed evidence artifact that exists in the current checkout.
- A design-doc phrase such as `implemented evidence` is not the same as a complete product capability. Treat it as a foundation unless roadmap acceptance says the capability is closed.
- A planned, supported, or design-led item remains unchecked until the roadmap gate closes and acceptance evidence is recorded.
- Source, test, generated-roadmap, or operator evidence must be linked before moving any TODO to checked.

## Verification Snapshot

Reviewed on 2026-06-16 against:

- `docs/roadmap/data/roadmap-items.yml`
- `docs/roadmap/generated/ROADMAP_SUMMARY.md`
- `docs/roadmap/generated/roadmap-register.md`
- `docs/product/meridian-design-document.md`
- evidence paths listed in the roadmap rows

Result:

- W1 through W5 roadmap rows are verified as `done` with `evidence_posture: complete`.
- W5X-FREX-001 is verified as `done` with `evidence_posture: complete`; W5X-FINOPS remains `planned` with direct-lending operations proof artifacts now attached, while W6 and W7 remain `planned` with `evidence_posture: planned_evidence`.
- Broader domain rows in the design document are evidence-backed foundations, not independent completion claims.

## Verified Complete Items

- [x] `W1-DATA-001`: Provider trust gate and data confidence baseline.
  Evidence: `docs/reference/provider-validation-matrix.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W2-TRD-001`: Paper trading cockpit reliability.
  Evidence: `docs/testing/WAVE2_ACCEPTANCE_TESTS.md`, `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W2-PROMO-001`: Paper promotion evidence and operator acceptance.
  Evidence: `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W3-CONT-001`: Research-to-paper continuity.
  Evidence: `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W4-RECON-001`: Portfolio ledger reconciliation readiness.
  Evidence: `tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs`, `tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs`, `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W4-RPT-001`: Governed report pack readiness.
  Evidence: `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs`, `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs`, `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W5-ACCT-001`: Accounting records and operational evidence.
  Evidence: `tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs`, `tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs`, `src/Meridian.Ui/dashboard/src/screens/accounting-screen.test.tsx`, `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`.
- [x] `W5-MASSET-001`: Multi-asset operational coverage proof lane.
  Evidence: `tests/Meridian.Tests/SecurityMaster/SecurityMasterOperationalReadinessServiceTests.cs`, `tests/Meridian.Tests/Ui/WorkstationMultiAssetCoverageEndpointsTests.cs`, `tests/Meridian.Wpf.Tests/ViewModels/WorkspaceCockpitShellViewModelTests.cs`, `src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.test.ts`; roadmap status `done`; evidence posture `complete`.
- [x] `W5X-FREX-001`: Shared financial record explorers.
  Evidence: `tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs`, `tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs`, `src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.test.tsx`, `tests/fixtures/security-instrument-explorer-parity.json`; roadmap status `done`; evidence posture `complete`.

## Evidence-Backed Foundations Not Marked Complete

These are documented as implemented evidence, supported foundations, or design-led foundations in the design document. They should not be marked complete in this tracker until they have roadmap rows or acceptance evidence of their own.

- [ ] Data & Integration: map provider SDK, adapters, provider validation, credential/setup flows, source-module validation, and confidence gates to explicit owner evidence.
- [ ] Financial Operations: map reconciliation, casework, close, evidence routing, NAV-support posture, and fund-event accounting records to W5X-FINOPS acceptance evidence.
- [ ] Portfolio & Investment Operations: map portfolio, fund-structure, brokerage sync, fund accounts, positions, paper sessions, valuation evidence, and ledger-backed workflows to closed roadmap rows.
- [ ] Reference Data: map Security Master contracts, provider-to-security mapping, trust/conflict summaries, and multi-asset readiness coverage to explicit proof artifacts.
- [ ] Instrument, Contract & Obligation Management: map Security Master, direct-lending/F# rule kernels, factor/corporate-action evidence, and obligation ledger support to proof artifacts.
- [ ] Client & Stakeholder Reporting: keep W4 governed report-pack readiness checked only at baseline level; add separate evidence before claiming full stakeholder reporting completion.
- [ ] Administration & Governance: separate completed settings/policy/audit evidence from planned fund/book/period/report/delivery administration targets.
- [ ] Audit, Compliance & Regulatory: map audit events, evidence manifests, approval history, and close/report controls to acceptance tests before marking complete as a domain.
- [ ] Reporting & Analytics Platform: separate W4/W5 report-pack baselines from full reporting platform completion.

## W5X-FREX-001 Complete: Shared Financial Record Explorers

- [x] Define shared explorer contracts for scope bars, saved views, filters, summary strips, grids, drawers, proof ribbons, proof panels, column layouts, record graphs, `Used In`, `Impacts`, evidence links, approval state, reconciliation state, report usage, and audit timelines.
- [x] Implement shared read models in `src/Meridian.Contracts/`, `src/Meridian.Ui.Services/`, and `src/Meridian.Ui.Shared/` before browser/WPF surface work.
- [x] Build Ledger Explorer with Journal Entries and Ledger Detail views, core filters, saved views, journal drawer/detail routing, evidence links, approval posture, reversal-chain context, and report-usage drill-through.
- [x] Build Portfolio Explorer with Holdings and Transactions views, position drawer/detail routing, valuation state, reconciliation state, ledger-impact links, instrument links, evidence posture, and report usage.
- [x] Build Security & Instrument Explorer with instrument list, identifier map, terms/obligations, source conflicts, held positions, evidence links, valuation state, expected cash flows, and accounting classification.
- [x] Build Report-Line Provenance Explorer with report-line inputs, approved source records, reconciliations, journal impact, evidence packets, template/package versions, approvals, delivery history, restatements, and audit events.
- [x] Prove cross-explorer trail from Instrument to Position/Transaction to Reconciliation to Journal to Report Line to Evidence to Audit Event.
- [x] Add browser workstation tests for the explorer flow that consumes shared DTOs without browser-local readiness rules.
- [x] Add WPF workstation tests for the same explorer flow and verify parity with the browser read model.
- [x] Update roadmap evidence and generated docs before moving `W5X-FREX-001` from planned to complete.

## Next Feature Slice: Multi-Asset Reference-Data Workbench

- [ ] Complete the still-partial multi-asset reference-data workbench inside the existing Security Master detail flow.
- [ ] Keep the work anchored to the current Security Master detail/passport route and shared read models; do not create a new route for this slice.
- [ ] Extend the detail flow to cover multi-asset reference-data review, provider evidence, identifier confidence, terms/obligations, projected cash-flow readiness, ledger classification, and operations handoff from the retained Security Master context.
- [ ] Add focused endpoint, browser, and WPF proof only where the existing Security Master detail flow exposes the new workbench state.

## W5X-FINOPS-001 TODOs: Financial Operations Control Center

- [ ] Define FINOPS command-surface DTOs for reconciliation posture, exception aging, close checklist state, approval/workflow control, and audit evidence readiness.
- [ ] Build unified queue rows for reconciliation cases, breaks, assignments, escalations, approvals, close tasks, and evidence packets.
- [ ] Add deterministic status, owner, due date, severity, SLA, blocker type, and close/report impact fields to shared read models.
- [x] Add direct-lending endpoint parity for collateral, status transitions, PIK, restructures, and discount/premium amortization using `ViewDirectLending`/`ManageDirectLending` permissions.
- [x] Add a shared direct-lending operations read model for loan health, collateral coverage, covenant/status posture, servicing calendar, evidence, journals, reconciliation exceptions, close blockers, and servicer statement posture.
- [x] Wire direct-lending operations into existing Security & Instrument Explorer and Financial Operations shared read surfaces before introducing any new route.
- [x] Expand WPF DirectLending from accrual-focused review into dense operations panels for collateral, status, exceptions, close blockers, and evidence.
- [x] Triage direct-lending residuals with focused tests for closed-period originating postings, prepayment penalty replay/outbox idempotency, and portfolio endpoint authorization.
- [ ] Implement close support with period state, lock/reopen posture, NAV-support dependencies, report-pack dependencies, unresolved exceptions, required approvals, and retained evidence gaps.
- [ ] Ensure no FINOPS surface can show synthetic completion when required evidence, approvals, or lock state are missing.
- [ ] Route assignment, escalation, approval, reopen, and evidence-retention actions through shared services.
- [ ] Add browser tests for FINOPS queue, close blockers, approval requirements, and blocked completion behavior.
- [ ] Add WPF tests for dense workpaper execution against the same FINOPS DTOs and service decisions.
- [ ] Update generated roadmap/product docs to state exactly what shipped before moving `W5X-FINOPS-001` out of planned.

Direct-lending evidence produced for this FINOPS slice:

- `src/Meridian.Ui.Shared/Services/DirectLendingOperationsReadService.cs`
- `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs`
- `src/Meridian.Ui.Shared/Services/FinancialRecordExplorerReadService.cs`
- `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`
- `src/Meridian.Wpf/ViewModels/DirectLendingViewModel.cs`
- `src/Meridian.Wpf/Views/DirectLendingPage.xaml`
- `tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs`
- `tests/Meridian.Tests/Application/DirectLending/PostgresDirectLendingCommandServiceTests.cs`
- `tests/Meridian.Tests/Application/DirectLending/DirectLendingOutboxDispatcherTests.cs`
- `tests/Meridian.Wpf.Tests/ViewModels/DirectLendingViewModelTests.cs`

## W6 TODOs: Backtesting Studio Evidence Loop

- [ ] Define the narrow W6 acceptance boundary: backtesting results must link to strategy lineage and operator-facing acceptance criteria.
- [ ] Connect backtest results to retained evidence, accounting records, approvals, paper-validation lineage, or governed reporting before building broader research workbench features.
- [ ] Add roadmap evidence paths for W6 tests or generated artifacts before changing status from planned.
- [ ] Keep broad Backtesting Studio UX deferred unless it strengthens the W1-W5 operational record baseline.

## W7 TODOs: Live-Readiness Governance

- [ ] Define explicit live-readiness gates for trusted data, paper validation, reconciliation, approvals, accounting records, governed reporting evidence, and governance sign-off.
- [ ] Keep live action surfaces paper-first until all readiness gates are green.
- [ ] Add evidence for sign-off, exception handling, rollback/kill-switch posture, and audit retention before claiming live-readiness completion.
- [ ] Avoid live execution productization until W7 acceptance evidence exists.


## Codex Memory Inventory Pass: 2026-06-22

Reviewed files: `.codex/memory/README.md`, `.codex/memory/index.yml`, `.codex/memory/repo/*.md`, `.codex/memory/tasks/*.yml`, `.codex/memory/goals/*.yml`, `docs/ai/codex/memory-system.md`, `build/scripts/docs/check-codex-memory.py`, and `build/scripts/docs/tests/test_check_codex_memory.py`.

Classification key: already implemented; partially implemented; missing; implemented but not validated; implemented but not integrated into workflow guidance.

| Proposed feature | Classification | Evidence and follow-up |
| --- | --- | --- |
| Repo-local memory layout with README, index, repo entries, task descriptors, goal inventories, branch/session/archive folders, and disabled user/global tiers. | Already implemented. | The storage contract and root README define the layout; the index declares active tiers plus disabled `user` and `global`; the checker rejects disabled tiers. No follow-up. |
| Source-backed indexed Markdown entries with front matter parity, required metadata, source references, freshness, review dates, invalidation triggers, and unindexed-file detection. | Already implemented. | The index and repo entries carry the metadata; the checker validates required fields, front matter/index parity, source refs, stale review dates, duplicate IDs, and unindexed files; tests cover the core failures. No follow-up. |
| Selective routing by task descriptor, intent, selected skill, work mode, planned path, branch, explicit tag, and negative `exclude_when` selectors. | Already implemented. | The memory contract documents those selectors; the index includes them; the checker builds routing contexts and decisions from task, skill, intent, branch, path, and tag inputs, then applies exclusion reasons; tests cover path/tag selection, task selection, generic-tag behavior, routing explanations, and `exclude_when`. No follow-up. |
| Compact memory receipt with referenced and dereferenced entries, stale warnings, and task/goal context. | Already implemented. | The contract requires a receipt; the checker builds and prints `memory_receipt`; tests verify referenced/dereferenced counts and output. No follow-up. |
| Task descriptor routing under `.codex/memory/tasks/*.yml`, including scoped task memory and no cross-task leakage. | Already implemented. | The example descriptor exists; the checker validates descriptor shape, loads descriptors only from `tasks/`, and marks task-scope mismatches as skipped; tests verify matching task memory plus skipped conflicting task memory. No follow-up. |
| Goal inventories under `.codex/memory/goals/*.yml` for long Codex work, routing through the active task descriptor, and progress recording through `--record-goal-progress`. | Implemented but not validated. | The contract, example inventory, checker validation, routing, and progress mutation are present, and tests cover goal routing plus progress writes. However, the seeded example inventory still has `long-goal-inventory` marked `in_progress`, so this pass should not classify the feature as fully accepted until the live inventory is advanced after a real validation checkpoint. Follow-up: complete or defer that progress item with evidence. |
| Promotion workflow from session memory to task/branch/repo memory, including dry-run promotion, explicit apply, source-backed repo promotion, optional archive-source, and write-stub scaffolding. | Partially implemented. | The contract documents promotion rules and the checker exposes `--write-stub` and `--promote-session`; tests cover write-stub guardrails, but there is no focused test covering successful or failed `--promote-session`, archive-source output, or promotion metadata parity. Follow-up: add focused promotion tests before relying on it as a fully validated workflow. |
| Branch-tier and session-tier memory lifecycle. | Partially implemented. | The index schema, layout folders, checker tier/scope validation, and branch selector routing exist. There are no seeded branch/session entries and no focused tests for branch-tier selection or session-tier behavior beyond promotion/write paths. Follow-up: add a minimal branch/session fixture when the first real branch/session memory is introduced. |
| Archive-tier memory lifecycle and active-route exclusion. | Partially implemented. | The layout and checker support archive entries, promotion can archive source session memory, and routing skips archive entries when filters are present. There is no seeded archive entry and no focused test for archive routing or archive-source promotion. Follow-up: add an archive fixture/test when archive memory is first used. |
| User/global memory opt-in. | Missing. | The contract intentionally keeps user/global disabled and the checker rejects disabled tiers. This is not a current implementation gap unless a future opt-in design is accepted. |
| Workflow guidance integration for memory startup, receipts, long-goal progress, validation commands, and mirrored assistant surfaces. | Already implemented. | The root and Codex AGENTS guidance, quickstart, Codex workflow index, shared execution contract, and memory contract all route memory-aware tasks through the index, descriptors, goal inventories, receipts, and `check-codex-memory.py` validation. No follow-up. |
| Checker validation coverage for the full memory contract. | Partially implemented. | Focused tests cover index presence, duplicate IDs, missing files/front matter/source refs, disabled tiers, stale review dates, unindexed files, path/tag/task routing, `exclude_when`, explain/json output, goal routing, receipts, goal progress writes, and write-stub safety. Missing focused tests remain for successful promotion, archive-source promotion, branch-tier selection, archive routing, CLI argument validation for promotion metadata, and source-ref parity after promotion. |

## Deferred Expansion TODOs

- [ ] Treasury payments: design native live payment execution, bank release automation, return/reversal evidence, payment processor orchestration, and payment approval proof before implementation.
- [ ] Treasury payments: add tests for payment request, approval, expected cash movement, bank confirmation, reconciliation, reversal evidence, and audit linkage.
- [ ] Alternative asset operations: define the minimum asset classes and provider/source evidence needed beyond current structured/private coverage rows.
- [ ] Enterprise risk: define stress/scenario, independent risk cockpit, cross-portfolio governance, breach acceptance, and evidence-retention requirements.
- [ ] Forecasting: define the forecasting engine boundary, scenario inputs, retained evidence model, budget/cash/close/report links, and acceptance tests.
- [ ] Capital structure modeling: define debt/equity waterfall, commitment, obligation, covenant, and financing-event evidence requirements.
- [ ] Client portal: keep broad self-service portal deferred; first define entitlement, recipient approval, delivery evidence, request history, amendment, and restatement gates.
- [ ] No-code workflow designer: define policy-safe workflow configuration boundaries, approval rules, versioning, test cases, and activation controls before UI design.
- [ ] Document vault: define request lists, immutable manifests, extracted-field review, document-to-object links, retention policy, and audit evidence before marking as complete.
- [ ] Collaboration: define operator comments, assignments, waiting-on-evidence state, waiting-on-approval state, escalation history, and audit retention beyond current workflow queue support.
- [ ] Mobile: keep native iOS/Android, MAUI, React Native, Flutter, and mobile-first workflows closed unless roadmap explicitly reopens the lane.

