# Codex Execution Contract

Use this file as the Codex-only execution standard for Meridian skill runs. It complements
`project-context.md` and should be referenced by every repo-local Codex skill.

## Start Gate

- Run `git status --short` before editing.
- Treat unrelated worktree changes as user-owned.
- Choose the smallest safe task scope that satisfies the request.
- For implementation tasks, identify whether docs, tests, catalogs, or generated metadata must
  move with the code change before editing.

## Workflow Disclosure Gate

- Tell the user the current working phase and why it matters at startup or orientation, before file
  edits, during longer investigation or validation, when a blocker changes the plan, and before final
  validation.
- Keep updates concise: name the current phase, files or subsystem being inspected or changed, the
  evidence being sought, and any blocker or scope change.
- Summarize discoveries and next actions instead of pasting step-by-step command transcripts, raw
  file dumps, or full audit logs unless the user explicitly asks for a detailed trace.

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
  push the branch and dispatch GitHub Actions `Targeted Test` with the same .NET project/filter or
  dashboard test file before retrying broad local scripts. Do not omit the .NET filter; the lane is
  designed to fail closed rather than run a whole test project.
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
  intentionally reviewed, refresh the baseline with
  `python3 build/scripts/docs/validate-doc-hashes.py --write --summary`.
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
- Advisory: `make ai-audit*`, `make ai-report`, `make ai-docs-freshness`,
  `make ai-docs-drift`, `make ai-docs-sync-report`, `make ai-arch-check-summary`,
  `make ai-arch-check-json`.
- Maintenance/reporting: `make ai-maintenance-light`, `make ai-maintenance-full`,
  `make ai-docs-archive`, `make ai-docs-archive-execute`.

## Final Response Shape

```md
**Implemented**
- Code: <behavior-level changes>
- Docs: <updated docs or "No docs needed: <reason>">

**Validation**
- Required gates: <commands/results>
- Narrow checks: <commands/results>
- Advisory checks: <commands/results or "not run: <reason>">

**Concurrency And Scope**
- Work split: <none | worker ownership>
- Isolation: <build/test isolation used>
- Skipped churn: <cosmetic edits intentionally avoided>

**Residual Risk**
- <real remaining blocker or gap>
```
