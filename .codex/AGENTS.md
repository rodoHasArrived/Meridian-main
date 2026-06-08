# Meridian Codex Workspace Instructions

This file applies to files under `.codex/`. Keep repository-wide guidance in the root
`AGENTS.md`, `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, and
`.codex/skills/_shared/codex-execution-contract.md`. Keep this file focused on Codex-local
configuration, skills, prompts, checklists, agents, and environment definitions.

Codex discovers `AGENTS.md` files from the project root down to the current working directory, so
this file is a narrower override for `.codex/**` work. If a nested Codex directory later needs
stricter rules, add a closer `AGENTS.md` or `AGENTS.override.md` rather than expanding this file.

## Start Gate

- Run `git status --short` before editing and preserve unrelated user-owned changes.
- Read `.codex/skills/_shared/project-context.md` before changing Codex guidance that references
  product scope, active surfaces, commands, roadmap direction, or canonical terminology.
- Read `.codex/skills/_shared/codex-execution-contract.md` before changing skills, agents,
  prompts, hooks, config, or automation guidance.
- For source-facing guidance changes, verify the referenced source path, README, command, or script
  exists before documenting it as current.
- Prefer targeted edits over broad rewrites. Do not reformat generated, archived, or unrelated
  Codex assets.
- For multi-lane AI work, route coordination and handoff format through
  `docs/ai/agent-handoff-checklist.md`; select a mode from `docs/ai/work-modes.md`; and initialize
  `docs/ai/parallel-task-manifest-template.md` before concurrent lane execution.

## Codex Configuration Rules

- `.codex/config.toml` is the repository-local default configuration. Keep it conservative and
  fail-closed; do not set unattended full-access defaults there.
- Prefer top-level Codex configuration keys that are documented by the current Codex configuration
  reference. Avoid experimental keys unless the file comment labels them as provisional.
- Keep `sandbox_mode = "workspace-write"` and `approval_policy = "on-request"` as the default
  baseline unless the user explicitly asks for a different repository policy.
- Do not store credentials, tokens, endpoint secrets, user-specific machine paths, or personal model
  preferences in repository-local config.
- If an operator needs broader access, document it as a user-profile override rather than changing
  shared repository defaults.
- When changing TOML, parse it with Python `tomllib` or another TOML parser before committing.

## Skill, Agent, And Prompt Maintenance

- Use existing Meridian skills before adding new ones; add a new skill only when the workflow is
  reusable, distinct, and too detailed for a prompt or checklist.
- Keep skill descriptions trigger-oriented and short enough for reliable selection.
- Put shared project facts in `.codex/skills/_shared/project-context.md` instead of duplicating them
  across multiple skills or agents.
- Keep execution rules in `.codex/skills/_shared/codex-execution-contract.md` when they apply to
  most Codex skills.
- Keep `.codex/agents/*.toml` aligned with their matching `.codex/skills/*/SKILL.md` when an agent
  is a thin specialization of a skill.
- Use lowercase `.codex/` paths in new guidance and links unless citing legacy text verbatim.
- Do not add a prompt, checklist, or agent without a discoverability path in `.codex/skills/README.md`
  or the appropriate docs/AI index when the asset is meant for broad reuse.

## Desktop Implementation Routing

Use these Codex-local skills before making desktop or workstation implementation changes from a
`.codex/**` prompt or checklist:

- `modular-desktop-mvvm` for new or changed WPF view/view-model/service implementation.
- `workstation-screen-composition` before adding a new workspace or large screen.
- `shared-component-extraction` when repeated UI or view-model patterns appear.
- `provider-management-workflow` for provider setup, credentials, health, degradation, or validation
  workflows.
- `research-data-acquisition` for research ingestion, symbol discovery, backfill, import, or dataset
  preparation workflows.
- `dense-data-grid-inspector-panel` for dense tables, grid selection, details, and inspector panels.
- `diagnostics-audit-timeline` for diagnostics, timeline, evidence, or audit surfaces.
- `performance-resource-review` before finalizing broad UI, provider, data, or workflow changes.
- `safe-refactoring` for behavior-preserving consolidation or cleanup.
- `desktop-test-generation` when adding or repairing WPF tests.

## Implementation Guardrails

- Prefer reusable modules over screen-specific implementations.
- Use MVVM consistently: views declare UI, view models own presentation state, services own workflow
  and provider orchestration.
- Keep business logic, provider calls, file I/O, and validation rules out of XAML code-behind.
- Use shared workstation primitives before creating new controls, commands, models, or services.
- Extract repeated layout, dense-table, status, command, empty-state, loading, error, diagnostics,
  and inspector patterns when a second real usage appears.
- Preserve existing behavior unless the user explicitly asks for behavior change.
- Avoid large risky rewrites. Prefer incremental changes with narrow tests and a rollback path.
- Add or update tests for every meaningful view model, command, service, mapping, or workflow change.
- Avoid unnecessary package dependencies and one-off framework choices.
- Document assumptions and tradeoffs in the final response or nearest design doc when they affect
  architecture, performance, resource use, or operator workflow.

## Internal Review Questions

Before editing implementation guidance, prompts, or checklists, answer these internally:

1. Can this be a shared component, view model, command, or service?
2. Can this be tested without the UI?
3. Can this scale to large data without unnecessary memory or CPU use?
4. Can this remain behavior-preserving unless the user explicitly asks for behavior change?
5. Can this be safely refactored or rolled back later?

## Resource Guardrails

- Memory: use virtualization for large grids, avoid full materialization, prefer paging or streaming,
  avoid duplicate in-memory copies, dispose unmanaged resources, and unsubscribe from events.
- CPU: debounce expensive filters, batch high-frequency UI updates, cache calculated values only with
  clear invalidation, and move heavy work off the UI thread.
- I/O: avoid synchronous file, database, and network I/O on UI paths; use async APIs, cancellation
  tokens, bounded retries, and batching where appropriate.
- Rendering: avoid rebuilding full visual trees for live data, throttle telemetry updates, and use
  lightweight row view models for dense tables.
- Concurrency: make long operations cancelable, prevent duplicate command execution, avoid
  unsupervised fire-and-forget tasks, and surface progress and errors to the operator.
- Lifecycle: dispose closeable view models, stop subscriptions when panels close, release provider
  connections, clean temporary research datasets, and define cache retention policies.

## Local Quality Scripts

Run focused scripts from the repository root when they cover the changed surface:

```powershell
pwsh ./tools/codex/run-codex-quality-suite.ps1 -Fast -MarkdownPath artifacts/codex/codex-quality-suite.fast.md
pwsh ./tools/codex/run-codex-quality-suite.ps1 -MarkdownPath artifacts/codex/codex-quality-suite.md
pwsh ./tools/codex/architecture-scan.ps1 -MarkdownPath artifacts/codex/architecture-scan.md
pwsh ./tools/codex/mvvm-compliance-check.ps1
pwsh ./tools/codex/resource-review.ps1
```

Generator scripts are dry-run by default. Use `-Apply` only after reviewing the planned output.

## Validation Expectations

- Markdown-only Codex guidance edits: run `git diff --check -- <changed-files>`.
- TOML edits: run a TOML parser over each changed `.toml` file.
- Skill, agent, prompt, or AI workflow edits: also run the AI tooling gates from
  `.codex/skills/_shared/codex-execution-contract.md` when available, or report exactly why they
  were not run.
- Command guidance edits: verify the referenced script, Make target, project, or `--help` output
  before describing the command as current.
