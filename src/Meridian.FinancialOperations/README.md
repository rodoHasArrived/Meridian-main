---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-FINANCIAL-OPERATIONS
path: src/Meridian.FinancialOperations
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-10
---

# src/Meridian.FinancialOperations

## Purpose

Physical bounded-context module project for reconciliation, accounting records, payment approvals,
bank-transaction records, accounting-basis policy, ledger text-journal reporting, close workflows,
casework, and operational-record ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `src/Meridian.FinancialOperations` - registered source module root.
- `OperationsContinuity/OperationsContinuityWorkflow.cs` - account-period close workflow aggregate, gates, checklist state, audit evidence, and close-readiness posture inputs.
- `OperationsContinuity/OperationsContinuityWorkflowService.cs` - command transitions, optimistic version checks, audit writes, ledger-post coordination, and DTO projection.
- `OperationsContinuity/OperationsContinuityRepositories.cs` - in-memory and file-backed workflow/audit stores plus transactional commit store contracts.
- `OperationsContinuity/PostgresOperationsContinuityStore.cs` - PostgreSQL workflow snapshot, audit timeline, and transactional ledger-post commit store.
- `OperationsContinuity/OperationsStatusDerivationService.cs` - deterministic status derivation from gate/sub-state posture through the F# operations rules.
- `OperationsContinuity/OperationsWorkflowAuditHashing.cs` - append-only workflow audit hash creation and chain validation.
- `OperationsContinuity/OperationsApprovalPolicyMatrixService.cs` - server-owned approval-policy matrix, governed rule upsert validation, audit-event construction, and file-backed policy persistence.
- `OperationsContinuity/OperationsCloseCalendarService.cs` - account-close calendar projection, governed due-date/owner overrides, and audit-event construction backed by Financial Operations policy.
- `AccountingClose/` - deterministic journal posting, trial-balance projection, roll-forward,
  FX translation, source-linked audit rows, and period-close evidence gates.
- `Ledger/AccountingPolicyService.cs` - accounting-basis policy creation, resolution, listing,
  and projection metadata stamping for ledger writes.
- `Ledger/TextJournal/` - ledger-compatible text-journal parsing, validation, report rendering,
  and CLI-facing report service backed by the Meridian double-entry ledger engine.
- `AccountingSystem/AccountingSystemIntegrationService.cs` - provider-neutral external GL import, latest-import retention, ledger-truth reconciliation, provider availability projection, and read-only posting posture.
- `Reconciliation/StatementRunWorkflowService.cs` - statement-run workflow that persists canonical imports, linked breaks, and case materialization for shared UI consumers.
- `Reconciliation/StatementReconciliationService.cs` - broker/custodian statement intake, mapping-profile validation, duplicate detection, normalization, matching, and reconciliation result projection.
- `Reconciliation/StatementReconciliationOrchestrator.cs` - staged reconciliation orchestration, checkpoint persistence, failure recovery, and case intake coordination.
- `Reconciliation/StatementRepositories.cs` - statement-run, validation, match, break, and case-link repository contracts and file-backed implementations.
- `Reconciliation/StatementMatchingEngine.cs` and `Reconciliation/CanonicalReconciliationEngine.cs` - deterministic match, tolerance, candidate, and true-break evaluation.
- `Reconciliation/StatementBreakClassifier.cs`, `StatementMappingProfiles.cs`, and `StatementToleranceProfiles.cs` - canonical break taxonomy, broker mapping profiles, and tolerance governance.
- `Reconciliation/ReconciliationEngineService.cs` - Security Master-enriched portfolio-vs-ledger
  reconciliation engine that joins positions, ledger balances, and the F# ledger reconciliation
  kernel.
- `Reconciliation/FileReconciliationDecisionJournal.cs` - crash-safe copy-on-write JSONL decision and resolution history persistence.
- `Banking/` - payment initiation, approval/rejection workflow, bank-side transaction records,
  deterministic transaction seeding, and PostgreSQL-backed banking persistence adapter.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes. Operations Continuity workflow state, command transitions, status derivation, persistence, audit hashing, approval-policy rules, and close-calendar configuration live here so close, approval, report-pack, checklist-control, reviewer-independence, due-date, owner override, and retained audit policy remains part of Financial Operations rather than application orchestration or UI endpoints.

Statement reconciliation also lives here. Broker/custodian statement intake, mapping profiles, validation, duplicate detection, matching, break classification, reconciliation decision journals, statement-run persistence, and durable case materialization are Financial Operations behavior. Application commands and shared UI services invoke the module workflow, but they do not own reconciliation state, matching rules, or statement-run persistence.

Portfolio-vs-ledger reconciliation engine behavior also lives here. The engine enriches
portfolio/ledger candidates with the contracts-owned Security Master query surface and classifies
matches and breaks through the F# ledger reconciliation kernel instead of Application-local
service/logging ownership.

Accounting-system GL evidence integration lives here as provider-neutral Financial Operations behavior. The integration service lists accounting-system providers, chooses configured QuickBooks Online evidence when available, falls back to the read-only fixture provider when live company evidence is not configured, retains latest imports by provider/fund/book, and reconciles external trial-balance rows against Meridian-owned ledger totals when a ledger store is available. UI Shared maps endpoints and supplies credential-backed provider registration, but it does not own GL evidence reconciliation or posting-disable posture.

Accounting close projections live here as deterministic Financial Operations behavior. Journal
posting, FX translation, trial-balance, roll-forward, source-linked audit, and close evidence gates
are exposed to UI Services and WPF without making those surfaces own accounting-close state.

Accounting-basis policy and ledger text-journal reporting also live here. Application composition
registers the policy/projection services and the CLI command invokes the text-journal report service,
but Application no longer owns accounting policy resolution, ledger write projection metadata, or
text-journal parser/report semantics.
`AccountingJournalDraftService` accepts shared treasury ledger context and stamps the resulting
journal metadata with effective date, idempotency, fund-event, capital-account, investor,
payment-intent, and settlement references before a governed ledger write is projected. Keep this
behavior in Financial Operations so private-capital and payment-linked drafts are validated once
before browser, WPF, storage, or reporting surfaces inspect them.

Payment approval and bank-transaction records also live here. `IBankingService` publishes the
approval workflow and `IBankTransactionSource` evidence surface used by reconciliation, Plaid
workstation flows, and Direct Lending tests without making Direct Lending own bank-side
transaction state.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-FINANCIAL-OPERATIONS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5X-FINOPS-001` | Financial operations control center |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-FINANCIAL-OPERATIONS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.FinancialOperations/Meridian.FinancialOperations.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityWorkflowServiceTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityEndpoints_ApprovalPolicy --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~OperationsContinuityEndpoints_CloseCalendar --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~OperationsContinuityEndpoints_ApprovalPolicy|FullyQualifiedName~OperationsContinuityEndpoints_CloseCalendar|FullyQualifiedName~StorageFeatureRegistrationTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~StatementValidationServiceTests|FullyQualifiedName~StatementRepositoryTests|FullyQualifiedName~StatementReconciliationOrchestratorTests|FullyQualifiedName~StatementReconciliationContextAdapterTests|FullyQualifiedName~StatementMatchingEngineTests|FullyQualifiedName~CanonicalReconciliationMatchingEngineTests|FullyQualifiedName~StatementReconciliationServiceTests|FullyQualifiedName~StatementImportAndMatchingTests|FullyQualifiedName~StatementFixtureScenarioTests|FullyQualifiedName~StatementBreakClassifierTests|FullyQualifiedName~ReconciliationContractsTests|FullyQualifiedName~BrokerCustodianMatchingPipelineTests|FullyQualifiedName~ReconciliationApiServiceTests|FullyQualifiedName~StatementImportCommandsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~ReconciliationEngineServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingSystemIntegrationServiceTests|FullyQualifiedName~ProviderConnectionEndpointsTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter FullyQualifiedName~AccountingCloseServicesTests --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PaymentApprovalTests|FullyQualifiedName~BankTransactionSeedTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~AccountingPolicyServiceTests|FullyQualifiedName~LedgerCliCommandTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

### API and contract notes

`IOperationsContinuityWorkflowService` publishes account-period close workflow commands and reads. `IOperationsContinuityRepository`, `IOperationsWorkflowAuditStore`, `IOperationsContinuityWorkflowStartCommitStore`, and `IOperationsContinuityTransactionalCommitStore` publish workflow persistence and transactional audit/ledger commit contracts. `IOperationsApprovalPolicyMatrixService` publishes the policy matrix consumed by shared workstation endpoints. `IOperationsCloseCalendarService` publishes close-calendar reads and governed item upserts. Accounting-close services publish journal posting, FX translation, trial-balance, roll-forward, and evidence-gate projections. `IAccountingPolicyService` and `IAccountingBasisProjectionService` publish accounting-basis policy lookup and ledger write metadata projection for application workflows. `LedgerTextJournalReportService` publishes CLI-facing text-journal parsing and report rendering. `AccountingSystemIntegrationService` publishes provider listing, import preview/latest import, and latest external-GL reconciliation reads over `IAccountingSystemProvider` contracts. `IBankingService` publishes payment approval and bank-transaction workflows over `Meridian.Contracts.Banking` DTOs. `IStatementRunWorkflowService`, `IStatementReconciliationService`, `IStatementReconciliationOrchestrator`, `IStatementValidationService`, and reconciliation repository contracts publish statement intake, validation, matching, persistence, and casework orchestration for commands and UI services. DTOs remain in `Meridian.Contracts.Workstation`, `Meridian.Contracts.AccountingSystem`, `Meridian.Contracts.Banking`, and `Meridian.Contracts.Ledger`; authorization roles and permissions come from `Meridian.Identity.Auth`; durable local writes use `Meridian.Storage.Archival.AtomicFileWriter` and banking persistence uses `Meridian.Storage.Banking`.

### Migration and archive notes

`OperationsContinuityWorkflow`, `OperationsContinuityWorkflowService`, workflow repository/store contracts and implementations, status derivation, audit hashing, `OperationsApprovalPolicyMatrixService`, `IOperationsApprovalPolicyMatrixService`, `OperationsCloseCalendarService`, and `IOperationsCloseCalendarService` moved from `src/Meridian.Application/OperationsContinuity` into this module. Statement reconciliation models, contracts, services, repositories, orchestration, mapping/tolerance profiles, matching engines, break classification, decision journals, and statement-run workflow services moved from `src/Meridian.Application/Reconciliation` into this module. `ReconciliationEngineService` moved from `src/Meridian.Application/Services` into this module and now consumes the contracts-owned Security Master query surface. Accounting close services moved out of the legacy Application accounting-close folder into `AccountingClose/`. Payment approval and bank-transaction services moved out of the legacy Application banking folder into `Banking/`. Accounting policy/projection services and ledger text-journal parser/reporting services moved out of the legacy Application ledger folder into `Ledger/`. `AccountingSystemIntegrationService` moved from `src/Meridian.Ui.Shared/Services` into this module. Application composition, command handlers, and UI services consume these module services but do not own their workflow state, policy implementation, reconciliation state, matching rules, statement-run persistence, portfolio-vs-ledger reconciliation engine behavior, external-GL reconciliation, bank-side transaction state, accounting policy/projection behavior, ledger text-journal semantics, accounting-close projections, or posting-disable posture.

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
