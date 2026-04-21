# Governance and Fund Operations Blueprint

**Last Updated:** 2026-04-14

## Summary

Implement a Meridian-native governance and fund-operations capability set that makes `account and entity management`, `multi-ledger accounting`, `trial balance`, `cash-flow modeling`, `reconciliation`, `trade-management support`, and `report generation` first-class product workflows inside the existing `Governance` workstation and broader fund-management product, building on Meridian's already-delivered `Security Master` seam.

This blueprint starts from the current repository state:

- shared run, portfolio, and ledger read services already exist
- WPF already has `StrategyRuns`, `RunDetail`, `RunPortfolio`, and `RunLedger` surfaces
- Security Master contracts, services, storage, migrations, workstation propagation, and F# domain modules already exist as an authoritative instrument-definition seam
- run-scoped reconciliation contracts, services, and workstation endpoints already exist
- direct-lending services, migrations, projections, and `/api/loans/*` endpoints already exist as the first deep governance/UFL vertical slice
- export infrastructure already exists for JSONL, Parquet, Arrow, XLSX, and CSV

The design goal is to finish these capabilities without creating a parallel architecture outside Meridian's current workstation, strategy, ledger, and storage layers, while making Meridian credible as a comprehensive front-, middle-, and back-office fund-management platform. Security Master should be treated as a delivered baseline in this blueprint, not as a future foundation wave.

For the first deep vertical implementation of these governance patterns in direct lending, see [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md).

## Current implementation foundation

The current repository now includes the first organization-rooted governance structure slice:

- `FundStructure` contracts now support `Organization`, `Business`, `Client`, and `InvestmentPortfolio` node kinds alongside the existing fund, sleeve, vehicle, entity, and account nodes.
- `BusinessKindDto` supports advisory, fund-manager, and hybrid operating models in one structure graph.
- `IFundStructureService` and `InMemoryFundStructureService` provide the first shared graph service for:
  - organization/business/client/fund/portfolio creation
  - ownership links and assignments
  - organization graph queries
  - advisory operating views
  - fund operating views
  - accounting ledger-group views
  - compatibility `FundStructureGraphDto` output for one-wave legacy consumers
- `/api/fund-structure/*` endpoints now expose the same structure graph and projection queries used by the new in-memory service.
- `CreateAccountRequest` now accepts `PortfolioId`, `LedgerReference`, `StrategyId`, and `RunId` so accounts can participate in the new structure graph without breaking the existing fund-account API.
- Fund-account and fund-structure services now persist local-first JSON snapshots under the configured storage root so organizations, businesses, clients, portfolios, links, assignments, and account state survive restarts without introducing a second governance storage architecture.
- Governance structure, advisory, fund, and accounting views now expose a shared data-access summary for `Security Master`, `historical price data`, and `backfill state`, so those operator-facing capabilities are available across all governance projections without deriving them from position holdings.
- Governance cash-flow views now support `Organization`, `Business`, `Client`, `Fund`, `Sleeve`, `Vehicle`, `InvestmentPortfolio`, `Account`, and `LedgerGroup` scopes with:
  - trailing realized cash ladders sourced from bank statements or balance-snapshot deltas
  - forward projected ladders sourced from future bank statements, pending settlement/accrued interest snapshots, or balance-trend fallback
  - Security Master-driven instrument rule projections for structure-assigned instruments, including coupon/dividend/maturity events sourced from economic definitions and corporate actions without querying position holdings
  - realized-vs-projected variance summaries and per-account contribution breakdowns
  - a shared `/api/fund-structure/cash-flow-view` query path that reuses the F# cash ladder kernel without relying on position holdings
- A shared governance fund-operations projection now exists for `fundProfileId` scopes through `FundOperationsWorkspaceReadService` plus `/api/fund-structure/workspace-view` and `/api/fund-structure/report-pack-preview`, combining:
  - account summaries and bank snapshots sourced from fund-account state
  - cash/financing posture derived from linked runs and balance snapshots
  - fund-ledger journal/trial-balance summaries
  - reconciliation posture across account and run-scoped seams
  - NAV attribution and report/export profile preview metadata
  - one reusable HTTP/service query path now consumed by the Governance WPF shell so workstation entry points stop rebuilding the same posture through parallel read services

This is intentionally still an early governance slice. Durable local-first persistence, shared Security Master/price/backfill accessibility summaries, governance cash-flow projection/variance views, and a fund-scoped workspace/report-preview API baseline are now in place, but Postgres-backed governance persistence, deeper amortization/direct-loan schedule rules, generalized reconciliation, full report packs, and publication/readiness controls still remain future implementation waves.

## Scope

### In scope

- governance and fund-operations workflows built on the delivered Security Master seam
- Multi-ledger accounting model for fund, sleeve, vehicle, and entity views
- Consolidated and per-ledger trial-balance views
- Cash-flow projection and realized-vs-projected governance views
- Reconciliation engine for portfolio, ledger, cash, positions, and external statements
- Report generation tools for board, investor, compliance, and fund-ops packs
- Governance workspace UX for breaks, review queues, and drill-ins

### Out of scope

- Full self-service investor portal (distribution of regulatory documents to LPs is a future milestone after core reporting is stable)
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

1. Delivered Security Master platform seam
2. Fund accounting and multi-ledger kernel
3. Cash-flow and reconciliation domain services
4. Governance application services and projections
5. Governance workstation UI and API endpoints
6. Report generation and export packaging

### Layer 1: Delivered Security Master platform seam

Use the existing Security Master components as the authoritative instrument-definition source for:

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

Current expectation:

- do not rebuild Security Master as a separate governance subsystem
- reuse the delivered workstation propagation and drill-in patterns already present across workstation and governance surfaces
- limit new Security Master work to governance-specific gaps, not baseline productization that is already complete

Governance architecture guard (required):

- Security Master is the sole instrument-definition and instrument-metadata source for governance and fund-ops surfaces.
- Any governance DTO that carries instrument metadata (symbol, issuer, coupon, maturity, classification, venue, lot/tick parameters, corporate-action context, or similar term fields) must reference Security Master identity/provenance fields (for example `WorkstationSecurityReference` and Security Master IDs/provenance summaries).
- Governance-local instrument definitions are prohibited unless they are adapter-only ingestion/transformation intermediates that are explicitly mapped back to Security Master identities before workstation DTO publication or persistence.

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
- `WorkstationSecurityReference`

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

Landed foundation:

- `GovernanceCashFlowQuery`
- `GovernanceCashFlowScopeKindDto`
- `GovernanceCashFlowScopeDto`
- `GovernanceCashFlowAccountViewDto`
- `GovernanceCashFlowEntryDto`
- `GovernanceCashFlowBucketDto`
- `GovernanceCashFlowLadderDto`
- `GovernanceCashFlowVarianceSummaryDto`
- `GovernanceCashFlowViewDto`

Current anchors:

- `src/Meridian.Contracts/FundStructure/FundStructureQueries.cs`
- `src/Meridian.Contracts/FundStructure/FundStructureDtos.cs`
- `src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs`
- `src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs`
- `src/Meridian.FSharp/Domain/CashFlowProjection.fs`

Current behavior:

- realized windows are built from bank-statement cash lines when present and fall back to balance-snapshot deltas when no realized lines exist
- projected windows use future-dated bank-statement lines first, then synthetic pending-settlement/accrued-interest entries, then a recent balance-trend fallback when no forward events are available
- governance nodes can attach `SecurityMasterInstrument` assignments that project coupon/dividend/maturity cash events from Security Master economic definitions and corporate actions
- projected cash entries now carry optional `SecurityId`, `SecurityDisplayName`, and `SecurityTypeName` so rule-driven flows remain traceable into later reconciliation/reporting work
- variance compares the next projected window to the trailing realized window on the same scope and currency basis
- the current slice is governance/account/ledger scoped and deliberately does not require position holdings to render cash ladders; explicit structure assignments provide the non-position basis when instrument-aware projections are needed

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

1. User selects an organization, business, client, fund, sleeve, vehicle, investment portfolio, account, or ledger group.
2. Governance application service loads:
   - current and recent balance snapshots
   - bank statement history and future-dated statement lines
   - linked account and ledger-group context
3. F# cash-flow ladder kernel buckets realized and projected entries into a common ladder shape.
4. C# governance service maps the result into scope, per-account, ladder, and variance DTOs.
5. HTTP and future workstation/reporting surfaces render the same projection from one query path.

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
- treat the delivered Security Master seam plus multi-ledger and reconciliation kernels as the enabling path
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

### Phase F1: Security Master seam delta closure

- [ ] Audit existing Security Master contracts, services, storage, workstation propagation, and F# domain modules for remaining governance-specific gaps.
- [ ] Reuse and extend current workstation DTOs only where governance workflows still need missing identity, classification, or economic-definition fields.
- [ ] Close remaining Security Master enrichment gaps in portfolio, ledger, reconciliation, and report-generation paths.
- [ ] Add governance-facing drill-ins only where the delivered workstation pattern does not yet cover the required governance workflow.
- [ ] Add delta tests for unresolved instrument identity, degraded metadata, and governance-specific classification edge cases.

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

- [x] Define governance cash-flow DTOs and scope/query model.
- [x] Reuse the existing F# cash ladder kernel for governance cash ladders.
- [x] Add Security Master-backed instrument cash rules for assigned governance instruments.
- [x] Add realized-vs-projected variance views.
- [x] Add governance cash ladder read path and HTTP endpoint.
- [x] Add tests for bank-statement, balance-snapshot, trend-fallback, and Security Master rule-driven cash-flow cases.

### Phase F4: Fund Operations Workstation

Current delivered slice: fund-level shared workspace and report-preview API projections now exist for governance/fund-ops entry points, but broader workstation UX and queue workflows still remain.

- [ ] Define governance dashboard sections and quick actions.
- [ ] Add NAV and attribution baseline service.
- [ ] Add governance queue for valuation exceptions, reconciliation breaks, and pending actions.
- [ ] Add report generation wizard entry points from Governance.
- [ ] Ensure all flows reuse shared workstation query paths.

### Phase F4.5: Report Generation Tools

Current delivered slice: report-pack preview contracts and a shared preview endpoint now exist, but governed artifact packaging/export flows remain incomplete.

- [ ] Define `ReportPackRequest`, `ReportPackDto`, and report section models.
- [ ] Add governance export profiles for board, investor, compliance, and operations packs.
- [ ] Implement report section assembly using shared read models.
- [ ] Add XLSX-first reporting support with audit metadata and source references.
- [ ] Add packaging of reconciliation, cash-flow, trial-balance, and portfolio outputs into one governed artifact set.
- [ ] Add tests for section assembly, export validation, and artifact completeness.

### Phase F5: Compliance and Policy Overlay

- [ ] Define classification-aware mandate rule inputs.
- [ ] Connect Security Master classifications to risk/compliance evaluation.
- [ ] Add pre-trade compliance hard-stops wired into the execution path: block order submission when mandate limits, concentration rules, or exposure constraints would be breached.
- [ ] Add post-trade compliance validation: flag fills that cause mandate drift, generate compliance breach events, and surface them in the governance exception queue.
- [ ] Add governance exception queue DTOs and UI for breaks, overrides, and approval workflows.
- [ ] Add promotion gates that require governance/accounting readiness.
- [ ] Add tests for mandate breaches, overrides, pre-trade blocking, post-trade flagging, and promotion blocking.

### Phase F6: Post-Trade Allocation and External Custodian Reconciliation

*FundStudio source: rules-based post-trade allocation by strategy, trader, or tax lot; automated multi-prime reconciliation to reduce T+1 breaks.*

- [ ] Define `TradeAllocationRule` domain model and F# rule engine: allocation by strategy, tax lot, account, or trader.
- [ ] Add `PostTradeAllocationService` to apply rules to fills from the execution layer and distribute quantities to fund/sleeve/account ledger lines.
- [ ] Add workstation UI for reviewing and overriding allocations before confirmation.
- [ ] Extend the existing reconciliation engine to accept external custodian position and cash statements as structured inputs (CSV, SWIFT MT940/942, or JSON adapters).
- [ ] Add break classification and severity for internal-vs-custodian mismatches (quantity, settlement date, currency, instrument).
- [ ] Add multi-custodian break queue with assignment and resolution workflow, targeting automated exception-based T+1 reconciliation.
- [ ] Add tests for allocation rule evaluation, custodian feed parsing, and break matching accuracy.

### Phase F7: Regulatory and Investor Reporting

*FundStudio source: automated multi-fund NAV calculation; shadow-NAV; drag-and-drop report builder; automated distribution via portal or email; AIFMD Annex IV; SEC reporting data exports; locked periods; version control.*

- [ ] Define regulatory reporting domain models: `TransactionReportRecord`, `CostChargeDisclosureRecord`, `RegulatoryReportBatch`.
- [ ] Implement MiFID II RTS 28 best-execution report generation (quarterly aggregation by venue and instrument class).
- [ ] Implement MiFID II Article 24 cost-and-charges disclosure records, linked to fills and portfolio positions.
- [ ] Add PRIIPs KID data assembly service (performance scenarios, cost structures, risk rating inputs); defer full PDF rendering to a later milestone but produce the underlying data model.
- [ ] Add shadow-NAV calculation path: Meridian-computed NAV alongside administrator-issued NAV for cross-check and versioning.
- [ ] Add locked reporting periods: prevent backdating of ledger entries or NAV changes once a period is closed.
- [ ] Add governed report pack profiles — `board`, `investor`, `compliance-mifid`, `regulatory-batch`, `aifmd-annex-iv` — each with automated format outputs (PDF/XLSX/CSV).
- [ ] Add automated report distribution metadata: record recipient, format, timestamp, and version for each published report pack.
- [ ] Add regulatory report history with immutable audit log and version control.
- [ ] Add tests for batch generation correctness, classification mapping, shadow-NAV delta detection, and required field completeness.

### Phase F8: Model Portfolio Management and Rebalancing

*FundStudio source: discretionary mandate management with drift monitoring and rebalancing; cross-asset model portfolios for equity long/short, global macro, and credit strategies.*

- [ ] Define `ModelPortfolio` domain model: target weights by instrument, asset class, duration bucket, or factor exposure.
- [ ] Add drift monitoring service: compare current portfolio weights against model targets; compute absolute and relative drift per position.
- [ ] Add rebalancing signal generator: produce candidate order lists to restore target weights within configurable drift tolerances.
- [ ] Add mandate-aware rebalancing constraints: respect position limits, liquidity minimums, and mandate restrictions from Security Master and the compliance layer.
- [ ] Add WPF and API surfaces for model portfolio construction, drift dashboard, and rebalancing review/approval workflow.
- [ ] Add tests for drift calculation accuracy, constraint satisfaction, and rebalancing order sizing.

### Cross-cutting

- [ ] Keep F# kernels pure and deterministic wherever possible.
- [ ] Keep WPF code-behind thin; prefer `BindableBase` view models and services.
- [ ] Reuse existing export and workstation infrastructure before adding new stacks.
- [ ] PR/review validation: no governance-local instrument definitions unless adapter-only and explicitly mapped to Security Master identity/provenance before exposure.
- [ ] Reviewer search guidance: inspect governance DTO/service changes for instrument-term fields (for example `Symbol`, `Cusip`, `Isin`, `Coupon`, `Maturity`, `Issuer`, `Venue`, `AssetClass`) introduced without Security Master references/provenance wiring.
- [ ] Update roadmap, feature inventory, and improvements docs in the same PRs as implementation changes.

## Suggested first PR slices

1. Security Master governance delta closure in portfolio, ledger, reconciliation, and report-generation paths
2. Multi-ledger grouping and consolidated trial-balance query path
3. Reconciliation DTOs plus F# rules spike and C# orchestration shell
4. Cash-flow projection kernel plus governance cash-ladder read path
5. Report-pack contracts plus export-profile integration
6. Pre/post-trade compliance hard-stops wired into `IRiskRule` and the execution path
7. Post-trade allocation rule engine (F# kernel) plus allocation DTOs and `PostTradeAllocationService`
8. External custodian statement adapter plus break classification kernel for T+1 reconciliation
9. Shadow-NAV calculation path, locked reporting periods, and governed report pack profiles
10. MiFID II RTS 28 and cost/charges data model plus `RegulatoryReportingService` shell
11. Model portfolio domain model, drift monitoring service, and rebalancing signal generator

## Open Questions

- What external statement formats should the first reconciliation engine support: CSV position files, SWIFT MT940/942, or a JSON adapter against a specific custodian API?
- Should multi-ledger grouping be explicit configuration, derived from strategy/fund metadata, or both?
- Which cash-flow categories are required for the first release: coupons, distributions, financing, fees, margin, subscriptions/redemptions?
- Does NAV/attribution need to ship before or after the first governed report-pack workflow?
- Should report generation produce only artifacts, or also versioned stored report snapshots queryable inside Meridian?
- Should post-trade allocation rules be configured per fund/strategy in `appsettings.json` or managed through a governance UI?
- Should the MiFID II reporting baseline target only best-execution (RTS 28) for the first release, or also include cost-and-charges in the same slice?
- Should model portfolio weights be manually entered, derived from a reference strategy run, or both?
- Which regulatory reporting jurisdiction should be the first target: MiFID II (EU/UK) or SEC (US)?

## Related Documents

- [Fund Management Product Vision and Capability Matrix](fund-management-product-vision-and-capability-matrix.md)
- [Fund Management Module Implementation Backlog](fund-management-module-implementation-backlog.md)
- [Fund Management PR-Sequenced Roadmap](fund-management-pr-sequenced-roadmap.md)
- [UFL Direct Lending Target-State Package V2](ufl-direct-lending-target-state-v2.md)
- [Project Roadmap](../status/ROADMAP.md)
