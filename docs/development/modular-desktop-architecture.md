# Modular Desktop Architecture

Meridian desktop workstation implementation should produce reusable modules instead of isolated
screens. The target shape is:

- Views declare layout, bindings, resources, and visual states.
- View models own presentation state, command state, validation, loading, error, empty, stale, and
  selected-detail state.
- Services own workflow orchestration, provider access, persistence, research acquisition,
  diagnostics aggregation, and long-running work.
- Shared controls, templates, styles, and child view models carry repeated workstation patterns.
- Tests prove behavior at the view-model or service seam whenever possible.

## Shared Primitive Candidates

Extract or reuse shared primitives for:

- command bars and command state
- status badges and readiness summaries
- dense data grids and lightweight row view models
- inspector panels and detail tabs
- diagnostics panels and audit timelines
- loading, empty, error, stale, and disabled states
- provider setup and health/recovery flows
- research acquisition preview and validation surfaces

## Architecture Rules

- Do not place provider calls, file I/O, business rules, or workflow decisions in views.
- Do not duplicate provider, research, trading, accounting, ledger, diagnostics, or audit models for
  one screen.
- Do not create a new control until existing `Controls`, `Templates`, `Styles`, `Behaviors`, and
  view-model helpers have been checked.
- Keep large workflows cancelable and testable without launching the UI.
- Keep public routes, automation IDs, serialization shapes, and operator copy stable during
  refactors unless the task explicitly changes them.

## Safe Growth Pattern

1. Implement one screen with clear MVVM boundaries.
2. When a second usage appears, extract a shared primitive with tests.
3. Migrate one call site at a time.
4. Run MVVM, resource, shared-pattern, and test-gap scans.
5. Document resource and behavior tradeoffs in the final implementation summary.
