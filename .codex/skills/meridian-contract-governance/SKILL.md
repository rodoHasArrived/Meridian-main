---
name: meridian-contract-governance
description: Trace Meridian shared contract changes across DTOs, API routes, read models, provider interfaces, WPF, browser workstation, services, tests, and docs. Use when Codex needs contract impact analysis, compatibility checks, route/read-model governance, or cross-surface validation before changing shared contracts.
---

# Meridian Contract Governance

Govern shared contracts as product-facing compatibility surfaces, not just local C# types.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before assessing a
contract change. Read `references/contract-impact-checklist.md` when the change touches shared DTOs,
routes, provider interfaces, or read models consumed by both browser and WPF surfaces.

## Use When

Use this skill when changing or reviewing shared contracts, API/route shapes, DTOs, provider
interfaces, workstation read models, identity/accounting/reporting payloads, or compatibility
behavior across browser, WPF, services, tests, and docs.

Trigger examples:

- "Use $meridian-contract-governance to map this DTO change across browser and WPF."
- "Check contract impact before changing WorkstationEndpoints."
- "Trace this provider interface change through tests, docs, and consumers."

## Do Not Use When

Use `meridian-code-architecture` for project/layer boundary decisions, `meridian-provider-builder`
for implementing provider adapters, and `meridian-code-review` for findings-first review of a diff.

Non-trigger examples:

- "Should this workflow live in Application or Ui.Shared?"
- "Implement the Fred provider adapter."
- "Review this PR for logic bugs only."

## Workflow

1. Identify the contract surface and whether it is additive, breaking, compatibility-preserving, or removal.
2. Run `scripts/contract_impact.py --path <contract-path> --summary` for consumer, test, and docs evidence.
3. Inspect source-owned services, shared UI endpoints, WPF/browser consumers, serialization, and route catalogs.
4. Decide compatibility strategy: additive field, adapter shim, versioned route, migration, or coordinated breaking change.
5. Name required tests and docs before implementation begins.
6. Hand off with a traceable impact map: changed contract -> consumers -> validation -> docs.

## Handoffs

- Hand off to `meridian-code-architecture` when the impact shows a module-boundary decision is needed.
- Hand off to `meridian-browser-workstation` or `modular-desktop-mvvm` for UI-specific consumer edits.
- Hand off to `meridian-test-writer` when compatibility or migration coverage is missing.
- Hand off to `meridian-implementation-assurance` when implementing or certifying the contract change.

## Validation

- Run `python .codex/skills/meridian-contract-governance/scripts/contract_impact.py --path <path> --summary`.
- Run `python .codex/skills/meridian-contract-governance/scripts/run_evals.py --all --dry-run --summary` after editing this skill.
- For shared DTO/route changes, include the narrow .NET tests plus browser/WPF tests that consume the contract.
- For Codex catalog changes, run `python build/scripts/docs/check-codex-skills.py --summary` and
  `python build/scripts/docs/check-ai-inventory.py --summary`.

## Automation Scripts

- `scripts/contract_impact.py` maps a contract path to likely services, UI surfaces, tests, docs, and validation commands.
- `scripts/run_evals.py` runs deterministic contract-impact fixtures by default and only runs live
  Codex traces with `--live-run`.
- `scripts/score_eval.py` scores contract governance output for compatibility, consumer coverage,
  tests, docs, and traceability.

## Output Standards

- State whether the contract change is additive, breaking, compatibility-preserving, or removal.
- List impacted consumers by surface: services, browser, WPF, tests, and docs.
- Name required migration or compatibility shims before implementation.
- Include validation commands and residual compatibility risk.
