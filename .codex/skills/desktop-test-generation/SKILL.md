---
name: desktop-test-generation
description: Generate focused Meridian WPF desktop tests for view models, commands, services, shell routes, bindings, dense tables, provider workflows, diagnostics panels, and resource-sensitive state transitions. Use when adding or repairing desktop workflow coverage.
---

# Desktop Test Generation

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before editing.

## Purpose

Add tests that make desktop workstation changes safer to refactor and verify without relying on
manual UI inspection.

## Inputs Required

- Target view model, service, command, view, or workflow.
- Expected states, commands, and failure modes.
- Existing test class patterns and fixture helpers.

## Use When

Use this skill when WPF workflow, view-model, command, binding, or service tests are needed.

Trigger examples:

- "Write tests for this ViewModel."
- "Add coverage for command disabled reasons."
- "Test this desktop workflow state."

## Do Not Use When

Use `modular-desktop-mvvm` when implementation is still required and `meridian-test-writer` for
non-desktop or cross-project scenario tests.

Non-trigger examples:

- "Implement a new screen with tests."
- "Build a provider adapter."
- "Only update Codex prompts."

## Workflow

1. Inventory neighboring tests for naming, fixture setup, fake services, dispatcher helpers, and
   assertion style.
2. Define scenarios across loading, empty, error, disabled, busy, cancel, retry, success, selected,
   stale, and partial states.
3. Prefer view-model/service tests that do not launch the UI.
4. Add view/binding smoke tests only where XAML binding or route coverage matters.
5. Avoid live provider calls, real credentials, and nondeterministic time.
6. Run the narrowest `tests/Meridian.Wpf.Tests` filter.

## Output Expectations

- Focused tests that protect behavior and refactoring seams.
- Clear fake data and no external dependencies.
- Validation command and result.

## Files Likely Affected

- `tests/Meridian.Wpf.Tests/ViewModels/`, `Views/`, `Services/`, `Shell/`
- Production files only when testability requires a small seam.

## Architecture Rules

- Test through public view-model/service behavior, not private implementation details.
- Do not weaken production MVVM boundaries just to make tests pass.
- Keep fixture helpers reusable but not overly generic.

## Testing Requirements

- Include success and at least one failure/recovery path for meaningful workflow changes.
- Include command can-execute and duplicate-execution guards for async commands.
- Include cancellation when work can be long-running.

## Common Mistakes To Avoid

- Tests that only instantiate a view model without asserting behavior.
- Live provider/network/file dependencies in unit tests.
- Sleeps or timing-dependent assertions.
- Copy-paste fixtures that drift from shared test helpers.

## Resource Management Considerations

- Use bounded fixture collections.
- Add regression tests for cancellation, throttling, disposal, and unsubscription when relevant.
- Avoid test helpers that retain static mutable state across tests.

## Handoffs

- Hand off to `safe-refactoring` when tests reveal behavior that should be extracted.
- Hand off to `performance-resource-review` when coverage involves resource-sensitive flows.
- Hand off to `modular-desktop-mvvm` if production seams are missing.

## Validation

- Run the narrowest WPF test filter for the added tests.
- Run `pwsh ./tools/codex/test-gap-scan.ps1`.
- Run `git diff --check -- <changed files>`.

## Output Standards

- List scenarios covered, test command, and remaining untested risk.
