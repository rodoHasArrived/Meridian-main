# Meridian Analytics Productization Blueprint

**Owner:** Core Team
**Audience:** Research, Trading, Governance, Desktop, API, and Architecture contributors
**Last Updated:** 2026-04-21
**Status:** Proposed blueprint for productizing the `Research -> Trading -> Governance` operator journey

> Companion to:
>
> - [trading-workstation-migration-blueprint.md](trading-workstation-migration-blueprint.md)
> - [research-backtest-trust-and-velocity-blueprint.md](research-backtest-trust-and-velocity-blueprint.md)
> - [governance-fund-ops-blueprint.md](governance-fund-ops-blueprint.md)

---

## Summary

This blueprint productizes Meridian's existing analytics, notebook, export, comparison, promotion, and governance seams into one coherent operator workflow:

`Research -> Trading -> Governance`

The intent is packaging and workflow continuity, not a new model-library program and not a separate governance stack. Meridian already has the right foundations:

- QuantScript notebooks and document storage in `src/Meridian.Wpf/ViewModels/QuantScriptViewModel.cs` and `src/Meridian.QuantScript/Documents/IQuantScriptNotebookStore.cs`
- analysis export profiles and loader generation in `src/Meridian.Storage/Export/AnalysisExportService.cs`
- local-first export preset persistence in `src/Meridian.Ui.Services/Services/ExportPresetServiceBase.cs`
- shared run read models in `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- WPF run/workspace orchestration in `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs`
- research briefing and saved-comparison projection logic already living inside `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`
- governance workspace projection and report preview in `src/Meridian.Ui.Shared/Services/FundOperationsWorkspaceReadService.cs`, `src/Meridian.Application/Services/ReportGenerationService.cs`, and `src/Meridian.Ui.Shared/Endpoints/FundStructureEndpoints.cs`

The design keeps Meridian's top-level workstation information architecture unchanged:

- `Research`
- `Trading`
- `Data Operations`
- `Governance`

WPF remains the primary operator shell. Shared services and workstation/fund-structure endpoints remain the integration boundary for desktop, web, and API consumers.

The milestone order should stay intentionally balanced:

1. productize research packaging and richer run comparison first
2. harden trading handoff and promotion review second
3. finish governance-grade artifact generation third

---

## Scope

### In scope

- Starter research templates for notebooks, exports, and comparisons.
- Notebook creation from a selected run using the existing QuantScript notebook store.
- Richer run comparison that goes beyond a flat metric table and becomes a real research review surface.
- Export recipes and evidence packages built on the current analysis export stack.
- Research, Trading, and Governance workspace presets that reuse the existing shell layout system instead of adding navigation roots.
- Trading promotion review that consumes research comparison evidence before paper or live handoff.
- Governed report-pack generation built on the existing fund-operations projection and preview seam.
- Local-first persistence for templates, saved comparisons, and recipes using Meridian's current JSON/workspace-state patterns.

### Out of scope

- Replacing the WPF shell.
- Cloud-only workflow design.
- A separate governance application or reporting pipeline.
- A new calculation engine for promotion review.
- A bank-style generic model-governance framework.
- Broad analytics expansion that is not run-centered research, promotion review, or governance evidence.

### Assumptions

- WPF remains the primary operator experience.
- Shared service and endpoint layers remain the cross-surface boundary.
- Additive contracts are preferred over breaking changes.
- Local-first ownership of notebooks, presets, exports, and governed artifacts remains a hard product constraint.
- Where the current repo has page-local or endpoint-local workflow shaping, this blueprint may extract those seams into shared services rather than duplicating logic.

---

## Architecture

### 1. Keep research notebooking QuantScript-first

Do not build a second notebook product. Reuse the existing QuantScript stack:

- `src/Meridian.Wpf/ViewModels/QuantScriptViewModel.cs`
- `src/Meridian.QuantScript/Documents/IQuantScriptNotebookStore.cs`
- `src/Meridian.QuantScript/Documents/QuantScriptNotebookStore.cs`

The missing productization layer is not notebook execution itself. The missing layer is reusable template and import orchestration.

Add a shared template catalog that can surface:

- starter notebooks
- run-review notebook starters
- export recipes
- comparison templates

The catalog should be consumed by WPF and, when needed, workstation endpoints. View models should stop hard-coding starter content or treating templates as page-local decisions.

Notebook import from a selected run should reuse:

- `StrategyRunReadService.GetRunDetailAsync(...)`
- existing QuantScript document save/load behavior

The import path should inject:

- run ID
- strategy name
- mode
- fund scope
- parameter values
- dataset/feed references

into notebook metadata and starter cells, rather than creating a separate research document type.

### 2. Turn run comparison into a bundle-based review seam

`StrategyRunReadService` already exposes:

- `CompareRunsAsync(...)`
- `GetRunComparisonDtosAsync(...)`
- fills, attribution, ledger, and equity-curve drill-ins through the workstation endpoint surface

That is the correct base seam, but the current comparison is still too flat for promotion and governance review.

Add a richer comparison-bundle builder on top of:

- `src/Meridian.Strategies/Services/StrategyRunReadService.cs`
- `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs`
- the existing workstation endpoints in `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`

The first supported bundle sections should be:

- summary metrics
- portfolio exposure deltas
- fills and execution-cost deltas
- ledger and trial-balance deltas
- promotion-readiness and acceptance differences

`RunComparisonDto` remains for backward compatibility, but it becomes a derived or flattened projection from the richer bundle until all major consumers migrate.

### 3. Productize exports through the existing export stack

Do not create a second research export path. Reuse:

- `src/Meridian.Storage/Export/AnalysisExportService.cs`
- `src/Meridian.Ui.Services/Services/ExportPresetServiceBase.cs`
- `src/Meridian.Contracts/Export/ExportPreset.cs`
- `src/Meridian.Contracts/Export/StandardPresets.cs`

Milestone 1 should add first-class research/governance recipe IDs such as:

- `python-research`
- `run-review-evidence`
- `governance-supporting-data`

Each recipe should continue to map into the current `ExportRequest` and current loader/data-dictionary generation flow. The design goal is recipe reuse, not export-path duplication.

Because `ExportPresetServiceBase` already provides file-backed local persistence, the new recipes should extend that service rather than introducing a competing preset store.

### 4. Introduce a shared workflow projection service for research/trading/governance cards

The brief references `WorkstationWorkflowReadService`, but the current repository does not yet contain that service. Today the Research briefing, saved-comparison summaries, and several workflow cards are built directly inside `WorkstationEndpoints.cs`.

The implementation should therefore introduce a shared workflow projection seam, for example:

- `src/Meridian.Ui.Shared/Services/WorkstationWorkflowReadService.cs`

by extracting and formalizing logic currently embedded in:

- `BuildResearchBriefingAsync(...)`
- `BuildResearchBriefingFromRuns(...)`
- `BuildSavedComparisons(...)`
- related quick-action and workspace-summary helpers in `WorkstationEndpoints.cs`

This new service should surface workflow cards and quick actions for:

- starter notebooks
- latest comparison bundle
- export recipes
- promotion handoff tasks
- governance report readiness and artifact actions

The endpoint layer should become a thin transport wrapper over this service rather than the place where workflow intelligence lives.

### 5. Reuse workspace shell presets instead of adding navigation roots

The repo already has named layout presets in:

- `src/Meridian.Wpf/Models/ShellNavigationCatalog.Workspaces.cs`
- `src/Meridian.Wpf/Services/WorkspaceShellStateProviders.cs`

This blueprint should extend that mechanism with additive presets, not new top-level workspaces.

Research presets:

- `research-toolkit`
- `research-compare`
- `research-export-desk`

Trading preset:

- `trading-promotion-review`

Governance preset:

- `governance-report-review`

The existing `research-compare` preset should evolve rather than be replaced. Trading and Governance presets should open the existing run, ledger, reconciliation, and fund-ops panes in a review-friendly layout.

### 6. Make trading handoff depend on comparison evidence

Milestone 2 should remain read-model and orchestration focused. Reuse:

- `src/Meridian.Wpf/Models/WorkspaceShellModels.cs` for `ActiveRunContext`
- `src/Meridian.Wpf/Services/StrategyRunWorkspaceService.cs`
- the current promotion summaries already projected through `StrategyRunReadService`

Trading review should not duplicate promotion logic page-by-page. Instead, it should consume the comparison bundle for a selected lineage pair so operators can review what changed between backtest and paper before promotion.

If the existing trading surfaces already have a scenario-review seam, reuse it. If that seam is still page-local, normalize it during Milestone 2 as part of the trading-promotion preset instead of inventing a separate trading-only model.

### 7. Extend governance from preview-only to governed artifact generation

Governance already has:

- fund-operations workspace projection in `FundOperationsWorkspaceReadService`
- preview endpoint in `/api/fund-structure/report-pack-preview`
- persisted governed artifact endpoint in `/api/fund-structure/report-packs`
- report-pack preview, generation, artifact, provenance, history, and schema-version DTOs in `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`

Milestone 3 should extend, not fork, that path.

`FundOperationsWorkspaceReadService` and the report-generation path have moved beyond preview-only into a local-first governed artifact baseline. The next step is broadening fixed report-pack kinds:

- `board`
- `investor`
- `operations`
- `compliance`

Every generated pack must reuse the same shared ledger, reconciliation, NAV, and Security Master readiness information already present in the workspace projection. No report-only projection layer should be introduced.

Artifact generation should be gated by readiness checks such as:

- blocking reconciliation posture
- missing Security Master coverage
- insufficient ledger/trial-balance evidence
- missing lineage to a selected run or fund profile

---

## Interfaces and Models

### Research template catalog

Add a shared template service interface plus reusable descriptors.

Recommended paths:

- `src/Meridian.Application/Research/IResearchTemplateCatalog.cs`
- `src/Meridian.Contracts/Workstation/ResearchToolkitDtos.cs`

Recommended public surface:

```csharp
namespace Meridian.Contracts.Workstation;

public enum ResearchTemplateKind : byte
{
    Notebook,
    ExportRecipe,
    ComparisonTemplate
}

public sealed record ResearchTemplateDescriptor(
    string Id,
    string Name,
    ResearchTemplateKind Kind,
    string Summary,
    string WorkspacePresetId,
    string? SourceRunId = null,
    IReadOnlyDictionary<string, string>? DefaultParameters = null,
    IReadOnlyList<string>? Tags = null);
```

```csharp
namespace Meridian.Application.Research;

public interface IResearchTemplateCatalog
{
    Task<IReadOnlyList<ResearchTemplateDescriptor>> GetTemplatesAsync(CancellationToken ct = default);
    Task<ResearchTemplateDescriptor?> GetTemplateAsync(string templateId, CancellationToken ct = default);
}
```

### Comparison bundle contracts

Keep the current `RunComparisonDto` stable, but add a richer contract beside it.

Recommended path:

- `src/Meridian.Contracts/Workstation/StrategyRunReadModels.cs`

Recommended additions:

```csharp
public sealed record RunComparisonBundleDto(
    string BaseRunId,
    string OtherRunId,
    RunComparisonSummarySectionDto Summary,
    RunComparisonExposureSectionDto Exposure,
    RunComparisonExecutionSectionDto Execution,
    RunComparisonLedgerSectionDto Ledger,
    RunComparisonPromotionSectionDto Promotion,
    DateTimeOffset GeneratedAt);
```

The section DTOs should capture, at minimum:

- core metric deltas
- exposure deltas by symbol or grouping
- fill-count, commission, slippage, and execution-cost deltas
- trial-balance and journal-summary deltas
- acceptance, audit, and promotion-readiness differences

Service additions:

- `StrategyRunReadService.GetRunComparisonBundleAsync(string runId, string otherRunId, CancellationToken ct = default)`
- `StrategyRunWorkspaceService.GetRunComparisonBundleAsync(...)`

### Governed report-pack generation contracts

Keep the current preview contracts stable and add additive generation contracts.

Recommended path:

- `src/Meridian.Contracts/Workstation/FundOperationsWorkspaceDtos.cs`

Recommended additions:

```csharp
public enum FundReportPackKindDto : byte
{
    Board,
    Investor,
    Operations,
    Compliance
}

public sealed record FundReportPackGenerateRequestDto(
    string FundProfileId,
    FundReportPackKindDto PackKind,
    DateTimeOffset? AsOf = null,
    string? Currency = null,
    string? AnchorRunId = null,
    bool RequireReconciliationReady = true,
    bool RequireSecurityMasterCoverage = true);

public sealed record FundReportPackArtifactDto(
    Guid ArtifactId,
    string FundProfileId,
    FundReportPackKindDto PackKind,
    DateTimeOffset AsOf,
    DateTimeOffset GeneratedAt,
    string OutputDirectory,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Warnings,
    bool ReadinessAccepted);
```

`FundReportPackPreviewRequestDto` and `FundReportPackPreviewDto` stay contract-stable. Preview remains the compatibility lane; governed artifact generation through `/api/fund-structure/report-packs` is additive.

### Endpoint additions

Additive endpoint surface:

- `GET /api/workstation/research/templates`
- `GET /api/workstation/runs/{runId}/comparison-bundle?otherRunId=...`
- `POST /api/fund-structure/report-packs`

Compatibility requirements:

- keep `POST /api/fund-structure/report-pack-preview` backward compatible
- keep `/api/workstation/runs/compare` and `/api/strategies/runs/compare` working while bundle consumers migrate
- keep `RunComparisonDto` derivable from `RunComparisonBundleDto` until existing consumers are cut over

---

## Data Flow

### 1. Research template -> notebook import -> local notebook persistence

1. Research workspace requests starter templates from `IResearchTemplateCatalog`.
2. Operator selects a starter notebook or a run-based notebook template.
3. Shared workflow service resolves the current run through `StrategyRunReadService.GetRunDetailAsync(...)`.
4. Template builder creates a `QuantScriptNotebookDocument` with run-context parameter injection.
5. `IQuantScriptNotebookStore.SaveNotebookAsync(...)` persists the notebook under the existing local QuantScript scripts directory.
6. WPF opens the notebook through the `research-toolkit` preset.

### 2. Comparison bundle -> research review -> legacy comparison projection

1. Operator selects two runs from the Research workspace or saved-comparison lane.
2. `StrategyRunReadService` loads run detail, portfolio, ledger, fills, attribution, and promotion summaries for the selected pair.
3. Bundle builders assemble summary, exposure, execution, ledger, and promotion sections.
4. The bundle is returned directly to WPF or through `GET /api/workstation/runs/{runId}/comparison-bundle`.
5. Where older surfaces still require `RunComparisonDto`, derive the legacy projection from the bundle rather than rebuilding logic separately.

### 3. Research export recipe -> evidence package

1. Operator selects a built-in or saved export recipe.
2. The recipe resolves through `ExportPresetServiceBase` plus analysis-export profile mapping.
3. `AnalysisExportService` executes the export using the existing format and loader-generation path.
4. Evidence outputs are materialized as research-ready or governance-supporting data without introducing a second export stack.
5. Workflow cards update to surface the latest package and quick-open actions.

### 4. Research handoff -> trading promotion review

1. Operator selects a run lineage pair for promotion review.
2. `StrategyRunWorkspaceService` refreshes `ActiveRunContext` for the selected run.
3. Trading preset `trading-promotion-review` opens portfolio, risk, ledger, notes, and scenario-review panes in one layout.
4. The comparison bundle is shown beside promotion posture so the operator can review what changed between the source and target modes.
5. Promotion logic remains centralized in existing shared run/promotion summaries rather than being reimplemented in the page.

### 5. Governance readiness -> preview -> governed artifact generation

1. Governance workspace loads `FundOperationsWorkspaceReadService.GetWorkspaceAsync(...)`.
2. Preview continues to run through `PreviewReportPackAsync(...)` and `/api/fund-structure/report-pack-preview`.
3. Once readiness thresholds pass, the operator issues `FundReportPackGenerateRequestDto`.
4. `ReportGenerationService` reuses the same shared ledger, reconciliation, NAV, and Security Master evidence already present in the projection.
5. Artifacts are written locally and returned as `FundReportPackArtifactDto`.
6. Governance preset `governance-report-review` anchors review on preview/readiness first and generation second.

---

## Edge Cases and Risks

### Missing workflow service today

Risk:

- workflow projection logic stays trapped in `WorkstationEndpoints.cs`, causing WPF, web, and future API surfaces to drift

Mitigation:

- introduce `WorkstationWorkflowReadService` by extraction, not by duplicating the endpoint logic into new helpers

### Comparison bundle fan-out and payload growth

Risk:

- comparison requests may become allocation-heavy and high-latency if every request eagerly loads full portfolio, ledger, fills, and curve payloads for both runs

Mitigation:

- keep cancellation flow intact
- load only the two selected runs
- use bounded `Task.WhenAll(...)` fan-out
- keep large artifacts sectioned so WPF or HTTP consumers can avoid redundant payload duplication

Performance-sensitive note:

- the comparison-bundle builder will sit on a read-heavy path; avoid recomputing the same equity-curve or ledger sections twice when both the bundle and compatibility DTO are requested

### Local-first persistence drift

Risk:

- templates, saved comparisons, or export recipes fall back to page-local state and stop surviving restarts

Mitigation:

- reuse `IQuantScriptNotebookStore`, `ExportPresetServiceBase`, workspace shell state providers, and durable file-write utilities rather than storing mutable state only in view models

### Governed generation duplicating report logic

Risk:

- report generation forks into a report-only pipeline that re-derives ledger or NAV differently from the workspace projection

Mitigation:

- require `FundOperationsWorkspaceReadService` and `ReportGenerationService` to consume shared fund-ops inputs
- keep report formatting separate from financial projection logic

### Readiness false positives

Risk:

- governance generation succeeds when reconciliation, Security Master coverage, or ledger completeness is not actually acceptable

Mitigation:

- keep governed generation gated
- return warnings and rejection reasons explicitly
- add failure-path endpoint tests for rejected generation requests

### Trading review without a valid lineage pair

Risk:

- operators try to promote from a run that has no valid baseline or no meaningful comparison target

Mitigation:

- make comparison bundle availability part of the promotion-review posture
- degrade gracefully to a single-run review state without allowing the comparison-dependent promotion action to claim readiness

---

## Test Plan

### Unit tests

- template catalog resolution
- notebook import and run-context injection
- export recipe/preset mapping
- comparison-bundle section builders
- report-pack profile selection and readiness gating

Suggested projects:

- `tests/Meridian.Tests/Application/`
- `tests/Meridian.Tests/Strategies/`
- `tests/Meridian.Wpf.Tests/`

### Shared service and endpoint tests

- backtest-vs-paper comparison bundles
- paper-vs-live comparison bundles
- missing acceptance checks
- missing Security Master coverage
- empty-ledger and zero-fill edge cases
- successful and rejected `/api/fund-structure/report-packs` requests
- backward-compatible `report-pack-preview` requests

### WPF and workspace-shell tests

- QuantScript template flows
- Strategy Run comparison rendering
- workspace preset hydration for:
  - `research-toolkit`
  - `research-compare`
  - `research-export-desk`
  - `trading-promotion-review`
  - `governance-report-review`

### Acceptance-style scenario

Add one end-to-end acceptance scenario that:

1. starts from a recorded run
2. opens a notebook template
3. runs analysis
4. compares two runs
5. generates a Python loader/export recipe
6. hands off into Trading review
7. previews and generates a governance report pack for the same lineage

### Validation commands

```bash
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
```

---

## Implementation Phasing

### Milestone 1: Research toolkit productization

- Add `IResearchTemplateCatalog` and `ResearchTemplateDescriptor`.
- Add notebook starters and import-from-run flows on top of `IQuantScriptNotebookStore`.
- Add `RunComparisonBundleDto` plus bundle builders on top of `StrategyRunReadService`.
- Extend `AnalysisExportService` and `ExportPresetServiceBase` with first-class research and evidence recipes.
- Introduce `WorkstationWorkflowReadService` so research cards and quick actions stop living only in `WorkstationEndpoints`.
- Add `research-toolkit`, `research-compare`, and `research-export-desk` presets.

### Milestone 2: Trading handoff hardening

- Extend `ActiveRunContext` and trading workflow projections so promotion review consumes research comparison evidence.
- Add `trading-promotion-review` preset.
- Keep trading work read-model and orchestration focused.
- Avoid new calculation engines or trading-only duplicate data models.

### Milestone 3: Governance artifact generation

- Extend the delivered `FundReportPackGenerateRequestDto`, artifact/provenance DTOs, and `POST /api/fund-structure/report-packs` path into broader governed report-pack kinds.
- Extend `FundOperationsWorkspaceReadService` and report-generation orchestration beyond the current local-first governed artifact baseline.
- Add `governance-report-review` preset.
- Persist generated artifacts through the existing local-first storage posture.

---

## Open Questions

- Should `RunComparisonBundleDto` be returned eagerly as one payload for WPF only, or should the endpoint support section flags once web/API consumers start using it heavily?
- Should governed generation introduce a new `FundReportPackKindDto` alongside the existing `GovernanceReportKindDto`, or is there a safe additive migration path that preserves preview compatibility without overloading the old enum?
- Which local storage root should own generated governance artifacts by default: a fund-profile-scoped workspace directory, or a dedicated governance-artifacts directory under the resolved Meridian data root?
