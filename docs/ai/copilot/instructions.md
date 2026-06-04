# Meridian Copilot Guide

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04
**Last Updated:** 2026-05-20

This file is the Copilot-specific companion to the shared Meridian AI guidance. Keep it short:
shared policy belongs in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md),
project grounding belongs in [`../../../CLAUDE.md`](../../../CLAUDE.md), and broad AI navigation
belongs in [`../README.md`](../README.md).

## What Copilot Should Load

Use this order for Copilot Chat, Copilot coding agent, and Copilot-authored PRs:

1. [`../../../.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) for
   repository-wide Copilot behavior.
2. Any matching path instruction under [`../../../.github/instructions/`](../../../.github/instructions/).
3. [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) for cross-provider
   workflow, safety rules, and source-of-truth ownership.
4. [`../ai-known-errors.md`](../ai-known-errors.md) before changing code or generated artifacts.
5. [`../navigation/README.md`](../navigation/README.md) and
   [`../generated/repo-navigation.md`](../generated/repo-navigation.md) for large-repo routing.
6. `../agent-handoff-checklist.md` for coordinator-to-specialist-to-assurance handoffs.
7. `../work-modes.md` to select Lightweight, Standard, or Deep Review context budgets before implementation.
8. [`../tooling/README.md`](../tooling/README.md) when the task needs AI validators, route tooling,
   or maintenance scripts.

Do not copy the full repository tree or long convention lists into Copilot prompts. Link to the
current source instead.

## Copilot Surfaces

| Surface | Purpose |
| --- | --- |
| [`../../../.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) | Native repository-wide Copilot instruction file |
| [`../../../.github/instructions/`](../../../.github/instructions/) | Auto-applied path-specific rules for C#, tests, docs, and WPF |
| [`../../../.github/agents/`](../../../.github/agents/) | Copilot coding-agent role definitions |
| [`../../../.github/prompts/`](../../../.github/prompts/) | Reusable Copilot Chat prompt templates |
| [`../../../archive/docs/workflows/legacy-github-actions-2026-05-18.md`](../../../archive/docs/workflows/legacy-github-actions-2026-05-18.md) | Archive note for retired Copilot workflow files |

## Current Product Framing

Meridian is a .NET 10 fund-management and trading platform. Active operator UI work spans
[`../../../src/Meridian.Ui/dashboard/`](../../../src/Meridian.Ui/dashboard/) and
[`../../../src/Meridian.Wpf/`](../../../src/Meridian.Wpf/), with built browser assets in
[`../../../src/Meridian.Ui/wwwroot/workstation/`](../../../src/Meridian.Ui/wwwroot/workstation/).
Shared product behavior should land behind shared contracts, local/web API endpoints, or shared
read models before either client composes it.

**No mobile development lane:** do not create mobile applications, mobile-specific product
surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
mobile-first workflows. Responsive browser validation may continue for the browser workstation.

Visible operator navigation should stay aligned to `Trading`, `Portfolio`, `Accounting`,
`Reporting`, `Strategy`, `Data`, and `Settings`.

## Task Routing

| Task | Start here |
| --- | --- |
| Need repo orientation | [`../navigation/README.md`](../navigation/README.md) |
| Bug or regression | [`../../../.github/agents/bug-fix-agent.md`](../../../.github/agents/bug-fix-agent.md) |
| Code review | [`../../../.github/agents/code-review-agent.md`](../../../.github/agents/code-review-agent.md) |
| Tests | [`../../../.github/instructions/dotnet-tests.instructions.md`](../../../.github/instructions/dotnet-tests.instructions.md) and [`../../../.github/agents/test-writer-agent.md`](../../../.github/agents/test-writer-agent.md) |
| Documentation | [`../../../.github/instructions/docs.instructions.md`](../../../.github/instructions/docs.instructions.md) and [`../../../.github/agents/documentation-agent.md`](../../../.github/agents/documentation-agent.md) |
| WPF/MVVM | [`../../../.github/instructions/wpf.instructions.md`](../../../.github/instructions/wpf.instructions.md) |
| Provider work | [`../../../.github/agents/provider-builder-agent.md`](../../../.github/agents/provider-builder-agent.md) |
| Prompt templates | [`../../../.github/prompts/README.md`](../../../.github/prompts/README.md) |

For multi-agent tasks, return a compact handoff packet using `../agent-handoff-checklist.md` before the
phase transition.

## Validation Defaults

Use the narrowest command that covers the touched files:

```bash
python build/scripts/docs/check-ai-inventory.py --summary
python -m unittest build/scripts/docs/tests/test_check_ai_inventory.py
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj -c Release /p:EnableWindowsTargeting=true
```

For docs-only Copilot guidance changes, prefer:

```bash
python build/scripts/docs/check-ai-inventory.py --summary
git diff --check
```

If the change touches shared handoff, manifest, or mode guidance, also run:

```bash
python build/scripts/docs/check-ai-handoff.py --strict
```

## Maintenance Rules

- Keep this file Copilot-specific; update shared rules in
  [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).
- Keep `.github/copilot-instructions.md`, `.github/instructions/`, `.github/agents/`,
  `.github/prompts/`, and this file aligned when Copilot behavior changes.
- Keep [`../tooling/README.md`](../tooling/README.md) aligned when Copilot-facing validation or
  script-discovery guidance changes.
- Update [`../README.md`](../README.md), [`../agents/README.md`](../agents/README.md),
  [`../prompts/README.md`](../prompts/README.md), or [`../instructions/README.md`](../instructions/README.md)
  when discoverability changes.
- Run `python build/scripts/docs/check-ai-inventory.py --summary` after adding, removing, or
  renaming Copilot AI assets.

## Repository Navigation

Do not embed a static repository tree here. Use the generated navigation sources instead:

- [`../navigation/README.md`](../navigation/README.md) for assistant routing.
- [`../generated/repo-navigation.md`](../generated/repo-navigation.md) for the current readable repo map.
- [`../generated/repo-navigation.json`](../generated/repo-navigation.json) for MCP and automation consumers.
- [`../../../docs/generated/repository-structure.md`](../../../docs/generated/repository-structure.md) only when a literal tree snapshot is required.

When the repo layout changes, update the generated navigation or structure sources through the docs automation rather than copying a tree into this Copilot-specific guide.

## AI Contract Coverage

- Repo navigation: [`../navigation/README.md`](../navigation/README.md) and [`../generated/repo-navigation.md`](../generated/repo-navigation.md)
- Agent edit rules: follow shared rules in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md)
  and host mechanics in [`../../../.github/copilot-instructions.md`](../../../.github/copilot-instructions.md)
- Generated-file handling: never hand-edit `docs/ai/generated/*` or `docs/generated/*`; rerun local navigation/regeneration lanes when routing truth changes
- Agent orchestration: initialize handoff discipline with `../agent-handoff-checklist.md` and `../parallel-task-manifest-template.md` when multiple agents are involved
- Parallel development workflows: keep surfaces disjoint and record lane scope in the manifest
- Token/context management: choose `../work-modes.md` first, keep context scoped to the lane, and escalate only when cross-provider or approval-gated decisions arise
- Validation procedures: `python build/scripts/docs/check-ai-inventory.py --summary`, `python -m unittest build/scripts/docs/tests/test_check_ai_inventory.py`, plus task-appropriate command set
- Ownership rules: [`../../documentation-ownership.md`](../../documentation-ownership.md), [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md)
