# Idea Dimensions — Meridian Brainstorm Reference

Seeded concept bank organized by domain. Use these as inspiration prompts, not final ideas — the goal is to develop them into full idea writeups grounded in the Meridian codebase.

> **See also:** [`../../_shared/project-context.md`](../../_shared/project-context.md) for the authoritative platform framing, solution map, and review guardrails, and `docs/product/meridian-design-document.md` for the canonical product charter.
>
> **Claim discipline:** the presence of a type in this table means the code exists, not that the capability is wired into the live operator path. Several entries below are explicitly dormant (see *Activation Targets*). Never describe a dormant capability as shipped.

---

## 🗺️ Codebase Anchor Table

Use these when referencing specific abstractions in ideas. File paths are relative to the repository root and were verified 2026-07-28.

### Import, providers, and data trust

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Streaming provider contract | `IMarketDataClient` | `src/Meridian.ProviderSdk/IMarketDataClient.cs` |
| Historical provider contract | `IHistoricalDataProvider` | `src/Meridian.Infrastructure/Adapters/Core/IHistoricalDataProvider.cs` |
| Provider SDK attribute | `DataSourceAttribute` | `src/Meridian.ProviderSdk/DataSourceAttribute.cs` |
| Provider SDK discovery | `DataSourceRegistry` | `src/Meridian.ProviderSdk/DataSourceRegistry.cs` |
| Failover provider | `FailoverAwareMarketDataClient` | `src/Meridian.Infrastructure/Adapters/Failover/` |
| Backfill orchestration | `HistoricalBackfillService` | `src/Meridian.Application/Backfill/HistoricalBackfillService.cs` |
| Gap detection | `GapBackfillService` | `src/Meridian.Application/Backfill/GapBackfillService.cs` |
| F# validation pipeline | `ValidationPipeline` | `src/Meridian.FSharp/Validation/ValidationPipeline.fs` |
| Statement connector (BAI2) | `Bai2StatementConnector` | `src/Meridian.FinancialOperations/Reconciliation/Connectors/Bai2/Bai2StatementConnector.cs` |
| Statement connector (CAMT.053) | `Camt053StatementConnector` | `src/Meridian.FinancialOperations/Reconciliation/Connectors/Camt/Camt053StatementConnector.cs` |
| Statement ingestion scheduling | `DefaultReconciliationIngestionScheduler` | `src/Meridian.FinancialOperations/Reconciliation/DefaultReconciliationIngestionScheduler.cs` |
| Import-to-evidence bridge | `StatementImportEvidenceBridge` | `src/Meridian.Ui.Shared/Evidence/StatementImportEvidenceBridge.cs` |

### Reconciliation and breaks

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Sided statement matching | `StatementMatchingEngine` | `src/Meridian.FinancialOperations/Reconciliation/StatementMatchingEngine.cs` |
| Reconciliation matching engine | `ReconciliationMatchingEngine` | `src/Meridian.FinancialOperations/Reconciliation/ReconciliationMatchingEngine.cs` |
| Match kernel | `ReconciliationMatchKernel` | `src/Meridian.FinancialOperations/Reconciliation/ReconciliationMatchKernel.cs` |
| Tolerance policy | `MatchingTolerances` | `src/Meridian.FinancialOperations/Reconciliation/MatchingTolerances.cs` |
| Normalization | `ReconciliationNormalizationService` | `src/Meridian.FinancialOperations/Reconciliation/ReconciliationNormalizationService.cs` |
| Run orchestration | `ReconciliationRunService` | `src/Meridian.Strategies/Services/ReconciliationRunService.cs` |
| Break queue persistence | `FileReconciliationBreakQueueRepository` | `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs` |
| Casework workflow | `ReconciliationCaseWorkflowService` | `src/Meridian.Strategies/Services/ReconciliationCaseWorkflowService.cs` |
| SLA calculation | `ReconciliationSlaCalculator` | `src/Meridian.Strategies/Services/ReconciliationSlaCalculator.cs` |
| Reconciliation governance | `ReconciliationGovernanceService` | `src/Meridian.Strategies/Services/ReconciliationGovernanceService.cs` |
| Position reconciliation | `PositionReconciliationService` | `src/Meridian.Execution/Services/PositionReconciliationService.cs` |

### Ledger, accounting, and close

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Journal entry | `JournalEntry` | `src/Meridian.Ledger/JournalEntry.cs` |
| Journal evidence link | `JournalEvidenceReference` | `src/Meridian.Ledger/JournalEvidenceReference.cs` |
| Automated journal draft | `AutomatedJournalDraft` | `src/Meridian.Ledger/AutomatedJournalDraft.cs` |
| Journal reversal | `LedgerJournalReversal` | `src/Meridian.Ledger/LedgerJournalReversal.cs` |
| Multi-currency translation | `LedgerCurrencyTranslation` | `src/Meridian.Ledger/LedgerCurrencyTranslation.cs` |
| Tax-lot policy | `LedgerAccountTaxLotPolicy` | `src/Meridian.Ledger/LedgerAccountTaxLotPolicy.cs` |
| Period reopen evidence | `PeriodReopenEvidence` | `src/Meridian.Ledger/PeriodReopenEvidence.cs` |
| Period lock reader | `ILedgerPeriodLockReader` | `src/Meridian.Application/SecurityMaster/ILedgerPeriodLockReader.cs` |
| Close management | `AccountingCloseManagementService` | `src/Meridian.FinancialOperations/AccountingClose/AccountingCloseManagementService.cs` |
| Close posting workbench | `AccountingClosePostingWorkbench` | `src/Meridian.FinancialOperations/AccountingClose/AccountingClosePostingWorkbench.cs` |
| Accounting report package | `AccountingReportPackageService` | `src/Meridian.FinancialOperations/AccountingClose/AccountingReportPackageService.cs` |
| Approval policy matrix | `OperationsApprovalPolicyMatrixService` | `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs` |
| Asset accounting event spine | `AssetAccountingEventSpineService` | `src/Meridian.FinancialOperations/Ledger/AssetAccountingEventSpineService.cs` |
| Capital account workbench | `CapitalAccountWorkbenchService` | `src/Meridian.Ui.Shared/Services/CapitalAccountWorkbenchService.cs` |
| Capital-account subledger | `PrivateCapitalCapitalAccountSubledgerBuilder` | `src/Meridian.FinancialOperations/PrivateCapital/PrivateCapitalCapitalAccountSubledgerBuilder.cs` |

### Fund economics (dormant — see Activation Targets)

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Unitized NAV | `NavPerUnitCalculator` | `src/Meridian.Ledger/NavPerUnitCalculator.cs` |
| European waterfall | `EuropeanDistributionWaterfall` | `src/Meridian.Ledger/EuropeanDistributionWaterfall.cs` |
| Preferred return | `PreferredReturnCalculator` | `src/Meridian.Ledger/PreferredReturnCalculator.cs` |
| Carry clawback | `CarriedInterestClawbackCalculator` | `src/Meridian.Ledger/CarriedInterestClawbackCalculator.cs` |
| Equalization | `EqualizationCalculator` | `src/Meridian.Ledger/EqualizationCalculator.cs` |
| Capital call planning | `CapitalCallPlanBuilder` | `src/Meridian.Ledger/CapitalCallPlanBuilder.cs` |

### Evidence and provenance

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Evidence graph | `EvidenceGraphService` | `src/Meridian.Ui.Shared/Evidence/EvidenceGraphService.cs` |
| Proof chain builder | `EvidenceProofChainBuilder` | `src/Meridian.Ui.Shared/Evidence/EvidenceProofChainBuilder.cs` |
| Evidence packet validation | `EvidencePacketValidationService` | `src/Meridian.Ui.Shared/Evidence/EvidencePacketValidationService.cs` |
| Evidence artifact store | `FileEvidenceArtifactStore` | `src/Meridian.Ui.Shared/Evidence/FileEvidenceArtifactStore.cs` |
| Document field extraction | `EvidenceDocumentExtraction` | `src/Meridian.Ui.Shared/Evidence/EvidenceDocumentExtraction.cs` |
| Evidence templates | `EvidenceTemplateRegistry` | `src/Meridian.Ui.Shared/Evidence/EvidenceTemplateRegistry.cs` |
| Amount-level provenance | `LedgerAmountProvenanceService` | `src/Meridian.Ui.Shared/Services/LedgerAmountProvenanceService.cs` |
| Hash-chained audit | `AuditChainService` | `src/Meridian.Storage/Services/AuditChainService.cs` |

### Reporting and delivery

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Report generation | `ReportGenerationService` | `src/Meridian.Reporting/ReportGenerationService.cs` |
| Report-writer grids | `ReportWriterGridEngine` | `src/Meridian.Reporting/ReportWriterGridEngine.cs` |
| Reporting governance | `ReportingGovernanceService` | `src/Meridian.Reporting/ReportingGovernanceService.cs` |
| Snapshot diff | `ReportSnapshotDiffEngine` | `src/Meridian.Reporting/ReportSnapshotDiffEngine.cs` |
| Certified snapshot | `CertifiedReportingSnapshotBuilder` | `src/Meridian.Reporting/CertifiedReportingSnapshotBuilder.cs` |
| NAV attribution | `NavAttributionService` | `src/Meridian.Reporting/NavAttributionService.cs` |
| Partners-capital projection | `PartnersCapitalProjection` | `src/Meridian.Reporting/PartnersCapitalProjection.cs` |
| Report pack builder | `LedgerReportPackBuilder` | `src/Meridian.Ledger/LedgerReportPackBuilder.cs` |
| Report pack signature | `LedgerReportPackSignature` | `src/Meridian.Ledger/LedgerReportPackSignature.cs` |
| Report pack delivery | `ReportPackDeliveryService` | `src/Meridian.Ui.Shared/Services/ReportPackDeliveryService.cs` |
| Client-grade rendering | `ClientGradeReportRenderer` | `src/Meridian.Documents/ClientGradeReportRenderer.cs` |
| Financial document rendering | `FinancialReportDocumentRenderer` | `src/Meridian.Documents/FinancialReportDocumentRenderer.cs` |

### Execution, risk, and research

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Order gateway | `IOrderGateway` | `src/Meridian.Execution/Interfaces/IOrderGateway.cs` |
| Broker adapter SDK contract | `IExecutionGateway` | `src/Meridian.Execution.Sdk/IExecutionGateway.cs` |
| Live strategy context | `IExecutionContext` | `src/Meridian.Execution/Interfaces/IExecutionContext.cs` |
| Paper trading gateway | `PaperTradingGateway` | `src/Meridian.Execution/Adapters/PaperTradingGateway.cs` |
| Broker fill gateway | `AlpacaBrokerageGateway` | `src/Meridian.Infrastructure/Adapters/Alpaca/AlpacaBrokerageGateway.cs` |
| Order lifecycle tracking | `OrderManagementSystem` | `src/Meridian.Execution/OrderManagementSystem.cs` |
| Pre-trade risk validation | `CompositeRiskValidator` | `src/Meridian.Risk/CompositeRiskValidator.cs` |
| Risk rule contract | `IRiskRule` | `src/Meridian.Risk/IRiskRule.cs` |
| Strategy lifecycle contract | `IStrategyLifecycle` | `src/Meridian.Strategies/Interfaces/IStrategyLifecycle.cs` |
| Strategy run read model | `StrategyRunReadService` | `src/Meridian.Strategies/Services/StrategyRunReadService.cs` |
| Strategy run archive | `StrategyRunStore` | `src/Meridian.Strategies/Storage/StrategyRunStore.cs` |
| Backtest strategy contract | `IBacktestStrategy` | `src/Meridian.Backtesting.Sdk/IBacktestStrategy.cs` |
| Market-impact fill model | `MarketImpactFillModel` | `src/Meridian.Backtesting/FillModels/MarketImpactFillModel.cs` |
| Order-book fill model | `OrderBookFillModel` | `src/Meridian.Backtesting/FillModels/OrderBookFillModel.cs` |
| P&L ledger | `Ledger` | `src/Meridian.Ledger/Ledger.cs` |

### Platform, storage, and workstation seams

| Concept | Interface / Class | File Path |
|---------|-------------------|-----------|
| Event pipeline coordinator | `EventPipeline` | `src/Meridian.Application/Pipeline/EventPipeline.cs` |
| Storage sink contract | `IStorageSink` | `src/Meridian.Storage/Interfaces/IStorageSink.cs` |
| Write-ahead log | `WriteAheadLog` | `src/Meridian.Storage/Archival/WriteAheadLog.cs` |
| Crash-safe file writes | `AtomicFileWriter` | `src/Meridian.Storage/Archival/AtomicFileWriter.cs` |
| JSON source-gen context | `MarketDataJsonContext` | `src/Meridian.Core/Serialization/MarketDataJsonContext.cs` |
| Storage catalog | `StorageCatalogService` | `src/Meridian.Storage/Services/StorageCatalogService.cs` |
| Graceful shutdown | `GracefulShutdownService` | `src/Meridian.Platform/Runtime/GracefulShutdownService.cs` |
| Shared route constants | `UiApiRoutes` | `src/Meridian.Contracts/Api/UiApiRoutes.cs` |
| Shared workstation surface | `WorkstationEndpoints` | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` |
| Browser accounting workspace | `accounting-screen.tsx` | `src/Meridian.Ui/dashboard/src/screens/accounting-screen.tsx` |
| Browser trust strip | `app-shell.trust-strip.ts` | `src/Meridian.Ui/dashboard/src/app-shell.trust-strip.ts` |
| Browser evidence timeline | `app-shell.evidence-timeline.ts` | `src/Meridian.Ui/dashboard/src/app-shell.evidence-timeline.ts` |
| Browser provenance badge | `app-shell.data-provenance-badge.ts` | `src/Meridian.Ui/dashboard/src/app-shell.data-provenance-badge.ts` |
| Browser command palette | `app-shell.command-palette.ts` | `src/Meridian.Ui/dashboard/src/app-shell.command-palette.ts` |
| MVVM base class | `BindableBase` | `src/Meridian.Wpf/ViewModels/BindableBase.cs` |
| Desktop shell orchestration | `MainPageViewModel` | `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs` |

---

## ⚡ Activation Targets

The charter's headline finding (`docs/product/meridian-design-document.md` §2.1) is that the codebase is more capable than the running product. These capabilities exist in source with tests but are not the wired operator path. Activation ideas — connecting, labeling, and proving them — are the highest-return lane available, and an activated capability must *replace* its weaker predecessor rather than sit beside it.

- **Sided statement-vs-ledger matching** (`StatementMatchingEngine`) — the live path still uses a weaker per-row check
- **Institutional bank formats** (`Bai2StatementConnector`, `Camt053StatementConnector`) — in source, registry acceptance planned
- **Client-grade PDF/XLSX rendering** (`ClientGradeReportRenderer`, `FinancialReportDocumentRenderer`) — production path still emits text-grade artifacts
- **Unitized NAV and fund economics** (`NavPerUnitCalculator`, `EuropeanDistributionWaterfall`, `PreferredReturnCalculator`, `CarriedInterestClawbackCalculator`, `EqualizationCalculator`) — not yet the wired economics path
- **Broker fill streaming** (`AlpacaBrokerageGateway`) — live fill loop into order and ledger state incomplete
- **Realistic fill and cost models** (`MarketImpactFillModel`, `OrderBookFillModel`) — implemented for backtests; paper trading must adopt them
- **Kill-switch, cancel-all, notional and collar controls** — partial foundation; safety surfaces must be wired or visibly demoted
- **Hash-chained audit for the accounting ledger and route-level authorization** — `AuditChainService` covers storage; the journal chain and blanket route coverage do not exist
- **Operational Evidence Graph as a shared product surface** — explorer, proof-drawer, and manifest primitives exist; the product surface is planned

A strong activation idea names the dormant type, the live path it replaces, the operator-visible difference, and how the predecessor gets retired.

---

## 📥 Import, Providers & Data Trust

- **Mapping profile library** — reusable, versioned column/field mappings per counterparty with drift detection when a file's shape changes mid-relationship
- **Import run certification UX** — one screen where an import run is certified, rejected with repair instructions, or replayed; blocked downstream outputs named explicitly
- **Connector confidence scoring** — per-connector parse confidence and drift signals surfaced before commit, not after
- **Provider capability matrix** — what each of the 22 adapter families actually supplies (asset classes, fields, history depth, refresh cadence) as a browsable operator surface
- **Credential and connection health** — expiry warnings, scope verification, and last-successful-pull per connection in `Settings`
- **Raw payload preservation and replay** — every import replayable from retained source bytes with a diff against what was originally committed
- **Email/SFTP intake lane** — scheduled pickup with the same preview → confidence → commit rails as manual upload
- **Cross-source disagreement surfacing** — when two sources disagree on a position or price, show both with lineage rather than silently preferring one
- **Excel round-trip maintenance** — export a domain to a workbook, edit offline, re-upload, diff, amend under approval
- **Fixture and simulated-data labeling** — datum-level provenance badges so no synthetic value ever reads as operational truth

---

## 🔀 Reconciliation & Break Resolution

- **Sided matching on the live path** — pair-level matching with confidence scoring, replacing per-row checks
- **Tolerance policy workbench** — per-account, per-currency, per-transaction-type tolerances with a preview of how many breaks a change would create or clear
- **Break root-cause clustering** — group breaks by inferred cause (timing, FX, fee, missing security, duplicate) so one fix clears many
- **Bulk resolution with retained justification** — resolve a cluster in one action while retaining per-row evidence
- **Aging and SLA pressure view** — breaks by age band and owner, with escalation state visible before the SLA is missed
- **Match explanation** — for any matched pair, show which rule matched, at what tolerance, and what the alternatives were
- **Unmatch and re-match audit** — reversing a match is an auditable event with before/after state
- **Intercompany and cross-entity elimination** — reconcile between related entities, not just against external statements
- **Cash vs. position break separation** — different queues, different owners, different resolution rails
- **Recurring break suppression with expiry** — known differences suppressed under a dated, reviewable policy rather than silently ignored

---

## 📒 Ledger, Accounting & Close

- **Close readiness score** — one number per book/period, decomposed into the blocking items with links to the records causing them
- **Blocked-close report** — when a period cannot close, produce the artifact that explains why to a controller
- **Journal template library** — parameterized templates for recurring entries with approval requirements attached
- **Automated journal draft review** — drafts proposed by the system, approved by a human, with the evidence that produced them visible in the same view
- **Period lock enforcement surfacing** — show what a lock prevents and who can grant a governed reopen, before the operator tries
- **Multi-currency exposure view** — translation impact by book and period with the FX rates and their source retained
- **Tax-lot method comparison** — show disposal outcomes under alternative lot policies before committing a method
- **Capital account statement generation** — per-investor activity, contributions, distributions, and closing balances from the subledger
- **Trial balance drill-through** — from any trial balance line to the journals, and from a journal to its evidence
- **Late-activity handling** — post-close adjustments routed through explicit reopen or next-period accrual with retained rationale

---

## 🧾 Evidence, Provenance & Governance

- **Number Passport surface** — for any amount, expose basis, dates, positions, journals, source records, extracted fields, transformations, valuation method, reconciliation state, approval history, and freshness (charter §22)
- **Proof drawer everywhere** — the same drill-down component reachable from report lines, ledger rows, portfolio marks, and dashboard metrics
- **Evidence completeness meter** — per packet, what is present, stale, or missing, and what output it blocks
- **Immutable manifest freeze** — freeze the evidence set attached to a delivered package so later changes cannot rewrite history
- **Document extraction review lane** — extracted fields shown next to the source document region with accept/correct actions
- **Scoped access review packet** — periodic certification of who holds what scope, with revocation flowing to an audit event
- **Journal-level hash chaining** — extend the storage audit chain into the accounting ledger
- **Segregation-of-duties enforcement** — preparer ≠ approver enforced structurally, with attempted-violation events retained
- **Auditor read-only workspace** — entitlement-scoped access that lets an external reviewer drill from a package line to support without operator mediation
- **Legal hold and retention policy** — hold state that blocks purge and is visible wherever affected records appear

---

## 📤 Reporting & Delivery

- **Client-grade rendering activation** — replace text-grade production artifacts with the existing PDF/XLSX renderers
- **Report-line lineage** — every line traceable to its inputs, with the trace surviving into the delivered artifact
- **Package diff and restatement** — show what changed between two versions of a package and why an amendment was issued
- **Delivery evidence** — who received what, when, under which entitlement, with acknowledgement state
- **Report-writer authoring UX** — durable drafts, live preview, multi-filter support, formula workbench, keyboard-first token composition
- **Saved views and template catalog** — governed, shareable report definitions with versioning
- **Scheduled package runs** — recurring generation with pre-release consistency gates and hold-on-failure
- **Board and IC pack composition** — assemble multiple certified outputs into one governed binder
- **Stakeholder verification view** — read-only drill-through attached to a delivered package (not a broad portal — see deferred boundaries)
- **Export evidence retention** — every export recorded with parameters, snapshot identity, and requester

---

## 📈 Portfolio, Valuation & Reference Data

- **Security master passport** — one identity view per instrument with provider mappings, conflicts, and trust summary
- **Stale mark detection** — flag positions whose valuation inputs have aged past policy, with the age and source visible
- **Corporate action evidence** — factor and action records joined to the positions and journals they affected
- **Multi-asset coverage gaps** — which asset classes lack required terms, evidence, or accounting classification
- **Cash ladder and liquidity view** — scenario-aware, per-currency projections aggregated from per-security runs
- **Valuation methodology disclosure** — show which method produced a mark and what its inputs were
- **Position-to-custodian tie-out** — daily agreement state per account with break linkage
- **Household and entity rollup** — consolidated views across entities with elimination visibility
- **Private and structured asset terms capture** — declared required terms with evidence and close/reporting handoff
- **Commitment and unfunded tracking** — commitments, drawdowns, and remaining unfunded with capital-call linkage

---

## 🎯 Execution, Risk & Research

- **Paper realism** — limit/stop semantics and trading costs in paper sessions before their statistics feed promotion decisions
- **Broker fill loop** — streamed fills flowing into order state and ledger postings with reconciliation against the broker's own record
- **Kill-switch and cancel-all** — a wired safety control with confirmation, scope, and retained event
- **Pre-trade guardrails** — fat-finger, notional, and price-collar rules with clear operator-facing rejection reasons
- **Promotion evidence gate** — paper-to-live promotion showing the full required evidence set and any manual override
- **Run comparison** — diff two strategy runs across parameters, fills, costs, and outcomes
- **Backtesting Studio evidence loop** — link a backtest run to the data snapshot, code version, and resulting decisions
- **QuantScript ergonomics** — authoring, validation, and charting improvements for research users
- **Execution-to-ledger continuity** — trades landing as postings with lot creation and disposal in one governed transaction
- **Risk breach acknowledgement** — breaches with owner, acknowledgement, and acceptance records rather than transient alerts

---

## 🖥️ Workstation UX & Information Design

- **Screen consolidation behind the seven roots** — fewer, deeper screens; retired routes remain redirects
- **Operator focus and task modes** — surface what is waiting on this operator, in this scope, right now
- **Trust strip and provenance badges** — persistent, honest state about data freshness and simulation at the top of every workspace
- **Command palette coverage** — every meaningful navigation and action reachable by keyboard
- **Queue-first workspace design** — start from what needs resolving, not from a static dashboard
- **Empty states with a next action** — a fresh install shows a concrete path, never a wall of zeros
- **Linked context across workspaces** — carrying entity, period, and account scope as the operator moves between roots
- **Blocked-state explanations** — reason, owner, and next action attached to every blocked indicator
- **Density and progressive disclosure** — summary rows expand to detail without a page change
- **WPF web-parity closure** — bring desktop screens that shipped browser-first onto the shared contracts (`W8-WPF-PARITY-001`)
- **Shared design-system compliance** — new surfaces compose existing components rather than forking per-screen variants

---

## 🚀 Adoption, Onboarding & Time to First Proof

- **One-command seeded demo** — a truthful, populated, durable demonstration workspace from a fresh install
- **Shadow-mode onboarding** — read-only parallel views, opening-balance reconciliations, and close-readiness scores before a customer migrates official books
- **Statement reconciliation wedge** — browser-first import → commit → retained proof as the first slice a new customer runs
- **Excel onboarding workbook** — download → fill → upload → review → governed commit for security master, entities, chart of accounts, opening balances
- **Get-connected hub** — provider setup tied to the imported instrument universe with a coverage handshake
- **Guided first close** — a checklist that walks a new operator through the canonical spine once, end to end
- **Time to First Proof instrumentation** — measure and expose how long a fresh install takes to produce its first verified number
- **Activation coverage surface** — show which shipped capability is reachable in the running product, keeping unwired capability visible as debt

---

## 🏗️ Architecture & Refactoring

- **Shared-seam compliance audit** — find product state that a workstation lane computes locally instead of consuming from `Meridian.Ui.Services` / `Meridian.Ui.Shared` / `Meridian.Contracts`
- **Endpoint surface consolidation** — 845 routes across 137 endpoint files; identify overlapping read models and duplicate contracts
- **Partial-class sprawl review** — services split across many partials (close management, evidence stores, break repositories) and whether the split still reflects real seams
- **Read-model boundary clarity** — which projections are books of record vs. derived views, and enforcing that distinction in code
- **Durable-store abstraction** — consistent file/Postgres store selection with fail-closed behavior when the durable option is unavailable
- **Contract-pack extensibility** — schema, lifecycle, valuation, accounting, validation, and reporting hooks for new asset types without core-ledger surgery
- **Domain module registration** — consistent `DesignModule` and DI registration patterns across the newer projects
- **F# kernel boundaries** — which deterministic calculations belong in the F# projects vs. C# services
- **Options pattern and hot reload** — `IOptionsMonitor<T>` coverage for runtime-mutable configuration
- **Hot/cold path separation** — keep the market-data hot path isolated from the evidence and accounting workloads

---

## 🧹 Technical Debt, Quality & Operations

- **Mutation testing baseline** — measure real test effectiveness beyond the ~12,900 fact/theory count
- **Static analysis gates** — Roslyn analyzers and `.editorconfig` enforcement wired into the quality gate
- **Dead and duplicated seam elimination** — overlapping services that both own a slice of close, evidence, or reconciliation state
- **Test isolation audit** — tests depending on wall-clock time, the file system, or network state
- **Fixture-vs-production separation** — development seeding surfaces unreachable in production profiles by construction, not by comment
- **Durability call-site review** — every persistence path routed through WAL or `AtomicFileWriter`
- **Structured logging consistency** — no interpolation inside log calls; correlation IDs across the operator spine
- **Observability for the operator spine** — traces and metrics that follow import → reconcile → post → approve → report
- **Documentation-to-code alignment** — generated indexes, module READMEs, and skill packages kept current with the source registry
- **CI feedback speed** — targeted test selection for touched subsystems instead of broad suite runs
