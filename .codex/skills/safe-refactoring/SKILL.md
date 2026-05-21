---
name: safe-refactoring
description: Plan and execute behavior-preserving Meridian desktop and shared-code refactors with small reversible steps, characterization tests, narrow validation, and no unrelated churn. Use for consolidating WPF screens, view models, services, controls, commands, or shared workstation modules.
---

# Safe Refactoring

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Improve structure, reuse, and maintainability without changing operator-visible behavior or shared
contracts unless the user explicitly requests that change.

## Inputs Required

- Target files and desired end state.
- Behavior-preservation constraints and known risky paths.
- Existing test coverage or characterization strategy.

## Use When

Use this skill for cleanup, consolidation, extraction, renaming, or dependency simplification that
must preserve behavior.

Trigger examples:

- "Refactor this screen without changing behavior."
- "Consolidate these duplicate view models safely."
- "Create an incremental refactor plan."

## Do Not Use When

Use `modular-desktop-mvvm` for new behavior and `shared-component-extraction` for component-focused
extraction.

Non-trigger examples:

- "Add a new provider workflow feature."
- "Review performance only."
- "Brainstorm future roadmap ideas."

## Workflow

1. Write the behavior constraints before editing.
2. Inventory call sites, tests, public contracts, routes, automation IDs, serialization, and docs.
3. Add characterization tests where current behavior is not covered.
4. Split the refactor into reversible steps with one ownership boundary per step.
5. Extract, migrate, validate, then remove old code.
6. Avoid broad formatters, cosmetic churn, and unrelated renames.
7. Run focused tests and document rollback/residual risk.

## Output Expectations

- Safer structure with equivalent behavior.
- Tests or evidence covering the refactored path.
- Clear statement of what did not change.

## Files Likely Affected

- `src/Meridian.Wpf/`, shared UI/service projects, and matching tests.
- Docs only when workflow, validation, or architecture guidance changes.

## Architecture Rules

- Keep public contracts and operator behavior stable unless explicitly changed.
- Preserve MVVM boundaries while moving logic.
- Use interfaces or shared models at module boundaries.

## Testing Requirements

- Characterize existing behavior before moving complex logic.
- Run focused tests after each meaningful step when feasible.
- Add regression tests for bugs found during refactor.

## Common Mistakes To Avoid

- Mixing refactor with feature changes.
- Deleting edge-case behavior because it looks unused.
- Renaming broadly without need.
- Weakening tests to match a broken intermediate state.

## Resource Management Considerations

- Do not introduce larger object graphs while extracting.
- Preserve cancellation, disposal, throttling, and virtualization behavior.
- Watch for event subscriptions and timers when moving code.

## Handoffs

- Hand off to `desktop-test-generation` when characterization coverage is missing.
- Hand off to `performance-resource-review` when refactor changes resource flow.
- Hand off to `modular-desktop-mvvm` if new behavior becomes necessary.

## Validation

- Run focused tests for target behavior.
- Run `pwsh ./tools/codex/refactor-plan-generator.ps1 -TargetFiles <files> -DesiredEndState <state>`.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State preserved behavior, changed structure, tests, rollback path, and residual risk.
