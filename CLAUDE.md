# CLAUDE.md - Meridian AI Assistant Guide

This file is intentionally short. Keep it focused on actionable, high-signal instructions for
Claude-compatible assistants. Use the linked AI indexes for broad repository navigation instead
of copying generated trees into this file.

## What Meridian Is

Meridian is a .NET 10 trading and fund-operations platform with:

- market data ingestion, streaming, and historical backfill,
- strategy, backtesting, paper validation, and execution workflows,
- risk, ledger, reconciliation, approval, and governed reporting surfaces,
- a browser-based operator workstation,
- an active WPF desktop workstation alongside the browser-based operator workstation,
- MCP and AI workflow assets for repository navigation and task automation.

## Core Working Rules

1. Make the safest change that satisfies the request.
2. Preserve behavior unless the user explicitly asks for behavior changes.
3. Run targeted validation for touched areas; avoid unrelated full-suite runs by default.
4. Keep docs and code aligned when behavior, workflows, contracts, skills, or agent guidance change.
5. Use structured, explicit summaries of what changed and how it was validated.
6. Treat unrelated dirty-worktree changes as user-owned.
7. For workflows with multiple agents/lanes, use `docs/ai/agent-handoff-checklist.md` as the required handoff format.
8. Select `docs/ai/work-modes.md` mode before implementation; if running parallel lanes, initialize `docs/ai/parallel-task-manifest-template.md`.

## Current Product Direction

- Start documentation and planning orientation from `docs/README.md`, `docs/product/README.md`,
  `docs/engineering/README.md`, and the [canonical design charter](docs/product/meridian-design-document.md).
  Use the roadmap registry (`docs/roadmap/README.md` and
  `docs/roadmap/data/*.yml`) for authoritative planning interpretation.
- Active operator UI work spans `src/Meridian.Ui/dashboard/` and `src/Meridian.Wpf/`.
- Built browser-workstation assets live in `src/Meridian.Ui/wwwroot/workstation/`.
- Shared read-model and endpoint support belongs in `src/Meridian.Ui.Services/` and
  `src/Meridian.Ui.Shared/`.
- Keep browser and desktop workflows backed by shared contracts, read models, and API seams where
  the product behavior is common.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation may continue for the browser workstation.
- Keep top-level operator navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`,
  `Strategy`, `Data`, and `Settings`.

## Fast Validation Commands

Use the narrowest relevant command:

```bash
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
make test
```

## High-Value Paths

- `src/Meridian.Ui/dashboard/` - active browser-based operator workstation
- `src/Meridian.Ui/wwwroot/workstation/` - built workstation assets served by `Meridian.Ui`
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` - shared UI/API read-model surface
- `src/Meridian.Wpf/` - active Windows desktop shell, workflow automation, and desktop validation lane
- `src/Meridian.Application/` - orchestration and pipelines
- `src/Meridian.Infrastructure/` - provider and integration adapters
- `src/Meridian.Storage/` - WAL, archival, packaging, and durability paths
- `src/Meridian.Execution/`, `src/Meridian.Risk/` - order routing and pre-trade controls
- `tests/` - regression and subsystem coverage

## Quality Guardrails

- Keep cancellation flow intact for async code.
- Prefer structured logging; do not interpolate strings inside log calls.
- Respect source-generated JSON patterns already used in the repo.
- Do not bypass durability patterns such as WAL or atomic writes in cleanup/refactors.
- Do not add package versions directly to project files; central package management applies.

## Claude Surfaces

- `.claude/settings.json` - tracked Claude Code hooks, permissions, and model defaults.
- `.claude/settings.local.json` - tracked repo-local allowlist used by this project.
- `.claude/agents/` - lightweight Claude agent entrypoints that route to shared skills and docs.
- `.claude/skills/` - portable Agent Skill packages for Claude-compatible hosts.
- `.claude/plugins/` - checked-in Claude plugin packages with plugin manifests, agents, and skills.
- `.claude/skills/_shared/project-context.md` - canonical Claude-side project grounding.

Keep Claude-specific files focused on host mechanics and discovery. Shared policy belongs in
`docs/ai/assistant-workflow-contract.md`; broad routing belongs in generated navigation docs.

## Orchestration and Multi-Agent Dispatch

The Chief of Staff (CoS) runtime (`tools/chief-of-staff-runtime/runtime.py`) is the repo's
out-of-process ADK orchestration layer for multi-domain, approval-gated, or evidence-synthesis
tasks. Route work through it when any of the following apply:

- The request crosses multiple subsystems and needs evidence from more than one source.
- The request requires an approval gate or operator sign-off before an action can proceed.
- The request needs a structured briefing with trace/evidence retention (e.g. readiness reviews,
  reconciliation summaries, report-pack approvals).

Use specialist agents (blueprint, test-writer, code-review, etc.) for single-domain tasks; route
multi-domain, approval-gated, or evidence-synthesis tasks through the CoS runtime.

### Agent Design Patterns

When composing multiple agents, choose the right topology for the work:

- **Parallel** — subtasks are independent with no output dependency between them. Use when
  investigating separate subsystems concurrently, or running review and security scan
  simultaneously.
- **Sequential** — each step's output feeds the next. Use for the default single-domain lane:
  Repo Navigation → Specialist → Implementation → Review → Assurance.
- **Hierarchical** — a coordinator delegates to specialist agents, aggregates evidence, and
  enforces approval gates before proceeding. Use the CoS runtime for this pattern whenever a
  task is multi-domain, gated, or requires structured evidence synthesis.

Key resources:
- `tools/chief-of-staff-runtime/runtime.py` — ADK node pipeline and integration boundary.
- `docs/development/chief-of-staff-runtime.md` — API routes, config reference, and integration details.
- `.codex/skills/cos-runtime-development/SKILL.md` — Codex workflow for extending the CoS runtime.

## Skills

Use the skill that best matches the task and follow that skill's `SKILL.md` workflow. Current
Claude-compatible portable packages are indexed in `docs/ai/skills/README.md`; repo-local Codex
skills live under `.codex/skills/`.

## AI Maintenance Workflow

Before substantial AI-guidance edits, review known pitfalls:

```bash
python3 build/scripts/ai-repo-updater.py known-errors
```

For broader maintenance/audit lanes:

```bash
make ai-maintenance-light
make ai-maintenance-full
```

## Repository Structure

Do not embed the generated repository tree in `CLAUDE.md`. Use these maintained sources instead:

- `docs/README.md` for the canonical documentation front door.
- `docs/start/README.md`, `docs/product/README.md`, `docs/engineering/README.md`, and
  `docs/operators/README.md` for the rebuilt audience paths.
- `docs/documentation-ownership.md` for docs ownership, generated-doc, and archive rules.
- `docs/architecture/project-structure.md` for the maintained repository map.
- `docs/architecture/module-map.md` for layer ownership and dependency boundaries.
- `docs/start/README.md` and `docs/engineering/README.md` for current local build, test, and run commands.
- `docs/prompts/repo-maintenance-prompts.md` for prompt, agent, and skill maintenance rules.
- `docs/ai/navigation/README.md` for the generated repo-navigation workflow.
- `docs/ai/generated/repo-navigation.md` for subsystem routing and entrypoints.
- `docs/generated/repository-structure.md` for the full generated repository tree.
- `docs/ai/agents/README.md` for agent catalogs.
- `docs/ai/skills/README.md` for skill catalogs and validation commands.

## Keep This File Lean

When updating this file:

- remove stale inventory/checklist bloat,
- avoid duplicating deep architecture docs,
- keep only evergreen guidance that accelerates task execution,
- update the generator in `build/scripts/docs/update-claude-md.py` if automation would reintroduce
  bloat.
