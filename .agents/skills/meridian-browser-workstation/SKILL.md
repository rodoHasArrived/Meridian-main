---
name: meridian-browser-workstation
description: Route and implement TypeScript/React browser workstation tasks in src/Meridian.Ui/dashboard with Meridian-specific guardrails.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts focused on Meridian's browser workstation lane.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Browser Workstation

Use this skill when work is centered on the browser operator workstation in `src/Meridian.Ui/dashboard/`.

Read in order:
1. `../_shared/project-context.md`
2. `../../../docs/ai/generated/repo-navigation.md`
3. `../../../docs/ai/generated/recent-changes.md`

## Roles

### `dashboard-router`
- Confirm the request belongs to the browser workstation lane.
- Name the first dashboard paths and shared contracts to inspect.

### `ui-guardrail-checker`
- Keep browser workflow behavior aligned with shared UI contracts.
- Enforce non-mobile and top-level navigation guardrails.

### `dashboard-validator`
- Run browser workstation test/build commands.
- Report exact command outcomes and residual risks.

## Workflow

1. Confirm ownership under `src/Meridian.Ui/dashboard/`.
2. Trace related shared seams in `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` when behavior overlaps.
3. Make the smallest safe dashboard-focused change.
4. Validate with dashboard-local commands.

## Meridian Rules

- Keep visible top-level navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- Do not create mobile-specific clients or mobile-only workflows.
- Keep browser and desktop behavior aligned through shared contracts when behavior is common.
