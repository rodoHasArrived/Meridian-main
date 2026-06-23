# Codex Quickstart

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-16

Use this page for the first 10 minutes of a Codex task in Meridian. It compresses the shared
workflow into a task routing checklist, proof matrix, and dirty-worktree protocol. Shared policy
still lives in `../assistant-workflow-contract.md`.

## Startup Checklist

1. Run `git status --short` and separate existing user-owned changes from the task. For PR-bound
   implementation, start from the latest `origin/main` on a `codex/<short-task-name>` branch,
   never write directly to `main`, and open or update a PR targeting `main`.
2. Classify the request as orient, review, docs, browser, WPF, provider, storage, execution, roadmap,
   cleanup, or test work.
3. Disclose the working mode, intended scope, and first evidence source before deeper exploration.
   Use the canonical AI User Notification template in `../assistant-workflow-contract.md`, adding
   Codex-specific skill or workflow metadata only when it helps route the task.
4. Treat root `AGENTS.md`, `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, and
   `.codex/skills/_shared/codex-execution-contract.md` as the Codex-loaded development baseline.
   When a shared development, validation, workflow, prompt, skill, or agent rule changes, also
   inspect and synchronize `.github/copilot-instructions.md`,
   `.github/agents/implementation-assurance-agent.md`, `.github/workflows/README.md`,
   `docs/engineering/README.md`, `docs/start/README.md`,
   `.claude/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`.
   For memory-aware tasks, inspect `.codex/memory/index.yml` before loading durable memory; when
   the work has a named scope, use a `.codex/memory/tasks/<task-id>.yml` descriptor and load only
   entries selected by the descriptor, current intent, skill, changed paths, branch, or explicit
   tags. For very long goals, also use a `.codex/memory/goals/<goal-id>.yml` progress inventory.
   Include a compact memory receipt for selected IDs, match reasons, stale warnings, goal progress,
   and task/branch scope skips. Prefer canonical docs, source, tests, scripts, scoped `AGENTS.md`,
   and selected `SKILL.md` files when memory disagrees.
5. Read `../navigation/README.md` and `../generated/repo-navigation.md` for large-repo routing.
6. For stakeholder/product-scoped tasks, read `../product/meridian-design-document.md` before planning updates.
7. For broad generation, domain modeling, workflow design, or architecture-sensitive refactors, load the MDIF spine: `../../architecture/meridian-development-intelligence-framework.md`, `../../architecture/meridian-vision.md`, `../../architecture/meridian-domain-model.md`, `../../domain/README.md`, and the relevant pack in `../context/README.md`.
8. Read the narrowest relevant Codex skill in `.codex/skills/`.
9. In the first substantive response after skill routing, include the skill selection receipt from
   `.codex/skills/_shared/codex-execution-contract.md` as a four-field `Skill Selection` block:
   selected skill or `none`, mode, reason, and required opening shape. For `meridian-brainstorm`,
   put the detected mode in that receipt, then start with the compact triage table.
10. Before loading a new context family, invoking a meaningful tool, or widening the task scope,
   include the context update notice from the execution contract so the user can see why context is
   expanding and whether edits are still in scope.
11. For source edits under `src/**`, read the nearest `README.md` and identify the module in
   `docs/source/data/source-modules.yml`.
12. Choose the smallest validation lane from the task-to-proof matrix before editing.
   For completed PR-ready work, run `bash scripts/ci.sh`; GitHub Actions
   `Meridian CI / quality-gate` remains the merge authority.
   For local .NET tests, prefer
   `python build/python/cli/buildctl.py test --project <project> --filter "<filter>" --queue`
   so agent-triggered validation uses isolated outputs and avoids parallel test collisions.
   If local machine limits or MSBuild/package contention make that lane unreliable, plan to push
   the branch and dispatch GitHub Actions `Targeted Test` with the same repo-relative .NET test
   project under `tests/` plus filter.
13. Update the nearest docs or AI index when behavior, workflow, prompt, skill, or agent guidance
   changes.

14. If more than one subsystem or AI surface is in scope, initialize
   [`../parallel-task-manifest-template.md`](../parallel-task-manifest-template.md) first and keep
   each lane scoped to a unique file set.
15. For parallel implementation or concurrent codebase changes, initialize the task-local working
    memory ledger from [`../working-memory.md`](../working-memory.md) and keep active claims,
    inspected files, assumptions, merge order, and validation reuse current.
16. For AI/documentation updates:
    1) update `assistant-workflow-contract.md` when shared rules change,
    2) run `check-ai-inventory.py` / `check-codex-skills.py`,
    3) run `validate-docs-structure.py --top-level ai --summary` for narrow AI-doc metadata/structure proof,
    4) avoid direct edits to `docs/ai/generated/*` unless refreshing generation.
17. For AI tooling or validator changes, read [`../tooling/README.md`](../tooling/README.md)
    before choosing scripts or broader maintenance lanes.
18. Update `../documentation-inventory.md` for each rebuild batch so migration-state and audit trail remain current.
19. For deterministic lane preflight (pilot), run:
    `python3 build/scripts/docs/prompt-route-linter.py --prompt "<user prompt>"`.
20. For deterministic lane handoff packets (pilot), run:
    `python3 build/scripts/docs/handoff-packet-generator.py --route-json docs/status/prompt-route-lint-report.json --scope "<scope>" --next-lane "<lane>"`.
21. For route schema v2, keep `docs/status/prompt-route-lint-report.json` and
    `docs/status/ai-handoff-packet.json` as the canonical local evidence artifacts. The route
    artifact must include `modelRouteId`, validation requirements, required telemetry, and
    escalation triggers.
22. For Codex lifecycle hooks, use [`advanced-configuration.md`](advanced-configuration.md) as the
    canonical reference. Keep hooks enabled with `[features].hooks = true`, but add executable
    project hooks only after script review, repository-relative path checks, and Codex `/hooks`
    trust review. Hooks may flag missing intent or ambiguity, but clarification should be a concise
    model question with two or three concrete options instead of a hidden hook decision.

## Read Budget

Load only enough context to route and validate the task.
Progress updates should use the canonical AI User Notification template in
`../assistant-workflow-contract.md`: phase, intent understood, current action, evidence/source, next
gate, and validation intent. Summarize discoveries, next actions, blockers, and validation intent;
do not paste raw file contents or broad command output unless the user asks for a trace.

| Task state | Read first | Avoid until needed |
| --- | --- | --- |
| Unknown subsystem | `../navigation/README.md`, `../generated/repo-navigation.md` | Full repo scans and broad plan families |
| Broad generation or architecture-sensitive work | `../../architecture/meridian-development-intelligence-framework.md`, `../../architecture/meridian-vision.md`, `../../architecture/meridian-domain-model.md`, `../../domain/README.md`, `../context/README.md` | Full feature packs or generated exports until scope is clear |
| Source change | Nearest `src/**/README.md`, `docs/source/data/source-modules.yml` | Generated source docs unless a generator is in scope |
| AI docs change | `../assistant-workflow-contract.md`, this page, `README.md` | Host-specific mirrors unless shared policy changes |
| Codex memory change | `memory-system.md`, `.codex/memory/index.yml`, matching indexed entries, relevant `tasks/*.yml` descriptor, relevant `goals/*.yml` inventory | User/global memory tiers unless explicitly opted in by a future design |
| Codex hook config | `advanced-configuration.md`, `.codex/config.toml` | Executable hook scripts before trust and validation ownership are defined |
| Parallel or concurrent implementation | `../working-memory.md`, `../parallel-task-manifest-template.md`, `../agent-handoff-checklist.md` | Broad logs, overlapping writes, and stale validation reuse |
| WPF task | `.codex/AGENTS.md`, relevant WPF skill, nearest view model/tests | Broad WPF suites before focused filters |
| Browser task | `.codex/skills/meridian-browser-workstation/SKILL.md`, package tests; Codex Browser plugin for unauthenticated rendered-route inspection when the task is visual or interactive | WPF validation unless shared contracts changed; signed-in browser flows or secret entry |

AI-doc proof lane defaults: `python build/scripts/docs/check-codex-memory.py --summary`,
`python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary`,
`python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary`,
`python -m unittest build.scripts.docs.tests.test_check_codex_memory`,
`python3 build/scripts/docs/check-ai-inventory.py --summary`,
`python3 build/scripts/docs/check-codex-skills.py --summary`,
`python3 build/scripts/docs/validate-docs-structure.py --top-level ai --summary`,
`python3 build/scripts/docs/repair-links.py --summary`, `git diff --check`

Hosted proof fallback: after pushing a branch, use `gh workflow run targeted-test.yml --ref <branch>`
with a `tests/` `dotnet_project` and `dotnet_filter` when local resources are the blocker.

Local .NET proof lane default: use `python build/python/cli/buildctl.py test` instead of raw
`dotnet test` when another agent, shell, WPF validation, or desktop launch may be active. The
runner writes `.ai/validation-runs/<run-id>.json`, serializes through `.ai/locks/validation.lock`,
and uses `MeridianBuildIsolationKey` output roots by default.

## Dirty Worktree Protocol

- Treat pre-existing changes as user-owned.
- Do not revert, format, or rewrite files outside the requested scope.
- If committed and uncommitted changes coexist, report them separately.
- If a user-owned change touches the same file, read the local diff before editing and preserve
  unrelated hunks.
- Use focused `git diff -- <path>` checks before finalizing so the final summary only claims files
  changed for this task.

## Parallel Context Hygiene

- Prefer short batches and handoffs over all-at-once rewrites.
- One lane should not exceed the target doc surface + one generated artifact.
- If a task enters context-heavy or uncertain mode, use `../work-modes.md` and record escalation
  reason in your final handoff.

## AI Contract Coverage

- Repo navigation: `../navigation/README.md`, `../generated/repo-navigation.md`
- MDIF grounding: `../../architecture/meridian-development-intelligence-framework.md`, `../../domain/README.md`, `../context/README.md`, and generated snapshots in `../exports/`
- Agent edit rules: `../assistant-workflow-contract.md`, including the canonical AI User Notification template, plus `.codex/skills/_shared/project-context.md` and `.codex/skills/_shared/codex-execution-contract.md` for Codex-specific gates
- Generated-file handling: run generator commands for `docs/ai/generated` and `docs/generated` updates; do not hand-edit generated outputs.
- Agent orchestration: start lane planning with `../parallel-task-manifest-template.md` when multiple skills/surfaces are in scope.
- Working memory: use `../working-memory.md` to track task-local active claims, inspected files,
  assumptions, codebase drift, merge order, and validation reuse during concurrent changes.
- Codex memory: inspect `.codex/memory/index.yml` only as a selective repo-local memory catalog;
  use task descriptors plus `--receipt` for scoped memory routing; use goal inventories for
  long-running progress tracking; load only selected entries; follow `memory-system.md` for
  promotion, staleness, disabled user/global tiers, and canonical-doc precedence over memory
  tiers.
- Receipt example:

  ```text
  $ python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
  Codex memory status: pass; 5 entrie(s), 2 selected, 0 error(s), 0 warning(s).
  selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md
  selected: repo:validation -> .codex/memory/repo/validation.md

  Memory receipt:
  task_descriptor_path: .codex/memory/tasks/example.yml
  task: codex-memory-routing-example
  selectors: branches=['main']; paths=['docs/ai/codex/**', '.codex/memory/**', 'build/scripts/docs/**']
  selected: repo:validation -> .codex/memory/repo/validation.md (task work mode matches implementation; task intent matches ai-tooling; ...)
  selected: repo:ai-guidance -> .codex/memory/repo/ai-guidance.md (task work mode matches implementation; task intent matches ai-tooling; ...)
  skipped: repo:architecture -> .codex/memory/repo/architecture.md (excluded by intent ai-tooling)
  ```

  ```text
  $ python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
  Memory receipt:
  task_descriptor_path: .codex/memory/tasks/example.yml
  goal_inventory_path: .codex/memory/goals/example.yml
  goal: codex-memory-long-goal-example
  task: codex-memory-routing-example
  selectors: branches=['main']; paths=['docs/ai/codex/**', '.codex/memory/**', 'build/scripts/docs/**']
  selected: repo:validation -> .codex/memory/repo/validation.md (task work mode matches implementation; ...)
  skipped: repo:architecture -> .codex/memory/repo/architecture.md (excluded by intent ai-tooling)
  ```
- Parallel development workflows: keep each lane scoped to unique path sets and document handoff boundaries.
- Token/context management: load only startup checks then escalate context only by phase; use one lane at a time.
- Validation procedures: `python build/scripts/docs/check-codex-memory.py --summary`, scoped Codex memory receipt checks when memory changes, `python -m unittest build.scripts.docs.tests.test_check_codex_memory`, `python3 build/scripts/docs/check-ai-inventory.py --summary`, `python3 build/scripts/docs/check-codex-skills.py --summary`, `python3 build/scripts/docs/validate-docs-structure.py --top-level ai --summary`, `git diff --check`
- Documentation ownership: `../../documentation-ownership.md`, `../assistant-workflow-contract.md`

## Task-To-Proof Matrix

| Change type | Default docs | Narrow validation |
| --- | --- | --- |
| Prompt-routing rules or execution trace | `docs/ai/codex/prompt-execution-trace.md`, `docs/ai/codex/prompt-route-rules.json` | `python build/scripts/docs/prompt-route-linter.py --summary`; `python build/scripts/docs/check-mode-escalation.py --route-json docs/status/prompt-route-lint-report.json --summary`; `git diff --check -- <paths>` |
| Prompt-routed handoff packet generation | `docs/ai/agent-handoff-checklist.md`, `docs/ai/codex/prompt-route-rules.json` | `python build/scripts/docs/handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json`; `python build/scripts/docs/check-handoff-packet-schema.py --packet-json docs/status/ai-handoff-packet.json --summary`; `git diff --check -- <paths>` |
| Scoped repository text rewrite | `docs/ai/codex/quickstart.md`, `src/Meridian.Mcp/README.md` when MCP exposure changes | `python -m unittest build.scripts.ai.tests.test_ai_edit_tool`; `dotnet build src/Meridian.Mcp/Meridian.Mcp.csproj`; `git diff --check -- build/scripts/ai src/Meridian.Mcp docs/ai/codex/quickstart.md` |
| Codex skill, prompt, checklist, or AI index | `docs/ai/codex/README.md`, `.codex/skills/README.md`, nearest skill or prompt index | `python build/scripts/docs/check-codex-skills.py --summary`; `python build/scripts/docs/check-ai-inventory.py --summary`; `git diff --check -- <paths>` |
| Codex memory index, entry, descriptor, goal inventory, or promotion workflow | `docs/ai/codex/memory-system.md`, `.codex/memory/README.md` | `python build/scripts/docs/check-codex-memory.py --summary`; `python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary`; `python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary`; `python -m unittest build.scripts.docs.tests.test_check_codex_memory`; `git diff --check -- <paths>` |
| Shared AI policy | `docs/ai/assistant-workflow-contract.md`, `docs/ai/README.md`, affected host index | `python build/scripts/docs/check-ai-inventory.py --summary`; contract-drift check when policy JSON changes |
| Repo navigation or MCP routing | `docs/ai/navigation/README.md`, generated navigation inputs | `python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary`; `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14` |
| Browser workstation | `src/Meridian.Ui/dashboard/README.md` when present, related screen docs | `npm --prefix src/Meridian.Ui/dashboard run test`; targeted Vitest file when the change is narrow; `npm --prefix src/Meridian.Ui/dashboard run build` for build-facing changes |
| Shared workstation endpoint or DTO | Nearest `src/**/README.md`, `docs/architecture/module-map.md` | Focused `tests/Meridian.Tests` endpoint filter plus UI consumer tests when DTOs are consumed by WPF or browser |
| WPF view model, shell route, or desktop workflow | `.codex/AGENTS.md`, relevant WPF skill/checklist, desktop testing docs | Focused `tests/Meridian.Wpf.Tests` filter for the touched view model, shell route, or workflow script |
| Provider adapter or provider workflow | Provider docs, `src/Meridian.ProviderSdk`, nearest adapter README | Focused provider tests or provider-validation script that covers the adapter and credential path |
| Storage, WAL, or archival behavior | Storage docs and nearest `src/Meridian.Storage` README | Focused storage/WAL tests; avoid direct file writes outside WAL or `AtomicFileWriter` patterns |
| Docs-only change | Nearest docs index | `git diff --check -- <paths>` plus inventory checks when AI catalogs are affected |

## High-Traffic Skill Defaults

| Skill | Default validation snippet |
| --- | --- |
| `meridian-repo-navigation` | `python build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14` |
| `meridian-docs` | `git diff --check -- <paths>` and `python build/scripts/docs/check-ai-inventory.py --summary` for AI docs |
| `meridian-browser-workstation` | `npm --prefix src/Meridian.Ui/dashboard run test` or targeted Vitest files |
| `modular-desktop-mvvm` | Focused `dotnet test tests/Meridian.Wpf.Tests/Meridian.Wpf.Tests.csproj --filter "FullyQualifiedName~<TouchedViewModelOrRoute>" /p:EnableWindowsTargeting=true /p:EnableFullWpfBuild=true` |
| `meridian-implementation-assurance` | Touched-project build/test plus docs sync; AI/tooling changes also run the AI inventory and skill checks |

## Preview-First Repo Edits

Use `build/scripts/ai/ai-edit-tool.py` for deterministic text edits when the requested change is a
scoped codemod rather than a semantic refactor. The tool is preview-first:

```bash
python build/scripts/ai/ai-edit-tool.py plan \
  --recipe-json "{\"kind\":\"literal_replace\",\"include\":[\"docs/**/*.md\"],\"exclude\":[],\"find\":\"old\",\"replace\":\"new\",\"maxFiles\":3,\"maxEdits\":10}" \
  --scope-json "{\"paths\":[\"docs/ai\"]}" \
  --output .codex/tmp/ai-edit-plans/example.json \
  --summary

python build/scripts/ai/ai-edit-tool.py apply --plan .codex/tmp/ai-edit-plans/example.json --summary
```

Supported recipes are `literal_replace`, `regex_replace`, `anchored_block_replace`, and
`identifier_text_rename`. Plans store original SHA-256 hashes per file and `apply` fails if any file
drifts before mutation. Generated outputs, `bin/`, `obj/`, `node_modules/`, `archive/`, and paths
outside the repository root are blocked by default. Identifier renames are textual and non-semantic;
run compiler or type checks for touched languages before claiming they are safe.

MCP clients should use `preview_repo_edit`, `explain_repo_edit_plan`, and `apply_repo_edit` from
`src/Meridian.Mcp`. Those tools call the same CLI and do not move rewrite behavior into WPF,
browser workstation, or product UI code.

## Final Response Checklist

- Skill selection receipt with selected skill, mode, reason, and required opening shape.
- Memory receipt from `--receipt` when `.codex/memory/index.yml`, a task descriptor, or a goal
  inventory matched the task, plus any `--record-goal-progress` update made during the run.
- Files changed and why.
- Validation commands with pass/fail results.
- Unrelated dirty-worktree changes explicitly excluded.
- Any docs, tests, or generated artifacts intentionally not updated.
- Residual risk or blocked validation, if any.
