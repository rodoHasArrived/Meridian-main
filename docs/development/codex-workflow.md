# Codex Desktop Workstation Workflow

This guide explains how to use the repo-local Codex support system for modular Meridian desktop
workstation work. It complements `.codex/AGENTS.md`, `.codex/skills/`, and the existing WPF
implementation/testing guides.

## Use The Skills First

Pick the narrowest skill before implementation:

| Task | Skill |
| --- | --- |
| New or changed WPF MVVM behavior | `modular-desktop-mvvm` |
| New workspace or screen-level layout | `workstation-screen-composition` |
| Repeated UI or view-model pattern | `shared-component-extraction` |
| Provider setup, credentials, health, or degradation | `provider-management-workflow` |
| Research ingestion, preview, validation, or lineage | `research-data-acquisition` |
| Dense grid, table, blotter, or inspector panel | `dense-data-grid-inspector-panel` |
| Diagnostics, audit, evidence, or activity timeline | `diagnostics-audit-timeline` |
| Memory, CPU, I/O, rendering, concurrency, or lifecycle risk | `performance-resource-review` |
| Behavior-preserving cleanup or consolidation | `safe-refactoring` |
| WPF view-model, command, service, or binding tests | `desktop-test-generation` |

Each skill requires inventory before coding, reuse of existing primitives, MVVM boundaries, focused
tests, and a resource review where the change can scale.

## Recommended Loop

1. Read the nearest source README, WPF implementation notes, and the relevant skill.
2. Run an inventory script before adding new controls or view models:

```powershell
pwsh ./tools/codex/component-inventory.ps1
pwsh ./tools/codex/shared-pattern-suggest.ps1
```

3. Make the smallest modular change that satisfies the workflow.
4. Add or update focused tests.
5. Run the narrowest WPF test filter.
6. Run the Codex quality suite:

```powershell
pwsh ./tools/codex/run-codex-quality-suite.ps1 -MarkdownPath artifacts/codex/codex-quality-suite.md
```

7. Summarize reuse decisions, MVVM ownership, tests, resource risks, and remaining gaps.

## Prompt Templates

Prompt templates live under `.codex/prompts/`. Use them when starting repeatable tasks such as
implementing a desktop workspace, refactoring a screen into shared components, adding provider or
research workflows, reviewing MVVM compliance, optimizing resource usage, or generating tests.

## Quality Gate Before PR

Use this gate for desktop workstation work:

```powershell
pwsh ./tools/codex/run-codex-quality-suite.ps1 -MarkdownPath artifacts/codex/codex-quality-suite.md
dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "<focused filter>" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true --logger "console;verbosity=normal"
git diff --check
```

Use `-FailOnWarning` on the quality suite when the PR is explicitly a cleanup, refactor, or release
hardening pass and warnings should block the change.

## Limitations

The PowerShell scans are heuristic. They flag likely risks such as large files, code-behind logic,
sync I/O, blocking waits, unbounded collections, missing tests, timers, and repeated patterns. Read
the code before treating a finding as a bug.
