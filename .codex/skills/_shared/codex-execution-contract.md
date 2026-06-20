# Codex Execution Contract

Use this file as the Codex-only execution standard for Meridian skill runs. It complements
`project-context.md` and should be referenced by every repo-local Codex skill.

## Start Gate

- Run `git status --short` before editing.
- Treat unrelated worktree changes as user-owned.
- Choose the smallest safe task scope that satisfies the request.
- For implementation tasks, identify whether docs, tests, catalogs, or generated metadata must
  move with the code change before editing.
- For memory-aware Codex tasks, inspect `.codex/memory/index.yml` and, when a scoped task is known,
  route through a `.codex/memory/tasks/<task-id>.yml` descriptor. Load only entries selected by the
  current task descriptor, intent, skill, changed paths, branch, or explicit tags. Canonical docs
  and selected skills remain authoritative when memory disagrees.
- For very long Codex goals, keep a `.codex/memory/goals/<goal-id>.yml` inventory with objective,
  status, active task descriptor, progress items, evidence refs, next actions, blockers, and
  promotion candidates. Refresh it before compaction, after validation, and when the active task
  descriptor changes.
- When memory is loaded or skipped for task/branch scope, include a compact receipt: selected memory
  IDs, match reason, stale warnings, goal progress count when a goal inventory is active, and
  task/branch entries skipped because their scope did not match.

## Workflow Disclosure Gate

- Use the provider-agnostic AI User Notification template in
  `docs/ai/assistant-workflow-contract.md` for startup or orientation, before file edits, during
  longer investigation or validation, when a blocker changes the plan, and before final validation.
- Populate the canonical fields instead of maintaining separate Codex-only progress prose:
  `Phase`, `Intent understood`, `Current action`, `Evidence/source`, `Next gate`, and
  `Validation intent`. Keep each field concise and avoid raw command transcripts, file dumps, or
  full audit logs unless the user explicitly asks for a detailed trace.
- Add Codex-specific workflow, skill, context, route, or tool metadata only when it helps route the
  task; such metadata must not replace or contradict the shared notification fields.
- When a task expands to a meaningful tool, source family, or context pack, send another AI User
  Notification that explains why scope is widening and whether source files will be edited, left
  unchanged, or deferred.

## Skill Selection Receipt Gate

- In the first substantive response after skill routing, include a compact skill selection receipt.
- Use this four-field block so the receipt is scannable without becoming a preamble:

  ```md
  **Skill Selection**
  - Skill: `<skill-or-none>`
  - Mode: `<skill-specific mode or n/a>`
  - Reason: <short clause>
  - Required Opening: <skill-specific opening requirement or n/a>
  ```

- For skills with explicit opening output rules, merge those rules into the receipt instead of adding
  a competing preamble. For example, `meridian-brainstorm` records the detected mode in the receipt,
  then starts the compact triage table.
- If no repo-local skill applies, set `skill=none` and name the nearest routing basis in the reason.
- Keep the receipt brief. It does not replace the final response evidence for files changed,
  validation, skipped gates, or residual risk.

## Concurrent Work Gate

- Split work only across disjoint files, projects, or subsystems.
- Give every worker explicit ownership and assume other workers may be editing nearby files.
- Keep one coordinator responsible for integration, conflict resolution, and final validation.
- Avoid overlapping writes unless the overlap is deliberately sequenced.
- For parallel implementation or a dirty changing worktree, maintain task-local working memory from
  `docs/ai/working-memory.md`: active claims, inspected files, validated facts, open assumptions,
  merge order, codebase drift, and validation reuse.
- Refresh working memory before a lane starts editing, after validation, and whenever `git status`
  changes unexpectedly.
- For concurrent .NET validation, prefer isolated outputs:

```bash
python3 build/python/cli/buildctl.py build --project Meridian.sln --configuration Release --isolation-key codex-<task>
```

## Narrow Validation Gate

- Run the narrowest command that covers the touched surface.
- Prefer filtered `dotnet test`, package-local dashboard commands, doc-only checks, and `--no-build`
  after a valid isolated build.
- When local resource limits, package restore, or MSBuild/output locks make local proof unreliable,
  push the branch and dispatch GitHub Actions `Targeted Test` with the same repo-relative .NET test
  project under `tests/` plus filter before retrying broad local scripts.
  Do not omit the .NET filter; the lane is designed to fail closed rather than run a whole test
  project.
- Use broad suites only when the changed layer or release risk requires them.
- If `make` is unavailable on Windows, run the target's underlying command directly and report that
  the Make wrapper could not be invoked.

## Cosmetic Churn Gate

- Skip capitalization, uncapitalization, whitespace, punctuation, or wording-only changes unless
  tied to canonical naming, broken docs, accessibility, lint/test failure, API contract names, or
  user-visible copy correctness.
- Do not run broad formatters over unrelated files.

## Code And Docs Sync

- Update docs in the same change when behavior, workflow, contract, configuration, AI guidance, or
  operator-facing behavior changes.
- Prefer existing docs over new docs.
- Put Codex-specific docs under `docs/ai/codex/`.
- For shared development or validation workflow changes, keep the Codex-loaded baseline and mirrored
  assistant surfaces aligned: `AGENTS.md`, `CLAUDE.md`, `.codex/AGENTS.md`,
  `.codex/skills/_shared/project-context.md`, `.codex/skills/_shared/codex-execution-contract.md`,
  `.github/copilot-instructions.md`, `.github/agents/implementation-assurance-agent.md`,
  `.github/workflows/README.md`, `docs/engineering/README.md`, `docs/start/README.md`,
  `.claude/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`.
- For registered `src/**` modules, use `docs/source/data/source-modules.yml` to identify the
  module README, update required docs when behavior changes, then run
  `python3 build/scripts/docs/validate-doc-hashes.py --summary`. If source/docs alignment was
  intentionally reviewed for specific stale modules, refresh only those entries with
  `python3 build/scripts/docs/validate-doc-hashes.py --write-module <MODULE_ID> --summary`; reserve
  broad `--write --summary` for a full accepted-baseline review.
- Use optional source README sections when applicable: plans, end-user value, benchmarks and
  performance, operational evidence, security or credential handling, API/contract notes, and
  migration/archive notes. Do not add empty optional sections only for cosmetic symmetry.
- Keep `.codex/skills/README.md`, `docs/ai/README.md`, and `docs/ai/skills/README.md` linked when
  Codex skill discovery or validation changes.

## AI Tooling Gates

For AI tooling, Codex skill, Codex catalog, prompt, docs automation, or assistant-workflow changes:

- Required: run the direct Python/script checks that back the relevant Make targets. If GNU Make is
  installed, `make ai-verify` and `make ai-arch-check` are acceptable wrappers. Confirm the CI
  `Validate AI contract drift` step remains present.
- Required for Codex memory changes: `python build/scripts/docs/check-codex-memory.py --summary`.
- Advisory: `make ai-audit*`, `make ai-report`, `make ai-docs-freshness`,
  `make ai-docs-drift`, `make ai-docs-sync-report`, `make ai-arch-check-summary`,
  `make ai-arch-check-json`.
- Maintenance/reporting: `make ai-maintenance-light`, `make ai-maintenance-full`,
  `make ai-docs-archive`, `make ai-docs-archive-execute`.

## Final Response Shape

```md
**Skill Selection**
- Skill: `<skill-or-none>`
- Mode: `<mode-or-n/a>`
- Reason: <why this lane>
- Required Opening: <followed opening rule or n/a>

**Implemented**
- Code: <behavior-level changes>
- Docs: <updated docs or "No docs needed: <reason>">

**Validation**
- Required gates: <commands/results>
- Narrow checks: <commands/results>
- Advisory checks: <commands/results or "not run: <reason>">

**Concurrency And Scope**
- Work split: <none | worker ownership>
- Memory receipt: <loaded/skipped memory IDs, active goal progress, or "none">
- Isolation: <build/test isolation used>
- Skipped churn: <cosmetic edits intentionally avoided>

**Residual Risk**
- <real remaining blocker or gap>
```
