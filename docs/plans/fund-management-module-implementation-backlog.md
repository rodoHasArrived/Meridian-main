# Meridian Fund Management Module Implementation Backlog

**Owner:** Core Team
**Audience:** Product, architecture, engineering, and delivery leads
**Last Updated:** 2026-04-27
**Status:** Active implementation backlog

> **Retirement note (2026-04-09):** Any references here to the browser workstation, `src/Meridian.Ui`, or browser-first run detail flows describe the retired standalone dashboard. Keep the product intent, but re-scope future implementation to WPF and the retained desktop-local API surface.

## Purpose

This document turns the fund-management product vision into a module-by-module implementation backlog mapped directly to Meridian projects and file anchors.

It is meant to answer four questions:

1. Which Meridian modules own each capability?
2. What code anchors already exist?
3. What is still missing from the current repo state?
4. What should be built next, by phase, to reach the target product?

## Planning Inputs

This backlog is derived from:

- [Fund Management Product Vision and Capability Matrix](fund-management-product-vision-and-capability-matrix.md)
- [Trading Workstation Migration Blueprint](trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)
- [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md)
- [Project Roadmap](../status/ROADMAP.md)
- [Feature Inventory](../status/FEATURE_INVENTORY.md)

## Phase Map

| Phase | Goal |
|------|------|
| Phase 1 | Workstation core and shared run model |
| Phase 2 | Front-office implementation, trade management, account/entity foundations, Security Master productization |
| Phase 3 | Accounting, multi-ledger, trial balance, and cash-flow |
| Phase 4 | Reconciliation, NAV, attribution, and governance operations |
| Phase 5 | Reporting, investor outputs, and governed distribution workflows |
| Phase 6 | Full live operating lifecycle and policy-driven controls |

## Module Backlog

### 1. Workspace Shell and Navigation

**Projects**

- `src/Meridian.Wpf`
- `src/Meridian.Ui.Services`

**Key anchors**

- `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Wpf/Services/WorkspaceService.cs`
- `src/Meridian.Ui.Services/Services/WorkspaceModels.cs`

**Current state**

Workspace vocabulary exists, but the product still behaves too much like page navigation instead of durable operator workspaces.

**Primary backlog**

- Build persistent workspace-native shells for `Research`, `Trading`, `Data Operations`, and `Governance`.
- Move session restoration from page state toward workspace/task state.
- Add quick actions, workspace summaries, and cross-surface entry points.
- Define workspace-specific layouts for research, trade implementation, and governance review.

**Target phase**

- Phase 1

### 2. Shared Run Model and Workstation Drill-Ins

**Projects**

- `src/Meridian.Contracts`
- `src/Meridian.Strategies`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunBrowserViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunDetailViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunPortfolioViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs`

**Current state**

Backtest-first shared run workflows exist and already power the first workstation browser/detail/portfolio/ledger flow. A run-scoped reconciliation seam now also exists through `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs`, `src/Meridian.Strategies/Services/ReconciliationRunService.cs`, and workstation endpoints.

**Primary backlog**

- Expand run models to cover paper and live-facing history.
- Add promotion state, execution summaries, and reusable comparison snapshots.
- Add account/entity context to run and portfolio surfaces.
- Make run drill-ins a universal operator entry point across research and governance.

**Target phase**

- Phase 1
- Phase 2

### 3. Research and Backtesting Workflow Unification

**Projects**

- `src/Meridian.Wpf`
- `src/Meridian.Ui.Services`
- `src/Meridian.Backtesting`
- `src/Meridian.Strategies`

**Key anchors**

- `src/Meridian.Wpf/Views/BacktestPage.xaml.cs`
- `src/Meridian.Wpf/ViewModels/BacktestViewModel.cs`
- `src/Meridian.Wpf/Views/LeanIntegrationPage.xaml.cs`
- `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`
- `src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs`

**Current state**

Native and Lean-backed research flows still feel like separate experiences.

**Primary backlog**

- Unify native and Lean backtests behind one research workflow.
- Standardize experiment comparison around shared run objects.
- Connect research output directly to implementation and promotion workflows.
- Add approval and readiness checks before paper/live handoff.

**Target phase**

- Phase 1
- Phase 2

### 4. Trading Implementation and Trade Management

**Projects**

- `src/Meridian.Execution`
- `src/Meridian.Execution.Sdk`
- `src/Meridian.Wpf`
- `src/Meridian.Contracts`

**Key anchors**

- `src/Meridian.Execution/OrderManagementSystem.cs`
- `src/Meridian.Execution/Services/OrderLifecycleManager.cs`
- `src/Meridian.Execution/PaperTradingGateway.cs`
- `src/Meridian.Execution/Models/OrderStatusUpdate.cs`
- `src/Meridian.Wpf/Views/LiveDataViewerPage.xaml.cs`
- `src/Meridian.Wpf/Views/OrderBookPage.xaml.cs`

**Current state**

Execution primitives and paper-trading infrastructure exist, but not yet as a full operator-facing trade-management product surface.

**Primary backlog**

- Add trading cockpit views for orders, fills, positions, and execution state.
- Introduce trade-management DTOs and view models shared between execution and WPF.
- Add implementation workflows that bridge research output into trade intent.
- Add guardrails, approvals, and exception handling for paper/live progression.
- Add pre-trade compliance checks wired into the order submission path (mandate limits, concentration rules, exposure constraints).
- Add post-trade compliance validation: flag fills that cause mandate drift, surface breaks in the governance exception queue.

**Target phase**

- Phase 2
- Phase 6

### 5. UI and API Workstation Bootstrap

**Projects**

- `src/Meridian.Ui.Shared`
- `src/Meridian.Wpf`
- `src/Meridian.Ui.Services`

**Key anchors**

- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/UiEndpoints.cs`
- `src/Meridian.Ui.Services/Services/SearchService.cs`
- `src/Meridian.Wpf/Views/StrategyRunsPage.xaml.cs`
- `src/Meridian.Wpf/Views/RunDetailPage.xaml.cs`
- `src/Meridian.Wpf/Views/RunPortfolioPage.xaml.cs`
- `src/Meridian.Wpf/Views/RunLedgerPage.xaml.cs`

**Current state**

Workstation APIs and bootstrap behavior still need stronger real-data orchestration.

**Primary backlog**

- Replace placeholder workstation payloads with real shared-run and governance bootstrap data.
- Keep WPF and HTTP workstation contracts aligned.
- Add workspace bootstrap for governance queues, report readiness, and reconciliation summaries.

**Target phase**

- Phase 1
- Phase 2

### 6. Security Master Domain and Contracts

**Projects**

- `src/Meridian.Contracts`
- `src/Meridian.FSharp`
- `src/Meridian.Application`

**Key anchors**

- `src/Meridian.Contracts/SecurityMaster/SecurityDtos.cs`
- `src/Meridian.Contracts/SecurityMaster/SecurityCommands.cs`
- `src/Meridian.Contracts/SecurityMaster/SecurityQueries.cs`
- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`
- `src/Meridian.FSharp/Domain/SecurityClassification.fs`
- `src/Meridian.Application/SecurityMaster/SecurityMasterService.cs`
- `src/Meridian.Application/SecurityMaster/SecurityMasterQueryService.cs`

**Current state**

Security Master already has a strong backend baseline, but it is still under-productized.

**Primary backlog**

- Elevate Security Master into first-class workstation workflows.
- Extend instrument metadata to support cash-flow, reporting, and reconciliation use cases.
- Add explicit links between instruments and future account/entity/fund structures.
- Add governance-grade query surfaces and richer workstation DTOs.

**Target phase**

- Phase 2

### 7. Security Master Storage and Projection Pipeline

**Projects**

- `src/Meridian.Storage`
- `src/Meridian.Application`

**Key anchors**

- `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.cs`
- `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterEventStore.cs`
- `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterSnapshotStore.cs`
- `src/Meridian.Storage/SecurityMaster/SecurityMasterProjectionCache.cs`
- `src/Meridian.Storage/SecurityMaster/SecurityMasterMigrationRunner.cs`
- `src/Meridian.Storage/SecurityMaster/Migrations/001_security_master.sql`
- `src/Meridian.Application/SecurityMaster/SecurityMasterProjectionService.cs`
- `src/Meridian.Application/Composition/SecurityMasterStartup.cs`

**Current state**

Projection-backed storage exists and is the right seam for expanding governance query support.

**Primary backlog**

- Add projection fields for reporting classifications, liquidity attributes, and issuer hierarchies.
- Add ownership and tagging hooks for fund, sleeve, vehicle, and account mapping.
- Add cached governance-facing query shapes for reconciliation and reporting.

**Target phase**

- Phase 2
- Phase 3

### 8. Account, Entity, and Fund Structure Models

**Projects**

- `src/Meridian.Contracts`
- `src/Meridian.FSharp`
- `src/Meridian.Application`
- `src/Meridian.Wpf`

**Key anchors**

- New module family to be introduced near:
  - `src/Meridian.Contracts/`
  - `src/Meridian.FSharp/Domain/`
  - `src/Meridian.Application/Services/`
  - `src/Meridian.Wpf/ViewModels/`

**Current state**

No first-class `Account`, `Entity`, `Fund`, `Sleeve`, or `Vehicle` model is obvious in the current source tree.

**Primary backlog**

- Add contracts and domain models for account, entity, fund, sleeve, and vehicle structures.
- Add ownership relationships between entities, strategies, portfolios, ledgers, and instruments.
- Add governance UI and read-model support for these structures.

**Target phase**

- Phase 2

### 9. Ledger Kernel, Multi-Ledger, and Trial Balance

**Projects**

- `src/Meridian.Ledger`
- `src/Meridian.FSharp.Ledger`

**Key anchors**

- `src/Meridian.Ledger/ProjectLedgerBook.cs`
- `src/Meridian.Ledger/LedgerBookKey.cs`
- `src/Meridian.Ledger/Ledger.cs`
- `src/Meridian.Ledger/LedgerSnapshot.cs`
- `src/Meridian.Ledger/LedgerAccount.cs`
- `src/Meridian.FSharp.Ledger/LedgerTypes.fs`
- `src/Meridian.FSharp.Ledger/Posting.fs`
- `src/Meridian.FSharp.Ledger/LedgerReadModels.fs`
- `src/Meridian.FSharp.Ledger/JournalValidation.fs`

**Current state**

The kernel already supports meaningful ledger logic and trial-balance read models, but still feels project-scoped instead of fund-structure-scoped.

**Primary backlog**

- Extend ledger grouping from project-ledger concepts to fund/entity/sleeve/account-ledger groupings.
- Add consolidated and per-ledger trial-balance support.
- Add account-summary and governance-grade ledger views.
- Preserve F# ownership for rule-heavy accounting logic.

**Target phase**

- Phase 3

### 10. Cash-Flow and Reconciliation Kernel

**Projects**

- `src/Meridian.FSharp.Ledger`
- `src/Meridian.Strategies`
- `src/Meridian.Contracts`

**Key anchors**

- `src/Meridian.FSharp.Ledger/Reconciliation.fs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`
- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`

**Current state**

There is already a narrow reconciliation kernel, but it does not yet represent a full fund-ops reconciliation engine.

**Primary backlog**

- Expand reconciliation into generalized cash, position, ledger, and external-statement matching.
- Add cash-ladder and realized-vs-projected cash-flow views.
- Add new workstation DTOs for breaks, exceptions, and cash-flow summaries.
- Keep rule-heavy matching logic in F# and orchestration in C#.
- Add external custodian statement adapters (CSV position file, SWIFT MT940/942, JSON) for automated T+1 reconciliation.
- Add break classification (quantity, settlement date, currency, instrument) and severity scoring.
- Add multi-custodian break queue with assignment, resolution tracking, and automated exception-based processing.

**Target phase**

- Phase 3
- Phase 4

### 11. Governance Read Models and Workstation DTOs

**Projects**

- `src/Meridian.Strategies`
- `src/Meridian.Contracts`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`
- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunDetailViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunPortfolioViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs`
- `src/Meridian.Wpf/Views/StrategyRunsPage.xaml`
- `src/Meridian.Wpf/Views/RunPortfolioPage.xaml`
- `src/Meridian.Wpf/Views/RunLedgerPage.xaml`

**Current state**

Shared run, portfolio, and ledger read services are already the best seam for governance productization.

**Primary backlog**

- Add DTOs for accounts/entities, multi-ledger selectors, reconciliation queues, cash ladders, NAV, attribution, and report-pack requests.
- Add governance view models and pages that sit above the existing run/portfolio/ledger drill-ins.
- Use shared read models as the central orchestration seam instead of creating parallel query stacks.

**Target phase**

- Phase 2
- Phase 4

### 12. NAV, Attribution, and Governance Exceptions

**Projects**

- `src/Meridian.Application`
- `src/Meridian.Strategies`
- `src/Meridian.Contracts`

**Key anchors**

- New service layer near:
  - `src/Meridian.Application/Services/`
- Existing supporting seams:
  - `src/Meridian.Strategies/Services/PortfolioReadService.cs`
  - `src/Meridian.Strategies/Services/LedgerReadService.cs`
  - `src/Meridian.Application/SecurityMaster/SecurityMasterQueryService.cs`

**Current state**

No dedicated NAV or attribution service is obvious yet.

**Primary backlog**

- Introduce `NavAttributionService`-style orchestration.
- Add valuation, attribution, and governance exception projections.
- Link valuation exceptions to Security Master, ledger, and reporting outputs.

**Target phase**

- Phase 4

### 13. Reporting and Governed Export Workflows

**Projects**

- `src/Meridian.Storage`
- `src/Meridian.Ui.Shared`
- `src/Meridian.Wpf`
- `src/Meridian.Contracts`

**Key anchors**

- `src/Meridian.Storage/Export/AnalysisExportService.cs`
- `src/Meridian.Storage/Export/AnalysisExportService.Formats.Xlsx.cs`
- `src/Meridian.Storage/Export/AnalysisQualityReport.cs`
- `src/Meridian.Storage/Export/ExportRequest.cs`
- `src/Meridian.Storage/Export/ExportProfile.cs`
- `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`
- `src/Meridian.Wpf/Views/AnalysisExportPage.xaml`
- `src/Meridian.Wpf/Views/AnalysisExportWizardPage.xaml`
- `src/Meridian.Wpf/Views/ExportPresetsPage.xaml`
- `src/Meridian.Wpf/Services/ExportPresetService.cs`

**Current state**

Export infrastructure is mature, but still analysis-export oriented rather than governed-report oriented.

**Primary backlog**

- Add `ReportGenerationService` above the current export layer.
- Add report-pack contracts for investor, board, compliance, and fund-ops outputs.
- Extend XLSX export into templated workbook and report-pack generation.
- Add governed reporting pages and API endpoints.
- Add persisted report definitions, publication history, and version metadata.

**Target phase**

- Phase 5

### 14. Report Quality, Validation, and Audit Attachments

**Projects**

- `src/Meridian.Storage`
- `src/Meridian.Ui.Shared`

**Key anchors**

- `src/Meridian.Storage/Export/AnalysisQualityReport.cs`
- `src/Meridian.Storage/Export/ExportVerificationReport.cs`
- `src/Meridian.Storage/Export/ExportValidator.cs`
- `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs`

**Current state**

Quality-report and export-verification primitives already exist and should be promoted into reporting workflows.

**Primary backlog**

- Add dataset certification outputs for investor and compliance packs.
- Attach quality and verification artifacts to governed report bundles.
- Add quality gates before report publication.
- Add account/entity/report-scope-aware quality summaries.

**Target phase**

- Phase 5

### 15. Replay, Storage, Evidence, and Provenance

**Projects**

- `src/Meridian.Storage`
- `src/Meridian.Ui.Shared`
- `src/Meridian.Wpf`
- `src/Meridian.Application`

**Key anchors**

- `src/Meridian.Storage/Replay/JsonlReplayer.cs`
- `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs`
- `src/Meridian.Ui.Shared/Endpoints/ReplayEndpoints.cs`
- `src/Meridian.Wpf/Views/EventReplayPage.xaml`
- `src/Meridian.Application/Services/HistoricalDataQueryService.cs`
- `src/Meridian.Storage/Services/StorageCatalogService.cs`
- `src/Meridian.Storage/Services/DataLineageService.cs`

**Current state**

Replay and lineage are strong technical assets, but not yet tightly integrated into governance and reporting evidence flows.

**Primary backlog**

- Add replay-backed evidence links from reconciliation and reporting outputs.
- Replace in-memory replay session handling with a more durable coordination model.
- Add export and report provenance records tied to lineage and catalog services.
- Add account/entity/run-scoped replay queries.

**Target phase**

- Phase 4
- Phase 5

### 16. Diagnostics, Operational Summaries, and Readiness Workflows

**Projects**

- `src/Meridian.Application`
- `src/Meridian.Ui.Shared`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.Application/Services/DiagnosticBundleService.cs`
- `src/Meridian.Application/Services/DailySummaryWebhook.cs`
- `src/Meridian.Application/Services/ErrorTracker.cs`
- `src/Meridian.Application/Services/PreflightChecker.cs`
- `src/Meridian.Ui.Shared/Endpoints/DiagnosticsEndpoints.cs`
- `src/Meridian.Wpf/Views/DiagnosticsPage.xaml`

**Current state**

Operational diagnostics exist, but not yet in fund-ops or reporting-readiness form.

**Primary backlog**

- Extend diagnostics to include reconciliation and reporting execution artifacts.
- Add report-readiness and data-readiness endpoints.
- Add scheduled operational summaries for governance and reporting workflows.
- Add durable history for generated summaries and bundles.

**Target phase**

- Phase 4
- Phase 5

### 17. Provider and Data-Ops Control Plane

**Projects**

- `src/Meridian.Infrastructure`
- `src/Meridian.Ui.Shared`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/ProviderEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/CatalogEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/StorageEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs`
- `src/Meridian.Wpf/Views/ProviderPage.xaml`
- `src/Meridian.Wpf/Views/ProviderHealthPage.xaml`
- `src/Meridian.Wpf/Views/StoragePage.xaml`
- `src/Meridian.Wpf/Views/DataCalendarPage.xaml`

**Current state**

Data-ops controls exist, but they are not yet shaped around fund-ops reporting and governance readiness.

**Primary backlog**

- Add data-readiness workflows for reporting cutoffs, coverage, freshness, and provider confidence.
- Add account/entity/report-scope-aware readiness dashboards.
- Add source-selection and fallback rules for governance/reporting workflows.
- Add scheduled refresh and reporting cutoff controls.

**Target phase**

- Phase 5

### 18. Post-Trade Allocation Rule Engine

**Projects**

- `src/Meridian.FSharp`
- `src/Meridian.Application`
- `src/Meridian.Contracts`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.FSharp/Domain/` (new `TradeAllocation.fs`)
- `src/Meridian.Execution/OrderManagementSystem.cs`
- `src/Meridian.Application/` (new `PostTradeAllocationService.cs`)
- `src/Meridian.Contracts/Workstation/` (new allocation DTOs)

**Current state**

Fills flow from the execution layer into the ledger directly. There is no rules-based allocation layer to distribute quantities by strategy, tax lot, account, or trader before ledger posting.

**Primary backlog**

- Define `TradeAllocationRule` domain model: allocation by strategy, tax lot, account, or trader.
- Add F# allocation kernel (pure function: fill + rules → allocation breakdown).
- Add `PostTradeAllocationService` in `Meridian.Application`: apply rules to fills, distribute quantities to fund/sleeve/account ledger lines.
- Add workstation UI for reviewing and overriding allocations before confirmation.
- Add allocation approval workflow: operator review → confirm or reject → ledger posting.
- Add tests for allocation rule evaluation, override paths, and edge cases (partial fills, fractional quantities).

**Target phase**

- Phase 6

### 19. Model Portfolio Management and Rebalancing

**Projects**

- `src/Meridian.FSharp`
- `src/Meridian.Application`
- `src/Meridian.Contracts`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.FSharp/Domain/` (new `ModelPortfolio.fs`)
- `src/Meridian.Application/` (new `ModelPortfolioDriftMonitor.cs`, `RebalancingSignalService.cs`)
- `src/Meridian.Contracts/` (new `ModelPortfolioDtos.cs`)
- `src/Meridian.Wpf/ViewModels/` (new `ModelPortfolioViewModel.cs`)

**Current state**

There is no model portfolio concept — strategies produce fills but there is no target-weight definition, drift monitoring, or rebalancing workflow.

**Primary backlog**

- Define `ModelPortfolio` domain model: target weights by instrument, asset class, duration bucket, or factor exposure.
- Add drift monitoring service: compare current portfolio weights against model targets; compute absolute and relative drift per position.
- Add rebalancing signal generator: produce candidate order lists to restore target weights within configurable drift tolerances.
- Add mandate-aware rebalancing constraints: position limits, liquidity minimums, and mandate restrictions from Security Master and compliance layer.
- Add WPF surfaces: model portfolio construction, drift dashboard, rebalancing review and approval workflow.
- Add tests for drift calculation accuracy, constraint satisfaction, and order sizing.

**Target phase**

- Phase 7

### 20. Regulatory Reporting

**Projects**

- `src/Meridian.Application`
- `src/Meridian.FSharp`
- `src/Meridian.Contracts`
- `src/Meridian.Wpf`

**Key anchors**

- `src/Meridian.Application/` (new `RegulatoryReportingService.cs`)
- `src/Meridian.FSharp/Domain/` (new `RegulatoryReporting.fs`)
- `src/Meridian.Contracts/` (new `RegulatoryReportingDtos.cs`)
- `src/Meridian.Storage/Export/AnalysisExportService.cs`

**Current state**

The export and report-pack infrastructure exists for internal governance reports. There are no regulatory-specific data models or generation services for MiFID II, AIFMD, or PRIIPs.

**Primary backlog**

- Define regulatory reporting domain models: `TransactionReportRecord`, `CostChargeDisclosureRecord`, `RegulatoryReportBatch`.
- Implement MiFID II RTS 28 best-execution report generation (quarterly aggregation by venue and instrument class).
- Implement MiFID II Article 24 cost-and-charges disclosure records linked to fills and portfolio positions.
- Add PRIIPs KID data assembly service (performance scenarios, cost structures, risk rating inputs); defer PDF rendering to a later milestone but produce the data model.
- Add AIFMD Annex IV data aggregation: AUM, leverage, liquidity, and risk exposure summaries.
- Add shadow-NAV calculation path: Meridian-computed NAV alongside administrator-issued NAV for cross-check and versioning.
- Add locked reporting periods: prevent backdating once a period is closed.
- Add regulated report history with immutable audit log and version control.
- Add automated report distribution metadata: recipient, format, timestamp, and version for each published pack.
- Add tests for batch generation correctness, classification mapping, shadow-NAV delta detection, and required field completeness.

**Target phase**

- Phase 7

## Cross-Module Sequencing

### Wave A

- Workspace shell and shared run model
- Research workflow unification
- Workstation bootstrap alignment

### Wave B

- Trade management
- Security Master productization
- Account/entity foundations

### Wave C

- Multi-ledger
- Trial balance
- Cash-flow modeling

### Wave D

- Reconciliation engine
- NAV and attribution
- Governance exception workflows

### Wave E

- Report generation
- Report validation and quality attachments
- Evidence, provenance, and investor/stakeholder delivery

### Wave F

- Post-trade allocation rule engine
- Regulatory reporting (MiFID II, AIFMD Annex IV)
- Shadow-NAV and locked reporting periods

### Wave G

- Model portfolio management and drift monitoring
- Rebalancing signal generation and approval workflows

## Immediate Next Slices

1. Extend the delivered account/entity structures into richer Fund Accounts review workflows, building on the current account-queue, provider-routing, shared-data, balance-evidence, and reconciliation-readiness briefing.
2. Expand shared run and ledger read services for paper/live, multi-ledger, and reconciliation summaries.
3. Introduce application services for reconciliation orchestration and report generation.
4. Add WPF governance surfaces for reconciliation, cash-flow, and reporting.
5. Extend export infrastructure into governed report-pack workflows.
6. Add post-trade allocation rule domain model and `PostTradeAllocationService` shell.
7. Add external custodian statement adapter and break classification for T+1 reconciliation.
8. Add MiFID II RTS 28 and cost/charges data models plus `RegulatoryReportingService` shell.
9. Add model portfolio domain model and drift monitoring service.

## Related Documents

- [Fund Management Product Vision and Capability Matrix](fund-management-product-vision-and-capability-matrix.md)
- [Trading Workstation Migration Blueprint](trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)
- [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md)
- [Project Roadmap](../status/ROADMAP.md)
