# Meridian Fund Management PR-Sequenced Execution Roadmap

**Owner:** Core Team
**Audience:** Engineering leads, implementers, and reviewers
**Last Updated:** 2026-03-21
**Status:** Active execution roadmap

## Purpose

This document translates the fund-management module backlog into PR-sized execution slices with:

- explicit dependency order
- recommended parallel lanes
- suggested ownership boundaries
- low-conflict file/module groupings

The goal is to let multiple contributors work concurrently without repeatedly colliding in the same projects and files.

## How to Use This Document

- Treat each `PR-XX` item as a reviewable implementation slice.
- Prefer merging shared-contract and shared-read-model slices before UX-heavy dependent slices.
- Use the `Can run with` column to identify safe concurrency lanes.
- Avoid combining slices that share the same primary write set unless they are intentionally coordinated.

## Parallel Delivery Lanes

| Lane | Theme | Primary write scope |
|------|-------|---------------------|
| Lane A | Workstation and front-office UX | `src/Meridian.Wpf`, `src/Meridian.Ui.Services`, parts of `src/Meridian.Ui.Shared` |
| Lane B | Shared contracts and orchestration | `src/Meridian.Contracts`, `src/Meridian.Strategies`, `src/Meridian.Application` |
| Lane C | Governance and accounting kernel | `src/Meridian.FSharp`, `src/Meridian.FSharp.Ledger`, `src/Meridian.Ledger`, parts of `src/Meridian.Storage` |
| Lane D | Reporting, evidence, and data-ops support | `src/Meridian.Storage`, `src/Meridian.Ui.Shared`, selected WPF pages |

## Dependency Rules

- Contract-first slices should merge before dependent UI and endpoint slices.
- F# kernel changes should stabilize before broad orchestration and WPF visualization layers are built on top.
- Reporting should build on top of reconciliation, evidence, and quality primitives instead of duplicating them.
- WPF shell/layout slices can run early in parallel with backend contracts as long as they avoid hard-coding temporary models.

## PR Roadmap

| PR | Title | Primary lane | Depends on | Can run with | Primary write scope |
|----|-------|--------------|------------|--------------|---------------------|
| PR-01 | Workspace shell hardening | Lane A | None | PR-02, PR-03 | `Meridian.Wpf`, `Meridian.Ui.Services` |
| PR-02 | Shared run contract expansion | Lane B | None | PR-01, PR-03 | `Meridian.Contracts`, `Meridian.Strategies` |
| PR-03 | Workstation bootstrap payload alignment | Lane B | None | PR-01, PR-02 | `Meridian.Ui.Shared`, `Meridian.Strategies`, `Meridian.Contracts` |
| PR-04 | Research workflow unification | Lane A | PR-01, PR-02 | PR-05, PR-06 | `Meridian.Wpf`, `Meridian.Ui.Services`, `Meridian.Backtesting` |
| PR-05 | Trade-management contract baseline | Lane B | PR-02 | PR-04, PR-06 | `Meridian.Contracts`, `Meridian.Execution`, `Meridian.Execution.Sdk` |
| PR-06 | Security Master productization baseline | Lane B | PR-02 | PR-04, PR-05, PR-07 | `Meridian.Contracts`, `Meridian.Application`, `Meridian.FSharp` |
| PR-07 | Account/entity/fund-structure contracts | Lane B | PR-02 | PR-06, PR-08 | `Meridian.Contracts`, `Meridian.FSharp`, `Meridian.Application` |
| PR-08 | Security Master projection enrichment | Lane C | PR-06, PR-07 | PR-09 | `Meridian.Storage`, `Meridian.Application` |
| PR-09 | Multi-ledger kernel baseline | Lane C | PR-07 | PR-08, PR-10 | `Meridian.Ledger`, `Meridian.FSharp.Ledger` |
| PR-10 | Governance DTOs and read-model expansion | Lane B | PR-07, PR-09 | PR-11, PR-12 | `Meridian.Contracts`, `Meridian.Strategies`, `Meridian.Application` |
| PR-11 | Trading cockpit baseline | Lane A | PR-05 | PR-10, PR-12 | `Meridian.Wpf`, `Meridian.Execution` |
| PR-12 | Trial balance and cash-flow surfaces | Lane A | PR-09, PR-10 | PR-11, PR-13 | `Meridian.Wpf`, `Meridian.Strategies` |
| PR-13 | Reconciliation kernel expansion | Lane C | PR-09, PR-10 | PR-12, PR-14 | `Meridian.FSharp.Ledger`, `Meridian.Ledger` |
| PR-14 | Reconciliation orchestration and queues | Lane B | PR-13 | PR-15 | `Meridian.Application`, `Meridian.Contracts`, `Meridian.Strategies` |
| PR-15 | Reconciliation UX and governance review pages | Lane A | PR-14 | PR-16 | `Meridian.Wpf`, `Meridian.Ui.Shared` |
| PR-16 | NAV and attribution services | Lane B | PR-10, PR-13 | PR-17 | `Meridian.Application`, `Meridian.Strategies`, `Meridian.Contracts` |
| PR-17 | Report-generation contract and service baseline | Lane D | PR-10, PR-14 | PR-18, PR-19 | `Meridian.Contracts`, `Meridian.Application`, `Meridian.Storage` |
| PR-18 | XLSX/report-pack templating and persistence | Lane D | PR-17 | PR-19 | `Meridian.Storage`, `Meridian.Wpf` |
| PR-19 | Evidence, lineage, and report-quality attachments | Lane D | PR-14, PR-17 | PR-18, PR-20 | `Meridian.Storage`, `Meridian.Application`, `Meridian.Ui.Shared` |
| PR-20 | Governance reporting UX and endpoints | Lane A / D | PR-17, PR-18 | PR-19 | `Meridian.Wpf`, `Meridian.Ui.Shared` |
| PR-21 | Data-readiness and reporting-cutoff controls | Lane D | PR-17, PR-19 | PR-20 | `Meridian.Ui.Shared`, `Meridian.Wpf`, `Meridian.Storage` |
| PR-22 | Live-operations approvals and policy hooks | Lane B / C | PR-11, PR-14, PR-16 | None | `Meridian.Application`, `Meridian.Execution`, `Meridian.FSharp`, `Meridian.Contracts` |

## PR Details

### PR-01: Workspace Shell Hardening

**Goal**

Turn workspace vocabulary into durable operator shells.

**Primary anchors**

- `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Wpf/Services/WorkspaceService.cs`
- `src/Meridian.Ui.Services/Services/WorkspaceModels.cs`

**Deliverables**

- workspace-level layout persistence
- quick actions and workspace summaries
- clearer shell ownership between page navigation and workspace state

### PR-02: Shared Run Contract Expansion

**Goal**

Expand the shared run model beyond backtests.

**Primary anchors**

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`

**Deliverables**

- paper/live-compatible run shapes
- promotion state and execution summary fields
- reusable comparison and governance hooks

### PR-03: Workstation Bootstrap Payload Alignment

**Goal**

Replace placeholder workstation bootstrap payloads with real shared data.

**Primary anchors**

- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/UiEndpoints.cs`
- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`

**Deliverables**

- real bootstrap DTOs
- aligned WPF/web workstation startup payloads
- governance-ready summary hooks

### PR-04: Research Workflow Unification

**Goal**

Unify native and Lean research paths into one research workflow.

**Primary anchors**

- `src/Meridian.Wpf/Views/BacktestPage.xaml.cs`
- `src/Meridian.Wpf/ViewModels/BacktestViewModel.cs`
- `src/Meridian.Wpf/Views/LeanIntegrationPage.xaml.cs`
- `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`
- `src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs`

### PR-05: Trade-Management Contract Baseline

**Goal**

Introduce explicit trade-management contracts between execution and UI layers.

**Primary anchors**

- `src/Meridian.Execution/OrderManagementSystem.cs`
- `src/Meridian.Execution/Services/OrderLifecycleManager.cs`
- `src/Meridian.Execution/PaperTradingGateway.cs`
- `src/Meridian.Execution/Models/OrderStatusUpdate.cs`

### PR-06: Security Master Productization Baseline

**Goal**

Make Security Master a first-class product capability.

**Primary anchors**

- `src/Meridian.Contracts/SecurityMaster/SecurityDtos.cs`
- `src/Meridian.Contracts/SecurityMaster/SecurityQueries.cs`
- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.Application/SecurityMaster/SecurityMasterQueryService.cs`

### PR-07: Account/Entity/Fund-Structure Contracts

**Goal**

Introduce first-class account, entity, fund, sleeve, and vehicle structures.

**Primary anchors**

- new contract/domain/service modules under:
  - `src/Meridian.Contracts/`
  - `src/Meridian.FSharp/Domain/`
  - `src/Meridian.Application/Services/`

### PR-08: Security Master Projection Enrichment

**Goal**

Extend stored Security Master projections for governance and reporting use.

**Primary anchors**

- `src/Meridian.Storage/SecurityMaster/PostgresSecurityMasterStore.cs`
- `src/Meridian.Storage/SecurityMaster/SecurityMasterProjectionCache.cs`
- `src/Meridian.Storage/SecurityMaster/Migrations/001_security_master.sql`
- `src/Meridian.Application/SecurityMaster/SecurityMasterProjectionService.cs`

### PR-09: Multi-Ledger Kernel Baseline

**Goal**

Extend ledger foundations into explicit fund/entity/sleeve/account groupings.

**Primary anchors**

- `src/Meridian.Ledger/ProjectLedgerBook.cs`
- `src/Meridian.Ledger/LedgerBookKey.cs`
- `src/Meridian.Ledger/Ledger.cs`
- `src/Meridian.FSharp.Ledger/LedgerTypes.fs`
- `src/Meridian.FSharp.Ledger/LedgerReadModels.fs`

### PR-10: Governance DTOs and Read-Model Expansion

**Goal**

Create governance-facing DTOs and shared read models for multi-ledger, reconciliation, and reporting.

**Primary anchors**

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`

### PR-11: Trading Cockpit Baseline

**Goal**

Expose real order, fill, position, and execution-state workflows.

**Primary anchors**

- `src/Meridian.Wpf/Views/LiveDataViewerPage.xaml.cs`
- `src/Meridian.Wpf/Views/OrderBookPage.xaml.cs`
- `src/Meridian.Execution/OrderManagementSystem.cs`

### PR-12: Trial Balance and Cash-Flow Surfaces

**Goal**

Make accounting and treasury views visible in governance UX.

**Primary anchors**

- `src/Meridian.Wpf/Views/RunLedgerPage.xaml.cs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`

### PR-13: Reconciliation Kernel Expansion

**Goal**

Broaden reconciliation from narrow event matching to fund-ops break logic.

**Primary anchors**

- `src/Meridian.FSharp.Ledger/Reconciliation.fs`
- `src/Meridian.FSharp.Ledger/LedgerTypes.fs`
- `src/Meridian.Ledger/`

### PR-14: Reconciliation Orchestration and Queues

**Goal**

Add application-layer reconciliation runs, exception queues, and governance orchestration.

**Primary anchors**

- new services near `src/Meridian.Application/Services/`
- `src/Meridian.Contracts/Workstation/`
- `src/Meridian.Strategies/Services/`

### PR-15: Reconciliation UX and Governance Review Pages

**Goal**

Expose breaks, exceptions, and review workflows in WPF and API surfaces.

**Primary anchors**

- `src/Meridian.Wpf/`
- `src/Meridian.Ui.Shared/Endpoints/`

### PR-16: NAV and Attribution Services

**Goal**

Introduce valuation, attribution, and governance-exception logic.

**Primary anchors**

- new services near `src/Meridian.Application/Services/`
- `src/Meridian.Strategies/Services/PortfolioReadService.cs`
- `src/Meridian.Strategies/Services/LedgerReadService.cs`

### PR-17: Report-Generation Contract and Service Baseline

**Goal**

Build the orchestration layer above current analysis export services.

**Primary anchors**

- `src/Meridian.Storage/Export/AnalysisExportService.cs`
- `src/Meridian.Contracts/Export/`
- new services near `src/Meridian.Application/Services/`

### PR-18: XLSX/Report-Pack Templating and Persistence

**Goal**

Turn export formats into governed report packs.

**Primary anchors**

- `src/Meridian.Storage/Export/AnalysisExportService.Formats.Xlsx.cs`
- `src/Meridian.Storage/Export/ExportProfile.cs`
- `src/Meridian.Wpf/Views/AnalysisExportPage.xaml`
- `src/Meridian.Wpf/Services/ExportPresetService.cs`

### PR-19: Evidence, Lineage, and Report-Quality Attachments

**Goal**

Attach replay, lineage, validation, and quality evidence to governed outputs.

**Primary anchors**

- `src/Meridian.Storage/Replay/JsonlReplayer.cs`
- `src/Meridian.Storage/Services/DataLineageService.cs`
- `src/Meridian.Storage/Export/AnalysisQualityReport.cs`
- `src/Meridian.Storage/Export/ExportVerificationReport.cs`

### PR-20: Governance Reporting UX and Endpoints

**Goal**

Expose report-pack generation, preview, history, and download workflows.

**Primary anchors**

- `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`
- `src/Meridian.Wpf/Views/AnalysisExportWizardPage.xaml`
- `src/Meridian.Wpf/Views/ExportPresetsPage.xaml`

### PR-21: Data-Readiness and Reporting-Cutoff Controls

**Goal**

Add operator workflows for readiness, freshness, quality, and reporting cutoffs.

**Primary anchors**

- `src/Meridian.Ui.Shared/Endpoints/StorageQualityEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/ProviderExtendedEndpoints.cs`
- `src/Meridian.Ui.Shared/Endpoints/BackfillEndpoints.cs`
- `src/Meridian.Wpf/Views/ProviderHealthPage.xaml`
- `src/Meridian.Wpf/Views/StoragePage.xaml`

### PR-22: Live-Operations Approvals and Policy Hooks

**Goal**

Complete the lifecycle with approval and control hooks.

**Primary anchors**

- `src/Meridian.Application/`
- `src/Meridian.Execution/`
- `src/Meridian.FSharp/`
- `src/Meridian.Contracts/`

## Recommended Concurrency Plan

### Stage 1

Run in parallel:

- PR-01
- PR-02
- PR-03

Why:

- workspace shell, shared run contracts, and bootstrap alignment have limited overlap if teams coordinate DTO boundaries early

### Stage 2

Run in parallel after Stage 1:

- PR-04
- PR-05
- PR-06
- PR-07

Why:

- research UX, trade-management contracts, Security Master productization, and account/entity foundations can advance at the same time with only light contract coordination

### Stage 3

Run in parallel after Stage 2:

- PR-08
- PR-09
- PR-10
- PR-11

Why:

- Security Master storage enrichment, ledger kernel work, governance DTOs, and trading cockpit UX are mostly disjoint when ownership is kept clear

### Stage 4

Run in parallel after Stage 3:

- PR-12
- PR-13
- PR-16

Why:

- accounting surfaces, reconciliation kernel expansion, and NAV/attribution services can move together once shared ledger and governance models are established

### Stage 5

Run in parallel after Stage 4:

- PR-14
- PR-17
- PR-18

Why:

- reconciliation orchestration and reporting-service foundations can progress together, while report-pack templating can begin as long as contract shapes are stable

### Stage 6

Run in parallel after Stage 5:

- PR-15
- PR-19
- PR-20
- PR-21

Why:

- reconciliation UX, evidence/lineage integration, reporting UX, and readiness controls are adjacent but mostly separable by write scope

### Stage 7

Final dependent slice:

- PR-22

Why:

- approval/policy hooks should land after the core trade, governance, reconciliation, and reporting workflows exist

## Conflict-Avoidance Notes

- Keep `Meridian.Contracts` ownership narrow per PR. If two PRs need contracts, split files by domain instead of editing the same DTO file repeatedly.
- Keep `Meridian.Wpf` work partitioned by page/view model family where possible.
- Let `Meridian.FSharp.Ledger` own accounting and reconciliation rule changes rather than duplicating logic in C# services.
- Use orchestration services in `Meridian.Application` to connect modules instead of pushing cross-domain logic into endpoints or WPF code-behind.

## Related Documents

- [Fund Management Product Vision and Capability Matrix](fund-management-product-vision-and-capability-matrix.md)
- [Fund Management Module Implementation Backlog](fund-management-module-implementation-backlog.md)
- [Trading Workstation Migration Blueprint](trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](governance-fund-ops-blueprint.md)
