# CLAUDE.md - Meridian AI Assistant Guide

This file is intentionally short. Keep it focused on actionable, high-signal instructions for
Claude-compatible assistants. Use the linked AI indexes for broad repository navigation instead
of copying generated trees into this file.

## What Meridian Is

Meridian is a .NET 10 trading and operational-finance platform with fund operations as a first-class specialization and:

- market data ingestion, streaming, and historical backfill,
- strategy, backtesting, paper validation, and execution workflows,
- risk, ledger, reconciliation, approval, and governed reporting surfaces,
- a browser-based operator workstation,
- an active WPF desktop workstation (co-equal UI lane) whose current focus is web-UI parity over shared contracts,
- MCP and AI workflow assets for repository navigation and task automation.

**Solo-developer default:** [Proportional AI-assisted workflow](docs/ai/solo-developer-workflow.md). One developer owns merge and activation decisions; use synthetic fixtures and targeted checks. Live accounts, invented human review teams, and full production certification are not prerequisites for ordinary development. GitHub required checks still apply.

## Critical Rules

1. Make the safest change that satisfies the request.
2. Preserve behavior unless the user explicitly asks for behavior changes.
3. Run targeted validation for touched areas; avoid unrelated full-suite runs by default.
4. Keep docs and code aligned when behavior, workflows, contracts, skills, or agent guidance change.
5. Use structured, explicit summaries of what changed and how it was validated.
6. Treat unrelated dirty-worktree changes as user-owned.
7. For workflows with multiple agents/lanes, use `docs/ai/agent-handoff-checklist.md` as the required handoff format.
8. Select `docs/ai/work-modes.md` mode before implementation; if running parallel lanes, initialize `docs/ai/parallel-task-manifest-template.md`.
9. For broad generation, domain modeling, workflow design, or architecture-sensitive refactors, load the MDIF spine in `docs/architecture/meridian-development-intelligence-framework.md`, `docs/architecture/meridian-vision.md`, `docs/architecture/meridian-domain-model.md`, `docs/domain/README.md`, and `docs/ai/context/README.md`.
10. Local work may happen on `main` when the user explicitly requests it or the checkout is
    intentionally operating there. Do not bypass GitHub branch protections; for PR-ready publishing,
    use a `codex/<short-task-name>` or `claude/<short-task-name>` branch (either agent prefix is
    accepted), open a PR to `main`, and treat GitHub Actions `Meridian CI / quality-gate` as the
    authoritative merge gate.

## Current Product Direction

- Start documentation and planning orientation from `docs/README.md`, `docs/product/README.md`,
  `docs/engineering/README.md`, and the [canonical design charter](docs/product/meridian-design-document.md).
  Use the roadmap registry (`docs/roadmap/README.md` and
  `docs/roadmap/data/*.yml`) for authoritative planning interpretation.
- Keep product work grounded in current source evidence, the roadmap registry, and the design
  charter. Treat prior baselines and named productization targets as roadmap/status evidence, not
  development ceilings; expansion lanes can proceed when current source, roadmap, or user direction
  supports them.
- Operator UI work runs across two active, co-equal lanes: the browser workstation in
  `src/Meridian.Ui/dashboard/` and the reactivated WPF desktop workstation in `src/Meridian.Wpf/`.
  The WPF lane's immediate focus is closing web-UI parity gaps (`W8-WPF-PARITY-001`); see
  `docs/engineering/plans/wpf-web-ui-alignment-plan.md`.
- Keep both the browser and WPF workflows backed by shared contracts, read models, and API seams so
  neither client forks product state; new WPF parity surfaces consume the shared seam before
  composing presentation.
- **No mobile development lane:** do not create mobile applications, mobile-specific product
  surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
  mobile-first workflows. Responsive browser validation may continue for the browser workstation.
- Keep top-level operator navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`,
  `Strategy`, `Data`, and `Settings`.

## Quick Commands

Use the narrowest relevant command:

```bash
bash scripts/ci.sh
dotnet restore Meridian.sln /p:EnableWindowsTargeting=true
dotnet build Meridian.sln -c Release --no-restore /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
npm --prefix src/Meridian.Ui/dashboard run build
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
```

GNU Make targets are optional convenience wrappers. In Windows shells where `where.exe make` finds
nothing, use the direct `dotnet`, `npm`, `pwsh`, and `python` commands above instead of `make ...`.
For PR-ready work, use risk-appropriate checks first and state honestly if the full local `bash scripts/ci.sh` was not run; the GitHub-hosted `quality-gate` remains authoritative where required before merge.

For incremental .NET iteration, `buildctl.py recommend-validation` selects a stable session profile
per test project; add `--fresh` for independent-output recommendations. The local full gate overlaps
the .NET/browser pair within a bounded resource budget and runs docs/workflows afterward; `--jobs 1`
selects serial execution. `bash scripts/ci.sh --cold` measures fresh build outputs with dependency
caches retained. Warm edit-to-result timing requires an explicit edit-start timestamp. See
`docs/development/build-observability.md` for measurement definitions and retained evidence.


When local machine limits, restore failures, or MSBuild locks make validation unreliable, push the
branch and use the manual GitHub-hosted `Targeted Test` workflow instead of repeatedly retrying
broad local scripts. Select a whitelisted `mode`; the .NET lane uses `mode=dotnet-filtered` with a
repo-relative test project under `tests/` and a non-empty `dotnet_filter`:
After timed-out generation, build, or test attempts, first run
`python build/python/cli/buildctl.py validation-status --summary`, then `dotnet build-server
shutdown`; stop only abandoned repo-owned `dotnet`, `MSBuild`, `testhost`, `csc`, or
`VBCSCompiler` PIDs whose command lines clearly point at this checkout.

```bash
gh workflow run targeted-test.yml --ref <branch> -f mode=dotnet-filtered -f dotnet_project=tests/Meridian.Tests/Meridian.Tests.csproj -f dotnet_filter="FullyQualifiedName~<TestClassOrMethod>"
```

## Repository Layout

- `src/Meridian.Ui/dashboard/` - active browser-based operator workstation
- `src/Meridian.Ui/wwwroot/workstation/` - built workstation assets served by the `src/Meridian` host
- `src/Meridian.Ui.Services/`, `src/Meridian.Ui.Shared/` - shared UI/API read-model surface
- `src/Meridian.Wpf/` - active Windows desktop workstation (co-equal UI lane); current focus is web-UI parity
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

Route work by scope. Use a single specialist agent (blueprint, test-writer, code-review, etc.) for
single-domain tasks. For work that crosses subsystems, needs an approval gate, or requires evidence
synthesis, compose multiple specialist agents and hold to the provider-agnostic Human-in-the-loop
Gates in `docs/ai/assistant-workflow-contract.md`; those gates define when to pause and confirm
before an action proceeds. Readiness reviews, reconciliation summaries, and report-pack approvals
are typical gated work.

> The archived Chief of Staff (CoS) ADK runtime no longer owns this path and its code was removed
> from the tree (`982eea2d`). Do not wire new work to it.

### Agent Design Patterns

- **Parallel** — independent subtasks with no output dependency (separate subsystems, review
  alongside a security scan).
- **Sequential** — each step feeds the next; the default single-domain lane is Repo Navigation →
  Specialist → Implementation → Review → Assurance.
- **Hierarchical** — a coordinating session delegates to specialists, aggregates their evidence,
  and enforces the workflow-contract gates. Use it for multi-domain, gated, or evidence-synthesis
  work.

The handoff format and work-mode selection are Critical Rules 7 and 8.

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

CI/CD validation ownership and administrator rollout are maintained in
[CI/CD ownership and rollout](docs/engineering/ci-cd-optimization.md). Meridian CI owns the four canonical
`scripts/ci.sh` lanes; legacy CI owns Secret Scan and nightly/manual coverage.
The four-lane `Meridian CI / quality-gate` keeps its identity. The always-reported
`Meridian CI / integration-gate` companion reports service-backed PostgreSQL tests shared with
Production Certification, including pull requests and merge groups.
All CI governance changes require human review.
