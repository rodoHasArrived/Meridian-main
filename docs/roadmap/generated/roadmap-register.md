<!--
generated: true
generator: build/scripts/docs/render-roadmap-docs.py
generator_version: 3.0.0
render_contract: meridian.generated-docs.v1
schema_versions:
  - meridian.roadmap-items@2.0.0
inputs:
  - docs/roadmap/data/decision-log.yml
  - docs/roadmap/data/document-index.yml
  - docs/roadmap/data/production-readiness.yml
  - docs/roadmap/data/program-state.yml
  - docs/roadmap/data/risk-register.yml
  - docs/roadmap/data/roadmap-items.yml
  - docs/roadmap/data/stage-gates.yml
do_not_edit: true
-->

# Roadmap Register

Snapshot date: 2026-11-12

## W1-DATA-001 - Provider trust gate and data confidence baseline
| Field | Value |
| --- | --- |
| Wave | W1 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | complete |
| Last reviewed | 2026-09-28 |

### Current Summary

W1-DATA-001: done; health green; evidence complete. Provider trust evidence is bounded; adapter implementation readiness does not confer live-provider certification or replace operator sign-off.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W1-DATA-001.md)
- Evidence: [docs/reference/provider-validation-matrix.md](../../reference/provider-validation-matrix.md)
- Evidence: [docs/source/data/adapter-readiness.yml](../../source/data/adapter-readiness.yml)
- Evidence: [docs/source/generated/adapter-readiness-matrix.md](../../source/generated/adapter-readiness-matrix.md)


### Exit Criteria

- Provider parity packet exists and is linked from reference documentation.
- Operator sign-off evidence is available for DK1 readiness.
- Provider validation matrix remains in `docs/reference/provider-validation-matrix.md`.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-INFRASTRUCTURE`

## W2-PROMO-001 - Paper promotion evidence and operator acceptance
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

W2-PROMO-001: done; health green; evidence complete. Closed with W2 acceptance evidence; PaperPromotion is green in the 2026-05-27 pilot readiness run and promotion review remains the governed handoff into W4 casework/reporting work.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W2-PROMO-001.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)


### Exit Criteria

- Promotion candidates show evidence lineage before acceptance.
- Operator approval records link to the paper-session context.
- Follow-up TODOs are registry-backed and assigned.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W2-TRD-001 - Paper trading cockpit reliability
| Field | Value |
| --- | --- |
| Wave | W2 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

W2-TRD-001: done; health green; evidence complete. Closed in the 2026-05-27 evidence slice through shared readiness/operator-inbox tests, browser Trading parity, focused WPF Lane A tests, and green TrustedData, PaperPromotion, and PaperSession pilot gates.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W2-TRD-001.md)
- Evidence: [docs/testing/WAVE2_ACCEPTANCE_TESTS.md](../../testing/WAVE2_ACCEPTANCE_TESTS.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)


### Exit Criteria

- Trading readiness endpoint works for global and account-scoped checks.
- Operator inbox exposes actionable readiness and reconciliation routing.
- Paper session replay verification remains durable across restart.
- Browser and WPF workstation surfaces show readiness posture through shared contracts where each surface participates.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W3-CONT-001 - Research to paper continuity
| Field | Value |
| --- | --- |
| Wave | W3 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Strategy Analytics |
| Evidence posture | complete |
| Last reviewed | 2026-05-28 |

### Current Summary

W3-CONT-001: done; health green; evidence complete. Closed in the 2026-05-27 evidence slice through shared brokerage/continuity/pilot tests, focused WPF portfolio/accounting/cash-flow tests, browser route/API parity, and green ResearchRun, RunComparison, PortfolioLedgerReview, and Reconciliation pilot gates.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W3-CONT-001.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)


### Exit Criteria

- Research lineage persists through paper-session handoff.
- Strategy run evidence links to promotion candidates.
- Validation commands are documented in source READMEs.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`

## W4-RECON-001 - Portfolio ledger reconciliation readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-05-29 |

### Current Summary

W4-RECON-001: done; health green; evidence complete. Closed in the 2026-05-29 W4 evidence slice through operations-continuity close-lane coverage, reconciliation casework, browser Accounting parity, WPF Lane C acceptance, and green PortfolioLedgerReview/Reconciliation pilot gates.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W4-RECON-001.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)
- Evidence: [tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs](../../../tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs)


### Exit Criteria

- Reconciliation queue actions link to ledger evidence.
- Break resolution and sign-off states are operator-visible.
- Shared contracts remain compatible across browser and WPF workstation surfaces.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W4-RPT-001 - Governed report pack readiness
| Field | Value |
| --- | --- |
| Wave | W4 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-05-29 |

### Current Summary

W4-RPT-001: done; health green; evidence complete. Closed in the 2026-05-29 W4 evidence slice through governed report-pack workflow/provenance, publication/restatement readiness, evidence-vault manifest support, browser Reporting parity, and the green GovernedReportPack pilot gate.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W4-RPT-001.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)
- Evidence: [tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs](../../../tests/Meridian.Tests/Integration/EndpointTests/PilotAcceptanceHarnessTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs)


### Exit Criteria

- Report-pack lifecycle includes approval evidence.
- Export output records are linked to source data and acceptance status.
- Documentation states the operator value and validation commands.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-WPF`

## W5-ACCT-001 - Accounting records and operational evidence
| Field | Value |
| --- | --- |
| Wave | W5 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-06-04 |

### Current Summary

W5-ACCT-001: done; health green; evidence complete. Accounting records and six evidence categories use shared contracts and services across browser and WPF.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5-ACCT-001.md)
- Evidence: [docs/roadmap/generated/ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md)
- Evidence: [tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs](../../../tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs](../../../tests/Meridian.Tests/Ui/FundOpsCloseLaneScenarioTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.test.tsx)


### Exit Criteria

- Accounting record summaries show retained source data, normalized transactions or positions, reconciliation case history, ledger evidence, approvals, and report-pack links.
- Close-package status and audit/provenance timelines are operator-visible through shared read models consumed by browser and WPF surfaces where each surface participates.
- Report-pack exports and restatements retain source evidence, approval state, and publication provenance.
- Documentation identifies W1-W5 as the coherent operational record baseline and defers Backtesting Studio, live-readiness, payments, forecasting, enterprise risk, portal, workflow-designer, mobile, and other expansion lanes unless they directly strengthen that baseline.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5-MASSET-001 - Multi-asset operational coverage proof lane
| Field | Value |
| --- | --- |
| Wave | W5 |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-02 |

### Current Summary

W5-MASSET-001: done; health green; evidence complete. Shared multi-asset coverage projects validation, provider evidence, ledger classification, reconciliation and close blockers.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5-MASSET-001.md)
- Evidence: [tests/Meridian.Tests/SecurityMaster/SecurityMasterOperationalReadinessServiceTests.cs](../../../tests/Meridian.Tests/SecurityMaster/SecurityMasterOperationalReadinessServiceTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/SecurityMasterAssetClassSupportTests.cs](../../../tests/Meridian.Tests/SecurityMaster/SecurityMasterAssetClassSupportTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs](../../../tests/Meridian.Tests/SecurityMaster/SecurityValidationServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationMultiAssetCoverageEndpointsTests.cs](../../../tests/Meridian.Tests/Ui/WorkstationMultiAssetCoverageEndpointsTests.cs)
- Evidence: [tests/Meridian.FSharp.Tests/DomainTests.fs](../../../tests/Meridian.FSharp.Tests/DomainTests.fs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/WorkspaceCockpitShellViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/WorkspaceCockpitShellViewModelTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.test.ts](../../../src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.test.ts)


### Exit Criteria

- Equities, options, futures, FX, fixed income, loans, `StructuredCredit`, `PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`, structured/private `CustomAsset`, and `OtherSecurity` rows declare identifiers, economics, provider evidence, ledger classification, reconciliation signals, and close blockers.
- Structured credit, private fund, private company equity, real estate, and commitment/guarantee rows expose class-specific trustee/servicer, factor schedule, collateral tape, administrator/GP, capital call, distribution, NAV, capital account, cap table, valuation, appraisal, debt-service, fee/accrual, covenant, and release/expiry evidence targets.
- Missing retained provider data remains review-required or blocked evidence rather than fake completeness.
- Browser and WPF surfaces consume the shared DTO without client-local readiness rules.
- Follow-on multi-asset reference-data workbench work extends the existing Security Master detail/passport flow with provider evidence, identifier confidence, terms and obligations, projected cash-flow readiness, ledger classification, and operations handoff without introducing a new route.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-CONNECT-001 - Custodian and broker statement connector library
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-18 |

### Current Summary

W5X-CONNECT-001: done; health green; evidence complete. Connector delivery includes scheduled-fetch and brokerage-account evidence; source detail retains supported formats, diagnostic-only margin limits and certification gates.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-CONNECT-001.md)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementImportService.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementImportService.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementMappingProfileCatalog.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementMappingProfileCatalog.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/IbFlex/IbFlexStatementConnector.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/IbFlex/IbFlexStatementConnector.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/IbFlex/IbFlexWebServiceClient.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/IbFlex/IbFlexWebServiceClient.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/Ofx/OfxStatementConnector.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/Ofx/OfxStatementConnector.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/Alpaca/AlpacaActivityStatementConnector.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/Alpaca/AlpacaActivityStatementConnector.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.StatementConnectors.cs](../../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.StatementConnectors.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/MarginControlCenterReadService.cs](../../../src/Meridian.Ui.Shared/Services/MarginControlCenterReadService.cs)
- Evidence: [docs/adr/018-declarative-statement-mapping-profiles.md](../../adr/018-declarative-statement-mapping-profiles.md)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/StatementImportServiceTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/StatementImportServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/IbFlexStatementConnectorTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/IbFlexStatementConnectorTests.cs)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/IbFlexWebServiceClientTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/IbFlexWebServiceClientTests.cs)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/OfxStatementConnectorTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/OfxStatementConnectorTests.cs)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/AlpacaActivityStatementConnectorTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/AlpacaActivityStatementConnectorTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/statement-fetch-panel.tsx](../../../src/Meridian.Ui/dashboard/src/screens/statement-fetch-panel.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/statement-fetch-panel.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/statement-fetch-panel.test.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/margin-control-center-screen.tsx](../../../src/Meridian.Ui/dashboard/src/screens/margin-control-center-screen.tsx)
- Evidence: [tests/Meridian.Tests/Ui/MarginControlCenterReadServiceTests.cs](../../../tests/Meridian.Tests/Ui/MarginControlCenterReadServiceTests.cs)


### Exit Criteria

- Operators onboard a new custodian CSV or OFX layout by authoring a mapping profile document without a release.
- Statement imports preview detected columns with per-column mapping confidence and per-kind (position, transaction, cash, fee, dividend) record breakdowns before commit.
- Committed imports enter the existing reconciliation queue with retained raw and canonical evidence and duplicate-key idempotency.
- IB Flex XML, OFX bank and investment, Alpaca activity plus portfolio, and profile-driven CSV statements all normalize through one connector seam with golden-file regression coverage.
- Format drift against a profile's last accepted layout is surfaced as a warning before rows map incorrectly.
- Provider account margin, complete activity cursors, option lifecycle, tax-lot, and borrow evidence are retained without widening the legacy reconciliation CSV contract.
- Multi-account and multi-prime evidence rolls into a provider-authoritative Margin Control Center with diagnostic shadow calculations and permission-checked EOD certification.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W5X-EVIDENCE-001 - Evidence Vault productization
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

W5X-EVIDENCE-001: done; health green; evidence complete. Bounded browser-first Evidence Vault baseline; retained authority and immutable evidence boundaries remain applicable.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-EVIDENCE-001.md)
- Evidence: [src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs](../../../src/Meridian.Contracts/Workstation/EvidenceWorkflowDtos.cs)
- Evidence: [src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.cs](../../../src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.cs)
- Evidence: [src/Meridian.Ui.Shared/Evidence/ReportingStatementImportEvidenceRetainer.cs](../../../src/Meridian.Ui.Shared/Evidence/ReportingStatementImportEvidenceRetainer.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/evidence-workbench-screen.tsx](../../../src/Meridian.Ui/dashboard/src/screens/evidence-workbench-screen.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/app.tsx](../../../src/Meridian.Ui/dashboard/src/app.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/lib/workspace.test.ts](../../../src/Meridian.Ui/dashboard/src/lib/workspace.test.ts)
- Evidence: [tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs](../../../tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/Evidence/SecurityMasterAndVaultEvidenceContributorTests.cs](../../../tests/Meridian.Tests/Ui/Evidence/SecurityMasterAndVaultEvidenceContributorTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/StatementImportEvidenceBridgeTests.cs](../../../tests/Meridian.Tests/Ui/StatementImportEvidenceBridgeTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/StatementReconciliationProductionAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/StatementReconciliationProductionAuthorityTests.cs)


### Exit Criteria

- Evidence Vault retains imported source documents with immutable vault identity, manifest route, source hash, source record, reviewer state, and audit trail.
- Request-list and document-list queries can filter retained documents by subject, classification, review status, and object links.
- Document authority stays bounded to support, block, suggest, and link; documents cannot approve, post, certify, or release.
- The canonical browser Reporting Evidence Workbench route can deep-link into retained support for a selected subject; legacy Accounting and Data evidence routes redirect to that one owner surface without WPF-specific UI work.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W5X-FINOPS-001 - Financial operations control center
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-06-24 |

### Current Summary

W5X-FINOPS-001: done; health green; evidence complete. Financial Operations control-center delivery uses the shared governed continuity and close-readiness lifecycle.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-FINOPS-001.md)
- Evidence: [src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs](../../../src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs)
- Evidence: [src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs](../../../src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs)
- Evidence: [src/Meridian.FinancialOperations/OperationsContinuity/OperationsCloseCalendarService.cs](../../../src/Meridian.FinancialOperations/OperationsContinuity/OperationsCloseCalendarService.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.view-model.ts](../../../src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.view-model.ts)
- Evidence: [src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs](../../../src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/FundLedgerViewModel.Sections.cs](../../../src/Meridian.Wpf/ViewModels/FundLedgerViewModel.Sections.cs)
- Evidence: [src/Meridian.Wpf/Views/FundLedgerPage.xaml](../../../src/Meridian.Wpf/Views/FundLedgerPage.xaml)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/DirectLendingEndpoints.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/DirectLendingViewModel.cs](../../../src/Meridian.Wpf/ViewModels/DirectLendingViewModel.cs)
- Evidence: [src/Meridian.Wpf/Views/DirectLendingPage.xaml](../../../src/Meridian.Wpf/Views/DirectLendingPage.xaml)
- Evidence: [tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs](../../../tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.Wave4.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationWorkflowSummaryFinancialOperationsTests.cs](../../../tests/Meridian.Tests/Ui/WorkstationWorkflowSummaryFinancialOperationsTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/operations-continuity-screen.test.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts](../../../src/Meridian.Ui/dashboard/src/app-shell.view-model.test.ts)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/FundLedgerViewModelTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs](../../../tests/Meridian.Tests/Ui/DirectLendingEndpointsTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/DirectLendingViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/DirectLendingViewModelTests.cs)


### Exit Criteria

- Accounting workspace exposes a financial operations command surface that groups reconciliation posture, exception aging, close checklist state, approval/workflow control, and audit evidence readiness from shared read models.
- Reconciliation cases, breaks, assignments, escalations, approvals, close tasks, and evidence packets can be opened from a unified operator queue with deterministic status, owner, due date, and blocker signals.
- Close support shows period state, lock or reopen posture, NAV-support or report-pack dependencies, unresolved exceptions, required approvals, and retained evidence gaps without posting synthetic completion.
- Workflow controls expose assignment, escalation, approval, reopen, and evidence-retention actions through shared services so browser and WPF surfaces share the same policy decisions.
- Generated roadmap and product docs state the accepted Financial Operations control-center boundary and distinguish later proof-layer expansions from this completed W5X milestone.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-WORKFLOW`
- `SRC-DESIGN-AUDIT`
- `SRC-DESIGN-REPORTING`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-FREX-001 - Shared financial record explorers
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | complete |
| Last reviewed | 2026-06-22 |

### Current Summary

W5X-FREX-001: done; health green; evidence complete. Shared financial record explorers retain contract, API, saved-view and proof-action evidence.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-FREX-001.md)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs](../../../tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/FinancialRecordExplorerViewModelTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.test.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/financial-record-explorer.test.tsx)
- Evidence: [tests/fixtures/security-instrument-explorer-parity.json](../../../tests/fixtures/security-instrument-explorer-parity.json)


### Exit Criteria

- Shared explorer framework contracts and read models support scope bars, saved views, filters, summary strips, dense grids, record drawers, proof ribbons, proof panels, column layouts, record graphs, Used In, Impacts, evidence links, approval state, reconciliation state, report usage, and audit timelines without browser- or WPF-local business rules.
- Ledger Explorer exposes Journal Entries and Ledger Detail views with core filters, saved views, journal drawer and detail routing, evidence links, approval posture, reversal-chain context, and report-usage drill-through.
- Portfolio Explorer exposes Holdings and Transactions views with position drawer and detail routing, valuation status, reconciliation status, ledger-impact links, instrument links, evidence posture, and report usage.
- Security & Instrument Explorer exposes instrument list, identifier map, terms and obligations, source conflicts, held positions, evidence links, valuation status, expected cash flows, and accounting classification.
- Report-Line Provenance Explorer exposes report-line inputs, approved source records, reconciliations, journal impact, evidence packets, template and package versions, approvals, delivery history, restatements, and audit events.
- Cross-explorer Proof Trail can move from Instrument to Position or Transaction, Reconciliation, Journal, Report Line, Evidence, and Audit Event, and missing retained source evidence remains review-required or blocked rather than synthetic completeness.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-OEG-001 - Operational evidence graph product surface
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-06-15 |

### Current Summary

W5X-OEG-001: planned; health green; evidence planned_evidence. Planned shared evidence graph; implementation, both-workstation paths and acceptance evidence remain required.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-OEG-001.md)


### Exit Criteria

- Shared read models in SRC-UI-SHARED and service APIs in SRC-UI-SERVICES define stable subject identifiers, node identifiers, layer values, status values, warning codes, and typed evidence links for browser and WPF consumers without UI-local business rules.
- Compact proof ribbon, side proof drawer, full proof graph page, and exportable evidence manifest patterns are implemented from the same contracts with parity across browser and WPF or an explicit WPF parity plan recorded in implementation evidence.
- Ledger Explorer, Portfolio Explorer, Report-Line Provenance Explorer, Fund Event Command Center, and Evidence Vault can open graph context for their material records through stable deep links or routed actions.
- Evidence manifests include source hashes or vault references, graph schema version, generated timestamp, completeness state, validation warnings, reviewer or export attestation, and typed links from source through audit.
- Tests prove that missing required source, validation, reconciliation, ledger, close, reporting, delivery, or audit evidence remains review-required or blocked rather than being represented as complete.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the shared contracts, service endpoints, browser surface, WPF surface or parity plan, manifest export, and missing-evidence tests.

### Source Modules

- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W5X-STMT-ONBOARD-001 - Statement reconciliation onboarding wedge
| Field | Value |
| --- | --- |
| Wave | W5X |
| Status | done |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

W5X-STMT-ONBOARD-001: done; health green; evidence complete. Bounded browser-first statement onboarding reuses connector, immutable Reporting authority and reconciliation seams.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W5X-STMT-ONBOARD-001.md)
- Evidence: [src/Meridian.Ui.Shared/Evidence/StatementImportEvidenceBridge.cs](../../../src/Meridian.Ui.Shared/Evidence/StatementImportEvidenceBridge.cs)
- Evidence: [src/Meridian.Ui.Shared/Evidence/ReportingStatementImportEvidenceRetainer.cs](../../../src/Meridian.Ui.Shared/Evidence/ReportingStatementImportEvidenceRetainer.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.StatementConnectors.cs](../../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.StatementConnectors.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/statement-import-panel.tsx](../../../src/Meridian.Ui/dashboard/src/screens/statement-import-panel.tsx)
- Evidence: [tests/Meridian.Tests/Ui/StatementImportEvidenceBridgeTests.cs](../../../tests/Meridian.Tests/Ui/StatementImportEvidenceBridgeTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/StatementReconciliationProductionAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/StatementReconciliationProductionAuthorityTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationStatementReconciliationEndpointTests.cs](../../../tests/Meridian.Tests/Ui/WorkstationStatementReconciliationEndpointTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/statement-import-panel.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/statement-import-panel.test.tsx)


### Exit Criteria

- Browser statement import commit returns Evidence Vault identity, Evidence workbench route, reconciliation route, and next actions.
- The retained raw statement source is copied into Evidence Vault as a Statement document linked to statement run, fund account, period, and import source.
- Statement imports with reconciliation breaks mark retained statement evidence as review-required and route operators into reconciliation casework.
- The wedge reuses W5X-CONNECT-001 mapping profiles and connector parsing without requiring code releases for CSV/OFX layout onboarding.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W6-BTSTUDIO-001 - Backtesting studio evidence loop
| Field | Value |
| --- | --- |
| Wave | W6 |
| Status | done |
| Health | green |
| Priority | medium |
| Owner lane | Strategy Analytics |
| Evidence posture | complete |
| Last reviewed | 2026-08-03 |

### Current Summary

W6-BTSTUDIO-001: done; health green; evidence complete. Bounded Covered Call evidence loop; retained budgets, exact strategy lineage and fail-closed acceptance gates remain applicable.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W6-BTSTUDIO-001.md)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts](../../../src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.ts)
- Evidence: [src/Meridian.Contracts/Workstation/EvidenceVaultReference.cs](../../../src/Meridian.Contracts/Workstation/EvidenceVaultReference.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallRunProjection.cs](../../../src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallRunProjection.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallBacktestService.cs](../../../src/Meridian.Ui.Shared/Services/CoveredCall/CoveredCallBacktestService.cs)
- Evidence: [src/Meridian.Strategies/Storage/StrategyRunStore.cs](../../../src/Meridian.Strategies/Storage/StrategyRunStore.cs)
- Evidence: [src/Meridian.Strategies/Services/StrategyRunReadService.cs](../../../src/Meridian.Strategies/Services/StrategyRunReadService.cs)
- Evidence: [src/Meridian.Strategies/Services/PromotionService.cs](../../../src/Meridian.Strategies/Services/PromotionService.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/PromotionEndpoints.cs)
- Evidence: [src/Meridian.Ui.Shared/Evidence/EvidenceContributors.cs](../../../src/Meridian.Ui.Shared/Evidence/EvidenceContributors.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/strategy-screen.view-model.ts](../../../src/Meridian.Ui/dashboard/src/screens/strategy-screen.view-model.ts)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.test.ts](../../../src/Meridian.Ui/dashboard/src/screens/covered-call-screen.view-model.test.ts)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/covered-call-screen.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/covered-call-screen.test.tsx)
- Evidence: [tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallRunProjectionTests.cs](../../../tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallRunProjectionTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallBacktestServiceTests.cs](../../../tests/Meridian.Tests/Strategies/CoveredCall/CoveredCallBacktestServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Integration/EndpointTests/CoveredCallEndpointAuthorizationTests.cs](../../../tests/Meridian.Tests/Integration/EndpointTests/CoveredCallEndpointAuthorizationTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/StrategyRunStoreTests.cs](../../../tests/Meridian.Tests/Strategies/StrategyRunStoreTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/StrategyRunReadServiceTests.cs](../../../tests/Meridian.Tests/Strategies/StrategyRunReadServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/PromotionServiceTests.cs](../../../tests/Meridian.Tests/Strategies/PromotionServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/ExecutionWriteEndpointsTests.cs](../../../tests/Meridian.Tests/Ui/ExecutionWriteEndpointsTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs](../../../tests/Meridian.Tests/Ui/EvidenceWorkflowFabricTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.StrategyTenantScope.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.StrategyTenantScope.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.TradingTenantScope.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.TradingTenantScope.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/strategy-screen.view-model.test.ts](../../../src/Meridian.Ui/dashboard/src/screens/strategy-screen.view-model.test.ts)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/strategy-screen.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/strategy-screen.test.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/trading-screen.view-model.test.ts](../../../src/Meridian.Ui/dashboard/src/screens/trading-screen.view-model.test.ts)


### Exit Criteria

- The host-composed browser Covered Call request binds its native backtest to exact tenant/company canonical strategy-run lineage before execution.
- Count, value-length, and aggregate budgets fail before Vault I/O; at least one strict canonical Evidence Vault manifest reference must resolve inside the authenticated scope before queue admission.
- Operator-facing acceptance criteria are retained as requirements, while the four canonical Paper checklist items become Ready only from an approved durable promotion with operator/time/audit authority and keyed evidence exactly matching the source run.
- The approved Paper target is durably retained in the same tenant/company scope with exact parent-run and strategy lineage; missing, rejected, foreign, or mismatched authority stays review-required or rejected.
- Strategy promotion uses the governed promotion endpoint, and Strategy review plus Trading readiness consume scoped runs without a generic paper-session bypass or legacy fallback.
- The shared strategy-run store, replay, detail, review packet, evidence graph, and browser surfaces preserve the governed loop without client-local readiness rules.
- Source READMEs explain module ownership and test lanes.
- Scope remains limited to the supported Covered Call evidence and Paper-promotion loop unless a later roadmap change promotes broader Studio scope.

### Source Modules

- `SRC-BACKTESTING`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W7-LIVE-001 - Live-readiness governance
| Field | Value |
| --- | --- |
| Wave | W7 |
| Status | done |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-05 |

### Current Summary

W7-LIVE-001: done; health green; evidence complete. Bounded live-readiness governance does not authorize activation without the retained evidence and operator controls.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W7-LIVE-001.md)
- Evidence: [src/Meridian.Strategies/Promotions/PromotionApprovalChecklist.cs](../../../src/Meridian.Strategies/Promotions/PromotionApprovalChecklist.cs)
- Evidence: [src/Meridian.Strategies/Services/PromotionService.cs](../../../src/Meridian.Strategies/Services/PromotionService.cs)
- Evidence: [src/Meridian.Execution/Services/ExecutionOperatorControlService.cs](../../../src/Meridian.Execution/Services/ExecutionOperatorControlService.cs)
- Evidence: [tests/Meridian.Tests/Strategies/PromotionServiceTests.cs](../../../tests/Meridian.Tests/Strategies/PromotionServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs](../../../tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs)


### Exit Criteria

- Live action surfaces remain paper-first unless the live approval checklist, evidence references, manual override, and execution-control checks are all green.
- Credential and provider checks stay secret-safe and read-only by default; live promotion is blocked when brokerage configuration is not live-enabled.
- Governance sign-off, rollback or kill-switch posture, exception-handling evidence, and audit-retention evidence are linked before any live-readiness claim.
- The completed slice strengthens operational evidence and approval posture without productizing broader live execution or live portfolio operations.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-EXECUTION`
- `SRC-STRATEGIES`
- `SRC-UI-DASHBOARD`

## W8-UX-CONSOL-001 - Browser workstation screen consolidation
| Field | Value |
| --- | --- |
| Wave | W8 |
| Status | in_progress |
| Health | on_track |
| Priority | medium |
| Owner lane | Workstation Shell and UX |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-19 |

### Current Summary

W8-UX-CONSOL-001: in_progress; health on_track; evidence in_progress. Formula Workbench fold remains withdrawn pending a real catalog endpoint; that endpoint-bearing work belongs in a separate item. Retained redirects support persisted links.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W8-UX-CONSOL-001.md)
- Evidence: [docs/development/web-ui-structural-improvement-proposal.md](../../development/web-ui-structural-improvement-proposal.md)
- Evidence: [docs/engineering/plans/wpf-web-ui-alignment-plan.md](../../engineering/plans/wpf-web-ui-alignment-plan.md)


### Exit Criteria

- Retired screen routes redirect to their host screens with query and hash scope preserved.
- Sidebar sub-items, the workstation route catalog, mounted routes, and command palette route commands stay one taxonomy for each fold.
- The WPF web-UI alignment plan parity matrix reflects consolidated browser screen names in the same change that folds them.
- Folds compose tabs, master panes, or context rails without stacking screen content beyond the structural proposal page-height budget.
- No read-model, contract, or endpoint changes ship as part of screen consolidation.

### Source Modules

- `SRC-UI-DASHBOARD`

## W8-WPF-PARITY-001 - WPF desktop workstation reactivation and web-UI parity
| Field | Value |
| --- | --- |
| Wave | W8 |
| Status | in_progress |
| Health | on_track |
| Priority | high |
| Owner lane | Desktop Workstation |
| Evidence posture | in_progress |
| Last reviewed | 2026-07-06 |

### Current Summary

W8-WPF-PARITY-001: in_progress; health on_track; evidence in_progress. WPF remains an active co-equal lane; shared-contract parity and the recorded acceptance matrix remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W8-WPF-PARITY-001.md)
- Evidence: [docs/engineering/plans/wpf-web-ui-alignment-plan.md](../../engineering/plans/wpf-web-ui-alignment-plan.md)
- Evidence: [docs/development/wpf-implementation-notes.md](../../development/wpf-implementation-notes.md)
- Evidence: [src/Meridian.Wpf/Views/IntercompanyCounterpartProposalsControl.xaml](../../../src/Meridian.Wpf/Views/IntercompanyCounterpartProposalsControl.xaml)
- Evidence: [src/Meridian.Ui.Services/Services/Accounting/IntercompanyCounterpartQueueWorkspace.cs](../../../src/Meridian.Ui.Services/Services/Accounting/IntercompanyCounterpartQueueWorkspace.cs)
- Evidence: [tests/Meridian.Ui.Tests/Services/IntercompanyCounterpartQueueWorkspaceTests.cs](../../../tests/Meridian.Ui.Tests/Services/IntercompanyCounterpartQueueWorkspaceTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Views/IntercompanyNativeControlSmokeTests.CounterpartProposals.cs](../../../tests/Meridian.Wpf.Tests/Views/IntercompanyNativeControlSmokeTests.CounterpartProposals.cs)


### Exit Criteria

- The WPF lane is documented as active and co-equal with the browser workstation across the design charter, product README, CLAUDE.md, and desktop policy.
- Every browser workstation screen has a WPF equivalent page/view-model or an explicitly sequenced parity item, recorded in the WPF web-UI alignment plan.
- New WPF parity surfaces consume shared contracts, read models, and workstation endpoints rather than inventing desktop-local product state.
- Desktop parity work preserves existing WPF behavior, MVVM boundaries, cancellation flow, and shared-contract usage, validated through the desktop build and desktop-focused service tests.

### Source Modules

- `SRC-WPF`
- `SRC-UI-SERVICES`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-CONTRACTS`

## W9-TRUTH-001 - Loud fail-closed handling of simulated data and in-memory persistence
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | critical |
| Owner lane | Data Confidence and Validation |
| Evidence posture | complete |
| Last reviewed | 2026-09-26 |

### Current Summary

W9-TRUTH-001: accepted; health green; evidence complete. Acceptance uses an explicit owner exception for completed self-review; independent non-author verdicts were not obtained. Retained correction and certification evidence is unchanged.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001, DEC-W9-ACCEPTANCE-005
- PR references (not inferred acceptance): [#3000](https://github.com/rodoHasArrived/Meridian-main/pull/3000), [#3003](https://github.com/rodoHasArrived/Meridian-main/pull/3003)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-TRUTH-001.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [tests/Meridian.Tests/Application/Composition/ProductionProhibitedDurableBindingTests.cs](../../../tests/Meridian.Tests/Application/Composition/ProductionProhibitedDurableBindingTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/app.tsx](../../../src/Meridian.Ui/dashboard/src/app.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/data-provenance-banner.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/data-provenance-banner.tsx)
- Evidence: [src/Meridian.Ui.Services/Services/FixtureModeDetector.cs](../../../src/Meridian.Ui.Services/Services/FixtureModeDetector.cs)
- Evidence: [src/Meridian.Ui.Services/Services/StatusServiceBase.cs](../../../src/Meridian.Ui.Services/Services/StatusServiceBase.cs)
- Evidence: [src/Meridian.Strategies/Services/PromotionService.cs](../../../src/Meridian.Strategies/Services/PromotionService.cs)
- Evidence: [tests/Meridian.Tests/Strategies/PromotionServiceTests.cs](../../../tests/Meridian.Tests/Strategies/PromotionServiceTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/app.test.tsx](../../../src/Meridian.Ui/dashboard/src/app.test.tsx)
- Evidence: [src/Meridian.Wpf/Services/WorkspaceShellContextService.cs](../../../src/Meridian.Wpf/Services/WorkspaceShellContextService.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/WorkspaceShellProvenanceBadgeTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/WorkspaceShellProvenanceBadgeTests.cs)
- Evidence: [src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs](../../../src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs)
- Evidence: [tests/Meridian.Tests/Strategies/ReconciliationBreakQueueRepositoryTests.cs](../../../tests/Meridian.Tests/Strategies/ReconciliationBreakQueueRepositoryTests.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ReportPackValidationService.cs](../../../src/Meridian.Ui.Shared/Services/ReportPackValidationService.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/GovernanceReportPackRepository.cs](../../../src/Meridian.Ui.Shared/Services/GovernanceReportPackRepository.cs)
- Evidence: [tests/Meridian.Tests/Application/Services/FundOperationsWorkspaceReadServiceTests.cs](../../../tests/Meridian.Tests/Application/Services/FundOperationsWorkspaceReadServiceTests.cs)
- Evidence: [src/Meridian.Application/Composition/ProductionRegistrationGuardService.cs](../../../src/Meridian.Application/Composition/ProductionRegistrationGuardService.cs)
- Evidence: [tests/Meridian.Tests/Application/Composition/ProductionRegistrationGuardServiceTests.cs](../../../tests/Meridian.Tests/Application/Composition/ProductionRegistrationGuardServiceTests.cs)


### Exit Criteria

- Every simulated, sample, or synthetic data surface renders a persistent operator-visible simulation label in both the browser and WPF workstations.
- Supported production profiles refuse startup when an in-memory, null, no-op, or placeholder persistence implementation is selected for a durable role, with startup rejection tests per prohibited binding.
- Non-production adapters carry an explicit non-production marker and registration guards keep them out of production composition with focused test coverage.
- No figure derived from simulated or seeded data can enter ledger, reconciliation, report-pack, or promotion evidence without a blocking simulation provenance mark.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W9-DEMO-002 - One-command seeded demo with durable storage
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Workstation Shell and UX |
| Evidence posture | complete |
| Last reviewed | 2026-09-16 |

### Current Summary

W9-DEMO-002: done; health green; evidence complete. Bounded closure uses the retained status-documentation record; it certifies no release and changes no other roadmap row.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001, DEC-W9-DONE-001
- PR references (not inferred acceptance): [#2972](https://github.com/rodoHasArrived/Meridian-main/pull/2972)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-DEMO-002.md)
- Evidence: [docs/product/w9-demo-002-closure-2026-09-16.md](../../product/w9-demo-002-closure-2026-09-16.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [src/Meridian.Ui.Shared/Services/DemoTenantBlueprint.cs](../../../src/Meridian.Ui.Shared/Services/DemoTenantBlueprint.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/DemoTenantProvisioner.cs](../../../src/Meridian.Ui.Shared/Services/DemoTenantProvisioner.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/DemoWorkspaceSeeder.cs](../../../src/Meridian.Ui.Shared/Services/DemoWorkspaceSeeder.cs)
- Evidence: [src/Meridian/DemoWorkspaceCli.cs](../../../src/Meridian/DemoWorkspaceCli.cs)
- Evidence: [tests/Meridian.Demo.Tests/Demo/DemoWorkspaceSeederTests.cs](../../../tests/Meridian.Demo.Tests/Demo/DemoWorkspaceSeederTests.cs)
- Evidence: [tests/Meridian.Demo.Tests/Demo/DemoWorkspaceGuardTests.cs](../../../tests/Meridian.Demo.Tests/Demo/DemoWorkspaceGuardTests.cs)
- Evidence: [src/Meridian.DataIntegration/Historical/HistoricalDataQueryService.cs](../../../src/Meridian.DataIntegration/Historical/HistoricalDataQueryService.cs)
- Evidence: [src/Meridian.Storage/Policies/JsonlStoragePolicy.cs](../../../src/Meridian.Storage/Policies/JsonlStoragePolicy.cs)
- Evidence: [.github/workflows/demo-smoke.yml](../../../.github/workflows/demo-smoke.yml)


### Exit Criteria

- A single documented command provisions a demo workspace with seeded provider data, portfolios, ledger records, reconciliation cases, and report packs over durable storage rather than in-memory persistence.
- Seeded demo data survives restart and renders with the persistent simulation labeling required by W9-TRUTH-001 everywhere it appears.
- The seeded demo path runs in CI so a broken first-run experience fails a check instead of a first evaluation.
- Demo tear-down or reset is one command and cannot touch non-demo data roots.

### Source Modules

- `SRC-HOST`
- `SRC-APP`
- `SRC-STORAGE`
- `SRC-UI-DASHBOARD`

## W9-PAPER-003 - Paper-trading realism with limit/stop matching and costs
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | critical |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-09-25 |

### Current Summary

W9-PAPER-003: accepted; health green; evidence complete. Retained correction and bounded operator acceptance apply; implementation evidence does not certify a production release.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-PAPER-003.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [src/Meridian.Execution/PaperMatching/PaperOrderMatchingPolicy.cs](../../../src/Meridian.Execution/PaperMatching/PaperOrderMatchingPolicy.cs)
- Evidence: [src/Meridian.Execution/PaperMatching/PaperTradingCostModel.cs](../../../src/Meridian.Execution/PaperMatching/PaperTradingCostModel.cs)
- Evidence: [src/Meridian.Execution/Adapters/PaperTradingGateway.cs](../../../src/Meridian.Execution/Adapters/PaperTradingGateway.cs)
- Evidence: [src/Meridian.Execution/PaperTradingGateway.cs](../../../src/Meridian.Execution/PaperTradingGateway.cs)
- Evidence: [src/Meridian.Strategies/Promotions/PromotionApprovalChecklist.cs](../../../src/Meridian.Strategies/Promotions/PromotionApprovalChecklist.cs)
- Evidence: [tests/Meridian.Tests/Execution/PaperOrderMatchingPolicyTests.cs](../../../tests/Meridian.Tests/Execution/PaperOrderMatchingPolicyTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/PaperTradingCostModelTests.cs](../../../tests/Meridian.Tests/Execution/PaperTradingCostModelTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/PaperFillEnvelopeRegressionTests.cs](../../../tests/Meridian.Tests/Execution/PaperFillEnvelopeRegressionTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/PaperGatewayTickSizeBoundaryTests.cs](../../../tests/Meridian.Tests/Execution/PaperGatewayTickSizeBoundaryTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs](../../../tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs)


### Exit Criteria

- Paper matching honors order-type semantics - limit orders fill only at or better than the limit price, stops trigger per a documented trade/quote policy, and market orders fill from observed market data, never at placeholder prices such as one dollar.
- Commission, fee, slippage, and spread cost models apply to every paper fill and are visible in paper-session economics.
- A regression suite proves no paper fill can occur at a price outside the observed market-data envelope for the bar or tick in effect.
- Promotion evidence records the matching and cost model version used by the paper session it cites.

### Source Modules

- `SRC-EXECUTION`
- `SRC-APP`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`

## W9-ALPACA-004 - Alpaca fill streaming into order and ledger state
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-09-02 |

### Current Summary

W9-ALPACA-004: accepted; health green; evidence complete. Bounded fill-stream acceptance retains durable-inbox and booking boundaries; admission is not a production-certification claim.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001, DEC-W9-ACCEPTANCE-003
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-ALPACA-004.md)
- Evidence: [src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaTradeUpdatesClient.cs](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaTradeUpdatesClient.cs)
- Evidence: [src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs](../../../src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs)
- Evidence: [src/Meridian.Execution/OrderManagementSystem.cs](../../../src/Meridian.Execution/OrderManagementSystem.cs)
- Evidence: [src/Meridian.Execution/OrderManagementSystem.UntrackedFills.cs](../../../src/Meridian.Execution/OrderManagementSystem.UntrackedFills.cs)
- Evidence: [src/Meridian.Execution/Events/LedgerPostingConsumer.cs](../../../src/Meridian.Execution/Events/LedgerPostingConsumer.cs)
- Evidence: [src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs](../../../src/Meridian/HostedBrokerageGatewayServiceCollectionExtensions.cs)
- Evidence: [tests/Meridian.Tests/Infrastructure/Providers/AlpacaTradeUpdatesClientTests.cs](../../../tests/Meridian.Tests/Infrastructure/Providers/AlpacaTradeUpdatesClientTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/OrderManagementSystemReportStreamTests.cs](../../../tests/Meridian.Tests/Execution/OrderManagementSystemReportStreamTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/AlpacaStreamedFillLoopTests.cs](../../../tests/Meridian.Tests/Execution/AlpacaStreamedFillLoopTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/OrderManagementSystemDurableHandoffOrderingTests.cs](../../../tests/Meridian.Tests/Execution/OrderManagementSystemDurableHandoffOrderingTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/UntrackedFillPositionContextTests.cs](../../../tests/Meridian.Tests/Execution/UntrackedFillPositionContextTests.cs)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)


### Exit Criteria

- Alpaca trade-update and fill events stream into the execution gateway and drive order lifecycle state, including partial fills, cancels, and rejects, without polling.
- Streamed fills flow into the existing durable trade-fill posting and ledger handoff path.
- Reconnect recovery backfills fills missed during a disconnect, with tests covering duplicate and out-of-order delivery.

### Source Modules

- `SRC-EXECUTION`
- `SRC-INFRASTRUCTURE`
- `SRC-APP`

## W9-REPORT-005 - Client-grade PDF/XLSX exports and partners-capital statement
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-29 |

### Current Summary

W9-REPORT-005: accepted; health green; evidence complete. Bounded client-report rendering acceptance retains canonical checkpoint-bound document and evidence requirements.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001
- PR references (not inferred acceptance): [#2525](https://github.com/rodoHasArrived/Meridian-main/pull/2525)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-REPORT-005.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [src/Meridian.Ledger/PartnersCapitalStatementLayout.cs](../../../src/Meridian.Ledger/PartnersCapitalStatementLayout.cs)
- Evidence: [src/Meridian.Documents/FinancialReportDocumentRenderer.cs](../../../src/Meridian.Documents/FinancialReportDocumentRenderer.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ReportingCertifiedArtifactProducer.cs](../../../src/Meridian.Ui.Shared/Services/ReportingCertifiedArtifactProducer.cs)
- Evidence: [src/Meridian.Ledger/LedgerScheduledReportExportPackageBuilder.cs](../../../src/Meridian.Ledger/LedgerScheduledReportExportPackageBuilder.cs)
- Evidence: [tests/Meridian.Tests/Ledger/PartnersCapitalStatementLayoutTests.cs](../../../tests/Meridian.Tests/Ledger/PartnersCapitalStatementLayoutTests.cs)
- Evidence: [tests/Meridian.Tests/Ledger/PartnersCapitalBespokeRenderTests.cs](../../../tests/Meridian.Tests/Ledger/PartnersCapitalBespokeRenderTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/ReportingRunCertificationServiceTests.cs](../../../tests/Meridian.Tests/Ui/ReportingRunCertificationServiceTests.cs)


### Exit Criteria

- Governed report packs export deterministic client-presentable PDF and XLSX artifacts with retained hash and provenance manifests.
- A partners-capital statement covering opening balance, contributions, distributions, income/expense/gain allocations, fees, and closing balance renders per partner and per period from ledger-backed data.
- Exported artifacts carry the same approval and provenance evidence chain as existing report-pack outputs.

### Source Modules

- `SRC-DESIGN-REPORTING`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`

## W9-NAV-006 - Unitized NAV and real fee, waterfall, and capital-call economics
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-08-29 |

### Current Summary

W9-NAV-006: accepted; health green; evidence complete. Bounded economics acceptance retains ledger-backed unitization and golden-file evidence.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-NAV-006.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [src/Meridian.FSharp.Ledger/FundEconomics.fs](../../../src/Meridian.FSharp.Ledger/FundEconomics.fs)
- Evidence: [src/Meridian.Ledger/ShareClassUnitRegisterProjector.cs](../../../src/Meridian.Ledger/ShareClassUnitRegisterProjector.cs)
- Evidence: [src/Meridian.Ledger/NavPerUnitCalculator.cs](../../../src/Meridian.Ledger/NavPerUnitCalculator.cs)
- Evidence: [src/Meridian.Ledger/EqualizationCalculator.cs](../../../src/Meridian.Ledger/EqualizationCalculator.cs)
- Evidence: [src/Meridian.Ledger/EuropeanDistributionWaterfall.cs](../../../src/Meridian.Ledger/EuropeanDistributionWaterfall.cs)
- Evidence: [src/Meridian.Ledger/FundEconomicsJournalFactory.cs](../../../src/Meridian.Ledger/FundEconomicsJournalFactory.cs)
- Evidence: [src/Meridian.FinancialOperations/PrivateCapital/CommitmentRollForwardCalculator.cs](../../../src/Meridian.FinancialOperations/PrivateCapital/CommitmentRollForwardCalculator.cs)
- Evidence: [tests/Meridian.Tests/Ledger/FundEconomicsGoldenWorkedExampleTests.cs](../../../tests/Meridian.Tests/Ledger/FundEconomicsGoldenWorkedExampleTests.cs)
- Evidence: [tests/fixtures/fund-economics/golden/fund-economics-worked-examples.json](../../../tests/fixtures/fund-economics/golden/fund-economics-worked-examples.json)
- Evidence: [tests/Meridian.FSharp.Tests/FundEconomicsTests.fs](../../../tests/Meridian.FSharp.Tests/FundEconomicsTests.fs)
- Evidence: [src/Meridian.Ui.Shared/Services/RecurringJournalCalculations.cs](../../../src/Meridian.Ui.Shared/Services/RecurringJournalCalculations.cs)
- Evidence: [tests/Meridian.Tests/Ledger/FundEconomicsJournalFactoryTests.cs](../../../tests/Meridian.Tests/Ledger/FundEconomicsJournalFactoryTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/RecurringJournalCalculationPostgresTests.cs](../../../tests/Meridian.Tests/Storage/RecurringJournalCalculationPostgresTests.cs)
- Evidence: [tests/Meridian.Tests/Ledger/LedgerPartnersCapitalReconciliationTests.cs](../../../tests/Meridian.Tests/Ledger/LedgerPartnersCapitalReconciliationTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/PrivateCapital/CommitmentRollForwardCalculatorTests.cs](../../../tests/Meridian.Tests/FinancialOperations/PrivateCapital/CommitmentRollForwardCalculatorTests.cs)


### Exit Criteria

- Unitized NAV per share class is computed from ledger-backed valuations with an auditable calculation trail and restatement support.
- Management fee, performance fee or carried interest with hurdle, high-water mark, and crystallization treatment, and expense accruals post governed ledger entries.
- Distribution waterfall and capital-call or commitment schedules compute from capital-account records and reconcile to the partners-capital statement delivered by W9-REPORT-005.
- Golden-file tests cover the calculation kernels against worked examples.

### Source Modules

- `SRC-LEDGER`
- `SRC-FSHARP-LEDGER`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-CONTRACTS`

## W9-SAFETY-007 - Kill-switch cancel-all and fat-finger, notional, and collar rules
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | complete |
| Last reviewed | 2026-09-23 |

### Current Summary

W9-SAFETY-007: accepted; health green; evidence complete. Acceptance retains OCO worse-limb reservation/release follow-up, deferred risk journal/read surface and WPF parity. Restored prior acceptance is not a new decision.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-004
- PR references (not inferred acceptance): [#1](https://github.com/rodoHasArrived/Meridian-main/pull/1), [#2](https://github.com/rodoHasArrived/Meridian-main/pull/2), [#3](https://github.com/rodoHasArrived/Meridian-main/pull/3), [#4](https://github.com/rodoHasArrived/Meridian-main/pull/4), [#2945](https://github.com/rodoHasArrived/Meridian-main/pull/2945)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-SAFETY-007.md)
- Evidence: [docs/roadmap/data/decision-log.yml](../data/decision-log.yml)
- Evidence: [src/Meridian.Execution/Services/ExecutionOperatorControlService.cs](../../../src/Meridian.Execution/Services/ExecutionOperatorControlService.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/RiskRuleRuntimeService.cs](../../../src/Meridian.Ui.Shared/Services/RiskRuleRuntimeService.cs)
- Evidence: [src/Meridian.Risk/CompositeRiskValidator.cs](../../../src/Meridian.Risk/CompositeRiskValidator.cs)
- Evidence: [src/Meridian.Execution/OrderManagementSystem.KillSwitch.cs](../../../src/Meridian.Execution/OrderManagementSystem.KillSwitch.cs)
- Evidence: [src/Meridian.Wpf/Services/ExecutionSafetyControlClient.cs](../../../src/Meridian.Wpf/Services/ExecutionSafetyControlClient.cs)
- Evidence: [src/Meridian.Wpf/Services/TradingSafetyCommandService.cs](../../../src/Meridian.Wpf/Services/TradingSafetyCommandService.cs)
- Evidence: [src/Meridian.Execution.Sdk/KillSwitchSweepResult.cs](../../../src/Meridian.Execution.Sdk/KillSwitchSweepResult.cs)
- Evidence: [src/Meridian.Risk/Rules/FatFingerRule.cs](../../../src/Meridian.Risk/Rules/FatFingerRule.cs)
- Evidence: [src/Meridian.Risk/Rules/PriceCollarRule.cs](../../../src/Meridian.Risk/Rules/PriceCollarRule.cs)
- Evidence: [src/Meridian.Risk/Rules/OrderPriceLimbs.cs](../../../src/Meridian.Risk/Rules/OrderPriceLimbs.cs)
- Evidence: [src/Meridian.Risk/Rules/BracketChildLimbRule.cs](../../../src/Meridian.Risk/Rules/BracketChildLimbRule.cs)
- Evidence: [src/Meridian.Execution.Sdk/BracketOrderMetadata.cs](../../../src/Meridian.Execution.Sdk/BracketOrderMetadata.cs)
- Evidence: [src/Meridian.FSharp/Risk/RiskRules.fs](../../../src/Meridian.FSharp/Risk/RiskRules.fs)
- Evidence: [tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs](../../../tests/Meridian.Tests/Ui/ExecutionGovernanceEndpointsTests.cs)
- Evidence: [tests/Meridian.Tests/Risk/FatFingerRuleTests.cs](../../../tests/Meridian.Tests/Risk/FatFingerRuleTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RiskRuleRuntimeFatFingerStatusTests.cs](../../../tests/Meridian.Tests/Ui/RiskRuleRuntimeFatFingerStatusTests.cs)
- Evidence: [tests/Meridian.Tests/Risk/PriceCollarRuleTests.cs](../../../tests/Meridian.Tests/Risk/PriceCollarRuleTests.cs)
- Evidence: [tests/Meridian.Tests/Risk/BracketChildLimbRuleTests.cs](../../../tests/Meridian.Tests/Risk/BracketChildLimbRuleTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/KillSwitchCloseOnlyTests.cs](../../../tests/Meridian.Tests/Execution/KillSwitchCloseOnlyTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Services/TradingSafetyCommandTests.cs](../../../tests/Meridian.Wpf.Tests/Services/TradingSafetyCommandTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RiskRuleRuntimePriceCollarConfigTests.cs](../../../tests/Meridian.Tests/Ui/RiskRuleRuntimePriceCollarConfigTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/FixedIncomeFillBookingTests.cs](../../../tests/Meridian.Tests/Execution/FixedIncomeFillBookingTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RiskRuleRuntimeFirstRunDefaultsTests.cs](../../../tests/Meridian.Tests/Ui/RiskRuleRuntimeFirstRunDefaultsTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/KillSwitchBrokerTruthTests.cs](../../../tests/Meridian.Tests/Execution/KillSwitchBrokerTruthTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/KillSwitchTripRaceTests.cs](../../../tests/Meridian.Tests/Execution/KillSwitchTripRaceTests.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/EventReplayViewModel.cs](../../../src/Meridian.Wpf/ViewModels/EventReplayViewModel.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/EventReplayViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/EventReplayViewModelTests.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/PositionBlotterViewModel.cs](../../../src/Meridian.Wpf/ViewModels/PositionBlotterViewModel.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/PositionBlotterViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/PositionBlotterViewModelTests.cs)
- Evidence: [tests/Meridian.Tests/Execution/OrderManagementSystemGovernanceTests.cs](../../../tests/Meridian.Tests/Execution/OrderManagementSystemGovernanceTests.cs)


### Exit Criteria

- Kill-switch activation cancels all open orders across gateways, blocks new order submission, and persists breaker state fail-closed across restart.
- Pre-trade risk includes fat-finger quantity and price-deviation, max-notional, and price-collar rules enforced by the single mandatory production risk validator.
- Every WPF and browser safety control either invokes the real shared execution-control service or is disabled with an explicit not-wired state, leaving no dead safety buttons.
- Activation, failure, and override events append to the execution audit trail.

### Source Modules

- `SRC-EXECUTION`
- `SRC-RISK`
- `SRC-WPF`
- `SRC-UI-SHARED`

## W9-GOV-008 - Route-level authorization, fail-closed tenancy, and hash-chained accounting audit
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Platform Security and Governance |
| Evidence posture | complete |
| Last reviewed | 2026-10-18 |

### Current Summary

W9-GOV-008: accepted; health green; evidence complete. Owner-reported real-installation tenancy cutover has no retained cutover log/readiness output. External-checkpoint audit limit and integrity-panel WPF parity reservations remain.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-006
- PR references (not inferred acceptance): [#3190](https://github.com/rodoHasArrived/Meridian-main/pull/3190)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-GOV-008.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-10-18.md](../../product/w9-operator-acceptance-2026-10-18.md)


### Exit Criteria

- An authorization coverage test enumerates every mapped endpoint and fails when a route lacks an explicit policy or permission declaration.
- Cross-tenant reads and writes fail closed with tests, and requests without resolvable tenant scope are rejected rather than defaulted.
- Accounting and ledger audit events append to a hash-chained tamper-evident log with cross-process serialization, verification tooling, and tamper-detection tests.

### Source Modules

- `SRC-WPF`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-CORE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-DESIGN-IDENTITY`
- `SRC-DESIGN-AUDIT`
- `SRC-STORAGE`
- `SRC-LEDGER`
- `SRC-APP`

## W9-INGEST-009 - Institutional file ingestion (camt.053/BAI2) and sided reconciliation matcher
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-10-18 |

### Current Summary

W9-INGEST-009: accepted; health green; evidence complete. Acceptance retains shared-service/file-store evidence with a controlled population boundary; it is not PostgreSQL or production-certification evidence.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-006
- PR references (not inferred acceptance): [#2948](https://github.com/rodoHasArrived/Meridian-main/pull/2948)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-INGEST-009.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-10-18.md](../../product/w9-operator-acceptance-2026-10-18.md)
- Evidence: [tests/Meridian.Tests/Integration/StatementImportCaseworkEvidenceTests.cs](../../../tests/Meridian.Tests/Integration/StatementImportCaseworkEvidenceTests.cs)


### Exit Criteria

- camt.053 and BAI2 statement connectors normalize through the W5X-CONNECT-001 connector seam with retained raw and canonical evidence and golden-file regression coverage.
- Bounded schema-aware parsing enforces the PRD-010 ingress limits for both formats.
- The reconciliation matcher distinguishes statement-side and ledger-side populations with deterministic one-to-one, one-to-many, and unmatched-break outcomes, stable tie-breakers, and idempotent re-runs.
- Sided match results feed the existing reconciliation casework queue without synthetic completeness.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-DATA-INTEGRATION`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`

## W9-ASSET-010 - Asset Accounting Event Spine and atomic lot posting
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | done |
| Health | green |
| Priority | critical |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-07-28 |

### Current Summary

W9-ASSET-010: done; health green; evidence complete. Bounded asset accounting event-spine completion retains evidence, identity, accounting policy and governed posting boundaries.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-ASSET-010.md)
- Evidence: [docs/architecture/event-accounting-architecture.md](../../architecture/event-accounting-architecture.md)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.cs](../../../src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AtomicTaxLots.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AtomicTaxLots.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AssetAccountingPostingEvidenceValidatorTests.cs](../../../tests/Meridian.Tests/Storage/AssetAccountingPostingEvidenceValidatorTests.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AssetAccountingEventSpineContractTests.cs](../../../tests/Meridian.Tests/AssetOperations/AssetAccountingEventSpineContractTests.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/InMemoryAssetAccountingEventProjectionStoreTests.cs](../../../tests/Meridian.Tests/AssetOperations/InMemoryAssetAccountingEventProjectionStoreTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Ledger/AssetAccountingEventSpineServiceTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Ledger/AssetAccountingEventSpineServiceTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Ledger/AssetAccountingLifecycleSeparationTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Ledger/AssetAccountingLifecycleSeparationTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.AccountingConfiguration.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.AccountingConfiguration.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs](../../../tests/Meridian.Tests/Ui/WorkstationFinancialRecordExplorerEndpointTests.cs)


### Exit Criteria

- All eight canonical asset accounting event kinds resolve Security Master identity, authoritative book position and version, ledger book, period and version, accounting basis, promoted rule pack, projection lineage, and complete retained evidence before candidate drafting.
- Lifecycle contracts and shared read models never collapse Expected or Projected into a candidate, Approved into Posted, or Reported into Published; journal impact is absent unless an immutable journal id, ledger book, period, balanced amounts, currency, and Posted status are retained.
- Acquisition creates its lot with the journal, and disposal consumes explicitly selected lot ids and expected versions with retained selection evidence, relief policy, before/after snapshots, correction lineage, and replay-safe fingerprints in one database transaction.
- Production-readiness flags, service or endpoint availability, navigation links, legacy full tokens, and synthesized obligations cannot substitute for complete typed retained evidence.
- Focused contract, spine, storage, endpoint, shared-read-model, and readiness tests pass before the full repository CI and GitHub Actions authority gates.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-UI-SHARED`

## W9-CORPACT-011 - Durable corporate action case processing and accounting projection
| Field | Value |
| --- | --- |
| Wave | W9 |
| Status | accepted |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-10-19 |

### Current Summary

W9-CORPACT-011: accepted; health green; evidence complete. Unsupported branches stay blocked; posting requires a governed custom role. Acceptance rests on automated PostgreSQL evidence, not a live-installation run.

### Status References

- Decisions: DEC-W9-ACCEPTANCE-001, DEC-W9-ACCEPTANCE-002, DEC-W9-ACCEPTANCE-007
- PR references (not inferred acceptance): [#2852](https://github.com/rodoHasArrived/Meridian-main/pull/2852), [#2858](https://github.com/rodoHasArrived/Meridian-main/pull/2858)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W9-CORPACT-011.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-10-19.md](../../product/w9-operator-acceptance-2026-10-19.md)
- Evidence: [docs/product/w9-operator-acceptance-2026-08-29.md](../../product/w9-operator-acceptance-2026-08-29.md)
- Evidence: [docs/domain/corporate-action-case.md](../../domain/corporate-action-case.md)
- Evidence: [src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionOperationsService.cs](../../../src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionOperationsService.cs)
- Evidence: [src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionIngestOrchestrator.cs](../../../src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionIngestOrchestrator.cs)
- Evidence: [src/Meridian.Instruments/AssetOperations/CorporateActionAccountingProjectionService.cs](../../../src/Meridian.Instruments/AssetOperations/CorporateActionAccountingProjectionService.cs)
- Evidence: [src/Meridian.Storage/SecurityMaster/PostgresCorporateActionOperationsStore.cs](../../../src/Meridian.Storage/SecurityMaster/PostgresCorporateActionOperationsStore.cs)
- Evidence: [src/Meridian.Storage/SecurityMaster/Migrations/030_security_master_corporate_action_operations.sql](../../../src/Meridian.Storage/SecurityMaster/Migrations/030_security_master_corporate_action_operations.sql)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs](../../../src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.CorporateActionOperations.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsServiceTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsServiceTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionIngestOrchestratorTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionIngestOrchestratorTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionGoldenLedgerTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionGoldenLedgerTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsContractTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsContractTests.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsMigrationTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionOperationsMigrationTests.cs)
- Evidence: [tests/Meridian.Backtesting.Tests/CorporateActionGoldenAdjustmentTests.cs](../../../tests/Meridian.Backtesting.Tests/CorporateActionGoldenAdjustmentTests.cs)
- Evidence: [src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionCaseAccountingService.cs](../../../src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionCaseAccountingService.cs)
- Evidence: [src/Meridian.Storage/SecurityMaster/Migrations/031_security_master_corporate_action_accounting_lane.sql](../../../src/Meridian.Storage/SecurityMaster/Migrations/031_security_master_corporate_action_accounting_lane.sql)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidateService.cs](../../../src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidateService.cs)


### Exit Criteria

- A corporate action case binds one canonical Security Master source event to exact tenant, account, book, basis, period, and position scope, and keeps source facts, treatment decisions, and generated consequences as separate authoritative layers.
- Provider observations carry a persisted release gate that is rechecked at acceptance, so a review-only feed can never be accepted as a canonical Security Master fact.
- Every case transition is durable, actor-attributed, optimistic-concurrency checked, and idempotent, and a failed acceptance or posting attempt leaves the case recoverable in its prior state.
- Posting is refused without exact scope, approved policy coverage, an open period, balanced journals, valid lot mutations, retained evidence, and required maker-checker approval; journals and posted lot effects stay immutable and corrections add reversal, rebook, or restatement lineage.
- Golden-file ledger and price-adjustment coverage proves projected accounting and backtest adjustment against worked examples, with migration and contract tests over the durable store.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-INFRASTRUCTURE`
- `SRC-STORAGE`
- `SRC-UI-SHARED`

## W10-BOND-001 - Bond analytics, declared-schedule migration and contractual cash-flow projection
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-10-31 |

### Current Summary

W10-BOND-001: in_progress; health green; evidence in_progress. Bond analytics and schedule/cutover tools are bounded implementation. Curve pins, batch staging, settlement posting and floating-rate analytics remain open; no live cutover or acceptance.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3328](https://github.com/rodoHasArrived/Meridian-main/pull/3328)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-BOND-001.md)
- Evidence: [docs/engineering/plans/bond-capability-expansion.md](../../engineering/plans/bond-capability-expansion.md)
- Evidence: [docs/testing/evidence/next-work-20261029/README.md](../../testing/evidence/next-work-20261029/README.md)


### Exit Criteria

- Bond analytics, migration preview, cutover, keyset ladders and contractual cash-flow projection have browser and WPF consumers over shared contracts, with displayed amounts never resubmitted and every blocker surfaced as a refusal rather than a silent fallback.
- Retained analytics receipts, cutover receipts and cash-flow runs replay byte-identically under their recorded versions; golden fixtures and hash pins change only through a reviewed increment that names the affected version.
- A reviewed cutover enables the durable live-calculation gate for one complete inventory hash; later undeclared or explicitly blocked bonds cannot silently use legacy calculations, and amendments name the current receipt hash.
- Operator acceptance of the cutover procedure and the scenario workbench on a dedicated nonproduction host is recorded under docs/testing before any row-level acceptance; mocked browser checks, scratch compilation and green CI do not substitute for it.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-DEBT-001 - God-file burn-down against the file-size ratchet
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-08-29 |

### Current Summary

W10-DEBT-001: planned; health green; evidence planned_evidence. Unranked engineering hygiene retains ADR-017/ADR-018 burn-down targets and ratchet containment; it adds no product dependency.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#2860](https://github.com/rodoHasArrived/Meridian-main/pull/2860)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-DEBT-001.md)
- Evidence: [docs/engineering/plans/god-file-burn-down-plan.md](../../engineering/plans/god-file-burn-down-plan.md)


### Exit Criteria

- At least two files are retired from the baseline entirely per release, with files retired as the headline metric rather than lines removed. A file counts as retired only when it leaves by genuine decomposition, so relocating bulk into a new file that is itself near the threshold moves the debt rather than reducing it and does not count.
- Total current lines across baselined files fall by 15 percent per quarter, measured against the 168,123 recorded at registration. Reduction is measured on current lines rather than capped lines because caps now include deliberate working headroom, so measuring on caps would read a relaxation as regression and a tightening as progress when neither changed any code.
- Every reduction is locked in with --tighten-baseline in the same change that achieves it, so caps follow the code down instead of leaving room to grow back.
- Relaxing the baseline is never the mechanism by which a target is met. Granted headroom is recorded in the baseline and excluded from the reclaimable figure precisely so it cannot read as progress, and a relax run must not coincide with a claimed reduction.
- Roadmap status remains planned until this row links concrete evidence for retired baseline entries and for the measured reduction, rather than the plan document alone.

### Source Modules

- `SRC-UI-DASHBOARD`
- `SRC-UI-SHARED`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-WPF`
- `SRC-APP`
- `SRC-EXECUTION`
- `SRC-STORAGE`

## W10-MARK-001 - Fail-closed stale-mark policy and mark-age surfacing
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-09-23 |

### Current Summary

W10-MARK-001: in_progress; health green; evidence in_progress. Shared fail-closed valuation freshness is implemented; live acceptance and retained exit criteria remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-MARK-001.md)
- Evidence: [src/Meridian.Ledger/ValuationFreshnessPolicy.cs](../../../src/Meridian.Ledger/ValuationFreshnessPolicy.cs)
- Evidence: [src/Meridian.Ledger/ValuationMarkEvidence.cs](../../../src/Meridian.Ledger/ValuationMarkEvidence.cs)
- Evidence: [src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs](../../../src/Meridian.Contracts/Workstation/MarkFreshnessDtos.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/mark-freshness-cell.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/mark-freshness-cell.tsx)
- Evidence: [src/Meridian.Wpf/Models/MarkFreshnessPresentation.cs](../../../src/Meridian.Wpf/Models/MarkFreshnessPresentation.cs)
- Evidence: [tests/Meridian.Tests/Application/Accounting/ValuationFreshnessAcceptanceTests.cs](../../../tests/Meridian.Tests/Application/Accounting/ValuationFreshnessAcceptanceTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/MarkFreshnessPresentationTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/MarkFreshnessPresentationTests.cs)


### Exit Criteria

- A valuation cannot rest on a mark whose age or observation date falls outside policy, and the default posture blocks rather than accepts.
- Freshness is governed by one policy owner rather than two independently configured controls, and consolidation preserves every non-age gate the stricter control enforces today - minimum confidence, complete coverage, and a required observation date - rather than collapsing to age alone.
- Valuations blocked on mark freshness render as review-required with the offending positions named, on both the browser and desktop workstations.
- Mark age and observation date are visible wherever positions appear, and an override is bound to the position, mark observation, valuation date, and policy version it was approved for, expiring or requiring renewed review as the charter's override strategy requires, so it cannot become a standing bypass.
- Enabling the new default is preceded by a preview of how many current valuations it would block.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the policy, both workstation surfaces, and the fail-closed tests.

### Source Modules

- `SRC-LEDGER`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-001 - Durable break lineage identity and run-over-run break diff
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | yellow |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-11-10 |

### Current Summary

W10-RECON-001: in_progress; health yellow; evidence in_progress. Stable business lineage and recovery are implemented; source clearing never disposes governed casework. Retained acceptance gates remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3004](https://github.com/rodoHasArrived/Meridian-main/pull/3004)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-RECON-001.md)
- Evidence: [src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Lineage.cs](../../../src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Lineage.cs)
- Evidence: [src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementMappingExecutionEvidence.cs](../../../src/Meridian.FinancialOperations/Reconciliation/Connectors/StatementMappingExecutionEvidence.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/StatementReconciliationIntakeAuthority.Lineage.cs](../../../src/Meridian.Ui.Shared/Services/StatementReconciliationIntakeAuthority.Lineage.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/FileReconciliationSlaCalendarResolver.cs](../../../src/Meridian.Ui.Shared/Services/FileReconciliationSlaCalendarResolver.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/reconciliation-source-observation.ts](../../../src/Meridian.Ui/dashboard/src/screens/reconciliation-source-observation.ts)
- Evidence: [src/Meridian.Wpf/Models/FundReconciliationWorkbenchModels.cs](../../../src/Meridian.Wpf/Models/FundReconciliationWorkbenchModels.cs)
- Evidence: [tests/Meridian.Tests/Strategies/FileReconciliationBreakQueueLineageTests.cs](../../../tests/Meridian.Tests/Strategies/FileReconciliationBreakQueueLineageTests.cs)
- Evidence: [tests/Meridian.Tests/Strategies/ReconciliationSlaCalendarTests.cs](../../../tests/Meridian.Tests/Strategies/ReconciliationSlaCalendarTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/StatementReconciliationIntakeAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/StatementReconciliationIntakeAuthorityTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/StatementReconciliationLineageTests.cs](../../../tests/Meridian.Tests/Ui/StatementReconciliationLineageTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/reconciliation-source-observation.test.ts](../../../src/Meridian.Ui/dashboard/src/screens/reconciliation-source-observation.test.ts)
- Evidence: [tests/Meridian.Wpf.Tests/Services/FundReconciliationWorkbenchServiceTests.cs](../../../tests/Meridian.Wpf.Tests/Services/FundReconciliationWorkbenchServiceTests.cs)


### Exit Criteria

- A break keeps one identity across runs even when its amount, tolerance, or as-of date changes.
- A break that clears and later recurs is recognizable as the same lineage while remaining a distinct occurrence, so a recurrence neither inherits the age of the original nor overwrites the interval during which it was clear.
- The queue shows what is new, what remains open and for how long, and what cleared since the prior run, without hiding open work behind a default filter. A break reads as cleared only when the successor run durably recorded that reconciliation itself completed over the same account, source, period, and both the mapping and tolerance profile at a known matching version - a completed run that still carries open breaks counts, while a run whose recorded outcome is failed, validation-failed, or cancelled does not, and neither an unrecorded profile version nor a run status merely inferred from an absence of breaks is treated as agreement; after an inconclusive or scope-divergent run the prior break stays open rather than disappearing.
- Break age derived from that identity drives escalation before an SLA is missed, measured against the business and holiday calendars the SLA policy names rather than a weekends-only approximation.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the lineage identity, the diff projection, the queue surfaces on both workstation lanes, and the identity-stability and calendar-boundary tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-PROV-001 - Ledger-amount evidence subject and shared proof drawer
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | in_progress |
| Last reviewed | 2026-10-28 |

### Current Summary

W10-PROV-001: in_progress; health green; evidence in_progress. Posted-ledger and governed-run proof slices are implemented. Other report families, NAV/portfolio amounts, current-candidate verification and live acceptance remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3041](https://github.com/rodoHasArrived/Meridian-main/pull/3041)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-PROV-001.md)
- Evidence: [src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsApiClient.cs](../../../src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsApiClient.cs)
- Evidence: [src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsViewModel.cs](../../../src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsViewModel.cs)
- Evidence: [src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsControl.xaml](../../../src/Meridian.Wpf/Features/Reporting/ReportRetainedAmountsControl.xaml)
- Evidence: [src/Meridian.Wpf/Features/Reporting/ReportingGovernanceWorkbenchViewModel.cs](../../../src/Meridian.Wpf/Features/Reporting/ReportingGovernanceWorkbenchViewModel.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/LedgerAmountProofDrawerViewModel.cs](../../../src/Meridian.Wpf/ViewModels/LedgerAmountProofDrawerViewModel.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Features/Reporting/ReportRetainedAmountsViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/Features/Reporting/ReportRetainedAmountsViewModelTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Features/Reporting/ReportingGovernanceWorkbenchViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/Features/Reporting/ReportingGovernanceWorkbenchViewModelTests.cs)
- Evidence: [src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs](../../../src/Meridian.Contracts/Workstation/LedgerAmountProofDtos.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/PostedLedgerAmountProvenanceService.cs](../../../src/Meridian.Ui.Shared/Services/PostedLedgerAmountProvenanceService.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/LedgerAmountProvenanceService.cs](../../../src/Meridian.Ui.Shared/Services/LedgerAmountProvenanceService.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ReportAmountBindingBuilder.cs](../../../src/Meridian.Ui.Shared/Services/ReportAmountBindingBuilder.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ReportLedgerAmountProvenanceService.cs](../../../src/Meridian.Ui.Shared/Services/ReportLedgerAmountProvenanceService.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/LedgerReportingAuthoritativeSource.cs](../../../src/Meridian.Ui.Shared/Services/LedgerReportingAuthoritativeSource.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/reporting-retained-amounts.tsx](../../../src/Meridian.Ui/dashboard/src/screens/reporting-retained-amounts.tsx)
- Evidence: [tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs](../../../tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/EvidenceEndpoints.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/proof-drawer.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/proof-drawer.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/finance-standard-pages-screen.tsx](../../../src/Meridian.Ui/dashboard/src/screens/finance-standard-pages-screen.tsx)
- Evidence: [src/Meridian.Wpf/Controls/LedgerAmountProofDrawer.xaml](../../../src/Meridian.Wpf/Controls/LedgerAmountProofDrawer.xaml)
- Evidence: [src/Meridian.Wpf/ViewModels/PostedLedgerViewModel.AmountProof.cs](../../../src/Meridian.Wpf/ViewModels/PostedLedgerViewModel.AmountProof.cs)
- Evidence: [tests/Meridian.Tests/Ui/LedgerAmountProvenanceServiceTests.cs](../../../tests/Meridian.Tests/Ui/LedgerAmountProvenanceServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs](../../../tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/LedgerAmountProvenanceEndpointTests.cs](../../../tests/Meridian.Tests/Ui/LedgerAmountProvenanceEndpointTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/LedgerAmountProofInteractionTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/LedgerAmountProofInteractionTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/PostedLedgerBasisArrivalTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/PostedLedgerBasisArrivalTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/proof-drawer.test.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/proof-drawer.test.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/finance-standard-pages-screen.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/finance-standard-pages-screen.test.tsx)
- Evidence: [docs/testing/w10-amount-provenance.md](../../testing/w10-amount-provenance.md)


### Exit Criteria

- Any amount an operator sees can open its provenance from the surface it appears on, through one shared component rather than a per-screen variant.
- Provenance is served through the existing subject-addressed evidence surface rather than a parallel endpoint family beside it.
- The passport reflects retained evidence linked by stable subject and evidence identifiers scoped to the fund, ledger book, and period it belongs to, rather than inferring from surface labels or matching on names, symbols, and prose.
- Missing or stale provenance renders as review-required or blocked rather than an empty view that reads as proven.
- This item feeds W5X-OEG-001 rather than duplicating it, and states its WPF slice or an explicit deferral.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the evidence subject, the shared drawer, the browser surface, and the missing-evidence tests.

### Source Modules

- `SRC-UI-SHARED`
- `SRC-CONTRACTS`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-002 - Break clustering and bulk-resolution activation
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

W10-RECON-002: planned; health green; evidence planned_evidence. Activate existing bulk casework and retain break classification through the shared queue; recorded exit criteria remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-RECON-002.md)


### Exit Criteria

- An operator can resolve a group of related breaks in one governed action instead of one action per break.
- Every member of a resolved group retains its own case transition, actor, and evidence alongside the shared justification.
- A group requires the same approval separation an individual material break requires whenever any member is material or high-risk, and any additional group threshold is measured on gross exposure rather than a signed net that can offset a material member below it. That exposure is only ever aggregated across members whose amounts are genuinely comparable - value exposure within one currency or translated under a governed rate, and quantity exposure only across members that name the same instrument under a governed quantity unit - and a member whose instrument, unit, or rate cannot be established holds the group at the stricter approval rather than being summed into a total that mixes shares of one security with shares of another, or shares with cash.
- A preview shows the effect of a group action before any state changes, and the action that follows applies only to the breaks the preview showed in the state it showed them; drift returns the operator to a fresh preview rather than through.
- Break classification is queryable on a stored break rather than readable only in prose.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the persisted classification, the grouping projection, the wired bulk surface, and the per-break evidence retention tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-JRNL-001 - Durable recurring journal schedules and draft runner
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-11-11 |

### Current Summary

W10-JRNL-001: in_progress; health green; evidence in_progress. Recurring journals create governed drafts only; no submit/approve/post authority. Acceptance remains open; occurrence-store support is processes sharing one filesystem root.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-JRNL-001.md)
- Evidence: [docs/domain/recurring-journal.md](../../domain/recurring-journal.md)
- Evidence: [src/Meridian.FinancialOperations/FundAdministration/RecurringJournalState.cs](../../../src/Meridian.FinancialOperations/FundAdministration/RecurringJournalState.cs)
- Evidence: [src/Meridian.FinancialOperations/FundAdministration/FileRecurringJournalStore.cs](../../../src/Meridian.FinancialOperations/FundAdministration/FileRecurringJournalStore.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/AutomatedJournalScheduledWorker.cs](../../../src/Meridian.Ui.Shared/Services/AutomatedJournalScheduledWorker.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/RecurringJournalRunner.cs](../../../src/Meridian.Ui.Shared/Services/RecurringJournalRunner.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/RecurringJournalCalculations.cs](../../../src/Meridian.Ui.Shared/Services/RecurringJournalCalculations.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/RecurringJournalPeriodAuthority.cs](../../../src/Meridian.Ui.Shared/Services/RecurringJournalPeriodAuthority.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/RecurringJournalSubjectAuthority.cs](../../../src/Meridian.Ui.Shared/Services/RecurringJournalSubjectAuthority.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.PeriodLockOwner.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.PeriodLockOwner.cs)
- Evidence: [src/Meridian.Ledger/RecurringJournalEvidence.cs](../../../src/Meridian.Ledger/RecurringJournalEvidence.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Lifecycle.cs](../../../src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Lifecycle.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Recurring.cs](../../../src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.Recurring.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs](../../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.RecurringJournals.cs)
- Evidence: [src/Meridian.Contracts/Workstation/RecurringJournalDtos.cs](../../../src/Meridian.Contracts/Workstation/RecurringJournalDtos.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/accounting/RecurringJournalQueue.tsx](../../../src/Meridian.Ui/dashboard/src/components/accounting/RecurringJournalQueue.tsx)
- Evidence: [src/Meridian.Wpf/ViewModels/Accounting/RecurringJournalQueueViewModel.cs](../../../src/Meridian.Wpf/ViewModels/Accounting/RecurringJournalQueueViewModel.cs)
- Evidence: [src/Meridian.Wpf/Views/AccountingConfigurePage.xaml](../../../src/Meridian.Wpf/Views/AccountingConfigurePage.xaml)
- Evidence: [tests/Meridian.Tests/FinancialOperations/RecurringJournalStoreTests.cs](../../../tests/Meridian.Tests/FinancialOperations/RecurringJournalStoreTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RecurringJournalRunnerTests.cs](../../../tests/Meridian.Tests/Ui/RecurringJournalRunnerTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RecurringJournalLifecycleTests.cs](../../../tests/Meridian.Tests/Ui/RecurringJournalLifecycleTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RecurringJournalCalculationTests.cs](../../../tests/Meridian.Tests/Ui/RecurringJournalCalculationTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/RecurringJournalCalculationPostgresTests.cs](../../../tests/Meridian.Tests/Storage/RecurringJournalCalculationPostgresTests.cs)
- Evidence: [tests/fixtures/recurring-journal/golden/calculated-period-drafts.json](../../../tests/fixtures/recurring-journal/golden/calculated-period-drafts.json)
- Evidence: [tests/Meridian.Tests/Architecture/PostingCalculationConnectionTests.cs](../../../tests/Meridian.Tests/Architecture/PostingCalculationConnectionTests.cs)
- Evidence: [src/Meridian.FinancialOperations/FundAdministration/FundAdministrationRegisters.cs](../../../src/Meridian.FinancialOperations/FundAdministration/FundAdministrationRegisters.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/PrivateCapitalEventIntakes.cs](../../../src/Meridian.Ui.Shared/Services/PrivateCapitalEventIntakes.cs)
- Evidence: [tests/Meridian.Tests/Ui/PrivateCapitalEventIntakeTests.cs](../../../tests/Meridian.Tests/Ui/PrivateCapitalEventIntakeTests.cs)
- Evidence: [src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitService.cs](../../../src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitService.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitServiceTests.RecurringJournals.cs](../../../tests/Meridian.Tests/FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitServiceTests.RecurringJournals.cs)
- Evidence: [tests/Meridian.Tests/Ledger/RecurringJournalEvidenceTests.cs](../../../tests/Meridian.Tests/Ledger/RecurringJournalEvidenceTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/RecurringJournalPeriodAuthorityPostgresTests.cs](../../../tests/Meridian.Tests/Storage/RecurringJournalPeriodAuthorityPostgresTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/RecurringJournalSubjectAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/RecurringJournalSubjectAuthorityTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.RecurringJournals.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.RecurringJournals.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/accounting/RecurringJournalQueue.test.tsx](../../../src/Meridian.Ui/dashboard/src/components/accounting/RecurringJournalQueue.test.tsx)
- Evidence: [src/Meridian.Ui/dashboard/src/lib/api/recurring-journals.api.test.ts](../../../src/Meridian.Ui/dashboard/src/lib/api/recurring-journals.api.test.ts)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/RecurringJournalQueueViewModelTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/RecurringJournalQueueViewModelTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Services/WorkstationRecurringJournalQueueClientTests.cs](../../../tests/Meridian.Wpf.Tests/Services/WorkstationRecurringJournalQueueClientTests.cs)


### Exit Criteria

- Recurring schedules, their posting history, the template versions occurrences materialize from, and the period-lock state they honor all survive a restart, and the runner fails closed when any of that durable state is unavailable rather than treating a locked period as open.
- A due occurrence becomes a draft for human approval and never posts on its own, and a repeated run can produce neither a duplicate posting nor a second approval draft for the same occurrence - a retry claims or returns the draft that already exists, provided that draft was materialized from the same schedule and template definition the retry resolves. An occurrence claim that names an earlier definition fails closed until the existing draft is explicitly superseded or corrected, rather than silently returning a draft built from content the schedule no longer has.
- A recurring draft cannot reach approval without retained source evidence, and that evidence stays attached through posting.
- Occurrences blocked by a locked period name the lock owner and the governed reopen path rather than failing silently.
- The accounting draft queue shows what the calendar generated, what awaits approval, and what a period lock blocked.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the durable store, the worker, the evidence gate, the draft queue on both workstation lanes, and the restart and idempotency tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-TAX-001 - Tax character, wash-sale, and lot-relief operator surface
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-28 |

### Current Summary

W10-TAX-001: planned; health green; evidence planned_evidence. Retained-disposal read slice is bounded. WPF, pending-disposal comparisons, full period reproducibility and governed policy changes remain open; full status stays planned.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3104](https://github.com/rodoHasArrived/Meridian-main/pull/3104)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-TAX-001.md)
- Evidence: [src/Meridian.Contracts/Ledger/LedgerDisposalTaxDtos.cs](../../../src/Meridian.Contracts/Ledger/LedgerDisposalTaxDtos.cs)
- Evidence: [src/Meridian.Reporting.ReadModels/LedgerDisposalTaxReadService.cs](../../../src/Meridian.Reporting.ReadModels/LedgerDisposalTaxReadService.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Tax.cs](../../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.Tax.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.TaxLotDisposalHistory.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.TaxLotDisposalHistory.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/journal-entry-tax-results.tsx](../../../src/Meridian.Ui/dashboard/src/screens/journal-entry-tax-results.tsx)
- Evidence: [tests/Meridian.Tests/Ui/LedgerDisposalTaxReadServiceTests.cs](../../../tests/Meridian.Tests/Ui/LedgerDisposalTaxReadServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/LedgerDisposalTaxEndpointTests.cs](../../../tests/Meridian.Tests/Ui/LedgerDisposalTaxEndpointTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.TaxResultHistory.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.TaxResultHistory.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.TaxEconomics.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.TaxEconomics.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/journal-entry-tax-results.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/journal-entry-tax-results.test.tsx)


### Exit Criteria

- Tax character, holding period, and wash-sale impact are visible on the positions and disposals they belong to rather than only inside an export artifact.
- An operator comparing cost-basis relief methods for a pending disposal sees realized gain, character split, and wash-sale exposure per method, and any figure the engine cannot yet compute completely renders as incomplete rather than as a settled zero. A disposal that could still attract a wash sale while its replacement window is open - one a wash-sale policy actually governs on its own sale date, that either realizes a loss or holds mixed gain and loss lots meaning a loss cannot yet be excluded, and whose known replacement acquisitions do not yet cover the entire quantity sold - is labelled provisional with the remaining window shown and is re-evaluated or governed-finalized once that window closes. A disposal no governing policy reaches, whose every relieved lot realizes a gain, or whose matched quantity and disallowed loss have already reached their maximum, reads as settled rather than being marked provisional for a window that cannot change it.
- Reopening or regenerating an earlier period reproduces the tax figures originally reported, including after a relief-policy change.
- Changing an account standing relief policy requires approval and a retained rationale, and every surface presents the comparison as decision support rather than tax advice.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the operator surfaces on both workstation lanes, the relief comparison, the reproducibility guarantee, and the character and wash-sale tests.

### Source Modules

- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-SEAM-001 - Unified close-readiness projection behind one shared contract
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | in_progress |
| Last reviewed | 2026-10-28 |

### Current Summary

W10-SEAM-001: in_progress; health green; evidence in_progress. Shared authority gates close and publication; retained automated candidate evidence supplies no live operator acceptance or certification of later changes.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3108](https://github.com/rodoHasArrived/Meridian-main/pull/3108)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-SEAM-001.md)
- Evidence: [src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs](../../../src/Meridian.Contracts/Workstation/CloseReadinessDtos.cs)
- Evidence: [src/Meridian.FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.CloseInputs.cs](../../../src/Meridian.FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.CloseInputs.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseReadiness.cs](../../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.CloseReadiness.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.close-command-center.view-model.ts](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.close-command-center.view-model.ts)
- Evidence: [src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs](../../../src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.ScopeTests.cs](../../../tests/Meridian.Tests/FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.ScopeTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.AcceptanceTests.cs](../../../tests/Meridian.Tests/FinancialOperations/OperationsContinuity/FinancialOperationsCommandCenterReadService.AcceptanceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/CloseReadinessSubjectSourceTests.cs](../../../tests/Meridian.Tests/Ui/CloseReadinessSubjectSourceTests.cs)
- Evidence: [src/Meridian.Ui.Services/Services/Accounting/WorkstationAccountingCloseApiClient.cs](../../../src/Meridian.Ui.Services/Services/Accounting/WorkstationAccountingCloseApiClient.cs)
- Evidence: [src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitService.EvidenceScope.cs](../../../src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitService.EvidenceScope.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Features/Accounting/AccountingCloseHttpRecoveryTests.cs](../../../tests/Meridian.Wpf.Tests/Features/Accounting/AccountingCloseHttpRecoveryTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitServiceTests.EvidenceScope.cs](../../../tests/Meridian.Tests/FinancialOperations/PrivateCapital/PrivateCapitalCloseCockpitServiceTests.EvidenceScope.cs)
- Evidence: [docs/testing/evidence/w10-seam-reviewed-20261007/README.md](../../testing/evidence/w10-seam-reviewed-20261007/README.md)


### Exit Criteria

- One close-readiness projection is the shared source both workstation lanes consume rather than each lane deriving its own, and the caller or an explicit operator selection establishes the complete close scope - fund profile, ledger book, fund account, entity, and period - rather than the projection inferring any dimension from whichever workflow it happened to select; every contribution answers for that declared scope, and a dimension that is missing, ambiguous, or mismatched blocks the projection rather than being merged into it.
- Every blocker names its type, count, severity, owner, and the records causing it.
- A contributing lane that is unregistered, failing, stale, or out of scope makes the projection incomplete and blocking rather than silently absent, so readiness is never reported because a contributor did not answer.
- The contributing services report into that projection instead of publishing independent readiness vocabularies, and the asset-class coverage service no longer reads as close readiness.
- The sequencing handshake with W8-WPF-PARITY-001 is recorded so the client-side aggregation is retired rather than duplicated into the desktop lane.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the shared contract, the contributing services, both workstation consumers, and the blocker-projection tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-CONTRACTS`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-003 - Unified tolerance model and what-if replay workbench
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

W10-RECON-003: planned; health green; evidence planned_evidence. Matching-tolerance preview requires complete propagation and retained comparison evidence before operator acceptance.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-RECON-003.md)


### Exit Criteria

- One tolerance model reaches the matching engine, so every tolerance an operator can configure is one the engine actually applies.
- Tolerances can be scoped to the account, currency, and transaction type they belong to, and activating one is a governed policy change under the charter's straight-through conditions - versioned approval with materiality caps, retained evidence reversible through governed correction, sampling review and a kill switch - rather than an operator-local edit, with material or high-risk classes still resolved per item.
- An operator can see the effect of a proposed tolerance change against retained runs before committing it, and what commits is the profile version the simulation previewed rather than whatever the profile has become since; drift returns the operator to a fresh preview, and the retained simulation is the justification for that exact committed profile.
- A replayed result differs from the original run only because the tolerance differs, which requires both sides of that run to be retained as of it - the external statement population and the internal positions, cash, and ledger activity it was matched against - and replay determinism is proven before any simulation result reaches an operator.
- Run-artifact retention reuses the existing stores rather than introducing a second artifact vocabulary and storage path.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the unified model, the retained artifacts, the determinism proof, and the preview surface.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-RECON-004 - Operator-taught match rules with promotion gate
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

W10-RECON-004: planned; health green; evidence planned_evidence. Operator-taught matches remain candidate rules until governed promotion; retained exit criteria define activation.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-RECON-004.md)


### Exit Criteria

- A manual match teaches a candidate rule that never acts until an operator promotes it, and a promoted rule runs only inside the straight-through conditions the design charter requires - a human-approved versioned policy defining the eligible class with materiality caps, full retained evidence that stays reversible through governed correction, sampling review and a kill switch, and material or high-risk breaks still resolved per item.
- Promotion requires adjudicated outcomes meeting a stated sample size and precision bound, and a raw hit count is never sufficient on its own.
- A promoted rule retains every immutable identity predicate its match kind enforces - record kind, instrument, and currency - so it can never match across a boundary the underlying matcher keeps separate.
- Every match a learned rule produces names that rule and its promoting operator in retained evidence that survives a restart.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the candidate capture, the promotion gate, the engine seam, and the attribution tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DOMAIN`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-PERF-001 - Portfolio and investor return measurement
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-07-31 |

### Current Summary

W10-PERF-001: planned; health green; evidence planned_evidence. Extend or supersede the existing brokerage return seam; complete retained period, cash-flow and acceptance requirements before claiming fund performance.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-PERF-001.md)


### Exit Criteria

- Operators and investors can see portfolio and investor-level returns derived from Meridian's own records rather than only from a brokerage feed.
- The existing brokerage performance seam is extended or explicitly superseded with the reason recorded, and no parallel performance API is introduced beside it.
- Reported returns are correct across periods containing external capital flows, and any convention approximation is documented and labeled on the figure.
- A return spanning more than one currency is either reported per currency or translated into one declared presentation currency at each flow's and the terminal value's as-of rate; nominal amounts in different currencies are never summed.
- Returns resting on activity the ledger has not posted, or on incomplete inputs including a missing translation rate, render as labeled pro-forma or review-required rather than as the reported return, and every figure names its convention, period, currency, and completeness. A money-weighted return that fails to converge, or whose cash-flow series admits more than one rate, is review-required under a declared convention rather than a single reported number.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the brokerage-seam disposition, the shared kernel, the operator surface, and golden-file tests of the return kernels against worked examples.

### Source Modules

- `SRC-BACKTESTING`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-FSHARP`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-CONSOL-001 - Intercompany elimination on consolidated ledger views
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-11-13 |

### Current Summary

W10-CONSOL-001: planned; health green; evidence planned_evidence. Retained consolidation slices are bounded implementation; exact versioned policy, participating entities and acceptance requirements remain in the linked detail.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-CONSOL-001.md)
- Evidence: [docs/domain/intercompany-consolidation.md](../../domain/intercompany-consolidation.md)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationService.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationService.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ConsolidationWorkbenchService.cs](../../../src/Meridian.Ui.Shared/Services/ConsolidationWorkbenchService.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Consolidation.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Consolidation.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.consolidation-panel.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.consolidation-panel.tsx)
- Evidence: [tests/Meridian.Tests/Storage/ConsolidationSourcesPostgresTests.cs](../../../tests/Meridian.Tests/Storage/ConsolidationSourcesPostgresTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.consolidation-panel.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.consolidation-panel.test.tsx)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Governed.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Governed.cs)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationFxTranslator.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationFxTranslator.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/Accounting/EntityConsolidationViewModel.cs](../../../src/Meridian.Wpf/ViewModels/Accounting/EntityConsolidationViewModel.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedServiceTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedServiceTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationFxTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationFxTests.cs)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationTransitionService.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationTransitionService.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationTransitionServiceTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationTransitionServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/ConsolidationBookCreationTests.cs](../../../tests/Meridian.Tests/Storage/ConsolidationBookCreationTests.cs)
- Evidence: [src/Meridian.FinancialOperations/Intercompany/IntercompanyObligationMonitorService.cs](../../../src/Meridian.FinancialOperations/Intercompany/IntercompanyObligationMonitorService.cs)
- Evidence: [tests/Meridian.Tests/Integration/IntercompanyAgreementStorePostgresTests.StructuredTerms.cs](../../../tests/Meridian.Tests/Integration/IntercompanyAgreementStorePostgresTests.StructuredTerms.cs)


### Exit Criteria

- Consolidated views no longer double-count intercompany balances and state whether they are presenting gross or eliminated figures, and an eliminated figure reflects only approved and posted eliminations, with a proposed draft visible solely as a labeled preview.
- Eliminations are proposed as reviewable drafts on the existing approval rail and never post automatically, driven by a rule on the existing policy seam with no parallel discriminator beside it. Approval fails closed when either source balance has moved since the draft was computed, and a posted elimination is corrected through a linked reversing or adjusting entry rather than replaced in place.
- Rerunning the same perimeter and as-of date cannot produce a duplicate elimination for the same pair, and a pair is an exact reciprocal match on both the posting entity and its counterparty rather than the counterparty alone, with a missing or ambiguous dimension blocking the draft.
- The consolidation perimeter is enforced from authoritative ownership data rather than asserted, and an entity outside scope is rejected rather than silently eliminated.
- Balances are compared and eliminated in one declared consolidation currency translated at governed as-of rates, with translation differences retained separately and elimination blocked when a rate is unavailable, so a currency gap never reads as an intercompany mismatch. Balances that still do not agree between the two sides surface as their own reported break class rather than being netted away.
- Roadmap status remains planned until this item links implementation paths and concrete evidence entries for the treatment rule, the perimeter enforcement, the elimination drafts, and the unmatched-intercompany tests.

### Source Modules

- `SRC-STORAGE-ACCOUNTING`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W10-LOT-002 - Security-identified open-lot convergence
| Field | Value |
| --- | --- |
| Wave | W10 |
| Status | in_progress |
| Health | green |
| Priority | critical |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-10-28 |

### Current Summary

W10-LOT-002: in_progress; health green; evidence in_progress. Canonical lots and bounded amortization are implemented; consumer parity, broader corrections/successor schedules, compatibility retirement, independent reviews and live shadow acceptance remain open.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3102](https://github.com/rodoHasArrived/Meridian-main/pull/3102), [#3296](https://github.com/rodoHasArrived/Meridian-main/pull/3296), [#3328](https://github.com/rodoHasArrived/Meridian-main/pull/3328)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W10-LOT-002.md)
- Evidence: [src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.AssetApproval.cs](../../../src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.AssetApproval.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.AssetPosting.cs](../../../src/Meridian.Ui.Shared/Services/ManualJournalEntryWorkbenchService.AssetPosting.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.FactorPaydownReplay.cs](../../../src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.FactorPaydownReplay.cs)
- Evidence: [src/Meridian.Storage/Ledger/RetainedPostingEquivalence.cs](../../../src/Meridian.Storage/Ledger/RetainedPostingEquivalence.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.asset-workflow.ts](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.asset-workflow.ts)
- Evidence: [src/Meridian.Wpf/ViewModels/Accounting/FactorPaydownViewModel.Approval.cs](../../../src/Meridian.Wpf/ViewModels/Accounting/FactorPaydownViewModel.Approval.cs)
- Evidence: [tests/Meridian.Tests/Ui/AssetAccountingApprovalBridgeTests.cs](../../../tests/Meridian.Tests/Ui/AssetAccountingApprovalBridgeTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AssetApprovalBridge.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AssetApprovalBridge.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/FactorPaydownViewModelTests.Approval.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/FactorPaydownViewModelTests.Approval.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.FactorPaydown.cs](../../../src/Meridian.Ui.Shared/Endpoints/LedgerEndpoints.FactorPaydown.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.PreparationRevision.cs](../../../src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.PreparationRevision.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.FactorPaydownRecovery.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.FactorPaydownRecovery.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.PreparationRecovery.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.PreparationRecovery.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.factor-paydown-panel.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.factor-paydown-panel.tsx)
- Evidence: [src/Meridian.Wpf/ViewModels/Accounting/FactorPaydownViewModel.cs](../../../src/Meridian.Wpf/ViewModels/Accounting/FactorPaydownViewModel.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotSuccessors.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotSuccessors.cs)
- Evidence: [src/Meridian.Instruments/AssetOperations/CorporateActionAssetAccountingEventMapper.cs](../../../src/Meridian.Instruments/AssetOperations/CorporateActionAssetAccountingEventMapper.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.CorporateAction.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.CorporateAction.cs)
- Evidence: [src/Meridian.Storage/Ledger/CorporateActionSuccessorAncestry.cs](../../../src/Meridian.Storage/Ledger/CorporateActionSuccessorAncestry.cs)
- Evidence: [src/Meridian.Storage/Ledger/Migrations/V_ledger_042__atomic_lot_successors.sql](../../../src/Meridian.Storage/Ledger/Migrations/V_ledger_042__atomic_lot_successors.sql)
- Evidence: [src/Meridian.Storage/Ledger/Migrations/V_ledger_043__amortization_reversal_basis_restoration.sql](../../../src/Meridian.Storage/Ledger/Migrations/V_ledger_043__amortization_reversal_basis_restoration.sql)
- Evidence: [src/Meridian.Ui.Shared/Services/LedgerReportingAuthoritativeSource.Successors.cs](../../../src/Meridian.Ui.Shared/Services/LedgerReportingAuthoritativeSource.Successors.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotSuccessorTests.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotSuccessorTests.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotSpinOffTests.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotSpinOffTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SpinOff.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SpinOff.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotSuccessorTests.CorporateActions.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotSuccessorTests.CorporateActions.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/CanonicalLotCorporateActionServiceTests.cs](../../../tests/Meridian.Tests/AssetOperations/CanonicalLotCorporateActionServiceTests.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AmortizationReversalPreviewTests.cs](../../../tests/Meridian.Tests/AssetOperations/AmortizationReversalPreviewTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Amortization.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Amortization.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorScope.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorScope.cs)
- Evidence: [tests/Meridian.Tests/Storage/CorporateActionSuccessorAncestryTests.cs](../../../tests/Meridian.Tests/Storage/CorporateActionSuccessorAncestryTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorAncestry.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorAncestry.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorGuards.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorGuards.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorPublication.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorPublication.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotSuccessorMigrationTests.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotSuccessorMigrationTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/CanonicalOpenLotConsumerTests.CorporateActions.cs](../../../tests/Meridian.Tests/Storage/CanonicalOpenLotConsumerTests.CorporateActions.cs)
- Evidence: [tests/Meridian.Tests/Ui/LedgerReportingAuthoritativeSourceTests.SuccessorReportPack.cs](../../../tests/Meridian.Tests/Ui/LedgerReportingAuthoritativeSourceTests.SuccessorReportPack.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Successors.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Successors.cs)
- Evidence: [tests/Meridian.Tests/Ui/LedgerReportingAuthoritativeSourceSuccessorTests.cs](../../../tests/Meridian.Tests/Ui/LedgerReportingAuthoritativeSourceSuccessorTests.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.DiscreteLotRelief.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.DiscreteLotRelief.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.CurrentBasis.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.CurrentBasis.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AverageCostRelief.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.AverageCostRelief.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AverageCost.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AverageCost.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotDto.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotDto.cs)
- Evidence: [src/Meridian.Storage/Ledger/LedgerOpenLotProjection.cs](../../../src/Meridian.Storage/Ledger/LedgerOpenLotProjection.cs)
- Evidence: [src/Meridian.Ledger/OpenLotReliefService.cs](../../../src/Meridian.Ledger/OpenLotReliefService.cs)
- Evidence: [tests/Meridian.Tests/Storage/OpenLotConvergenceTests.cs](../../../tests/Meridian.Tests/Storage/OpenLotConvergenceTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/OpenLotPostgresTests.cs](../../../tests/Meridian.Tests/Storage/OpenLotPostgresTests.cs)
- Evidence: [docs/engineering/blueprints/security-lot-convergence-blueprint.md](../../engineering/blueprints/security-lot-convergence-blueprint.md)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotBackfillDtos.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotBackfillDtos.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.OpenLotBackfill.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.OpenLotBackfill.cs)
- Evidence: [src/Meridian.Storage/Ledger/CanonicalOpenLotDisposalGuard.cs](../../../src/Meridian.Storage/Ledger/CanonicalOpenLotDisposalGuard.cs)
- Evidence: [tests/Meridian.Tests/Storage/CanonicalOpenLotConsumerTests.cs](../../../tests/Meridian.Tests/Storage/CanonicalOpenLotConsumerTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/WorkstationEndpointsTests.OpenLotBackfill.cs](../../../tests/Meridian.Tests/Ui/WorkstationEndpointsTests.OpenLotBackfill.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.cs](../../../src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AssetAcquisitionLotPostgresRoundTripTests.cs](../../../tests/Meridian.Tests/AssetOperations/AssetAcquisitionLotPostgresRoundTripTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AcquisitionAdmission.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.AcquisitionAdmission.cs)
- Evidence: [src/Meridian.Contracts/AssetOperations/AssetAcquisitionTermsCompletionDto.cs](../../../src/Meridian.Contracts/AssetOperations/AssetAcquisitionTermsCompletionDto.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/SpineAcquisitionLotCompletion.cs](../../../src/Meridian.FinancialOperations/Ledger/SpineAcquisitionLotCompletion.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AcquisitionTermsCompletionTests.cs](../../../tests/Meridian.Tests/AssetOperations/AcquisitionTermsCompletionTests.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AssetAcquisitionLotPostgresRoundTripTests.Completion.cs](../../../tests/Meridian.Tests/AssetOperations/AssetAcquisitionLotPostgresRoundTripTests.Completion.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotAmortization.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotAmortization.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/CanonicalLotAmortizationService.cs](../../../src/Meridian.FinancialOperations/Ledger/CanonicalLotAmortizationService.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Amortization.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.Amortization.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Amortization.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.Amortization.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/AmortizationLotInstructionContractTests.cs](../../../tests/Meridian.Tests/AssetOperations/AmortizationLotInstructionContractTests.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotAmortizationContinuation.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotAmortizationContinuation.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotAmortizationContinuationTests.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotAmortizationContinuationTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorAmortization.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.SuccessorAmortization.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotCapitalReturn.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotCapitalReturn.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.CapitalReturn.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.CapitalReturn.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.CapitalReturn.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.CapitalReturn.cs)
- Evidence: [tests/Meridian.Tests/Ledger/LedgerTaxLotExactBasisReliefTests.cs](../../../tests/Meridian.Tests/Ledger/LedgerTaxLotExactBasisReliefTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.DirectWrites.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.DirectWrites.cs)
- Evidence: [src/Meridian.Contracts/Accounting/Lots/OpenLotFactorPaydown.cs](../../../src/Meridian.Contracts/Accounting/Lots/OpenLotFactorPaydown.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.FactorPaydown.cs](../../../src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.FactorPaydown.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/FactorPaydownLotInstructionService.cs](../../../src/Meridian.FinancialOperations/Ledger/FactorPaydownLotInstructionService.cs)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotFactorPaydownTests.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotFactorPaydownTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.FactorPaydown.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.FactorPaydown.cs)
- Evidence: [tests/Meridian.Tests/Application/Composition/FactorPaydownLotInstructionCompositionTests.cs](../../../tests/Meridian.Tests/Application/Composition/FactorPaydownLotInstructionCompositionTests.cs)
- Evidence: [src/Meridian.Storage/Ledger/SuccessorAmortizationEvidence.cs](../../../src/Meridian.Storage/Ledger/SuccessorAmortizationEvidence.cs)
- Evidence: [src/Meridian.Storage/Ledger/Migrations/V_ledger_050__atomic_lot_capital_return.sql](../../../src/Meridian.Storage/Ledger/Migrations/V_ledger_050__atomic_lot_capital_return.sql)
- Evidence: [tests/Meridian.Tests/AssetOperations/OpenLotCapitalReturnTests.cs](../../../tests/Meridian.Tests/AssetOperations/OpenLotCapitalReturnTests.cs)
- Evidence: [src/Meridian.Storage/Ledger/Migrations/V_ledger_053__atomic_lot_factor_paydown.sql](../../../src/Meridian.Storage/Ledger/Migrations/V_ledger_053__atomic_lot_factor_paydown.sql)
- Evidence: [tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.FactorPaydownPublication.cs](../../../tests/Meridian.Tests/Storage/AtomicTaxLotJournalStoreTests.FactorPaydownPublication.cs)
- Evidence: [src/Meridian.Application/SecurityMaster/CashFlow/SecurityPrincipalBalance.cs](../../../src/Meridian.Application/SecurityMaster/CashFlow/SecurityPrincipalBalance.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/QuotedPriceValuationModelTests.cs](../../../tests/Meridian.Tests/SecurityMaster/QuotedPriceValuationModelTests.cs)


### Exit Criteria

- Every authoritative open lot requires SecurityId and BookPositionId, uses decimal original/open quantity, and declares Units or Face quantity basis; symbols remain effective-dated display evidence only.
- Acquisition currency, functional currency, transaction-to-functional acquisition FX, transaction basis, and functional basis are retained and restatable without using a current market FX rate.
- FIFO, LIFO, HIFO, SpecificId, and AverageCost relief plus face-value factor and constant-yield amortization operate through one canonical contract with parity evidence for Execution, Ledger, Backtesting, and Reporting.
- Ticker changes preserve lot identity, while splits, exchanges, mergers, spin-offs, returns of capital, paydowns, redemptions, and advance refundings produce append-only predecessor/successor mutations that conserve basis and holding period under approved rules.
- Acquisition, disposal, amortization, and corporate-action lot mutations commit atomically with immutable journals, retained evidence, optimistic versions, idempotency fingerprints, and correction lineage.
- Legacy rows are backfilled only from retained evidence; unresolved SecurityId, quantity-basis, or acquisition-FX facts enter a governed exception queue and never receive synthetic defaults.
- The advance-refunding acceptance scenario produces unrefunded and pre-refunded successor lots with allocated book basis and holding period, distinguishes Schedule D treatment, and reconciles through retained report evidence.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-EXECUTION`
- `SRC-EXECUTION-SDK`
- `SRC-BACKTESTING`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W11-PASSPORT-001 - Number Passport contract across amount surfaces
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Workstation Shell and UX |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-16 |

### Current Summary

W11-PASSPORT-001: planned; health green; evidence planned_evidence. Generalize the posted-ledger proof slice into the charter Number Passport contract; planned scope confers no delivery acceptance.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-PASSPORT-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- One versioned Number Passport contract in Meridian.Contracts carries a stable subject identifier, subject kind, amount, currency, basis, as-of and effective dates, the complete declared scope (tenant, organization, entity, fund, book, period), an evidence status drawn from the shared vocabulary, freshness, and links to journal entries, source records, approvals, and reconciliation state.
- Report-line totals, capital-account values, and position valuations open their passport through the same subject-addressed evidence API and the same shared component that W10-PROV-001 delivers for ledger amounts, on both the browser and WPF workstations.
- A passport whose subject, scope, or evidence cannot be resolved exactly renders as review-required, blocked, stale, or missing, never as ready, and names the missing link.
- Period-over-period change on a passport is computed from retained snapshots rather than recomputed from current state.
- Roadmap status remains planned until W10-PROV-001 is accepted and this item links the contract, the shared endpoint, both workstation surfaces, and the scope-mismatch and missing-evidence tests.

### Source Modules

- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`
- `SRC-LEDGER`
- `SRC-DESIGN-REPORTING`

## W11-COVER-001 - Verified Coverage measurement and operator surface
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | planned |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-16 |

### Current Summary

W11-COVER-001: planned; health green; evidence planned_evidence. Implement deterministic Verified Coverage over passports and shared close readiness; preserve the charter denominator and missing-evidence rules.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-COVER-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- Verified Coverage is computed exactly as the design charter Section 1.5 defines it, by absolute reported value and by item count, for a declared tenant, entity, book, and period, and both figures are shown together.
- An item counts as verified only when its passport evidence status is ready, its reconciliation is current for the period, its approval is recorded, and its supporting evidence is retained; every other item is listed with the reason it is excluded.
- The figure is reproducible - recomputing it from the same retained snapshot yields the same value - and every published value records the snapshot, policy version, and computation time it came from.
- Simulated, fixture, or demo data is excluded from the numerator and labeled wherever the figure appears.
- The browser and WPF workstations consume one shared projection rather than computing coverage locally.
- Roadmap status remains planned until W11-PASSPORT-001 and W10-SEAM-001 are accepted and this item links the projection, both surfaces, and the reproducibility and exclusion tests.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W11-STAKE-001 - Governed stakeholder verification access on delivered packages
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Platform Security and Governance |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-16 |

### Current Summary

W11-STAKE-001: planned; health green; evidence planned_evidence. Planned read-only package evidence drill-through for recipients; retained authority and acceptance boundaries apply.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-STAKE-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- A grant binds one external principal to one delivered package version with an expiry, grantor, rationale, and audit event, through the existing scoped-access assignment model; revocation takes effect immediately.
- The recipient can drill from a package line to its passport and retained evidence, read-only, and cannot see any record outside that frozen package, including later amendments unless separately granted.
- Every view and download by a recipient is recorded as an audit event attached to the package's delivery record.
- No grant can approve, comment on, or change any governed record; the view exposes no write route.
- Roadmap status remains planned until W11-PASSPORT-001 is accepted and this item links the grant model, the read-only surface, and the cross-package isolation and revocation tests.

### Source Modules

- `SRC-DESIGN-IDENTITY`
- `SRC-DESIGN-REPORTING`
- `SRC-DESIGN-AUDIT`
- `SRC-UI-SHARED`
- `SRC-CONTRACTS`

## W11-SHADOW-001 - Shadow-mode onboarding with opening-balance reconciliation
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | in_progress |
| Health | green |
| Priority | medium |
| Owner lane | Data Confidence and Validation |
| Evidence posture | in_progress |
| Last reviewed | 2026-11-13 |

### Current Summary

W11-SHADOW-001: in_progress; health green; evidence in_progress. Shadow onboarding extends the existing read-only workspace and book-of-record guard; retained activation and acceptance gates remain open.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-SHADOW-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [src/Meridian.FinancialOperations/Onboarding/OnboardingWorkspaceService.Shadow.cs](../../../src/Meridian.FinancialOperations/Onboarding/OnboardingWorkspaceService.Shadow.cs)
- Evidence: [src/Meridian.FinancialOperations/Onboarding/OnboardingBookOfRecordGuard.cs](../../../src/Meridian.FinancialOperations/Onboarding/OnboardingBookOfRecordGuard.cs)
- Evidence: [src/Meridian.FinancialOperations/Onboarding/OpeningBalances/OnboardingOpeningSources.cs](../../../src/Meridian.FinancialOperations/Onboarding/OpeningBalances/OnboardingOpeningSources.cs)
- Evidence: [src/Meridian.Contracts/Workstation/OnboardingShadowDtos.cs](../../../src/Meridian.Contracts/Workstation/OnboardingShadowDtos.cs)
- Evidence: [src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs](../../../src/Meridian.Ui.Shared/Endpoints/OnboardingEndpoints.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.onboarding-shadow-panel.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.onboarding-shadow-panel.tsx)
- Evidence: [tests/Meridian.Tests/Ui/OnboardingShadowModeTests.cs](../../../tests/Meridian.Tests/Ui/OnboardingShadowModeTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/OnboardingEndpointTests.cs](../../../tests/Meridian.Tests/Ui/OnboardingEndpointTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/ClosePublicationWorkerAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/ClosePublicationWorkerAuthorityTests.cs)
- Evidence: [tests/Meridian.Tests/Reporting/ReportingGovernanceCanonicalValidationTests.cs](../../../tests/Meridian.Tests/Reporting/ReportingGovernanceCanonicalValidationTests.cs)
- Evidence: [tests/Meridian.Tests/Reconciliation/Connectors/StatementOpeningBalanceRoleTests.cs](../../../tests/Meridian.Tests/Reconciliation/Connectors/StatementOpeningBalanceRoleTests.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/screens/accounting-screen.onboarding-shadow-panel.test.tsx](../../../src/Meridian.Ui/dashboard/src/screens/accounting-screen.onboarding-shadow-panel.test.tsx)
- Evidence: [docs/operators/shadow-mode-onboarding.md](../../operators/shadow-mode-onboarding.md)


### Exit Criteria

- A tenant, entity, or book can be marked shadow; shadow books are labeled everywhere they appear, cannot be published as official records, and cannot release reports or payments as the book of record.
- Opening balances import from an external general ledger or administrator with retained source evidence and reconcile against Meridian's records with sided matching, producing a governed opening-balance reconciliation report.
- Ongoing shadow-period activity reconciles each period against the official books, and the close-readiness projection reports the shadow book's agreement with them.
- Promoting a shadow book to official requires a recorded operator decision with the final reconciliation attached, and the promotion is itself an audit event.
- Roadmap status remains planned until this item links the shadow designation, the opening-balance reconciliation path, the promotion gate, and the labeling and non-publication tests.

### Source Modules

- `SRC-DESIGN-DATA-INTEGRATION`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`

## W11-PACK-001 - Governed contract-pack lifecycle on the Security Master asset-class registry
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-16 |

### Current Summary

W11-PACK-001: planned; health green; evidence planned_evidence. Govern asset-class contract packs from the existing registry; planned scope retains the recorded migration and acceptance requirements.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-PACK-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/architecture/security-master-extensibility-review.md](../../architecture/security-master-extensibility-review.md)


### Exit Criteria

- A contract pack is a versioned, content-hashed unit that declares its asset class, term schema, lifecycle events, cash-flow behavior, valuation methods, accounting rule bindings, validations, and reporting taxonomy, and the existing asset-class registry is its first source.
- A pack moves through Draft, Tested, Reviewed, Approved, Active, Superseded, and Retired, and activation requires passing pack tests and a recorded approval.
- Records keep the pack version they were created under, so activating a new version never silently reinterprets existing terms; migrating records to a new version is an explicit, audited operation.
- The coverage tests that lock the registry to the F# SecurityKind union, validators, and storage writers extend to every active pack.
- Roadmap status remains planned until this item links the pack contract, the lifecycle, the version-pinning behavior, and the activation and reinterpretation tests.

### Source Modules

- `SRC-DESIGN-INSTRUMENTS`
- `SRC-CONTRACTS`
- `SRC-FSHARP`
- `SRC-STORAGE`
- `SRC-LEDGER`

## W11-EXEC-001 - Trading and Execution bounded-context ownership
| Field | Value |
| --- | --- |
| Wave | W11 |
| Status | planned |
| Health | green |
| Priority | low |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-16 |

### Current Summary

W11-EXEC-001: planned; health green; evidence planned_evidence. Record canonical execution ownership while preserving the existing OMS, risk and governed activation boundaries.

### Status References

- Decisions: DEC-CHARTER-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W11-EXEC-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- docs/architecture/module-map.md and docs/architecture/design-module-conformance.md name Trading and Execution as a bounded context, list its owning projects, and state which execution records it owns.
- A dependency test fails when a project outside the context writes execution-owned records directly, instead of going through the published contracts or events.
- The handoff from execution fills to ledger posting is documented as a contract boundary, with the ledger remaining the only source of accounting truth.
- Roadmap status remains planned until this item links the updated maps and the dependency test.

### Source Modules

- `SRC-EXECUTION`
- `SRC-EXECUTION-SDK`
- `SRC-RISK`
- `SRC-STRATEGIES`
- `SRC-BACKTESTING`
- `SRC-CONTRACTS`

## W12-BOOK-001 - Accounting book, basis, and FX control workbench
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-17 |

### Current Summary

W12-BOOK-001: planned; health green; evidence planned_evidence. Unranked fund-operations direction; connect existing book, FX and lot foundations through governed setup and retained acceptance gates.

### Status References

- Decisions: DEC-FUND-OPERATIONS-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-BOOK-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- Full-row acceptance demonstrates two books for one entity with declared supported accounting bases, functional currencies, fiscal calendars, chart mappings, policy versions, and independent period states. Financial-account, ledger-account, and accounting-book identities remain distinct.
- The approved cutover snapshot under charter Section 22.6 reconciles opening balances by book and currency and the quantities, remaining costs, and relevant acquisition history of open lots. Unknown opening state blocks the affected workflow rather than becoming a zero balance or an inferred lot basis.
- A required posting set binds one economic event to its exact approved per-book consequences. A foreign-currency partially settled sale explains basis, trade/settlement timing, lot treatment, and market versus FX differences without aggregating unlike currencies or silently rewriting history. Missing or partially committed consequences remain visible and block outputs requiring the complete set.
- Translation and revaluation retain original and functional amounts, rate direction/date/source revision, rule version, and rounding. Missing, stale, conflicting, or incorrectly directed rate evidence blocks the affected operation; retained-input replay reproduces the result.
- The authoritative write rechecks exact approved scope, input/policy versions, and period authority. Concurrent requests, interrupted commits, and lost acknowledgments follow Section 22.6 and recover committed per-book receipts without duplicate postings; remaining consequences require current authority. Closed periods reject ordinary changes, and corrections retain linked reversal/adjustment history across affected books and reports.
- Configuration cloning creates a new draft and validates target mappings; it cannot copy balances, transactions, credentials, approvals, or period locks. Book controls operate inside the existing supported deployment envelope.
- Browser and WPF use shared book/FX contracts and services. Acceptance follows Section 22.6 and links independently calculated golden results for opening-state reconciliation, the partial-settlement sale, revaluation, and correction, together with durable refusal/recovery evidence for the supported bases.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-ENTITIES`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W12-CAPITAL-001 - Governed investor capital and allocation lifecycle
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | in_progress |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | in_progress |
| Last reviewed | 2026-11-10 |

### Current Summary

W12-CAPITAL-001: in_progress; health green; evidence in_progress. Unranked fund-operations direction; deepen existing capital-account and share-class seams with retained operator and accounting controls.

### Status References

- Decisions: DEC-FUND-OPERATIONS-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-CAPITAL-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/engineering/plans/carta-inspired-capital-lifecycle.md](../../engineering/plans/carta-inspired-capital-lifecycle.md)


### Exit Criteria

- Fund, feeder, SPV, class, series, and investor participation retain effective dates, source terms, and approved policy versions. Commitment, subscription request, approved dealing, unit issuance, and observed cash receipt remain distinct linked events.
- The Section 22.6 cutover snapshot reconciles opening investor capital, units/holdings, and the fee history needed by the selected method, including high-water marks, equalization balances, and carryforwards. Missing history blocks the affected dealing or fee calculation rather than resetting it.
- Allocation binds the period-effective participant set, source balances, and policy-defined order of valuation, expense/fee accrual, crystallization, dealing, and series conversion. Allocated income, gains/losses, expenses, and fees conserve eligible source totals with retained deterministic rounding residuals, including under reordered inputs.
- The selected equalization or series method declares gross versus net NAV, per-unit versus total amounts, hurdle, high-water-mark scope, crystallization dates, and governed investor fee exceptions. One authoritative fee-state record owns each scope's high-water mark and carryforwards; contributed capital cannot become fee-eligible performance, and changed bound inputs invalidate pending approval.
- Transfers, resubscriptions, and series conversions preserve economic value, source/successor identity, relevant economic dates, and fee history. Backdated corrections retain prior issued results and use approved adjustment and restatement chains.
- Each supported master-feeder/SPV graph shape prevents cycles, foreign scope, and duplicate economic inclusion. Legal ownership, economic allocation, and consolidation perimeter remain separate; unsupported structures and fee conventions block rather than infer a treatment.
- Independently calculated golden examples cover different subscription dates, loss/recovery, partial redemption, full redemption at crystallization, and a later subscription. The zero-unit case avoids division by zero, and the selected policy explicitly closes or carries forward fee state without silently reviving or resetting it.
- A durable worked scenario reconciles fund NAV through class/series and investor capital to posted ledger, unit register, and issued statement on both operator surfaces. Section 22.6 acceptance includes exact-scope concurrency and recovery of the same allocation/dealing receipt without duplicate units or capital postings, plus the supported structures and reused W9 evidence.

### Source Modules

- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-ENTITIES`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-LEDGER`
- `SRC-FSHARP-LEDGER`
- `SRC-DESIGN-REPORTING`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W12-CREDIT-001 - DirectLoan operations and evidence reconciliation
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-17 |

### Current Summary

W12-CREDIT-001: planned; health green; evidence planned_evidence. Converge existing Direct Lending terms, servicing and atomic loan/ledger evidence through the governed operator workflow.

### Status References

- Decisions: DEC-FUND-OPERATIONS-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-CREDIT-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md](../../engineering/blueprints/direct-lending-pik-capitalization-and-rate-resets.md)


### Exit Criteria

- Every financial transition binds canonical Security Master identity, exact tenant/company/entity/account/book scope, approved terms and servicing revisions, source observations, and rule versions. Missing or changed authority blocks; servicing cannot create a competing instrument master.
- The Section 22.6 cutover snapshot reconciles contractual and accounting opening measures, including accrued cash/PIK interest, unamortized carrying adjustments, and unapplied receipts. The cutoff and retained prior event/receipt identities prevent pre-cutover activity from being applied again; unknown opening history blocks the affected operation.
- Commitment, funded and outstanding contractual principal, cash-interest receivable, PIK, fees, and carrying value remain distinct. Expected flows and accruals retain interval, principal basis, day-count/calendar, rate source, spread/floor/cap convention, and currency. Expected-flow scenarios may use labeled forecast fixings; posted accruals require approved observed fixings or an explicitly approved estimation policy with retained estimate provenance. Missing observations cannot silently become forecast-derived authoritative amounts, and unsupported inputs block the affected operation.
- Independent borrower/servicer batches retain original payload/hash, source revision, observation cutoff, completeness, normalized rows, mapping, and approval evidence. Changed content under the same revision is rejected, and retained-input replay preserves the original reconciliation and its source operands; absent evidence cannot become an observed zero.
- A mixed remittance conserves cash across principal, interest, fees, penalties, and unapplied cash. PIK is not cash received and discount/premium amortization is not principal repayment. Partial application, retry, or restart cannot duplicate a receipt, servicing application, or journal.
- Ledger-affecting loan events, canonical journals, and required audit records commit atomically under exact approved scope, source/terms versions, and period authority. Section 22.6 concurrency and lost-acknowledgment recovery distinguish an unapplied receipt from an already committed application and recover its canonical receipt. Applicable policy-controlled accruals preserve Section 21; corrections use the approved adjustment/reopen route with linked history.
- Covenant assessments retain the executed term, test rule, units, threshold, observation/test date, borrower evidence, and review disposition. Missing or unsupported clauses require review; breach acknowledgment cannot itself approve default, nonaccrual, restructuring, write-off, or payment.
- Independently calculated fixed-rate and floating-reset golden examples reconcile opening principal plus draws and capitalized PIK less principal repayments to closing principal, and reconcile cash separately by remittance component. Durable cases cover migration cutoff, partial/corrected remittance, closed-period correction, and restart recovery through shared browser/WPF services. Section 22.6 acceptance covers only demonstrated terms/workflows and one contractual currency per loan.

### Source Modules

- `SRC-APP`
- `SRC-FSHARP-DIRECTLENDING`
- `SRC-DESIGN-INSTRUMENTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-LEDGER`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W12-ENTITY-001 - Effective entity lifecycle and audited book-control migration
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-11-14 |

### Current Summary

W12-ENTITY-001: ready_for_acceptance; health green; evidence complete. Development verification recorded 2026-11-14 on hosted quality, PostgreSQL integration and Windows lanes (PR 3591), including legacy-book activation after a passing preflight; operator acceptance pending.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3591](https://github.com/rodoHasArrived/Meridian-main/pull/3591)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-ENTITY-001.md)
- Evidence: [docs/engineering/plans/intercompany-entity-controls.md](../../engineering/plans/intercompany-entity-controls.md)
- Evidence: [src/Meridian.Entities/FundStructure/EntityLifecyclePolicy.cs](../../../src/Meridian.Entities/FundStructure/EntityLifecyclePolicy.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/EntityTimelineService.cs](../../../src/Meridian.Ui.Shared/Services/EntityTimelineService.cs)
- Evidence: [tests/Meridian.Tests/Storage/LedgerBookControlsPostgresTests.cs](../../../tests/Meridian.Tests/Storage/LedgerBookControlsPostgresTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/LedgerBookControlsPostgresTests.Activation.cs](../../../tests/Meridian.Tests/Storage/LedgerBookControlsPostgresTests.Activation.cs)
- Evidence: [tests/Meridian.Tests/Ui/EntityTimelineServiceTests.cs](../../../tests/Meridian.Tests/Ui/EntityTimelineServiceTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/FundStructure/EntityLifecyclePostgresTests.cs](../../../tests/Meridian.Tests/Storage/FundStructure/EntityLifecyclePostgresTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/Views/IntercompanyNativeControlSmokeTests.cs](../../../tests/Meridian.Wpf.Tests/Views/IntercompanyNativeControlSmokeTests.cs)


### Exit Criteria

- Effective-dated lifecycle evidence gates posting and agreements at the commit authority boundary; missing historic periods are unknown rather than inferred from today's status.
- Winding-down permits evidenced settlement, closing expense, tax, revaluation and correction without permitting new commitments; dissolved/merged corrections require approved exact exceptions.
- New books enforce controls immediately and existing books transition only after current source-population and authority evidence passes audited preflight.
- Read models retain lifecycle revisions, ownership, agreements, successor links and books, and consolidation never silently omits residual balances because of lifecycle status.
- Tenant, replay, concurrent transition, migration and shared browser/WPF tests are evidenced before acceptance; Windows execution is reported separately.

### Source Modules

- `SRC-STORAGE-ACCOUNTING`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-DESIGN-ENTITIES`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W12-FINRPT-001 - Ledger-backed financial statement control pack
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-17 |

### Current Summary

W12-FINRPT-001: planned; health green; evidence planned_evidence. Bind existing statement builders and document rendering to governed definitions, comparative snapshots and tied fund/class/investor packs.

### Status References

- Decisions: DEC-FUND-OPERATIONS-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-FINRPT-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- Definitions bind entity/perimeter, book, basis, currency, period, cutoff, approved opening state and source snapshot, account mappings, and template/formula versions. Draft previews remain distinct from outputs derived from approved posted records; missing mappings, unsupported inputs, or incompatible scopes block certification. The declared slice identifies its conditional dependencies under Section 22.6.
- A retained mapping manifest accounts for every eligible source line within each statement's declared coverage and its signed contribution to detail rows. Declared splits conserve the mapped amount; exclusions, derived formulas, and presentation-only subtotals are explicit, so totals cannot silently omit or double-count source value.
- One retained journal set yields trial balance, balance sheet, income statement, cash-flow statement, and partners-capital outputs whose balance, accounting-equation, and income-to-capital checks agree. For a fixed declared cash perimeter, opening cash plus classified cash flows and separately evidenced exchange-rate effects ties to closing cash. An unchanged foreign-currency cash balance shows FX effects without a receipt/payment, and a compound cash-transfer/noncash-accrual journal does not invent flows. Other non-flow categories or perimeter changes require separately supported definitions and evidence; unexplained residuals block certification and release.
- Annual packs distinguish pre-close results, closing transfers, post-close balances, and next-year openings under W12-YEND-001. Closing/opening transfers remain in proof but cannot erase or create period income, cash flow, or investment performance; comparative and restated figures retain the applicable close snapshot.
- Fund/class/series NAV, units, capital movements, fees, and investor statements reconcile at one approved cutoff when their W12-CAPITAL-001 evidence is available. A component-level shadow-NAV bridge preserves independent source operands and individual quantity, price, FX, accrual, fee, capital, and allocation breaks even when total differences offset.
- P&L components and eligible security/strategy/sector/fund/class/vintage groupings reconcile to the declared total with residuals visible. Return schedules consume W10-PERF-001 with explicit method, cash-flow/valuation dates, fee/currency treatment, completeness, and ambiguous-result handling; master-feeder and consolidated versus gross inclusion remain explicit.
- Every amount-bearing line and total drills through the shared proof contract to retained journals or source calculations. Exports include machine-readable agreement totals and an evidence manifest; a frozen package reproduces after later source, mapping, or classification changes.
- Scheduled runs assemble candidates; release rechecks exact approved versions, close blockers, entitlements, and delivery authority. Section 22.6 concurrency/recovery evidence proves that retry returns the governed package/release receipt without duplicate publication. Changed bound inputs require renewed review, prior packages remain frozen, and tax/regulatory datasets do not imply filing authority.
- Both operator surfaces expose one shared set of statements, scopes, reconciliation results, and blockers. Full-row acceptance under Section 22.6 links the complete declared fund/class/investor pack, independently calculated golden agreement totals, missing/duplicate mapping refusals, comparative/restatement cases, and each dependency used. The earlier basic monthly slice alone cannot satisfy this criterion.

### Source Modules

- `SRC-LEDGER`
- `SRC-DESIGN-REPORTING`
- `SRC-DESIGN-DOCUMENTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W12-ICAGREE-001 - Intercompany accounting profiles and legacy attribution
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-11-14 |

### Current Summary

W12-ICAGREE-001: ready_for_acceptance; health green; evidence complete. Development verification recorded 2026-11-14 on hosted quality, PostgreSQL integration and Windows lanes (PR 3591); operator acceptance pending. Profile owner approval still never replaces independent legal approval.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3591](https://github.com/rodoHasArrived/Meridian-main/pull/3591)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-ICAGREE-001.md)
- Evidence: [docs/domain/intercompany-agreements-and-entity-lifecycle.md](../../domain/intercompany-agreements-and-entity-lifecycle.md)
- Evidence: [src/Meridian.FinancialOperations/Intercompany/IntercompanyAccountingArrangementService.cs](../../../src/Meridian.FinancialOperations/Intercompany/IntercompanyAccountingArrangementService.cs)
- Evidence: [src/Meridian.Storage/Ledger/PostgresLegacyIntercompanyAttributionStore.cs](../../../src/Meridian.Storage/Ledger/PostgresLegacyIntercompanyAttributionStore.cs)
- Evidence: [src/Meridian.FinancialOperations/Intercompany/IntercompanyBalanceScheduleService.cs](../../../src/Meridian.FinancialOperations/Intercompany/IntercompanyBalanceScheduleService.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyBalanceScheduleTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Intercompany/IntercompanyBalanceScheduleTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/LegacyIntercompanyAttributionTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/LegacyIntercompanyAttributionTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/LegacyIntercompanyAttributionPostgresTests.cs](../../../tests/Meridian.Tests/Storage/LegacyIntercompanyAttributionPostgresTests.cs)
- Evidence: [tests/Meridian.FundStructure.Tests/IntercompanyAccountingArrangementTests.LegalNovation.cs](../../../tests/Meridian.FundStructure.Tests/IntercompanyAccountingArrangementTests.LegalNovation.cs)
- Evidence: [tests/Meridian.Tests/Storage/FundStructure/IntercompanyAccountingArrangementPostgresTests.cs](../../../tests/Meridian.Tests/Storage/FundStructure/IntercompanyAccountingArrangementPostgresTests.cs)
- Evidence: [tests/Meridian.Ui.Tests/Services/IntercompanyAgreementWorkspaceTests.cs](../../../tests/Meridian.Ui.Tests/Services/IntercompanyAgreementWorkspaceTests.cs)


### Exit Criteria

- Native approved agreement versions retain legal parties, terms and evidence; accounting profiles pin their exact version/hash and own only authorized books, stable account roles, templates and bounded validity.
- Tenant isolation, independent legal approval, separately audited configuration approval, stale-version refusal, immutable history and idempotent retries preserve financial lineage; profile changes cannot perform legal novation.
- Existing journal attribution is explicit reviewed sidecar evidence over exact source digests, with missing or ambiguous historic identity remaining unresolved.
- Close and provider intercompany controls consume a structured due-to/from schedule and expose incomplete coverage rather than accepting keyword-only management records as proof.
- Added unit, PostgreSQL, API and shared workstation evidence is recorded before acceptance; no external posting or live-provider certification is implied.

### Source Modules

- `SRC-STORAGE-ACCOUNTING`
- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-DESIGN-ENTITIES`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`

## W12-ICINCOME-001 - Policy-governed intercompany revenue and expense elimination
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | ready_for_acceptance |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-11-14 |

### Current Summary

W12-ICINCOME-001: ready_for_acceptance; health green; evidence complete. Development verification recorded 2026-11-14 on hosted quality and PostgreSQL integration lanes (PR 3591); operator acceptance pending. Populations above two entities are proven for w10-v2 only; investment/equity elimination stays unsupported.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3591](https://github.com/rodoHasArrived/Meridian-main/pull/3591)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-ICINCOME-001.md)
- Evidence: [docs/domain/intercompany-consolidation.md](../../domain/intercompany-consolidation.md)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Governed.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Governed.cs)
- Evidence: [src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Matching.cs](../../../src/Meridian.FinancialOperations/Consolidation/ConsolidationService.Matching.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedServiceTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedServiceTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedPopulationTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationGovernedPopulationTests.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationServiceTests.GovernedRaces.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationServiceTests.GovernedRaces.cs)
- Evidence: [tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationFxTests.cs](../../../tests/Meridian.Tests/FinancialOperations/Consolidation/ConsolidationFxTests.cs)
- Evidence: [docs/engineering/plans/intercompany-entity-controls.md](../../engineering/plans/intercompany-entity-controls.md)


### Exit Criteria

- Income elimination requires the explicit approved revenue/expense policy rule with evidence and independent approval; account-role configuration alone cannot enable it.
- Exact participating policy rules, source/transaction identities, role bindings, translation policy and rates remain in retained review evidence and freshness guards.
- Principal/intercompany, translation and rounding differences remain separate and repeat runs or corrections preserve cumulative overlay semantics.
- Focused accounting, PostgreSQL append-boundary, authorization and workstation evidence is recorded before acceptance; investment/equity elimination remains unsupported.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`

## W12-ICPAIR-001 - Durable reviewed counterpart proposals and aged intercompany breaks
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | ready_for_acceptance |
| Health | green |
| Priority | high |
| Owner lane | Accounting and Ledger |
| Evidence posture | complete |
| Last reviewed | 2026-11-14 |

### Current Summary

W12-ICPAIR-001: ready_for_acceptance; health green; evidence complete. Development verification recorded 2026-11-14 on hosted quality, PostgreSQL integration and Windows lanes (PR 3591); operator acceptance pending. Delivery never posts; repricing and break reads check tenant, not company, at the service.

### Status References

- Decisions: none recorded
- PR references (not inferred acceptance): [#3591](https://github.com/rodoHasArrived/Meridian-main/pull/3591)
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-ICPAIR-001.md)
- Evidence: [docs/engineering/plans/intercompany-entity-controls.md](../../engineering/plans/intercompany-entity-controls.md)
- Evidence: [src/Meridian.FinancialOperations/Intercompany/IntercompanyProposalDraftBuilder.cs](../../../src/Meridian.FinancialOperations/Intercompany/IntercompanyProposalDraftBuilder.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/IntercompanyProposalDeliveryService.cs](../../../src/Meridian.Ui.Shared/Services/IntercompanyProposalDeliveryService.cs)
- Evidence: [tests/Meridian.Tests/Storage/IntercompanyProposalPostgresTests.cs](../../../tests/Meridian.Tests/Storage/IntercompanyProposalPostgresTests.cs)
- Evidence: [tests/Meridian.Tests/Storage/IntercompanyProposalPostgresTests.Restart.cs](../../../tests/Meridian.Tests/Storage/IntercompanyProposalPostgresTests.Restart.cs)
- Evidence: [tests/Meridian.Tests/Ui/IntercompanyProposalDeliveryRetryTests.cs](../../../tests/Meridian.Tests/Ui/IntercompanyProposalDeliveryRetryTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/IntercompanyProposalBreakAgingTests.cs](../../../tests/Meridian.Tests/Ui/IntercompanyProposalBreakAgingTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/IntercompanyProposalDestinationAuthorityTests.cs](../../../tests/Meridian.Tests/Ui/IntercompanyProposalDestinationAuthorityTests.cs)
- Evidence: [tests/Meridian.Ui.Tests/Services/IntercompanyCounterpartQueueWorkspaceTests.cs](../../../tests/Meridian.Ui.Tests/Services/IntercompanyCounterpartQueueWorkspaceTests.cs)
- Evidence: [src/Meridian.Ui.Services/Services/Accounting/IntercompanyCounterpartQueueWorkspace.cs](../../../src/Meridian.Ui.Services/Services/Accounting/IntercompanyCounterpartQueueWorkspace.cs)


### Exit Criteria

- Source commit retains durable proposal intent atomically; retry and restart cannot lose or duplicate counterpart economic work.
- Destination drafts are balanced, use exact approved agreement/account/currency evidence, and preserve the destination review and authorization boundary.
- Duplicate candidates are resolved by exact source/transaction identity or explicit evidenced linking rather than agreement, period and amount alone.
- Aging declares its due-date or effective-date basis and reports intercompany breaks separately from translation and rounding differences.
- Synthetic persistence, concurrency, delivery, authorization and shared workstation tests are recorded before acceptance.

### Source Modules

- `SRC-APP`
- `SRC-CONTRACTS`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-DASHBOARD`

## W12-YEND-001 - Fiscal year-end close and opening-balance roll-forward
| Field | Value |
| --- | --- |
| Wave | W12 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Accounting and Ledger |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-17 |

### Current Summary

W12-YEND-001: planned; health green; evidence planned_evidence. Activate the existing year-end projector through governed close readiness and retained acceptance requirements.

### Status References

- Decisions: DEC-FUND-OPERATIONS-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W12-YEND-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)


### Exit Criteria

- A fiscal close plan binds the approved opening state under Section 22.6, every required constituent period, accounting book, closing policy, retained pre-closing trial balance, and approver. Missing opening history, period authority, a required period's evidence, or any unclosed required period blocks fiscal close; none can be treated as an empty period.
- The close receipt distinguishes the pre-closing trial balance, closing-transfer journals, post-closing trial balance, and next-year opening representation. Entity, account, book, currency, and applicable capital dimensions survive the roll-forward; retained earnings versus investor-capital treatment and open-lot/accrual carryforwards follow declared rules.
- Closing journals use an explicitly authorized year-end posting period or adjustment route; they cannot implicitly reopen a closed constituent period or bypass the authoritative posting and approval controls.
- Closing transfers remain in ledger proof without erasing or creating period income or performance. Next-year openings represent the prior post-close balances exactly once; neither originating history nor interim investor allocations may be counted again as new opening value or income allocation.
- A retained close receipt links exact input snapshots, versions, approvals, and all closing/opening consequences. Concurrent attempts and restart or lost-acknowledgment recovery follow Section 22.6 and recover the committed receipt without closing income twice or duplicating opening entries.
- A late adjustment follows governed reopen/correction/re-close rules and creates linked successor close snapshots and affected subsequent opening balances while preserving the original close and issued packages. Changed pending inputs or a new correction require renewed approval; reading a committed receipt does not rerun the close.
- Independently calculated full-year golden examples preserve period income after closing and capital exactly once, including any supported interim investor allocation. Missing-period, changed-approval, duplicate-run, and late-correction cases pass Section 22.6 acceptance through shared browser/WPF services on durable storage.

### Source Modules

- `SRC-LEDGER`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-DESIGN-REPORTING`
- `SRC-DESIGN-DOCUMENTS`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W13-OMS-001 - Governed order management and broker execution workbench
| Field | Value |
| --- | --- |
| Wave | W13 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Execution and Fund Accounts |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-30 |

### Current Summary

W13-OMS-001: planned; health green; evidence planned_evidence. Planned unranked reviewed order intent and canonical OMS lifecycle direction; account, risk, broker and accounting controls remain required.

### Status References

- Decisions: DEC-TRADING-STRATEGY-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W13-OMS-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/product/implementation-todo-list.md](../../product/implementation-todo-list.md)


### Exit Criteria

- The declared slice names supported broker/account ownership, instrument classes, sizing conventions, order types, time-in-force rules, paper/live modes, and deployment posture. Capability evidence and refusal behavior cover unsupported combinations; configured integration alone cannot imply tested live support.
- Before exposing manual live orders, define and review an explicit operator-intent and execution-account authority contract. Current live admission requires a retained FundAccountId, the exact promoted runId, promotion audit evidence, and a current readiness snapshot. Either the selected workflow legitimately supplies that authority or a reviewed manual-intent path supplies equivalent scoped authorization, readiness, risk, and audit controls; missing strategy context cannot be filled with invented runs or bypassed gates.
- Operator order tickets and strategy-generated intents use one authoritative OMS and shared contracts for order identity, account scope, risk admission, gateway dispatch, lifecycle transitions, and durable evidence. Pre-trade authorization, provider ownership, instrument/reference validity, positions, limits, and execution controls are rechecked at the authoritative command boundary; stale or incomplete authority blocks dispatch.
- Submit, acknowledgment, partial/full fill, rejection, expiration, cancel, and cancel/replace retain broker observations and exact intent/order relationships. Pending, disputed, or indeterminate broker state stays visible. An unconfirmed replacement or requested cancellation cannot appear terminal, and cancel-all retains per-order outcomes and unresolved live exposure after the breaker blocks new submissions.
- Duplicate and out-of-order reports, client-order-ID conflicts, concurrent commands, reconnect gaps, interrupted commits, lost acknowledgments, and restart preserve durable receipts and monotonic lifecycle/fill state without duplicate dispatch or financial consequences. Recovery reconciles broker observations before resuming uncertain work and retains failures requiring operator resolution.
- Broker orders, fills, positions, and cash reconcile to retained internal execution evidence and the existing governed financial handoff. Exactly-once accounting consequences are demonstrated through idempotent replay and durable recovery; corrections preserve lineage, and the ledger remains the sole source of posted accounting truth.
- Browser and WPF use shared services for tickets, working orders, history, fills, recovery, readiness, risk decisions, and safety actions. Unsupported or unwired controls expose an explicit reason; the supported operator workflow includes authorized cancellation, breaker activation, reconnect recovery, and evidence inspection.
- Development verification uses a bounded synthetic/paper rehearsal, focused checks, and separate AI critique for consequential execution/risk, storage/security, and accounting changes under docs/ai/solo-developer-workflow.md. The developer owns acceptance decisions. A live-support claim additionally requires separately authorized broker-backed evidence for the named configuration and applicable product approvals; ordinary development needs no live credentials or human review organization. Existing W9 reservations and P0 release gates retain their production scope until their own evidence closes them; product-scope adoption cannot authorize live orders, credential changes, bypasses, or release certification.

### Source Modules

- `SRC-EXECUTION`
- `SRC-EXECUTION-SDK`
- `SRC-RISK`
- `SRC-INFRASTRUCTURE`
- `SRC-STORAGE`
- `SRC-CONTRACTS`
- `SRC-LEDGER`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W13-STRAT-001 - Governed strategy formulation and execution lifecycle
| Field | Value |
| --- | --- |
| Wave | W13 |
| Status | planned |
| Health | green |
| Priority | medium |
| Owner lane | Strategy Analytics |
| Evidence posture | planned_evidence |
| Last reviewed | 2026-10-30 |

### Current Summary

W13-STRAT-001: planned; health green; evidence planned_evidence. Planned unranked strategy formulation, research, backtesting, promotion and execution direction; it grants no live activation authority.

### Status References

- Decisions: DEC-TRADING-STRATEGY-DIRECTION-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W13-STRAT-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/development/policies/promotion-policy-matrix.md](../../development/policies/promotion-policy-matrix.md)


### Exit Criteria

- The declared slice identifies a supported strategy family, authoring route, instruments, data dependencies, execution modes, account, and broker capability. Existing definitions, Designer documents, QuantScript, and retained run/artifact records converge on one versioned strategy-artifact identity; parameters, universe, decision rules, cost assumptions, risk limits, and approval history remain reproducible after edits.
- Shared server-side validation and orchestration bind every request to exact strategy and data versions, owner scope, canonical inputs, engine/model versions, and supported capabilities before recording run results. Missing, stale, ambiguous, or unsupported inputs produce truthful blocked, degraded, or deferred states; no surface fabricates a completed run or financial result.
- Backtest and paper evidence retains reproducible inputs, execution and cost assumptions, fills, metrics, warnings, and independently calculated expected results for the selected strategy. Alternative authoring routes cannot inherit the bounded Covered Call acceptance without their own host-composed evidence; current PRD-106 orchestration and recovery work remains separately tracked.
- Promotion preserves exact parent/child run and strategy-artifact lineage, scoped account authority, retained evidence, independent approval, current readiness, and revocable live authorization. Approved deterministic strategy execution operates within its retained mandate; model-generated suggestions and changed strategy inputs cannot authorize trades or reuse stale approval.
- Strategy-generated intents enter the same governed OMS, risk, gateway, and fill/reconciliation path as W13-OMS-001. The existing retained FundAccountId and promoted run identity remain authoritative until an explicitly reviewed account/intent contract supersedes them; display labels, runtime parameters, or synthetic promotion records cannot supply missing authority.
- Closed-expression Designer execution remains distinct from arbitrary-code execution. Each supported authoring route proves its actual runtime capabilities and containment; arbitrary scripts require the PRD-012 isolation and resource limits before entering a supported production envelope, and unsupported constructs remain visibly deferred.
- Start, pause, stop, resume, fault, recovery, and revision transitions retain intent, current run state, open-order and exposure posture, cancellation outcomes, and audit lineage. Concurrent commands, interrupted writes, lost acknowledgments, cancellation-resistant work, and restart recover the committed outcome without repeated external actions or duplicate financial consequences.
- Browser and WPF use shared definitions, validation, run state, approvals, and evidence. A bounded end-to-end rehearsal follows one formulated strategy through backtest, reviewed paper validation, authorized execution, observed fills, reconciliation, and governed accounting handoff; paper evidence cannot substitute for separately authorized live-broker acceptance or frozen-release certification.

### Source Modules

- `SRC-STRATEGIES`
- `SRC-BACKTESTING`
- `SRC-QUANTSCRIPT`
- `SRC-EXECUTION`
- `SRC-CONTRACTS`
- `SRC-STORAGE`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`

## W14-ATTEST-001 - Single-principal attestation for governed approvals in the single-operator envelope
| Field | Value |
| --- | --- |
| Wave | W14 |
| Status | in_progress |
| Health | yellow |
| Priority | medium |
| Owner lane | Platform Security and Governance |
| Evidence posture | in_progress |
| Last reviewed | 2026-11-12 |

### Current Summary

W14-ATTEST-001: in_progress; health yellow; evidence in_progress. Default-off preparer assertions and bounded USD dividends remain implementation only. Native Windows, full workstation/matrix acceptance, operational review and financial-coverage adoption remain unestablished.

### Status References

- Decisions: DEC-SINGLE-PRINCIPAL-001
- PR references (not inferred acceptance): none recorded
- [Retained implementation detail and boundaries](../snapshots/status-prose-2026-11-14/W14-ATTEST-001.md)
- Evidence: [docs/product/meridian-design-document.md](../../product/meridian-design-document.md)
- Evidence: [docs/product/value-proposition-review-2026-11-01.md](../../product/value-proposition-review-2026-11-01.md)
- Evidence: [docs/product/single-principal-attestation-policy-proposal-2026-11-02.md](../../product/single-principal-attestation-policy-proposal-2026-11-02.md)
- Evidence: [docs/product/single-principal-attestation-acceptance-matrix-2026-11-02.md](../../product/single-principal-attestation-acceptance-matrix-2026-11-02.md)
- Evidence: [src/Meridian.Ui.Shared/Evidence/SinglePrincipalAttestationStore.cs](../../../src/Meridian.Ui.Shared/Evidence/SinglePrincipalAttestationStore.cs)
- Evidence: [src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionCaseAccountingService.CashDividend.cs](../../../src/Meridian.Application/SecurityMaster/CorporateActions/CorporateActionCaseAccountingService.CashDividend.cs)
- Evidence: [src/Meridian.Storage/SecurityMaster/PostgresCorporateActionOperationsStore.CashDividendAttestations.cs](../../../src/Meridian.Storage/SecurityMaster/PostgresCorporateActionOperationsStore.CashDividendAttestations.cs)
- Evidence: [src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.CashDividendAttestation.cs](../../../src/Meridian.FinancialOperations/Ledger/AccountingPostingCandidatePostService.CashDividendAttestation.cs)
- Evidence: [src/Meridian.Ui.Shared/Services/AccountingPreparerAttestationDisclosure.cs](../../../src/Meridian.Ui.Shared/Services/AccountingPreparerAttestationDisclosure.cs)
- Evidence: [src/Meridian.Ui/dashboard/src/components/meridian/cash-dividend-attestation-panel.tsx](../../../src/Meridian.Ui/dashboard/src/components/meridian/cash-dividend-attestation-panel.tsx)
- Evidence: [src/Meridian.Wpf/ViewModels/EvidenceWorkbenchViewModel.Attestations.cs](../../../src/Meridian.Wpf/ViewModels/EvidenceWorkbenchViewModel.Attestations.cs)
- Evidence: [src/Meridian.Wpf/ViewModels/SecurityMaster/CashDividendAttestationViewModel.cs](../../../src/Meridian.Wpf/ViewModels/SecurityMaster/CashDividendAttestationViewModel.cs)
- Evidence: [tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.CashDividend.cs](../../../tests/Meridian.Tests/SecurityMaster/CorporateActionAccountingPostgresRoundTripTests.CashDividend.cs)
- Evidence: [tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs](../../../tests/Meridian.Tests/Ui/PostedLedgerAmountProvenanceTests.cs)
- Evidence: [tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs](../../../tests/Meridian.Tests/Ui/ReportLedgerAmountProvenanceTests.cs)
- Evidence: [tests/Meridian.Wpf.Tests/ViewModels/EvidenceWorkbenchAttestationActionTests.cs](../../../tests/Meridian.Wpf.Tests/ViewModels/EvidenceWorkbenchAttestationActionTests.cs)


### Exit Criteria

- An audited, tenant-scoped single-principal declaration exists; with it off, existing guarded approvals and their documented exceptions behave exactly as today, with no attestation authority.
- With it on, eligible approvals are recorded as attestations by the preparer, distinct from independent approvals in contracts, storage, and audit events; material or high-risk classes named by policy stay blocked.
- Every Number Passport, decision trace, report package, and audit view shows "attested by preparer - not independently reviewed" on both the browser and WPF workstations, and a delivered package reproduces its original attestation label after the declaration is withdrawn.
- Verified Coverage reports attested items separately and never merges them with independently approved items; whether attested items count toward the headline is decided under W11-COVER-001.
- Turning the declaration on or off is an audited administrative action, and items created after it is withdrawn require independent approval.
- Roadmap status remains planned until this item links implementation paths, the excluded-class policy, and browser and WPF evidence; these active-development links were added on 2026-11-12 without claiming acceptance. The row changes no ADR-019 envelope or PRD-000 sign-off.

### Source Modules

- `SRC-APP`
- `SRC-DESIGN-IDENTITY`
- `SRC-DESIGN-FINANCIAL-OPERATIONS`
- `SRC-STORAGE`
- `SRC-STRATEGIES`
- `SRC-CONTRACTS`
- `SRC-UI-SHARED`
- `SRC-UI-SERVICES`
- `SRC-UI-DASHBOARD`
- `SRC-WPF`
