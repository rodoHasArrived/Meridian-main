# Meridian Copilot Guide

**Last Updated:** 2026-04-29

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

Do not copy the full repository tree or long convention lists into Copilot prompts. Link to the
current source instead.

## Copilot Surfaces

| Surface | Purpose |
| --- | --- |
| [`../../../.github/copilot-instructions.md`](../../../.github/copilot-instructions.md) | Native repository-wide Copilot instruction file |
| [`../../../.github/instructions/`](../../../.github/instructions/) | Auto-applied path-specific rules for C#, tests, docs, and WPF |
| [`../../../.github/agents/`](../../../.github/agents/) | Copilot coding-agent role definitions |
| [`../../../.github/prompts/`](../../../.github/prompts/) | Reusable Copilot Chat prompt templates |
| [`../../../.github/workflows/copilot-setup-steps.yml`](../../../.github/workflows/copilot-setup-steps.yml) | Copilot coding-agent environment bootstrap |
| [`../../../.github/workflows/copilot-swe-agent-copilot.yml`](../../../.github/workflows/copilot-swe-agent-copilot.yml) | Copilot SWE automation workflow |
| [`../../../.github/workflows/copilot-pull-request-reviewer.yml`](../../../.github/workflows/copilot-pull-request-reviewer.yml) | Copilot PR review workflow |

## Current Product Framing

Meridian is a .NET 9 fund-management and trading platform. The active operator UI lane is
[`../../../src/Meridian.Ui/dashboard/`](../../../src/Meridian.Ui/dashboard/), with built assets in
[`../../../src/Meridian.Ui/wwwroot/workstation/`](../../../src/Meridian.Ui/wwwroot/workstation/).
[`../../../src/Meridian.Wpf/`](../../../src/Meridian.Wpf/) is retained for shared contracts,
regression fixes, and desktop support rather than broad new UI feature work.

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

## Maintenance Rules

- Keep this file Copilot-specific; update shared rules in
  [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).
- Keep `.github/copilot-instructions.md`, `.github/instructions/`, `.github/agents/`,
  `.github/prompts/`, and this file aligned when Copilot behavior changes.
- Update [`../README.md`](../README.md), [`../agents/README.md`](../agents/README.md),
  [`../prompts/README.md`](../prompts/README.md), or [`../instructions/README.md`](../instructions/README.md)
  when discoverability changes.
- Run `python build/scripts/docs/check-ai-inventory.py --summary` after adding, removing, or
  renaming Copilot AI assets.
