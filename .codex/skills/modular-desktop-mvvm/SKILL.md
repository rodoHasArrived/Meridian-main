---
name: modular-desktop-mvvm
description: Implement modular Meridian WPF desktop workstation changes with MVVM boundaries, shared services, reusable view models, focused tests, and resource-conscious behavior. Use for new or changed desktop screens, commands, view models, services, shell workflow state, or WPF operator UI implementation.
---

# Modular Desktop MVVM

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Build Meridian desktop features as reusable workstation modules with clear view, view-model, service,
command, test, and resource boundaries.

## Inputs Required

- Target workflow or screen and operator outcome.
- Existing view, view model, service, route, and test files.
- Data size, provider, persistence, or live-update expectations.
- Behavior-preservation constraints when changing existing surfaces.

## Use When

Use this skill for WPF implementation where MVVM structure and modularity are central.

Trigger examples:

- "Add command state to this desktop screen."
- "Implement this WPF workspace feature."
- "Move this workflow logic out of the view."

## Do Not Use When

Use `workstation-screen-composition` for layout-first screen planning, `safe-refactoring` for
behavior-preserving cleanup only, and `desktop-test-generation` when only tests are requested.

Non-trigger examples:

- "Rank three product ideas."
- "Review this PR without editing."
- "Only update docs for the existing workflow."

## Workflow

1. Inventory nearest WPF view, view model, service, shell registration, shared controls, and tests.
2. Decide what can be shared: component, view model, command, service, DTO/read model, or style.
3. Keep XAML declarative; move decisions, labels, disabled reasons, validation, and progress into
   view models or services.
4. Add the smallest reusable seam before adding screen-specific code.
5. Wire async work with cancellation, duplicate-execution guards, progress, and recovery states.
6. Add or update focused tests for view-model state, commands, service orchestration, and bindings.
7. Run the narrowest validation command and summarize resource tradeoffs.

## Output Expectations

- Modular view/view-model/service changes with clear ownership.
- Tests or a documented reason a test was not practical.
- Final summary naming reused primitives, new seams, validation, and residual risk.

## Files Likely Affected

- `src/Meridian.Wpf/Views/`, `ViewModels/`, `Services/`, `Controls/`, `Shell/`, `Styles/`
- `src/Meridian.Ui.Services/` or `src/Meridian.Ui.Shared/` when shared read models are involved
- `tests/Meridian.Wpf.Tests/`

## Architecture Rules

- Keep business logic out of views and code-behind.
- Keep workflow orchestration in services and presentation state in view models.
- Do not duplicate provider, research, trading, accounting, or diagnostics models.
- Preserve routes, automation IDs, public contracts, and operator copy unless intentionally changed.

## Testing Requirements

- Prefer view-model and service tests that run without launching the UI.
- Add command tests for can-execute, busy, cancel, failure, and recovery paths.
- Add binding smoke tests when changing important XAML bindings.

## Common Mistakes To Avoid

- Hiding logic in code-behind event handlers.
- Creating a new screen-specific control when an existing primitive fits.
- Loading entire provider or research datasets into memory for a grid.
- Starting broad rewrites before locking current behavior with tests.

## Resource Management Considerations

- Use virtualization or paging for large collections.
- Avoid sync I/O and blocking waits on UI workflows.
- Debounce expensive filters and batch high-frequency UI updates.
- Dispose subscriptions and long-lived resources when workspaces close.

## Handoffs

- Hand off to `performance-resource-review` for broad or data-heavy changes.
- Hand off to `shared-component-extraction` when duplication becomes the main task.
- Hand off to `desktop-test-generation` when coverage is the next bottleneck.

## Validation

- Run the narrowest WPF test filter covering touched view models or services.
- Run `pwsh ./tools/codex/mvvm-compliance-check.ps1` for MVVM-sensitive work.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State what is shared, what remains screen-specific, and why.
- Report validation commands and results.
- Name any resource, lifecycle, or test coverage risks.
