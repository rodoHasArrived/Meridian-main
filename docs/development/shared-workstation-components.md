# Shared Workstation Components

This guide describes how Codex should identify and grow shared desktop workstation components.

## Inventory First

Before adding a screen-specific control or view-model helper, run:

```powershell
pwsh ./tools/codex/component-inventory.ps1 -MarkdownPath artifacts/codex/component-inventory.md
pwsh ./tools/codex/shared-pattern-suggest.ps1 -MarkdownPath artifacts/codex/shared-pattern-suggest.md
```

Then inspect nearby WPF folders:

- `src/Meridian.Wpf/Controls/`
- `src/Meridian.Wpf/Templates/`
- `src/Meridian.Wpf/Styles/`
- `src/Meridian.Wpf/Behaviors/`
- `src/Meridian.Wpf/ViewModels/`
- `src/Meridian.Wpf/Services/`

## Extraction Threshold

Extract a shared primitive when:

- the same layout or state pattern appears in two real screens;
- a workflow needs a consistent operator recovery path;
- dense tables or inspector panels repeat column, selection, or detail behavior;
- diagnostics and audit surfaces need shared evidence/timeline semantics;
- tests become easier at a shared seam than at each screen.

Do not extract when the behavior is speculative, when a one-off screen still has unclear product
shape, or when abstraction would hide important domain differences.

## Component Contract

A shared component should have:

- narrow inputs and outputs;
- no direct provider, storage, or workflow orchestration;
- clear accessibility and automation behavior;
- lifecycle cleanup for events, timers, and subscriptions;
- focused tests for state and command behavior when it has a view model.

## Common Candidates

- command bars and action groups
- status badges and readiness rows
- virtualized dense tables
- row detail inspectors and detail tabs
- diagnostics panels
- audit timelines
- loading, empty, stale, error, and disabled-state blocks
- provider health and credential readiness cards
