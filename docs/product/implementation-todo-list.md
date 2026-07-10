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
- W5X-FREX-001, W5X-FINOPS-001, and bounded W7-LIVE-001 governance are verified as `done` with `evidence_posture: complete`; W6 remains `planned` with `evidence_posture: planned_evidence`.
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
- [x] `W5X-FINOPS-001`: Financial operations control center.
  Evidence: `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs`, `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperationsContinuity.cs`, `src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.view-model.ts`, `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs`, `tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs`, `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs`, `tests/Meridian.Tests/Ui/WorkstationWorkflowSummaryFinancialOperationsTests.cs`, `src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.test.tsx`, `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts`, `tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs`, `tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs`, and `tests/Meridian.Wpf.Tests/ViewModels/DirectLendingViewModelTests.cs`; roadmap status `done`; evidence posture `complete`.
- [x] `W7-LIVE-001`: Live-readiness governance.
  Evidence: `src/Meridian.Strategies/Promotions/PromotionApprovalChecklist.cs`, `src/Meridian.Strategies/Services/PromotionService.cs`, `src/Meridian.Execution/Services/ExecutionOperatorControlService.cs`, `tests/Meridian.Tests/Strategies/PromotionServiceTests.cs`, `tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs`, and `docs/roadmap/generated/ROADMAP_SUMMARY.md`; roadmap status `done`; evidence posture `complete`. This is a bounded governance gate, not broader live execution productization or live portfolio operations.

## Evidence-Backed Foundations Not Marked Complete

These are documented as implemented evidence, supported foundations, or design-led foundations in the design document. They should not be marked complete in this tracker until they have roadmap rows or acceptance evidence of their own. Each item below now carries that mapping: a checked item links to one or more roadmap rows with `status: done` and `evidence_posture: complete` plus source and test artifacts that exist in the current checkout. Baseline-scoped foundations (stakeholder reporting, reporting/analytics platform) state explicitly that their checked status covers the W4/W5 baseline only and that full-domain completion remains deferred to its own evidence slice.

- [x] Data & Integration: map provider SDK, adapters, provider validation, credential/setup flows, source-module validation, and confidence gates to explicit owner evidence.
  Evidence: provider SDK and confidence gating are owned by `src/Meridian.ProviderSdk/IDataSource.cs`, `src/Meridian.ProviderSdk/DataSourceAttribute.cs`, and `src/Meridian.ProviderSdk/CredentialValidator.cs`; adapter discovery, setup orchestration, and data-quality gates by `src/Meridian.Infrastructure/Adapters/Core/ProviderRegistry.cs`, `src/Meridian.Application/ProviderRouting/ProviderSetupService.cs`, and `src/Meridian.Infrastructure/Adapters/Core/ProviderDataQualityValidator.cs`; proven by `tests/Meridian.Tests/Contracts/ProviderIntegrationContractsTests.cs`, `tests/Meridian.Tests/ProviderSdk/CredentialValidatorTests.cs`, and `tests/Meridian.Tests/Infrastructure/Providers/ProviderDataQualityValidatorTests.cs`; source-module validation is owned by the source-module registry `docs/source/data/source-modules.yml` (covering `SRC-HOST`, `SRC-APP`, and `SRC-CONTRACTS`), enforced by `build/scripts/docs/validate-design-module-conformance.py`, and traced to roadmap rows in `docs/source/generated/source-roadmap-traceability.md`; trust-gate baseline recorded in `docs/reference/provider-validation-matrix.md`. Roadmap row `W1-DATA-001` is `done` with evidence posture `complete`.
- [x] Financial Operations: map reconciliation, casework, close, evidence routing, NAV-support posture, and fund-event accounting records to W5X-FINOPS acceptance evidence.
  Evidence: `W5X-FINOPS-001` is now closed through the shared Operations Continuity lifecycle, close-readiness, approval-policy, close-calendar, break assignment/resolution, checklist, audit-evidence, and governed reopen controls; browser Operations Continuity and WPF Fund Ledger consume the shared state instead of local completion rules.
- [x] Portfolio & Investment Operations: map portfolio, fund-structure, brokerage sync, fund accounts, positions, paper sessions, valuation evidence, and ledger-backed workflows to closed roadmap rows.
  Evidence: portfolio-ledger status, fund accounts, fund-ledger valuation, paper sessions, and brokerage sync are owned by `src/Meridian.Ui.Shared/Services/PortfolioLedgerWorkflowStatusService.cs`, `src/Meridian.PortfolioRecords/FundAccounts/PostgresFundAccountService.cs`, `src/Meridian.Ledger/FundLedgerBook.cs`, `src/Meridian.Execution/Services/PaperTradingPortfolio.cs`, and `src/Meridian.Ui.Shared/Services/BrokeragePortfolioSyncService.cs`; proven by `tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs`, `tests/Meridian.Tests/PortfolioRecords/FundAccounts/FundAccountServiceTests.cs`, and `src/Meridian.Ui/dashboard/src/screens/accounting-screen.test.tsx`. Closed roadmap rows: `W2-TRD-001`, `W3-CONT-001`, `W4-RECON-001`, `W5-ACCT-001`, and `W5-MASSET-001` (all `done`, evidence posture `complete`).
- [x] Reference Data: map Security Master contracts, provider-to-security mapping, trust/conflict summaries, and multi-asset readiness coverage to explicit proof artifacts.
  Evidence: Security Master contracts and identifier mapping are owned by `src/Meridian.Contracts/SecurityMaster/SecurityIdentifiers.cs` and `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`; trust/conflict summaries by `src/Meridian.Application/SecurityMaster/SecurityMasterConflictService.cs` and `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs`; multi-asset readiness by `src/Meridian.Application/SecurityMaster/SecurityMasterOperationalReadinessService.cs`; proven by `tests/Meridian.Tests/SecurityMaster/SecurityMasterOperationalReadinessServiceTests.cs` and `tests/Meridian.Tests/Ui/WorkstationMultiAssetCoverageEndpointsTests.cs`. Closed roadmap rows: `W5-MASSET-001` and `W5X-FREX-001` (both `done`, evidence posture `complete`).
- [x] Instrument, Contract & Obligation Management: map Security Master, direct-lending/F# rule kernels, factor/corporate-action evidence, and obligation ledger support to proof artifacts.
  Evidence: instrument/obligation contracts and direct-lending endpoints are owned by `src/Meridian.Contracts/DirectLending/DirectLendingDtos.cs` and `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs`; F# rule kernels by `src/Meridian.FSharp.DirectLending.Aggregates/ContractAggregate.fs` and `src/Meridian.FSharp/Interop.DirectLending.fs`; obligation-ledger posting by `src/Meridian.Application/DirectLending/AccrualLedgerService.cs`; corporate-action evidence by `src/Meridian.Contracts/SecurityMaster/SecurityMasterCorporateActions.cs`; proven by `tests/Meridian.FSharp.Tests/DirectLendingInteropTests.fs`, `tests/Meridian.DirectLending.Tests/DirectLendingServiceTests.cs`, and `tests/Meridian.Tests/SecurityMaster/SecurityMasterCorporateActionCommandServiceTests.cs`. Closed roadmap rows: `W5-MASSET-001`, `W5X-FREX-001`, and `W5X-FINOPS-001` (all `done`, evidence posture `complete`).
- [x] Client & Stakeholder Reporting: keep W4 governed report-pack readiness checked only at baseline level; add separate evidence before claiming full stakeholder reporting completion.
  Evidence (baseline only): governed report-pack lifecycle, template governance, provenance, workflow, and approval persistence are owned by `src/Meridian.Ledger/LedgerReportPackLifecycle.cs`, `src/Meridian.Ui.Shared/Services/GovernedReportingTemplateCatalog.cs`, `src/Meridian.Ui.Shared/Services/LedgerAmountProvenanceService.cs`, `src/Meridian.Ui.Shared/Services/ReportingWorkflowService.cs`, and `src/Meridian.Ui.Shared/Services/GovernanceReportPackRepository.cs`; proven by `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs` and `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs`. This checked status covers the `W4-RPT-001` baseline (`done`, evidence posture `complete`) only; full stakeholder reporting (entitlement, recipient approval, delivery evidence, request history, amendment, restatement) remains deferred to its own evidence slice in the Client portal Deferred Expansion row.
- [x] Administration & Governance: separate completed settings/policy/audit evidence from planned fund/book/period/report/delivery administration targets.
  Evidence (completed settings/policy/audit only): settings administration endpoints by `src/Meridian.Ui.Shared/Endpoints/AdminEndpoints.cs`; approval-policy administration by `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs`; audit infrastructure by `src/Meridian.Storage/Services/AuditChainService.cs` and `src/Meridian.Execution/Services/ExecutionAuditTrailService.cs`; proven by `tests/Meridian.Tests/Integration/EndpointTests/AdminEndpointPermissionTests.cs`, `tests/Meridian.Ui.Tests/Services/SettingsConfigurationServiceTests.cs`, and `tests/Meridian.Tests/Execution/ExecutionAuditTrailServiceTests.cs`. Anchored to `W5X-FINOPS-001` (`done`, evidence posture `complete`); planned fund/book/period/report/delivery administration dashboards remain unbuilt and stay tracked as an explicit unchecked row under Deferred Expansion TODOs (`Administration dashboards`).
- [x] Audit, Compliance & Regulatory: map audit events, evidence manifests, approval history, and close/report controls to acceptance tests before marking complete as a domain.
  Evidence: audit events and compliance models by `src/Meridian.Audit/Compliance/ComplianceModels.cs` and `src/Meridian.Execution/Services/ExecutionAuditTrailService.cs`; tamper-evident chaining by `src/Meridian.Storage/Services/AuditChainService.cs`; evidence manifests by `src/Meridian.Ui.Shared/Evidence/EvidencePacketValidationService.cs`; approval history and audit timeline by `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs` and `src/Meridian.Ui.Shared/Services/AuditTrailExplorerService.cs`; close/report controls by `src/Meridian.FinancialOperations/AccountingClose/AccountingCloseManagementService.cs`; proven by `tests/Meridian.Tests/Compliance/CompliancePolicyEngineTests.cs`, `tests/Meridian.Tests/Storage/AuditChainServiceTests.cs`, and `tests/Meridian.Tests/Execution/ExecutionAuditTrailServiceTests.cs`. Closed roadmap rows: `W4-RECON-001`, `W5-ACCT-001`, and `W5X-FINOPS-001` (all `done`, evidence posture `complete`).
- [x] Reporting & Analytics Platform: separate W4/W5 report-pack baselines from full reporting platform completion.
  Evidence (W4/W5 baseline only): report-pack read models and orchestration by `src/Meridian.Ui.Shared/Services/ReportPackRunReadService.cs` and `src/Meridian.Reporting/ReportingOrchestrationService.cs`; governed lifecycle by `src/Meridian.Ledger/LedgerReportPackLifecycle.cs`; browser reporting surface and report-line provenance by `src/Meridian.Ui/dashboard/src/screens/reporting-screen.tsx` and `src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.tsx`; proven by `tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs` and `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs`. This checked status covers the `W4-RPT-001` and `W5-ACCT-001` report-pack baselines (both `done`, evidence posture `complete`) only; a full reporting/analytics platform remains deferred and stays tracked as an explicit unchecked row under Deferred Expansion TODOs (`Reporting and analytics platform`).

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

## Multi-Asset Reference-Data Workbench Complete: Security Master Detail/Passport Flow

- [x] Complete the multi-asset reference-data workbench inside the existing Security Master detail flow.
  Evidence: the instrument passport read model is built by `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.cs` (`BuildInstrumentPassportAsync` and `BuildReferenceDataWorkbench`) and exposed as `InstrumentPassportDto` in `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs`; the browser Security Master detail flow (`src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts`) and the WPF detail flow (`src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs`) render the same shared workbench sections. Anchored to roadmap row `W5-MASSET-001` (`done`, evidence posture `complete`), whose exit criteria name this follow-on slice.
- [x] Keep the work anchored to the current Security Master detail/passport route and shared read models; do not create a new route for this slice.
  Evidence: all workbench state is delivered inside `InstrumentPassportDto` on the pre-existing `UiApiRoutes.WorkstationSecurityMasterInstrumentPassport` route (`/api/workstation/security-master/securities/{securityId:guid}/passport`, `src/Meridian.Contracts/Api/UiApiRoutes.cs`) served by `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`; the browser client (`getSecurityInstrumentPassport` in `src/Meridian.Ui/dashboard/src/lib/api.ts`) and the WPF client (`src/Meridian.Wpf/Services/WorkstationSecurityMasterApiClient.cs`) call the same endpoint and consume the same DTO without client-local readiness rules.
- [x] Extend the detail flow to cover multi-asset reference-data review, provider evidence, identifier confidence, terms/obligations, projected cash-flow readiness, ledger classification, and operations handoff from the retained Security Master context.
  Evidence: `BuildReferenceDataWorkbench` in `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.cs` emits the `provider-evidence`, `identifier-confidence`, `terms-obligations`, `cash-flow-readiness`, `ledger-classification`, and `operations-handoff` sections, and the companion operations workbench emits identity/provider-evidence/terms/operations-readiness/handoff panels with valuation, reconciliation, ledger, close, and report readiness plus owner/blocker/impacted-output handoff rows — all sourced from retained Security Master state (`InstrumentPassportReferenceDataWorkbenchDto`, `InstrumentPassportOperationsWorkbenchDto`, `InstrumentPassportClassificationProfileDto`, and `InstrumentPassportProviderConfidenceDto` in `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs`).
- [x] Add focused endpoint, browser, and WPF proof only where the existing Security Master detail flow exposes the new workbench state.
  Evidence: endpoint proof in `tests/Meridian.Tests/Ui/SecurityMasterInstrumentPassportTests.cs` (asserts all six workbench section ids, provider-evidence and ledger-classification summaries, operations panels, readiness rows, and handoff completeness); browser proof in `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.test.ts` (reference-data workbench and operations workbench rendering; 43 tests passing on 2026-07-04); WPF proof in `tests/Meridian.Wpf.Tests/ViewModels/SecurityMasterViewModelTests.cs` (instrument passport fields including `Multi-asset reference-data workbench` and `Operations handoff`).
- [x] Closed-period propagation: replace the no-op restatement candidate resolver with a report-pack-backed `ReportPackRestatementCandidateResolver` that locates the published packs which consumed the edited security (from retained report-line provenance) and surfaces them as governed restatement candidates, so a closed-period reference-data edit no longer always degrades to a manual locate-affected-packs task. Remaining: soft-closed governed-adjustment posting, period-precise candidate narrowing, and a durable security→report-line index.
- [x] Passport governed-write workbench foundation: the write-surface slice now has shared command DTOs, source-generated JSON metadata, workbench route constants/endpoints, conflict-authority policy, field edit → submit → approve → publish lifecycle services, browser/WPF editor entry points, report-pack-backed restatement candidates, and `SecurityMasterWorkbench` configuration defaults, tracked in `docs/plans/security-master-passport-workbench.md`. Remaining follow-ons stay explicit: soft-closed governed-adjustment posting, repeated-restatement workflow support, period-precise candidate narrowing, a durable security→report-line index, and a single closed-period no-mutation lifecycle integration test.

## W5X-FINOPS-001 TODOs: Financial Operations Control Center

- [x] Define FINOPS command-surface DTOs for reconciliation posture, exception aging, close checklist state, approval/workflow control, and audit evidence readiness.
- [x] Build unified queue rows for reconciliation cases, breaks, assignments, escalations, approvals, close tasks, and evidence packets.
- [x] Add deterministic status, owner, due date, severity, SLA, blocker type, and close/report impact fields to shared read models.
- [x] Implement close support with period state, lock/reopen posture, NAV-support dependencies, report-pack dependencies, unresolved exceptions, required approvals, and retained evidence gaps.
- [x] Ensure no FINOPS surface can show synthetic completion when required evidence, approvals, or lock state are missing.
- [x] Route assignment, escalation, approval, reopen, and evidence-retention actions through shared services.
- [x] Add browser tests for FINOPS queue, close blockers, approval requirements, and blocked completion behavior.
- [x] Add WPF tests for dense workpaper execution against the same FINOPS DTOs and service decisions.
- [x] Update generated roadmap/product docs to state exactly what shipped before moving `W5X-FINOPS-001` out of planned.

Acceptance evidence produced for this FINOPS slice:

- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs`
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs`
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsCloseCalendarService.cs`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.OperationsContinuity.cs`
- `src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.view-model.ts`
- `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs`
- `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.Sections.cs`
- `src/Meridian.Wpf/Views/FundLedgerPage.xaml`
- `src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs`
- `src/Meridian.Wpf/ViewModels/DirectLendingViewModel.cs`
- `src/Meridian.Wpf/Views/DirectLendingPage.xaml`
- `tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs`
- `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs`
- `tests/Meridian.Tests/Ui/WorkstationWorkflowSummaryFinancialOperationsTests.cs`
- `src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.test.tsx`
- `src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts`
- `tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs`
- `tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs`
- `tests/Meridian.Wpf.Tests/ViewModels/DirectLendingViewModelTests.cs`
- `docs/roadmap/data/roadmap-items.yml`
- `docs/roadmap/generated/roadmap-register.md`

## W6 TODOs: Backtesting Studio Evidence Loop

- [ ] Define the narrow W6 acceptance boundary: backtesting results must link to strategy lineage and operator-facing acceptance criteria.
- [ ] Connect backtest results to retained evidence, accounting records, approvals, paper-validation lineage, or governed reporting before building broader research workbench features.
- [ ] Add roadmap evidence paths for W6 tests or generated artifacts before changing status from planned.
- [ ] Keep broad Backtesting Studio UX deferred unless it strengthens the W1-W5 operational record baseline.

## W7 TODOs: Live-Readiness Governance

- [x] Define explicit live-readiness gates for trusted data, paper validation, reconciliation, approvals, accounting records, governed reporting evidence, and governance sign-off.
  Evidence: `PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)` requires the paper baseline plus `PAPER_VALIDATION_REVIEWED`, `RECONCILIATION_EVIDENCE_REVIEWED`, `ACCOUNTING_RECORDS_REVIEWED`, `GOVERNED_REPORTING_REVIEWED`, and `GOVERNANCE_SIGNOFF_REVIEWED`; `PromotionService.GetMissingLiveEvidenceRequirements` rejects missing evidence references before creating a live run.
- [x] Keep live action surfaces paper-first until all readiness gates are green.
  Evidence: `PromotionService.EvaluateAsync` targets live mode only from a completed paper run, blocks live promotion when brokerage configuration is not live-enabled or still paper, and requires operator controls plus an `AllowLivePromotion` manual override; `ExecutionOperatorControlService.EvaluateLivePromotion` blocks promotion while the circuit breaker is open or the override is missing or inactive.
- [x] Add evidence for sign-off, exception handling, rollback/kill-switch posture, and audit retention before claiming live-readiness completion.
  Evidence: `PromotionApprovalChecklist.CreateRequiredFor(RunType.Live)` includes `GOVERNANCE_SIGNOFF_REVIEWED`, `EXCEPTION_HANDLING_REVIEWED`, `ROLLBACK_KILL_SWITCH_REVIEWED`, and `AUDIT_RETENTION_REVIEWED`; `PromotionServiceLiveGovernanceTests` proves missing evidence fails closed, approved live promotions write promotion audit metadata, and durable promotion history survives restart.
- [x] Avoid live execution productization until W7 acceptance evidence exists.
  Evidence: `W7-LIVE-001` closes only the bounded governance row; the roadmap summary and design document keep broader live execution productization and live portfolio operations outside this completion claim.


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
- [x] Alternative asset operations: define and implement the minimum first-class asset classes and provider/source evidence needed beyond current structured/private coverage rows.
  Evidence: `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`, and retained `DirectLoan` are covered by `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterOperationalReadinessService.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`, and `src/Meridian.FSharp/Domain/SecurityMaster.fs`; proven by `tests/Meridian.Tests/SecurityMaster/SecurityMasterOperationalReadinessServiceTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityMasterAssetClassSupportTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs`, `tests/Meridian.Tests/Ui/WorkstationMultiAssetCoverageEndpointsTests.cs`, `tests/Meridian.FSharp.Tests/DomainTests.fs`, and `src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.test.ts`. Roadmap row `W5-MASSET-001` remains `done` with evidence posture `complete`.
- [ ] Enterprise risk: define stress/scenario, independent risk cockpit, cross-portfolio governance, breach acceptance, and evidence-retention requirements.
- [ ] Forecasting: define the forecasting engine boundary, scenario inputs, retained evidence model, budget/cash/close/report links, and acceptance tests.
- [ ] Capital structure modeling: define debt/equity waterfall, commitment, obligation, covenant, and financing-event evidence requirements.
- [ ] Administration dashboards: build the planned fund, book, period, report, and delivery administration surfaces beyond the completed settings/policy/audit evidence; define their managed state, approval gates, and acceptance evidence before marking complete.
- [ ] Reporting and analytics platform: build the full reporting and analytics platform beyond the W4/W5 governed report-pack baselines; define analytics surfaces, datasets, scheduling, distribution, and acceptance evidence before marking complete.
- [ ] Client portal: keep broad self-service portal deferred; first define entitlement, recipient approval, delivery evidence, request history, amendment, and restatement gates.
- [ ] No-code workflow designer: define policy-safe workflow configuration boundaries, approval rules, versioning, test cases, and activation controls before UI design.
- [ ] Evidence Vault productization: active browser-first implementation is tracked by `W5X-EVIDENCE-001`; statement reconciliation onboarding is the first acceptance path, while broader document portal/collaboration expansion remains deferred.
- [ ] Statement reconciliation onboarding wedge: active browser-first implementation is tracked by `W5X-STMT-ONBOARD-001`; WPF UI parity is omitted from this v1 slice.
- [ ] Collaboration: define operator comments, assignments, waiting-on-evidence state, waiting-on-approval state, escalation history, and audit retention beyond current workflow queue support.
- [ ] Mobile: keep native iOS/Android, MAUI, React Native, Flutter, and mobile-first workflows closed unless roadmap explicitly reopens the lane.
