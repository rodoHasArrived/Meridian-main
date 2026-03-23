# Governance and Fund Operations Blueprint

**Last Updated:** 2026-03-22

## Summary

Implement a Meridian-native governance and fund-operations capability set that makes `Security Master`, `account and entity management`, `multi-ledger accounting`, `trial balance`, `cash-flow modeling`, `reconciliation`, `trade-management support`, and `report generation` first-class product workflows inside the existing `Governance` workstation and broader fund-management product.

This blueprint starts from the current repository state:

- shared run, portfolio, and ledger read services already exist
- WPF already has `StrategyRuns`, `RunDetail`, `RunPortfolio`, and `RunLedger` surfaces
- Security Master contracts, services, storage, migrations, and F# domain modules already exist
- run-scoped reconciliation contracts, services, and workstation endpoints already exist
- direct-lending services, migrations, projections, and `/api/loans/*` endpoints already exist as the first deep governance/UFL vertical slice
- export infrastructure already exists for JSONL, Parquet, Arrow, XLSX, and CSV

The design goal is to finish these capabilities without creating a parallel architecture outside Meridian's current workstation, strategy, ledger, and storage layers, while making Meridian credible as a comprehensive front-, middle-, and back-office fund-management platform.

For the first deep vertical implementation of these governance patterns in direct lending, see [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md).

## Scope

### In scope

- Security Master productization for workstation-facing use
- Multi-ledger accounting model for fund, sleeve, vehicle, and entity views
- Consolidated and per-ledger trial-balance views
- Cash-flow projection and realized-vs-projected governance views
- Reconciliation engine for portfolio, ledger, cash, positions, and external statements
- Report generation tools for board, investor, compliance, and fund-ops packs
- Governance workspace UX for breaks, review queues, and drill-ins

### Out of scope

- Full investor portal
- Full statutory/tax reporting engine
- OCR/document ingestion for broker statements
- Replacing the current ledger with an entirely new accounting subsystem
- Cloud-only or SaaS-specific workflows that conflict with Meridian's local-first architecture

### Assumptions

- Governance remains a workstation inside Meridian, not a separate app.
- Core financial kernels should prefer F# when the logic is math-heavy, state-heavy, or rule-heavy.
- Existing C# services remain the orchestration boundary for WPF, HTTP, and export flows.
- External statement ingestion will start from structured files and explicit adapters, not unstructured PDFs.

## Architecture

The implementation should be organized into six collaborating layers:

1. Security Master platform layer
2. Fund accounting and multi-ledger kernel
3. Cash-flow and reconciliation domain services
4. Governance application services and projections
5. Governance workstation UI and API endpoints
6. Report generation and export packaging

### Layer 1: Security Master platform layer

Use existing Security Master components as the authoritative instrument-definition source for:

- canonical identifiers
- classifications
- economic definitions
- term-level metadata required by cash-flow and reporting logic

Primary anchors:

- `src/Meridian.Contracts/SecurityMaster/`
- `src/Meridian.Application/SecurityMaster/`
- `src/Meridian.Storage/SecurityMaster/`
- `src/Meridian.FSharp/Domain/SecurityMaster.fs`
- `src/Meridian.FSharp/Domain/SecurityEconomicDefinition.fs`
- `src/Meridian.FSharp/Domain/SecurityClassification.fs`

### Layer 2: Fund accounting and multi-ledger kernel

Extend the ledger model to support:

- multiple ledger books per fund structure
- per-ledger and consolidated views
- reconciliation-friendly snapshots
- explicit entity, sleeve, and vehicle boundaries

Prefer to keep the accounting kernel centered around:

- `src/Meridian.Ledger/`
- `src/Meridian.FSharp.Ledger/`

Use F# for consolidation logic and accounting transforms when the logic becomes rule-heavy.

### Layer 3: Cash-flow and reconciliation domain services

Add pure or mostly-pure domain kernels for:

- projected cash ladders
- instrument-aware cash events
- reconciliation matching rules
- break classification and severity

Suggested new F# modules:

- `src/Meridian.FSharp/Domain/CashFlowProjection.fs`
- `src/Meridian.FSharp/Domain/CashFlowRules.fs`
- `src/Meridian.FSharp.Ledger/ReconciliationRules.fs`
- `src/Meridian.FSharp.Ledger/ReconciliationTypes.fs`

### Layer 4: Governance application services and projections

Use C# application services to orchestrate:

- portfolio + ledger + Security Master joins
- reconciliation runs
- break queues
- report-pack generation requests
- governance dashboards and drill-down projections

Suggested new C# services:

- `src/Meridian.Application/Services/ReconciliationEngineService.cs`
- `src/Meridian.Application/Services/ReportGenerationService.cs`
- `src/Meridian.Application/Services/NavAttributionService.cs`
- `src/Meridian.Application/Services/GovernanceExceptionService.cs`

### Layer 5: Governance workstation UI and API endpoints

Keep UI responsibilities in workstation-facing contracts and thin WPF view models.

Primary UI anchors:

- `src/Meridian.Contracts/Workstation/`
- `src/Meridian.Wpf/ViewModels/StrategyRunPortfolioViewModel.cs`
- `src/Meridian.Wpf/ViewModels/StrategyRunLedgerViewModel.cs`
- `src/Meridian.Wpf/ViewModels/RunMatViewModel.cs`
- `src/Meridian.Wpf/Views/RunPortfolioPage.xaml`
- `src/Meridian.Wpf/Views/RunLedgerPage.xaml`
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

Suggested new governance UI slices:

- reconciliation queue view
- cash-flow ladder view
- multi-ledger selector and consolidation view
- report-pack generation wizard

### Layer 6: Report generation and export packaging

Reuse Meridian's current export stack rather than inventing a second reporting pipeline.

Primary anchors:

- `src/Meridian.Storage/Export/AnalysisExportService.cs`
- `src/Meridian.Storage/Export/AnalysisExportService.Formats.Xlsx.cs`
- `src/Meridian.Storage/Export/ExportProfile.cs`
- `src/Meridian.Ui.Shared/Endpoints/ExportEndpoints.cs`

Report generation should add:

- governance-specific export profiles
- structured report-pack DTOs
- provenance links back to ledger, portfolio, and reconciliation sources

## Interfaces and Models

Name the public-facing surfaces before implementation.

### Security Master integration

Reuse where possible:

- `ISecurityMasterQueryService`
- `ISecurityMasterService`
- `ISecurityResolver`

Add if needed:

- `SecurityMasterWorkstationDto`
- `SecurityClassificationSummaryDto`
- `SecurityEconomicDefinitionSummaryDto`

Suggested path:

- `src/Meridian.Contracts/Workstation/SecurityMasterWorkstationDtos.cs`

### Multi-ledger and trial balance

Add:

- `LedgerGroupId`
- `LedgerGroupSummaryDto`
- `LedgerConsolidationRequest`
- `LedgerConsolidationResultDto`
- `TrialBalanceViewDto`
- `TrialBalanceRowDto`
- `MultiLedgerSelectionDto`

Suggested paths:

- `src/Meridian.Contracts/Workstation/GovernanceLedgerDtos.cs`
- `src/Meridian.FSharp.Ledger/ReconciliationTypes.fs`

### Cash-flow modeling

Add:

- `CashFlowProjectionRequest`
- `CashFlowProjectionDto`
- `CashFlowBucketDto`
- `ProjectedCashEventDto`
- `CashFlowVarianceDto`

Suggested paths:

- `src/Meridian.Contracts/Workstation/CashFlowDtos.cs`
- `src/Meridian.FSharp/Domain/CashFlowProjection.fs`

### Reconciliation engine

Add:

- `ReconciliationRunRequest`
- `ReconciliationRunDto`
- `ReconciliationMatchDto`
- `ReconciliationBreakDto`
- `ReconciliationBreakStatus`
- `ReconciliationBreakCategory`
- `ReconciliationSourceKind`

Suggested paths:

- `src/Meridian.Contracts/Workstation/ReconciliationDtos.cs`
- `src/Meridian.Application/Services/ReconciliationEngineService.cs`
- `src/Meridian.FSharp.Ledger/ReconciliationRules.fs`

### Report generation

Add:

- `ReportPackRequest`
- `ReportPackDto`
- `ReportSectionDto`
- `ReportArtifactDto`
- `GovernanceReportProfile`

Suggested paths:

- `src/Meridian.Contracts/Workstation/ReportPackDtos.cs`
- `src/Meridian.Application/Services/ReportGenerationService.cs`

## Data Flow

### 1. Security Master-backed governance read flow

1. User opens a governance surface from WPF or HTTP.
2. `PortfolioReadService` and `LedgerReadService` load current read models.
3. Security identifiers resolve through `ISecurityResolver`.
4. Security classifications and economic definitions are enriched through Security Master query services.
5. Workstation DTOs combine portfolio, ledger, and Security Master metadata for UI consumption.

### 2. Cash-flow projection flow

1. User selects a fund, sleeve, strategy run, or ledger group.
2. Governance application service loads:
   - positions and exposures
   - current ledger balances
   - journal history
   - Security Master economic definitions
3. F# projection kernel computes projected cash events and buckets.
4. C# service maps the projection into workstation DTOs.
5. WPF and export flows render the same projection from one query path.

### 3. Reconciliation engine flow

1. User starts a reconciliation run or an automated workflow triggers one.
2. Input sources are gathered:
   - portfolio positions
   - ledger balances/journals
   - cash balances
   - external structured statements
3. F# reconciliation rules perform matching and break detection.
4. `ReconciliationEngineService` persists run metadata and break outputs.
5. Governance queue surfaces unresolved breaks, matched items, and exception states.
6. Report packs can include reconciliation snapshots and unresolved breaks.

### 4. Report generation flow

1. User picks a report profile such as:
   - board pack
   - investor pack
   - compliance pack
   - operations pack
2. `ReportGenerationService` loads required portfolio, ledger, reconciliation, and cash-flow sections.
3. Existing export services generate artifacts in configured formats.
4. Generated report packs reference their source snapshots for auditability.

## Edge Cases and Risks

### Security Master drift

Risk:
- unresolved or stale instrument metadata pollutes cash-flow, reconciliation, and compliance outputs

Mitigation:
- explicit unresolved-state DTOs
- governance break queues for missing metadata
- report-pack warnings for degraded classification quality

### Multi-ledger ambiguity

Risk:
- ledger grouping semantics drift across funds, sleeves, and legal entities

Mitigation:
- define `LedgerGroupId` and grouping rules centrally
- keep consolidation logic in one kernel
- test per-ledger and consolidated cases together

### Reconciliation false positives

Risk:
- noisy break generation undermines trust

Mitigation:
- category-specific matching rules
- confidence scores
- manual resolution workflow with explicit status transitions

### Reporting forked logic

Risk:
- report generation re-implements portfolio or ledger logic separately

Mitigation:
- force reports to consume shared governance DTOs and read services
- keep formatting separate from calculation

### Overbuilding too early

Risk:
- trying to fully match enterprise fund admin systems before the foundations are stable

Mitigation:
- sequence the work strictly
- treat Security Master, multi-ledger, and reconciliation as enabling kernels
- delay advanced report polish until shared DTOs are stable

## Test Plan

### Unit tests

- F# cash-flow projection kernels
- F# reconciliation matching and break classification
- Security Master economic-definition and classification mapping
- ledger consolidation and trial-balance calculations

Suggested projects:

- `tests/Meridian.FSharp.Tests/`
- `tests/Meridian.Tests/Ledger/`
- `tests/Meridian.Tests/Strategies/`
- `tests/Meridian.Tests/SecurityMaster/`

### Application/service tests

- `ReconciliationEngineService` orchestration
- `ReportGenerationService` section assembly
- governance query service shaping

Suggested projects:

- `tests/Meridian.Tests/Application/`
- `tests/Meridian.Tests/Strategies/`

### WPF tests

- reconciliation queue view models
- multi-ledger selection and drill-in view models
- cash-flow and report-pack workflows

Suggested projects:

- `tests/Meridian.Wpf.Tests/Services/`
- `tests/Meridian.Wpf.Tests/Views/`

### Validation commands

```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Wpf.Tests -c Release /p:EnableWindowsTargeting=true
```

## Implementation Checklist

### Phase F1: Security Master as a Product Platform

- [ ] Audit existing Security Master contracts, services, storage, and F# domain modules for workstation-facing gaps.
- [ ] Define workstation DTOs for Security Master identity, classification, and economic-definition summaries.
- [ ] Wire Security Master enrichment into `PortfolioReadService` and `LedgerReadService`.
- [ ] Add governance-facing drill-ins for Security Master-backed instrument details.
- [ ] Add tests for unresolved instrument identity and degraded metadata cases.

### Phase F2: Multi-Ledger Governance Foundation

- [ ] Define `LedgerGroupId` and ledger grouping rules.
- [ ] Add consolidated trial-balance query support.
- [ ] Add multi-ledger selection and per-ledger drill-in DTOs.
- [ ] Add reconciliation-friendly snapshot outputs.
- [ ] Add WPF governance surfaces for trial balance and consolidated ledger views.
- [ ] Add tests for per-ledger, cross-ledger, and consolidated cases.

### Phase F2.5: Reconciliation Engine

- [ ] Define reconciliation DTOs and rule categories.
- [ ] Implement structured source adapters for ledger, portfolio, cash, and external statements.
- [ ] Build F# reconciliation rule kernel and C# orchestration service.
- [ ] Add break queue states, assignment, and resolution workflow.
- [ ] Add governance UI for unresolved breaks and matched items.
- [ ] Add tests for matching, mismatching, partial matching, and break severity.

### Phase F3: Cash-Flow Modeling and Projection

- [ ] Define cash-flow DTOs and projection request model.
- [ ] Implement F# cash-flow projection kernel.
- [ ] Add Security Master-backed instrument cash rules.
- [ ] Add realized-vs-projected variance views.
- [ ] Add governance cash ladder and liquidity views.
- [ ] Add tests for coupons, distributions, financing, fees, and projected-vs-realized reconciliation.

### Phase F4: Fund Operations Workstation

- [ ] Define governance dashboard sections and quick actions.
- [ ] Add NAV and attribution baseline service.
- [ ] Add governance queue for valuation exceptions, reconciliation breaks, and pending actions.
- [ ] Add report generation wizard entry points from Governance.
- [ ] Ensure all flows reuse shared workstation query paths.

### Phase F4.5: Report Generation Tools

- [ ] Define `ReportPackRequest`, `ReportPackDto`, and report section models.
- [ ] Add governance export profiles for board, investor, compliance, and operations packs.
- [ ] Implement report section assembly using shared read models.
- [ ] Add XLSX-first reporting support with audit metadata and source references.
- [ ] Add packaging of reconciliation, cash-flow, trial-balance, and portfolio outputs into one governed artifact set.
- [ ] Add tests for section assembly, export validation, and artifact completeness.

### Phase F5: Compliance and Policy Overlay

- [ ] Define classification-aware mandate rule inputs.
- [ ] Connect Security Master classifications to risk/compliance evaluation.
- [ ] Add governance exception queue DTOs and UI.
- [ ] Add promotion gates that require governance/accounting readiness.
- [ ] Add tests for mandate breaches, overrides, and promotion blocking.

### Cross-cutting

- [ ] Keep F# kernels pure and deterministic wherever possible.
- [ ] Keep WPF code-behind thin; prefer `BindableBase` view models and services.
- [ ] Reuse existing export and workstation infrastructure before adding new stacks.
- [ ] Update roadmap, feature inventory, and improvements docs in the same PRs as implementation changes.

## Suggested first PR slices

1. Security Master workstation DTOs plus enrichment in `PortfolioReadService` and `LedgerReadService`
2. Multi-ledger grouping and consolidated trial-balance query path
3. Reconciliation DTOs plus F# rules spike and C# orchestration shell
4. Cash-flow projection kernel plus governance cash-ladder read path
5. Report-pack contracts plus export-profile integration

## Open Questions

- What external statement formats should the first reconciliation engine support?
- Should multi-ledger grouping be explicit configuration, derived from strategy/fund metadata, or both?
- Which cash-flow categories are required for the first release: coupons, distributions, financing, fees, margin, subscriptions/redemptions?
- Does NAV/attribution need to ship before or after the first governed report-pack workflow?
- Should report generation produce only artifacts, or also versioned stored report snapshots queryable inside Meridian?

## Related Documents

- [Fund Management Product Vision and Capability Matrix](fund-management-product-vision-and-capability-matrix.md)
- [Fund Management Module Implementation Backlog](fund-management-module-implementation-backlog.md)
- [Fund Management PR-Sequenced Roadmap](fund-management-pr-sequenced-roadmap.md)
- [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md)
- [Project Roadmap](../status/ROADMAP.md)
