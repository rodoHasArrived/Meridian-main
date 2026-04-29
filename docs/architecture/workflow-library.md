# Workflow Library Architecture

**Owner:** Workstation Platform
**Scope:** Desktop workflow extensibility
**Status:** Phase 1 implementation

## Purpose

The workflow library turns repeated workstation handoffs into registered workflow definitions and action targets. It gives operators one catalog of reusable workflows while giving developers one place to add or reroute workflow actions used by the shell, workstation endpoints, workflow summaries, and operator inbox.

## Current Shape

- `WorkflowDefinitionDto` describes a user-facing workflow, its workspace, entry page, actions, evidence tags, and market-pattern tags.
- `WorkflowActionDto` describes a reusable action target with a stable action id, target page tag, optional operator work-item kind, and optional route matching.
- `IWorkflowDefinitionProvider` supplies workflow definitions.
- `WorkflowRegistry` indexes definitions and resolves action ids, operator inbox kinds, and API routes into page targets.
- `WorkflowLibraryService` exposes the shared catalog to desktop UI and `/api/workstation/workflows`.

The first provider is `BuiltInWorkflowDefinitionProvider`. It intentionally registers only workflows backed by current Meridian surfaces: Strategy, Trading, Portfolio, Accounting, Reporting, Data, and Settings.

## Runtime Use

`WorkstationWorkflowSummaryService` now resolves next-action page targets through `IWorkflowActionCatalog` while preserving the existing fallback page tags. `MainPageViewModel` resolves operator-inbox targets through the same catalog before falling back to legacy route and kind mappings. The WPF `WorkflowLibraryPage` reads `WorkflowLibraryService` and launches the registered action target through `NavigationService`.

## Saved Presets

Workflow presets are the first durable user-customization layer on top of the workflow catalog. A preset binds an operator-owned name, description, tags, pinned state, and recent-use timestamp to an existing registered workflow and optional workflow action. Presets cannot target arbitrary pages; `WorkflowPresetService` validates every save request against the current `WorkflowRegistry` so saved entries continue to launch real Meridian surfaces.

`FileWorkflowPresetStore` writes the preset snapshot under the resolved Meridian data root at `workstation/workflows/workflow-presets.json`. The store uses the shared `AtomicFileWriter` write-to-temp-then-rename pattern so browser or retained desktop hosts do not leave partial JSON snapshots after interruption. The shared workstation API exposes:

- `GET /api/workstation/workflows/presets`
- `POST /api/workstation/workflows/presets`
- `PUT /api/workstation/workflows/presets/{presetId}`
- `POST /api/workstation/workflows/presets/{presetId}/pin`
- `POST /api/workstation/workflows/presets/{presetId}/used`
- `DELETE /api/workstation/workflows/presets/{presetId}`

The browser dashboard should use these endpoints for the Workflow Studio and command-palette preset integration instead of storing presets in local component state.

## Extension Rules

- Add a workflow only when it launches real existing Meridian surfaces.
- Add route or work-item-kind mappings only when they are used by endpoint payloads or operator-inbox items.
- Keep views presentation-only; workflow resolution belongs in `Meridian.Ui.Shared.Workflows`.
- Keep action ids stable. If a target page is renamed, update the catalog and leave a compatibility alias where useful.
- Do not add placeholder plugins, fake providers, paid APIs, credentials, or proprietary workflow assets.

## Market-Informed Patterns

The model borrows broad, common patterns from trading and quant tools: reusable workflow catalogs, strategy-to-backtest-to-live handoffs, provider health dashboards, approval queues, export/report workflows, and extension registries. The implementation uses Meridian-owned copy, DTOs, services, and WPF views; it does not copy proprietary designs or code.

## Next Phases

1. Add a browser Workflow Studio surface that loads `/api/workstation/workflows` plus `/api/workstation/workflows/presets`.
2. Add import/export for saved workflow presets once the schema stabilizes.
3. Let workflow definitions and saved presets contribute command-palette entries without hard-coded shell edits.
4. Add workflow-specific validation summaries and audit history.
