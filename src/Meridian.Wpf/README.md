---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-WPF
path: src/Meridian.Wpf
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-02
---

# src/Meridian.Wpf

## Purpose

WPF workstation is the active Windows desktop operator workstation sharing contracts and read models
with the browser workstation.

## Layer responsibility

This module owns the desktop shell, WPF pages, route hosting, and desktop workstation view models.
Keep shared contracts and read-model logic in shared UI services when browser and desktop both need
the behavior. The seven operator workspaces now register through feature modules under
`src/Meridian.Wpf/Features/` so new workspace-level navigation and shell ownership lands in the
matching module before it expands through the older flat page folders.

## Key folders and files

- `Features/` - workspace-owned module registration for Trading, Portfolio, Accounting, Reporting,
  Strategy, Data, and Settings.
- `ViewModels/` - desktop operator workflow view models.
- `Views/` - WPF pages and controls.
- `Shell/` and `Services/` - navigation, route, launch, and desktop service seams.

## Important workflows

The Accounting workspace includes a dedicated `FundStructureSetupPage` and `FundStructureSetupViewModel` for operator entity setup. It uses the shared `FundStructureSetupWorkflowService` so desktop setup validation, graph preview, review-and-create, and account handoff behavior match `/api/fund-structure`.


Keep desktop support aligned with shared contracts and governance posture.
Convention-based view-model wiring is handled by `Services/ViewModelViewResolver.cs`; shell pages
that follow the `*Page` to `*ViewModel` naming convention can receive a DI-constructed DataContext
without page-specific registration, while pages that set their own DataContext remain authoritative.
Runtime desktop capability toggles are declared by feature modules and surfaced in Settings through
the feature capability gate. The Security Master page projects the workstation trust
snapshot's `scheduleBook` and `openLotReadModel` payloads into operator-visible schedule, factor,
provenance, and open-lot review sections.
Run Cash Flow consumes `StrategyRunContinuityService` when the desktop shell provides it, so the
cash-flow drill-in presents the same run, portfolio, ledger, cash-flow, reconciliation, and warning
posture used by shared workstation continuity endpoints.
Fund Ledger reconciliation actions call the shared workstation reconciliation endpoints, refresh the
queue from the shared break read model after review/resolve/dismiss, and keep the selected decision
note, audit event, pending close sign-off posture, and contract-owned "Explain the Break" summary
visible in the retained detail panel. The WPF queue projection carries the same source systems,
probable cause, ledger impact, suggested next action, and evidence links as the browser Accounting
detail so desktop operators do not rebuild reconciliation narratives locally.
Shared close-workflow target tags stay explicit in desktop routing: `OperationsContinuity` and
`OperationsClose` are WPF aliases for the Fund Operations page, with navigation parameters that
land on the overview and report-pack readiness tabs while the browser resolves both tags to
`/accounting/operations-continuity`.
The browser `AccountingApprovals` approval route also resolves in WPF to the Fund Audit Trail
surface, so the design-document approval step has a route-compatible desktop target for approval
history, retained evidence, and accounting audit references.
Shared evidence workflow target routing is also explicit: `EvidenceWorkbench` resolves to the WPF
Fund Audit Trail surface while the browser resolves the same shared tag to `/reporting/evidence`.
Parameterized desktop targets such as `EvidenceWorkbench:accounting-record/{recordId}` preserve the
canonical evidence subject for row/readiness metadata while resolving to the same Fund Audit Trail
route. Direct WPF navigation and embedded page-content creation canonicalize those parameterized
targets before resolving page content and carry the subject plus source target through
`FundOperationsNavigationContext`, so view models can use the same shared target string carried by
browser routes, workflow rows, and saved presets.
The route-registry parity test covers all built-in workflow entry and action target tags so shared
workflow catalog updates cannot silently become browser-only or desktop-only.
The WPF workflow library also projects the shared v0.15 `Accounting Records Evidence Review`
workflow, preserving the source-record, normalized-activity, reconciliation-case, ledger-evidence,
approval-history, report-lineage, export-evidence, and restatement-lineage action sequence from the
shared catalog.
When shared workflow actions carry parameterized evidence targets such as
`EvidenceWorkbench:accounting-record/{recordId}`, the workflow library keeps the raw target tag for
navigation and filtering but presents the operator action target as `EvidenceWorkbench` so route
syntax does not leak into desktop workflow copy.
Shared workstation affordance primitives under `Workstation/Models` and `Workstation/Controls`
standardize action posture, readiness tone, evidence links, recovery actions, and sign-off
requirements for W4 close/report surfaces. Fund Ledger reconciliation and Report Pack handoff
surfaces consume these primitives so blocker, evidence, recovery, and sign-off signifiers stay
visible without creating desktop-only business rules.
Portfolio cockpit decision items include the shared multi-asset coverage route
`/api/workstation/portfolio/multi-asset-coverage` so desktop operators can review asset-class
readiness, provider evidence, ledger coverage, reconciliation posture, and close blockers through
the same read model used by the browser Portfolio and Accounting screens.
Fund Ledger Report Pack handoff also renders the shared Operations Continuity accounting-record
summary, including retained source records, normalized activity, reconciliation history, ledger
evidence, approvals, report-pack lineage, export evidence, restatement lineage, measured
audit-pack timing, and 60-second target status. The WPF view model maps contract-owned category
status, required evidence labels, evidence links, route hints, readiness warnings, and timing into
desktop rows and readiness state instead of deriving audit readiness in XAML or desktop-only
services. Each accounting-record
evidence row now carries both the desktop shell target and canonical `accounting-record/{recordId}`
subject target so WPF operators can reconcile the row with the same Evidence Workbench subject used
by the browser and shared evidence endpoints.
`MainPage` remains the route-compatible desktop shell entry point, but shell chrome is now composed
from reusable WPF primitives: `InstitutionalShellFrameControl`, `ShellRailControl`,
`ShellMastheadControl`, `WorkspaceEvidenceStripControl`, `WorkspaceCommandSurfaceControl`,
`InstitutionalCommandPaletteControl`, and `WorkspaceInspectorHostControl`. Workspace shell posture
is WPF-only and resolved through `ShellNavigationCatalog.GetWorkspaceLayoutDescriptor`: Trading and
Data use `Terminal`, Portfolio, Accounting, Reporting, and Settings use `Cockpit`, and Strategy uses
`Workbench`. Legacy workspace names continue to resolve as aliases to the seven canonical roots.
Desktop launch surfaces, setup completion, environment starter lanes, and operating-context defaults
emit canonical page tags (`StrategyShell`, `DataShell`, and `AccountingShell`) so new persisted state
does not reintroduce legacy `Research`, `Data Operations`, or `Governance` root names.
Catalog-backed operator pages, including Accounting entity setup, are registered exactly once
through `AddMeridianWpfShell`; the App bootstrap keeps only non-catalog supplemental pages in its
extra page-registration block.
Shell coordinator, pane-host, and direct `NavigationService` paths accept compatibility aliases as
inbound route requests but store and execute canonical page tags in active pane state, restored
content creation, navigation history, navigation events, and pane-drop results.
Workspace id compatibility normalization and the canonical legacy-alias list are centralized through
`WorkstationNavigationDefaults` instead of repeated shell-local switch expressions.
The shared context-strip service normalizes legacy workspace titles before rendering owner copy, so
old callers cannot reintroduce `Research`, `Data Operations`, or `Governance` as visible workspace
labels.
The workspace layout page also renders built-in active workspace chips from canonical root names,
not from retained legacy category enum names, so Portfolio, Reporting, and Settings do not appear as
Accounting categories.
Operator-inbox attention badges use the same canonical workspace-title normalization before
rendering owner labels, while retained API payloads may still carry legacy workspace names for
compatibility.
The two-pane workstation layout keeps the retained `ResearchData` compatibility identifier but
renders the operator-facing label as `Strategy + Data`.
WPF icon resources and SVG source filenames keep retained identifiers such as `IconResearch`,
`IconDataOps`, `IconGovernance`, `research.svg`, `data-operations.svg`, and `governance.svg`, but
the icon asset documentation maps those compatibility names to canonical Strategy, Data,
Accounting, and Settings operator labels.
`WorkspaceService` uses that same seam for persisted session and layout restore while keeping only
older template aliases such as monitoring, storage-admin, and analysis-export local to the service.
Strategy shell presentation defaults also emit the canonical `strategy` workspace id while retaining
legacy research aliases for restored workflow summaries.
Workspace shell copy and automation names use canonical Strategy/Data shell constants instead of
legacy Research/Data Operations constant names.
Accounting shell workflow summary selection prefers the canonical `accounting` workspace id while
retaining `governance` as an inbound compatibility alias.
Accounting shell visible copy, accessibility names, queue summaries, and presentation-service
handoff text use canonical `Accounting` wording. The shared presentation service now uses an
`Accounting*` name, and Accounting shell page types, state providers, view models, page bases, and
automation IDs use canonical `AccountingWorkspace*` names. Remaining `GovernanceShell` and
`GovernanceWorkspace` names are route aliases only.
The Accounting shell also projects the design-document Financial Operations workflow
(`Receive Activity`, `Match Records`, `Resolve Exceptions`, `Approve Results`, `Produce Evidence`)
from `AccountingWorkspacePresentationService`, so operator workflow state is derived from shared
fund, reconciliation, approval, and audit posture instead of XAML-local copy.
Fund Ledger and Fund Accounts drill-in surfaces use Accounting wording for route banners,
report-pack preview, account queues, and reconciliation guidance while preserving compatibility
type names where needed.
Fund Ledger report-pack handoff preview copy also names the Accounting workspace so downstream
board, investor, compliance, and fund-ops packets do not surface the legacy Governance root label.
`WorkspaceCommandSurfaceControl` and `WorkspaceEvidenceStripControl` take explicit automation ID
properties from the active workspace layout descriptor so shell chrome can be reused without
depending on ambient `MainPage` bindings.
Modal surfaces should migrate through `WorkspaceDialogChromeControl`; provider API-key setup,
watchlist saving, and scheduled-job editing now use that shared dialog chrome with stable title,
subtitle, body, input, and action automation IDs. The control projects its chrome automation ID and
title onto the reusable control itself so UI automation can address both the control and template
parts consistently.
Standalone command-palette chrome should use the same shell tokens and stable automation IDs instead
of page-local colors or shadow effects.
High-value workbench pages should migrate through the shared workstation controls before broad
page sweeps; Strategy Runs now uses `DenseDataGridControl` plus tabbed inspector panes for run,
evidence, comparison, and artifact context while preserving existing page tags and navigation
commands.
The desktop shell visual system now targets a light institutional workstation frame with a
near-black global app bar, paper page bands, compact filter bars, and dense table chrome. New
workspace overhauls should prefer `WorkstationPageBandStyle`, `WorkstationFilterBarStyle`,
`WorkstationFilterChipStyle`, `DenseDataGridControl`, and inspector host primitives before adding
page-local cards or dark terminal styling.
Settings/Admin cockpit work uses `WorkstationStatePanelControl` for schedule and cleanup readiness
state so maintenance blockers, confirmation posture, and evidence summaries reuse the same
`WorkspaceTone` semantics as other operational pages.
Data Quality terminal work uses `DenseDataGridControl` for the symbol-quality table with
view-model-owned selected-row drilldown and provider-comparison command state, keeping the Data
workspace on shared dense-table behavior without changing data-quality service contracts.
Data terminal provider, backfill, and storage decision queues now use
`WorkspaceDecisionQueueControl` while retaining existing queue-region empty/loading/error state
templates and view-model-owned action resolution.
Data shell state providers and XAML page bases use canonical `DataWorkspace*` names; retained
`DataOperationsShell` and `DataOperationsWorkspace` tags are inbound route aliases only.
`DataWorkspacePresentationBuilder` projects the design-document Data Integration workflow
(`Connect Source`, `Acquire Data`, `Validate Data`, `Normalize Data`, `Store Data`, `Publish Data`)
into WPF inspector state so desktop operators see the same provider-to-publish progression as the
browser workstation without XAML-owned workflow copy. Retained `DataOperations` route and smoke-test
names are compatibility shims only, not the canonical presentation-model taxonomy.
`MainPageViewModel` projects the design-document primary operator workflow
(`Import`, `Validate`, `Reconcile`, `Investigate`, `Approve`, `Report`) into the shared
`WorkspaceEvidenceStripControl`, keeping browser and WPF shell chrome aligned while route targets
remain WPF page tags.
Provider health affected-workflow labels use canonical workspace names such as `Strategy` and
`Data`; retained provider DTO and page-tag compatibility names must not leak `Research` or
`Data Operations` into operator-facing recovery tables.
RunMat Lab is a Strategy workspace tool; its visible page descriptions and code comments use
`Strategy` wording while retaining the existing `RunMat` page tag and automation IDs.
QuantScript run-history handoffs use `CompareInStrategyCommand` for Strategy Runs comparison
routing while preserving the existing button automation ID and run-detail navigation targets.
`StrategyWorkspaceShellPresentationService` owns Strategy shell briefing, workflow, degraded-state,
command, and promotion presentation state. Strategy shell page types, state providers, view models,
page bases, automation IDs, and hero/workflow bindings use canonical `StrategyWorkspace*` and
`Strategy*` names, while `ResearchShell` and `ResearchWorkspace` remain route aliases only.
Strategy shell fallback briefing summaries, deterministic workflow guidance, degraded-state recovery
copy, and promotion notes also use canonical `Strategy` wording while retaining legacy research
DTO/interface names for API compatibility. The desktop briefing client requests the canonical
`/api/workstation/strategy/briefing` route while the shared host still serves the retained research
briefing route as a compatibility alias.
Backfill terminal work uses `DenseDataGridControl` for gap-analysis and per-symbol-progress tables,
with table descriptors owned by `BackfillWorkbenchSectionViewModel` so long-running provider
catch-up workflows reuse the shared dense-table/empty-state surface.
Trading terminal work uses `DenseDataGridControl` for active positions and view-model-owned
selected-position inspector state so paper/live desk review keeps row selection, P&L, mode, and
next-action context in the shared dense table surface. `WorkspaceInspectorHostControl` owns
empty, selected, loading, and error inspector states with caller-supplied automation IDs so
workspace pages can migrate selected-row detail without changing route/page tags.
Trading shell fallback and context-handoff copy routes reconciliation and kill-switch handoffs
through Accounting wording while preserving retained `GovernanceShell` target tags for route
compatibility.
Accounting, Portfolio, Reporting, and Settings/Admin cockpit home decisions bind to view-model-owned `WorkspaceQueueItem`
collections through `WorkspaceDecisionQueueControl`, preserving existing page tags while reusing
`WorkspaceTone` queue-card and badge semantics for summary, approval, exception, and delivery
decisions, including primary, secondary, and blocked cockpit actions.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-WPF -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-WPF -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```powershell
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
```

## Change rules

Keep WPF views declarative and move loading, disabled, preview, empty-state, and status-copy
behavior into view models. Do not duplicate product logic that belongs in shared UI services.

## Related docs

- `docs/status/desktop-application-screens.md` - registry-backed screen inventory for the WPF desktop application, including workspace, page tag, visibility tier, implementation status, gaps, and available desktop screenshot evidence.
- `src/Meridian.Ui.Shared/README.md`
- `docs/development/wpf-implementation-notes.md`
- `docs/source/generated/source-module-index.md`
