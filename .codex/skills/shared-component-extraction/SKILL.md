---
name: shared-component-extraction
description: Extract repeated Meridian desktop WPF layout, control, view-model, command, status, diagnostics, table, inspector, and workflow patterns into reusable shared components without behavior drift. Use when duplicate screen-specific code or XAML appears.
---

# Shared Component Extraction

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Turn repeated workstation UI and view-model patterns into small reusable modules that are easier to
test, refactor, and evolve.

## Inputs Required

- Source files with repeated XAML, code-behind, view-model, command, or service logic.
- Desired shared abstraction and behavior-preservation constraints.
- Existing tests or characterization path.

## Use When

Use this skill when two or more screens solve the same problem differently.

Trigger examples:

- "Extract these repeated status badges."
- "Consolidate similar toolbar commands."
- "Make these inspector panels reusable."

## Do Not Use When

Use `safe-refactoring` when the refactor is not component-focused and `modular-desktop-mvvm` when
building a new workflow from scratch.

Non-trigger examples:

- "Add a brand-new provider workflow."
- "Review resource usage only."
- "Generate tests without changing production code."

## Workflow

1. Run or inspect `shared-pattern-suggest.ps1` and inventory likely duplicates.
2. Identify stable common behavior, variable inputs, and screen-specific escape hatches.
3. Add characterization tests or reuse existing tests before moving logic.
4. Extract the smallest component, view model, command helper, service interface, style, or template.
5. Migrate one call site first, validate, then migrate additional call sites.
6. Remove duplication only after both old and new behavior are covered.

## Output Expectations

- A reusable component with a narrow public surface.
- Updated call sites with less duplication.
- Tests proving behavior stayed stable.

## Files Likely Affected

- `src/Meridian.Wpf/Controls/`, `Templates/`, `Styles/`, `ViewModels/`, `Services/`, `Views/`
- `tests/Meridian.Wpf.Tests/`

## Architecture Rules

- Extract view-only concerns to controls/styles/templates.
- Extract state and command concerns to view models or shared helpers.
- Extract provider/workflow orchestration to services.
- Keep dependencies pointed toward interfaces or shared read models.

## Testing Requirements

- Add shared-component tests at the new seam.
- Keep or update screen tests that prove call sites still behave the same.
- Include failure, empty, and disabled-state cases where relevant.

## Common Mistakes To Avoid

- Creating a large generic component with many unrelated options.
- Moving business rules into a WPF control.
- Changing operator behavior while claiming a pure refactor.
- Deleting specialized behavior that only one screen still needs.

## Resource Management Considerations

- Avoid reusable components that retain large collections or event subscriptions.
- Make live-update inputs incremental and disposable.
- Keep templates lightweight for dense repeated rendering.

## Handoffs

- Hand off to `safe-refactoring` for multi-step behavior-preserving migrations.
- Hand off to `dense-data-grid-inspector-panel` for table/inspector-specific primitives.
- Hand off to `performance-resource-review` if the shared component will render many instances.

## Validation

- Run `pwsh ./tools/codex/shared-pattern-suggest.ps1`.
- Run focused tests for migrated call sites and the new shared seam.
- Run `git diff --check -- <changed files>`.

## Output Standards

- State what duplication was removed and what abstraction replaced it.
- Name migrated call sites and tests.
- Note any remaining duplicate patterns intentionally left for later.
