# Codebase Audit and Cleanup Roadmap

**Last Updated:** 2026-04-28
**Status:** Active cleanup backlog
**Purpose:** Current cleanup and maintainability roadmap grounded in the repository as it exists today

This document is no longer a point-in-time audit snapshot. It is the active cleanup plan for the remaining codebase-maintenance work that still matters after the large architecture, tracing, canonicalization, and provider-registration foundations landed.

Use this document with:

- `docs/status/ROADMAP.md` for delivery-wave sequencing
- `docs/status/IMPROVEMENTS.md` for completed improvement history
- `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md` for the broader non-assembly implementation backlog
- `docs/plans/readability-refactor-roadmap.md` for deeper host/UI refactor sequencing

---

## Current Position

The earlier cleanup roadmap mixed completed work with open work. The completed items are now treated as history, not backlog:

- namespace and project-shape fixes are complete
- duplicate DTO/type-name ambiguity cleanup is complete
- C3 provider lifecycle consolidation is complete for the active platform baseline
- G2 trace propagation is complete for the current ingestion/storage path
- J8 canonicalization drift canary and maintenance workflow are complete

What remains is narrower and more practical: finish the repository and code-health work that still improves maintainability, contributor confidence, and operator trust.

---

## Active Cleanup Themes

### 1. Provider confidence and runtime clarity

This is the most valuable cleanup work because it affects whether the remaining workstation and operator workflows are trustworthy.

Open work:

- expand Polygon recorded-session replay coverage beyond the initial committed fixture set
- keep Interactive Brokers bootstrap/smoke-build guidance aligned with the actual vendor-DLL path
- keep StockSharp connector guidance aligned with the adapters that are actually validated
- improve unsupported-path messaging for provider/runtime prerequisites
- add stronger NYSE shared-lifecycle regression coverage

Primary anchors:

- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/`
- `src/Meridian.Infrastructure/Adapters/StockSharp/`
- `src/Meridian.Infrastructure/Adapters/NYSE/`
- `tests/Meridian.Tests/Infrastructure/Providers/`
- `docs/providers/`

### 2. WPF workflow cleanup

The app now has shared run surfaces and a delivered four-workspace shell baseline, including first-run readiness progress, Storage preview scope/guidance, compact hosted-page command chrome, actionable shell-context attention details, and Trading shared-work-item attention handling. Workflow depth and implementation quality are still mixed across high-traffic pages.

Open work:

- validate the delivered workspace shells against real Research, Trading, Data Operations, and Governance journeys
- identify placeholder or low-value pages and either strengthen them or label/re-scope them
- keep business logic moving out of page code-behind and into shared services/view models
- make run, portfolio, ledger, Position Blotter, and reconciliation flows feel like one workstation family rather than separate page islands

Primary anchors:

- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Ui.Services/Services/WorkspaceModels.cs`
- `src/Meridian.Wpf/ViewModels/`
- `src/Meridian.Wpf/Views/`

### 3. Structural readability and composition cleanup

The repository now has more capability than the earlier audit covered. The remaining cleanup work is less about emergency fixes and more about keeping the host/composition surface understandable.

Open work:

- continue modularizing startup/composition registration around feature slices
- reduce concentration in large orchestration files as new workstation and governance features land
- keep typed contracts, DTOs, and query/read services as the shared seam between application, UI, and storage
- continue replacing ad-hoc transport/JSON/UI coupling with explicit service boundaries

Primary anchors:

- `src/Meridian.Application/Composition/`
- `src/Meridian.Application/Config/`
- `src/Meridian.Ui.Shared/Endpoints/`
- `src/Meridian.Wpf/ViewModels/`

### 4. Code-health backlog

This is still real, but it should be pulled forward opportunistically when adjacent feature work touches the same files.

Open work:

- resolve remaining unused-event and suppression-only patterns where the owning feature is active
- continue sealing classes where inheritance is not intended
- consolidate conditional-compilation fallback helpers where they still create noise
- keep template/example content in docs/examples rather than in production compile paths
- remove or simplify thin wrappers and dead branches when the surrounding feature is being edited anyway

Guiding rule:

Do not run a massive mechanical cleanup sweep just to reduce counts. Prefer adjacency-driven cleanup in files already being changed for product work.

### 5. Repository and documentation hygiene

The repo structure is healthier than before, but it still needs ongoing maintenance. The generated 2026-04-28 documentation dashboard reports 89/100 health, and the generated coverage report shows 88.3% documented items; the largest practical gaps are endpoint and configuration documentation, not a new roadmap direction.

Open work:

- keep generated docs, status docs, and hand-maintained planning docs synchronized
- continue consolidating overlapping CI workflows where ownership and purpose are clear; the WPF screenshot-refresh lane now has scheduled/push/manual capture, least-privilege default permissions, diagnostic artifacts, and one final screenshot commit job, so future cleanup should preserve that ownership model
- move newly historical documents to `archive/docs/` once they stop serving as active guidance
- keep README/index/navigation docs aligned when planning documents are promoted or retired

Primary anchors:

- `.github/workflows/`
- `docs/status/`
- `docs/plans/`
- `archive/docs/`

---

## Recommended Execution Order

### Wave 1: Trust and operator clarity

1. provider runtime/setup hardening
2. Polygon/NYSE replay and lifecycle validation
3. docs/status/doc navigation cleanup that keeps those paths understandable

### Wave 2: Workstation shell cleanup

1. workspace-first navigation consolidation
2. placeholder/orphan-page review
3. broader run/portfolio/ledger workflow cleanup

### Wave 3: Composition and readability

1. host/composition modularization
2. typed service/query seam reinforcement
3. page-code-behind reduction in active workflow areas

### Wave 4: Opportunistic code-health work

1. unused-event cleanup
2. sealing and wrapper simplification
3. conditional-compilation cleanup

---

## Definition of Done for This Roadmap

This roadmap is complete when:

- provider runtime/setup docs match what is actually validated in code and tests
- workstation navigation is meaningfully workspace-first rather than mostly page-first
- the main host/composition surfaces are easier to reason about than the feature surfaces landing into them
- cleanup items remaining are consciously deferred rather than accidentally stale
- status, plan, and archive docs describe one coherent current repository state

---

## What This Document Does Not Track

This roadmap does not duplicate:

- product-delivery sequencing from `docs/status/ROADMAP.md`
- full non-assembly implementation scope from `docs/status/FULL_IMPLEMENTATION_TODO_2026_03_20.md`
- blueprint-level delivery details from the workstation, governance, QuantScript, or L3 simulation plans

It exists to keep cleanup work actionable and current instead of letting it drift back into historical audit prose.
