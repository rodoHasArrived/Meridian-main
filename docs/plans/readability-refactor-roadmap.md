# Readability refactor roadmap

_Date: 2026-03-20_

## Scope

This phase establishes the refactor scaffolding needed to improve readability without changing runtime behavior. The immediate goal is to make later extraction work safer across startup, desktop UI, provider adapters, and configuration flows.

### In scope
- Startup and host composition seams (`src/Meridian/Program.cs`, `src/Meridian.Application/Composition/HostStartup.cs`, `src/Meridian.Application/Commands/*`).
- Configuration loading and validation seams (`src/Meridian.Application/Config/*`, `src/Meridian.Application/Services/*`).
- WPF data-quality and connection management seams (`src/Meridian.Wpf/ViewModels/DataQualityViewModel.cs`, `src/Meridian.Wpf/Views/DataQualityPage.xaml.cs`, `src/Meridian.Wpf/Services/*`).
- Large provider adapter and resilience seams (`src/Meridian.Infrastructure/Adapters/**`, `src/Meridian.Infrastructure/Resilience/*`).
- Characterization tests and documentation that measure progress without forcing architectural rewrites up front.

### Out of scope for Phase 0
- Renaming public contracts purely for style.
- Rewriting provider protocols or changing wire formats.
- Replacing WPF or web UI frameworks.
- Moving business logic across bounded contexts without tests first.
- Throughput/performance tuning that is not directly required to keep behavior stable during refactors.

## Target modules

### Workstream A — Startup orchestration
- `src/Meridian/Program.cs`
- `src/Meridian.Application/Commands/CommandDispatcher.cs`
- `src/Meridian.Application/Services/CliModeResolver.cs`
- `src/Meridian.Application/Composition/HostStartup.cs`

### Workstream B — Configuration and validation
- `src/Meridian.Application/Config/*`
- `src/Meridian.Application/Services/ConfigurationService.cs`
- `src/Meridian.Application/Services/AutoConfigurationService.cs`

### Workstream C — Desktop UI composition
- `src/Meridian.Wpf/ViewModels/DataQualityViewModel.cs`
- `src/Meridian.Wpf/Views/*.xaml.cs` with direct network or JSON logic
- `src/Meridian.Wpf/Services/*`
- `src/Meridian.Ui.Services/Services/*`

### Workstream D — Provider adapters and lifecycle management
- `src/Meridian.Infrastructure/Adapters/Core/*`
- `src/Meridian.Infrastructure/Adapters/Polygon/*`
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/*`
- `src/Meridian.Infrastructure/Adapters/StockSharp/*`
- `src/Meridian.Infrastructure/Resilience/*`

## Proposed sequencing

1. **Freeze behavior with characterization tests.**
   - Strengthen command dispatch, mode selection, config validation, WPF mapping, and connection lifecycle coverage before moving logic.
2. **Document seams and current hotspots.**
   - Keep a living baseline of file size, direct HTTP/JSON usage, and oversized adapters.
3. **Extract startup responsibilities behind focused collaborators.**
   - Separate CLI dispatch, deployment mode handling, validation gates, and runtime pipeline startup.
4. **Extract WPF data loading into services/adapters.**
   - Move page/view-model transport and JSON mapping into UI services while preserving bindings.
5. **Split oversized provider adapters.**
   - Break transport, mapping, retry/backoff, and capability negotiation into separate components.
6. **Enforce module boundaries with tests and lightweight architecture rules.**
   - Prefer additive seams and adapters over large rewrites.

## Anti-goals

- Do not mix behavior changes with readability-only moves in the same PR unless tests explicitly prove equivalence.
- Do not introduce new dependency injection graphs for the sake of abstraction alone.
- Do not move HTTP and JSON logic into shared helpers that obscure provider- or page-specific behavior.
- Do not add temporary dual implementations unless there is a documented cutover plan.
- Do not treat file-count reduction as success if responsibility boundaries become less clear.

## Architecture rules

1. **Single startup coordinator, multiple collaborators.** `Program` should compose and delegate; it should not continue to accumulate policy, IO, validation, hosting, and runtime orchestration in one place.
2. **Command dispatch remains deterministic.** Registration order is the behavioral contract until a different policy is explicitly adopted and tested.
3. **Mode resolution stays centralized.** Legacy flags and unified modes must continue to flow through one translation point.
4. **Validation remains side-effect free.** Config validators can report errors and warnings, but should not mutate runtime state.
5. **WPF pages and view models should not own raw transport details long term.** HTTP calls, JSON parsing, and endpoint selection should migrate toward services with page/view-model-facing DTOs.
6. **Provider adapters should separate concerns.** Connection management, payload parsing, subscription orchestration, and failover logic should become independently testable.
7. **Refactors must preserve desktop and CLI guardrails.** Existing `dotnet build`, application tests, UI tests, and provider tests remain the minimum safety net.

## Migration status by workstream

| Workstream | Current state | Phase 0 status | Exit signal |
| --- | --- | --- | --- |
| Startup orchestration | Large `Program` runtime path with mixed CLI, validation, UI, and pipeline responsibilities | Baseline captured; characterization tests strengthened | Team agrees on extraction seams for dispatch, validation gate, and runtime startup |
| Configuration and validation | Validation already centralized but still broad across providers and symbol rules | Baseline captured; characterization tests strengthened | Error/warning behavior is pinned before refactoring validators |
| Desktop UI composition | Data-quality and several page/view-model types still contain direct HTTP/JSON logic | Baseline captured; WPF mapping characterization added | Transport/mapping extraction plan agreed for first page/view-model slice |
| Provider adapters and lifecycle | Several adapters remain very large and mix transport, retries, mapping, and capability logic | Baseline captured; connection lifecycle characterization strengthened | First adapter split candidate selected and protected by tests |

## Phase 0 completion checklist

- [x] Roadmap document exists.
- [x] Baseline complexity metrics are recorded.
- [x] Characterization tests cover startup dispatch, mode selection, config validation, WPF mapping, and provider connection lifecycle.
- [ ] Module boundaries reviewed with the team.
- [ ] Sequencing approved for Phase 1 extraction work.
# Readability Refactor Roadmap

**Date:** 2026-03-20  
**Status:** Draft implementation roadmap  
**Scope:** Startup/orchestration clarity, composition-root modularization, WPF/UI service separation, workflow modeling, declarative transforms, and provider capability extraction.

---

## Executive Summary

This roadmap converts the current readability and maintainability concerns into a phased refactor program that works with Meridian's existing architecture rather than fighting it.

The plan is designed to deliver five outcomes:

1. Make startup and orchestration readable and testable by shrinking `Program.cs` into explicit mode-based workflows.
2. Make DI and feature composition explicit by splitting the composition root into feature-oriented registration modules.
3. Move WPF page logic out of code-behind and into typed contracts, shared UI services, and WPF viewmodels.
4. Convert imperative setup/configuration flows into explicit workflow/state models.
5. Expand declarative, transform-oriented implementation patterns in rules-heavy areas, especially where the existing F# pipeline already provides a good precedent.

This document is intentionally implementation-focused: it defines architectural guardrails, a phased delivery plan, validation criteria, backlog breakdown, and sequencing guidance.

---

## Architectural Guardrails

The refactor should improve readability without weakening the repository's documented layer rules.

### 1. Respect layer boundaries

Non-negotiable dependency rules from the architecture documentation remain in force:

- `Infrastructure` must not reference `Application` or `Storage`.
- `Storage` must not reference `Application` or `Infrastructure`.
- `Ui.Services` must remain platform-neutral.
- `Meridian.Wpf` must not reference `Ui.Shared` or `Ui`.

**Implementation checklist for every refactor slice:**

- Did this change add a new assembly reference?
- Should a shared type move to `Contracts` or `Core` instead of staying in a host or feature assembly?
- Is platform-neutral logic accidentally remaining in `Meridian.Wpf`?

### 2. Keep UI hosts thin

The desktop layering guidance should continue to drive refactors:

- `Meridian.Wpf` owns host-specific views, viewmodels, and platform services.
- `Meridian.Ui.Services` owns shared UI/domain helper logic.
- `Meridian.Ui` stays a thin web host.

**Implementation rule:** when extracting logic from WPF pages, move it in this order:

1. `Contracts` for DTOs and request/response shapes.
2. `Ui.Services` for shared client/orchestration/presentation logic.
3. `Wpf` only for view, binding, and lifecycle glue.

### 3. Preserve composition as a single source of truth

`ServiceCompositionRoot` and `HostStartup` should remain the conceptual composition entry point even if the implementation is split into multiple files.

**Implementation rule:** split the code, not the concept.

The end state should still feel like:

- one composition entry point,
- one startup story,
- many small, readable modules behind it.

---

## Baseline Snapshot

The repo already has strong architectural guidance, but several files and flows are concentrated enough to justify a dedicated readability initiative.

### Current hotspots

| Area | Current observation |
| --- | --- |
| Startup entry point | `src/Meridian/Program.cs` is 573 lines and mixes bootstrap, config loading, command dispatch, validation, host startup, and mode-specific runtime logic. |
| Composition root | `src/Meridian.Application/Composition/ServiceCompositionRoot.cs` is 1,404 lines and acts as a mega-file for service registration. |
| WPF data quality page | `src/Meridian.Wpf/Views/DataQualityPage.xaml.cs` is 1,655 lines and is the largest C# file in the repo. |
| Configuration wizard | `src/Meridian.Application/Services/ConfigurationWizard.cs` is 1,252 lines and is a strong candidate for workflow extraction. |
| Provider adapter pilot | `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpMarketDataClient.cs` is 1,396 lines and bundles many concerns into one adapter. |

### Additional baseline metrics captured on 2026-03-20

- Largest C# file in the repo: `DataQualityPage.xaml.cs` at 1,655 lines.
- `ServiceCompositionRoot.cs` is the third-largest C# file in the repo.
- Four WPF page code-behind files currently contain direct JSON logic:
  - `SetupWizardPage.xaml.cs`
  - `DataQualityPage.xaml.cs`
  - `DataBrowserPage.xaml.cs`
  - `TradingHoursPage.xaml.cs`
- There are 91 `JsonElement` references under `src/`, including concentrated usage in WPF data quality code.

### Initial progress metrics to track through the program

- Size of `Program.cs`
- Size of `ServiceCompositionRoot.cs`
- Average size of the top 20 source files
- Number of WPF pages with direct HTTP/JSON logic
- Number of manual `JsonElement` parsing sites in UI-facing code
- Number of provider adapters using shared capability modules

---

## Phase 0 — Foundations and Safety Rails

### Objective

Create the scaffolding needed to refactor safely without changing user-visible behavior.

### Work items

#### 0.1 Add and maintain planning artifacts

Create and maintain this roadmap plus a companion technical design pack that captures:

- scope,
- target modules,
- sequencing,
- anti-goals,
- architecture rules,
- migration status per workstream.

#### 0.2 Baseline complexity and responsibility concentration

Capture and periodically refresh:

- largest files,
- startup-path responsibilities,
- WPF pages with direct HTTP/JSON logic,
- large provider adapters,
- existing test coverage focus areas.

#### 0.3 Add characterization tests around critical flows

Before major extraction work, add tests around:

- startup command dispatch,
- startup mode selection,
- config validation behavior,
- WPF data-quality mapping behavior,
- provider connection lifecycle behavior.

### Deliverables

- Roadmap document
- Baseline metrics snapshot
- Initial characterization tests

### Exit criteria

- Team agrees on module boundaries and sequencing.
- Critical startup, UI, and provider behaviors are covered well enough to refactor safely.

---

## Phase 1 — Shrink `Program.cs` into Explicit Mode Workflows

### Objective

Turn the current all-in-one entry point into a thin bootstrapper plus mode/workflow runners.

### Target design

#### 1.1 Introduce a startup domain model

Add records/classes such as:

- `StartupRequest`
- `StartupContext`
- `StartupValidationResult`
- `HostMode`
- `StartupPlan`

These should live in `Meridian.Application` or a dedicated host startup module depending on whether they are cross-host or host-specific.

#### 1.2 Split execution into mode handlers

Create focused units such as:

- `CommandModeRunner`
- `WebModeRunner`
- `DesktopModeRunner`
- `CollectorModeRunner`
- `BackfillModeRunner`

#### 1.3 Extract startup phases

Model startup as named phases:

1. Parse arguments
2. Resolve config path
3. Bootstrap logging
4. Load or prepare config
5. Dispatch commands if applicable
6. Validate config and environment
7. Build host
8. Start the selected runtime

#### 1.4 Add a startup orchestrator

Introduce a coordinator such as `StartupOrchestrator` to sequence phases and delegate to the correct mode runner.

### Suggested structure

```text
src/Meridian/
  Program.cs
  Startup/
    StartupOrchestrator.cs
    StartupBootstrap.cs
    StartupModels/
    ModeRunners/
      CommandModeRunner.cs
      WebModeRunner.cs
      DesktopModeRunner.cs
      CollectorModeRunner.cs
      BackfillModeRunner.cs
```

### Implementation sequence

#### Sprint 1

Extract pure helpers first:

- mode selection,
- config path resolution,
- startup validation helpers.

#### Sprint 2

Move command dispatch into `CommandModeRunner` while preserving behavior.

#### Sprint 3

Move web and desktop startup logic into dedicated runners.

#### Sprint 4

Move collector and backfill flows into dedicated runners.

### Acceptance criteria

- `Program.cs` becomes mostly bootstrap plus delegation.
- Each mode runner can be tested independently.
- Existing startup behaviors remain unchanged.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| Hidden side effects caused by initialization order changes | Preserve exact startup ordering first, then simplify. |
| Logging/bootstrap concerns leaking across modules | Treat logging bootstrap as an explicit phase with stable contracts. |
| Regressions in user-visible mode behavior | Gate the phase on a before/after behavior review and characterization tests. |

---

## Phase 2 — Split `ServiceCompositionRoot` into Feature Registration Modules

### Objective

Keep composition centralized while making service registration visible, testable, and aligned to feature concepts.

### Target design

#### 2.1 Create feature registration modules

Break service registration into smaller files such as:

- `ConfigurationFeatureRegistration.cs`
- `StorageFeatureRegistration.cs`
- `ProviderFeatureRegistration.cs`
- `SymbolManagementFeatureRegistration.cs`
- `BackfillFeatureRegistration.cs`
- `PipelineFeatureRegistration.cs`
- `MaintenanceFeatureRegistration.cs`
- `DiagnosticsFeatureRegistration.cs`
- `CanonicalizationFeatureRegistration.cs`

#### 2.2 Keep one top-level entry point

`ServiceCompositionRoot.AddMarketDataServices(...)` should remain as the orchestrator over feature modules.

#### 2.3 Replace boolean sprawl with profiles

Introduce host-oriented composition profiles such as:

- `HostCompositionProfile.Console`
- `HostCompositionProfile.Web`
- `HostCompositionProfile.Desktop`
- `HostCompositionProfile.Backfill`

Profiles should map to feature sets plus optional overrides.

### Implementation sequence

#### Sprint 1

Extract one module at a time, starting with configuration and storage, with no behavior changes.

#### Sprint 2

Extract provider, symbol, and backfill registrations.

#### Sprint 3

Introduce host profiles that translate into existing options.

#### Sprint 4

Deprecate scattered boolean wiring in favor of profile-driven composition.

### Acceptance criteria

- `ServiceCompositionRoot.cs` drops substantially in size.
- Feature registration classes are named after the architecture concepts they register.
- Host startup reads like a feature manifest instead of nested option logic.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| Shared types end up in the wrong assembly during extraction | If multiple layers need a type, move it to `Contracts` or `Core`. |
| Registration ordering regressions | Add composition tests that validate required registrations and critical singleton ordering. |

---

## Phase 3 — Move WPF Pages Toward MVVM Plus Typed API Contracts

### Objective

Remove direct networking, parsing, and orchestration logic from WPF pages.

### Pilot target

`DataQualityPage.xaml.cs` is the best pilot because it currently combines:

- polling,
- HTTP calls,
- JSON parsing,
- collection shaping,
- visual status mapping.

### Target design

#### 3.1 Introduce typed DTOs in `Contracts`

Create request/response types for data-quality endpoints.

#### 3.2 Add shared API clients and presentation services in `Ui.Services`

Introduce services such as:

- `IDataQualityApiClient`
- `DataQualityApiClient`
- `IDataQualityPresentationService`
- `DataQualityPresentationService`

These should own:

- API calling,
- fallback/demo-data policy,
- mapping from DTOs to presentation models.

#### 3.3 Create WPF viewmodels

For example:

- `DataQualityPageViewModel`

Responsibilities should include:

- refresh command,
- timer lifecycle coordination,
- observable properties,
- filtered collections,
- loading and error state.

#### 3.4 Reduce code-behind to lifecycle glue

The page should only:

- initialize bindings,
- hook navigation or page lifecycle events,
- delegate to the viewmodel.

### Rollout pattern

1. Pilot `DataQualityPage`.
2. Repeat the pattern for `BackfillPage`, `WatchlistPage`, `MainWindow`, and dashboard-heavy views.

### Testing strategy

- DTO deserialization tests
- `Ui.Services` tests for mapping and refresh logic
- WPF viewmodel unit tests
- minimal page smoke tests for binding and lifecycle wiring

### Acceptance criteria

- No direct `JsonElement` walking in migrated WPF pages.
- Pages depend on viewmodels and services, not ad hoc HTTP code.
- Shared logic is platform-neutral and lives outside WPF.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| DTOs change during migration and cause viewmodel churn | Introduce stable presentation models and isolate DTO mapping in one shared layer. |
| Formatting rules stay duplicated between page and viewmodel | Centralize grading/severity/status mapping in a presentation service. |

---

## Phase 4 — Convert Configuration and Setup into an Explicit Workflow Model

### Objective

Turn configuration/setup flows into an explicit workflow engine instead of long imperative scripts.

### Target design

#### 4.1 Introduce workflow primitives

Possible types:

- `WizardContext`
- `WizardStepId`
- `WizardStepResult`
- `WizardTransition`
- `WizardAction`
- `WizardSummary`

#### 4.2 Represent the wizard as a step graph

Instead of one long method:

- each step becomes an isolated unit,
- each step defines its input/output,
- transitions become explicit.

#### 4.3 Separate metadata from flow logic

Hardcoded provider signup, docs, and free-tier guidance should move to:

- configuration JSON,
- contract metadata,
- provider descriptor objects.

#### 4.4 Keep the workflow reusable across frontends

A workflow model should support future rendering adapters for:

- CLI setup,
- web setup,
- WPF first-run experience.

### Implementation sequence

#### Sprint 1

- Extract provider metadata into structured types.
- Extract wizard steps into separate methods/classes with no behavior changes.

#### Sprint 2

Introduce `WizardContext` and step-result models.

#### Sprint 3

Replace the imperative sequence with a workflow coordinator.

#### Sprint 4

Add rendering adapters for CLI and future web/WPF use.

### Acceptance criteria

- Steps are independently testable.
- Adding or removing a step does not require editing one giant method.
- Provider guidance becomes data-driven.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| First implementation feels too abstract | Start with a linear workflow and explicit step objects before introducing branching sophistication. |

---

## Phase 5 — Expand Declarative and Transform-Oriented Domain Rules

### Objective

Use the transform-based style already present in the F# pipeline for more rule-heavy subsystems.

### Recommended targets

#### 5.1 Validation rules

Candidates include:

- config validation,
- event validation,
- data-quality scoring rules,
- canonicalization decision logic.

#### 5.2 Data quality scoring

These rules often become clearer as:

- pure functions,
- discriminated unions or explicit result types,
- composable evaluators.

#### 5.3 Event enrichment and canonicalization

Any area with nested branching, mutable accumulators, or many special-case paths is a candidate.

### Delivery options

#### Option A — Gradual F# expansion

- F# owns pure transform modules.
- C# owns orchestration and integration boundaries.

#### Option B — Declarative C# first

If team F# comfort is limited, use:

- records,
- pattern matching,
- result types,
- pipeline helpers.

### Acceptance criteria

- Rule-heavy code is mostly pure and side-effect free.
- Transform chains are easier to read than service-method branching.
- Interop boundaries remain clear and low-friction.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| Team skill imbalance with F# | Start where F# clearly improves validation or transform clarity. |
| Mixed-language complexity grows too quickly | Keep F# focused on pure transforms and validation modules. |

---

## Phase 6 — Rebuild Provider Adapters Around Reusable Capabilities

### Objective

Reduce giant provider adapters by factoring recurring concerns into reusable capability modules.

### Target design

#### 6.1 Extract reusable capabilities

Examples:

- `IConnectionLifecycle`
- `IReconnectPolicy`
- `IHeartbeatMonitor`
- `ISubscriptionStateStore`
- `IProviderMessagePump`
- `IProviderEventTranslator`

#### 6.2 Introduce a composed provider runtime

Instead of one large adapter owning everything, define a runtime assembled from shared capability components.

#### 6.3 Standardize provider metadata

Extend provider metadata to capture capabilities such as:

- streaming support,
- depth support,
- reconnect policy,
- credential requirements,
- transport type,
- symbol model.

### Implementation sequence

#### Pilot

Start with `StockSharpMarketDataClient` as the first extraction target.

#### Then

Apply the same capabilities to a second and third provider where the behavior repeats.

### Acceptance criteria

- New providers require less boilerplate.
- Large adapters shrink meaningfully.
- Shared resilience behavior becomes consistent across providers.

### Risks and mitigation

| Risk | Mitigation |
| --- | --- |
| Capability interfaces become too granular | Only extract patterns that recur in at least two or three providers. |
| Over-abstraction hides provider-specific behavior | Start from concrete repeated concerns, not theoretical purity. |

---

## Cross-Cutting Validation Strategy

This initiative changes structure more than behavior, so validation must be built into every phase.

### Test pyramid by layer

#### Host/startup tests

Focus on:

- mode dispatch,
- config path resolution,
- startup phase ordering,
- error-to-exit-code mapping.

#### Composition tests

Focus on:

- required services registered for each host profile,
- optional features enabled or disabled correctly.

#### Shared UI service tests

Focus on:

- API client mapping,
- polling and refresh logic,
- presentation-model shaping,
- filtering behavior.

#### WPF tests

Keep them focused on:

- viewmodel behavior,
- navigation wiring,
- host-specific services.

#### Provider tests

Cover:

- connection lifecycle,
- reconnection behavior,
- subscription restore,
- event translation.

### Required quality gates per phase

For each phase, run:

1. release build for affected projects,
2. targeted unit tests,
3. architecture dependency review,
4. smoke run of the affected host or mode.

### Metrics to track

- average size of the top 20 source files,
- WPF pages with direct HTTP logic,
- manual `JsonElement` parsing sites in UI,
- size of `Program.cs`,
- size of `ServiceCompositionRoot.cs`,
- number of provider adapters using shared capability modules.

---

## Backlog Structure

### Epic A — Startup clarity

- A1: Add startup characterization tests
- A2: Extract startup models
- A3: Extract command mode runner
- A4: Extract web mode runner
- A5: Extract desktop mode runner
- A6: Extract collector/backfill runners
- A7: Reduce `Program.cs` to a thin bootstrapper

### Epic B — Composition root modularization

- B1: Extract configuration registration module
- B2: Extract storage registration module
- B3: Extract provider registration module
- B4: Extract backfill/pipeline registration modules
- B5: Introduce host composition profiles
- B6: Add composition registration tests

### Epic C — WPF readability

- C1: Introduce typed data-quality DTOs
- C2: Add `Ui.Services` data-quality client
- C3: Create `DataQualityPageViewModel`
- C4: Migrate `DataQualityPage`
- C5: Apply the same pattern to `BackfillPage`
- C6: Apply the same pattern to watchlist/dashboard pages

### Epic D — Workflow-driven setup

- D1: Extract provider metadata objects
- D2: Introduce wizard context model
- D3: Convert steps to separate units
- D4: Add workflow coordinator
- D5: Add rendering adapters for CLI/web/WPF reuse

### Epic E — Declarative rules / F# expansion

- E1: Identify rules-heavy modules
- E2: Pilot a pure transform refactor in one subsystem
- E3: Expand validation pipeline usage
- E4: Add result-type and pattern-matching guidance

### Epic F — Provider composition

- F1: Inventory repeated provider concerns
- F2: Create connection lifecycle capability
- F3: Create subscription state capability
- F4: Create message-pump capability
- F5: Refactor the StockSharp pilot
- F6: Apply capability modules to a second provider

---

## Recommended Sequencing

### Milestone 1 — Safe extraction

**Duration:** 2–3 weeks  
**Includes:** Phase 0 and the beginning of Phase 1  
**Goal:** Get `Program.cs` under control without changing platform behavior.

### Milestone 2 — Structural clarity

**Duration:** 2–4 weeks  
**Includes:** Finish Phase 1 and Phase 2  
**Goal:** Make startup and DI understandable at a glance.

### Milestone 3 — UI modernization

**Duration:** 3–5 weeks  
**Includes:** Phase 3 and the start of Phase 4  
**Goal:** Fully migrate one WPF page with a repeatable pattern.

### Milestone 4 — Domain/provider clarity

**Duration:** 4–8 weeks  
**Includes:** Finish Phase 4 plus Phase 5 and Phase 6 pilots  
**Goal:** Prove the transform-oriented and capability-oriented directions.

---

## Program-Level Definition of Done

This initiative should be considered complete when all of the following are true:

- `Program.cs` is mostly bootstrap and delegation.
- `ServiceCompositionRoot` reads like a feature manifest instead of a mega-file.
- Migrated WPF pages no longer perform direct API parsing/orchestration.
- Configuration workflows are represented as explicit step models.
- More rule-heavy logic is expressed as small, testable transforms or pure functions.
- Provider adapters share reusable lifecycle/capability components instead of growing independently.

---

## Strongest Recommendation

For best return at the lowest risk, execute the program in this order:

1. Refactor startup first.
2. Refactor composition second.
3. Use `DataQualityPage` as the UI pilot.
4. Convert the configuration wizard into a workflow model.
5. Expand F# and provider capability abstractions only after the earlier patterns are proven.

That sequencing yields:

- visible readability wins early,
- low architectural risk,
- faster developer comprehension,
- reusable patterns for later cleanup work.

---

## Anti-Goals

This roadmap is **not** intended to:

- rewrite the full system in a single pass,
- replace the documented layer model,
- introduce host-to-host references,
- move platform-specific logic into shared libraries,
- force F# adoption where declarative C# is sufficient.

---

## Companion Document

For concrete folder structure, interface proposals, DTO examples, and migration sketches, see `docs/plans/readability-refactor-technical-design-pack.md`.
