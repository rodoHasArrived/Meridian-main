# CLAUDE.md — Meridian AI Assistant Guide (Condensed)

This file is intentionally short. Keep it focused on **actionable, high-signal instructions**.

## What Meridian Is

Meridian is a .NET 9 trading and fund-operations platform with:
- market data ingestion (streaming + historical),
- strategy/backtesting workflows,
- execution and risk controls,
- storage/archival pipelines,
- and a desktop-first operator experience (WPF shell).

## Core Working Rules

1. **Make the smallest safe change** that satisfies the request.
2. **Preserve behavior** unless the user explicitly asks for behavior changes.
3. **Run targeted validation** for touched areas; avoid unrelated full-suite runs by default.
4. **Keep docs and code aligned** when behavior, workflows, or contracts change.
5. **Use structured, explicit summaries** of what changed and how it was validated.

## Fast Validation Commands

Use the narrowest relevant command:

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
make test
```

## High-Value Paths

- `src/Meridian.Wpf/` — desktop shell (primary operator UX)
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` — shared UI/service surface
- `src/Meridian.Application/` — orchestration + pipelines
- `src/Meridian.Infrastructure/` — provider/integration adapters
- `src/Meridian.Storage/` — WAL, archival, durability paths
- `src/Meridian.Execution/`, `src/Meridian.Risk/` — order routing + pre-trade controls
- `tests/` — regression and subsystem coverage

## Quality Guardrails

- Keep cancellation flow intact for async code.
- Prefer structured logging (no interpolated log strings).
- Respect source-generated JSON patterns already used in-repo.
- Do not bypass durability patterns (WAL / atomic writes) in cleanup/refactors.
- Avoid introducing package version drift (central package management applies).

## AI Maintenance Workflow

Before substantial edits, review known pitfalls:

```bash
python3 build/scripts/ai-repo-updater.py known-errors
```

For broader maintenance/audit lanes:

```bash
make ai-maintenance-light
make ai-maintenance-full
```

## Skills

Repo-local skills are under `.codex/skills/`. Use the skill that best matches the task (cleanup, blueprinting, review, testing, provider work, etc.), and follow that skill’s `SKILL.md` workflow.

## Keep This File Lean

When updating this file:
- remove stale inventory/checklist bloat,
- avoid duplicating deep architecture docs,
- keep only evergreen guidance that accelerates task execution.
